using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen.Services;

public static class XmlCfdiService
{
    public static FacturaXml LeerArchivoCfdiXml(string rutaXml)
    {
        var doc = XDocument.Load(rutaXml);
        var root = doc.Root ?? throw new InvalidOperationException("El archivo XML no tiene nodo raíz.");

        string serie = ObtenerAtributo(root, "Serie");
        string folio = ObtenerAtributo(root, "Folio");
        string fecha = ObtenerAtributo(root, "Fecha");
        string moneda = ObtenerAtributo(root, "Moneda", "MXN");
        decimal tipoCambio = ObtenerDecimal(root, "TipoCambio", 1m);
        decimal subtotal = ObtenerDecimal(root, "SubTotal", 0m);
        decimal descuento = ObtenerDecimal(root, "Descuento", 0m);
        decimal total = ObtenerDecimal(root, "Total", 0m);

        var emisor = root.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Emisor", StringComparison.OrdinalIgnoreCase));
        string emisorRfc = emisor != null ? ObtenerAtributo(emisor, "Rfc") : "XAXX010101000";
        string emisorNombre = emisor != null ? ObtenerAtributo(emisor, "Nombre", emisorRfc) : "Proveedor XML";

        var timbre = root.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("TimbreFiscalDigital", StringComparison.OrdinalIgnoreCase));
        string uuid = timbre != null ? ObtenerAtributo(timbre, "UUID") : string.Empty;
        string fechaTimbrado = timbre != null ? ObtenerAtributo(timbre, "FechaTimbrado", fecha) : fecha;
        if (string.IsNullOrWhiteSpace(uuid))
            uuid = $"SIN-UUID-{Path.GetFileNameWithoutExtension(rutaXml)}-{DateTime.Now:HHmmss}";

        // Impuestos globales trasladados (IVA)
        decimal ivaTotal = 0m;
        var impuestosRoot = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Impuestos", StringComparison.OrdinalIgnoreCase));
        if (impuestosRoot != null)
        {
            ivaTotal = ObtenerDecimal(impuestosRoot, "TotalImpuestosTrasladados", 0m);
        }

        var factura = new FacturaXml
        {
            Uuid = uuid.ToUpperInvariant(),
            SerieFolio = $"{serie}{(string.IsNullOrWhiteSpace(serie) || string.IsNullOrWhiteSpace(folio) ? "" : "-")}{folio}",
            FechaTimbrado = fechaTimbrado.Replace("T", " "),
            EmisorRfc = emisorRfc.ToUpperInvariant(),
            EmisorNombre = emisorNombre,
            Moneda = moneda,
            TipoCambio = tipoCambio,
            Subtotal = subtotal,
            Descuento = descuento,
            Iva = ivaTotal,
            Total = total,
            RutaArchivoXml = rutaXml
        };

        var conceptosNodos = root.Descendants()
            .Where(e => e.Name.LocalName.Equals("Concepto", StringComparison.OrdinalIgnoreCase) &&
                        e.Ancestors().All(a => !a.Name.LocalName.Equals("Addenda", StringComparison.OrdinalIgnoreCase)));

        foreach (var cNodo in conceptosNodos)
        {
            string claveSat = ObtenerAtributo(cNodo, "ClaveProdServ");
            string noId = ObtenerAtributo(cNodo, "NoIdentificacion");
            string desc = ObtenerAtributo(cNodo, "Descripcion");
            string unidad = ObtenerAtributo(cNodo, "Unidad", ObtenerAtributo(cNodo, "ClaveUnidad", "PZA"));
            decimal cantidad = ObtenerDecimal(cNodo, "Cantidad", 1m);
            decimal valorUnitario = ObtenerDecimal(cNodo, "ValorUnitario", 0m);
            decimal importe = ObtenerDecimal(cNodo, "Importe", 0m);

            if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(claveSat) && importe <= 0m && valorUnitario <= 0m)
                continue;

            decimal ivaConcepto = 0m;
            var traslados = cNodo.Descendants().Where(x => x.Name.LocalName.Equals("Traslado", StringComparison.OrdinalIgnoreCase));
            foreach (var t in traslados)
            {
                ivaConcepto += ObtenerDecimal(t, "Importe", 0m);
            }
            if (ivaConcepto == 0m && importe > 0m)
            {
                ivaConcepto = Math.Round(importe * 0.16m, 2);
            }

            // Comprobar si el producto ya está registrado en el catálogo (por vínculo previo, código o nombre)
            var prodExistente = DatabaseService.BuscarProductoExistenteParaConceptoXml(emisorRfc, noId, desc);

            factura.Conceptos.Add(new FacturaConcepto
            {
                ClaveProdServ = claveSat,
                NoIdentificacion = noId,
                Descripcion = desc,
                Unidad = unidad,
                Cantidad = cantidad,
                ValorUnitario = valorUnitario,
                Importe = importe,
                IvaConcepto = ivaConcepto,
                ProductoIdVinculado = prodExistente?.Id,
                ProductoVinculadoNombre = prodExistente != null
                    ? $"✔ Registrado: [{prodExistente.CodigoInterno}] {prodExistente.Nombre}"
                    : "➕ No registrado (se creará al guardar)"
            });
        }

        if (factura.Iva == 0m && factura.Conceptos.Count > 0)
            factura.Iva = factura.Conceptos.Sum(c => c.IvaConcepto);

        return factura;
    }

    public static string GenerarFacturaXmlDemoEnCarpeta()
    {
        string demoPath = Path.Combine(DatabaseService.XmlFolder, "CFDI40_Demo_TorniMax_OC2026_0001.xml");
        if (!File.Exists(demoPath) || !File.ReadAllText(demoPath).Contains("PROV-998"))
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<cfdi:Comprobante xmlns:cfdi=""http://www.sat.gob.mx/cfd/4"" xmlns:tfd=""http://www.sat.gob.mx/TimbreFiscalDigital""
    Version=""4.0"" Serie=""FAC"" Folio=""8842"" Fecha=""2026-10-14T09:15:00""
    SubTotal=""3855.17"" Moneda=""MXN"" Total=""4472.00"" TipoDeComprobante=""I"" Exportacion=""01"" MetodoPago=""PUE"" FormaPago=""03"" LugarExpedicion=""06000"">
  <cfdi:Emisor Rfc=""MAY150510KL4"" Nombre=""Mayorista de Tornillería y Herramientas S.A. de C.V."" RegimenFiscal=""601"" />
  <cfdi:Receptor Rfc=""EMP180101AA1"" Nombre=""DEPARTAMENTO DE COMPRAS E INGENIERIA S.A. DE C.V."" DomicilioFiscalReceptor=""06000"" RegimenFiscalReceptor=""601"" UsoCFDI=""G01"" />
  <cfdi:Conceptos>
    <cfdi:Concepto ClaveProdServ=""31161500"" NoIdentificacion=""PRD-0001"" Cantidad=""5"" ClaveUnidad=""XBX"" Unidad=""Caja (100 pzas)"" Descripcion=""Tornillo Hexagonal Inox 1/4 x 1 pulg"" ValorUnitario=""250.00"" Importe=""1250.00"" ObjetoImp=""02"">
      <cfdi:Impuestos>
        <cfdi:Traslados>
          <cfdi:Traslado Base=""1250.00"" Impuesto=""002"" TipoFactor=""Tasa"" TasaOCuota=""0.160000"" Importe=""200.00"" />
        </cfdi:Traslados>
      </cfdi:Impuestos>
    </cfdi:Concepto>
    <cfdi:Concepto ClaveProdServ=""31191500"" NoIdentificacion=""PRD-0002"" Cantidad=""4"" ClaveUnidad=""XPK"" Unidad=""Paquete (25 pzas)"" Descripcion=""Disco de Corte Fino 4-1/2 pulg Inox"" ValorUnitario=""538.79"" Importe=""2155.17"" ObjetoImp=""02"">
      <cfdi:Impuestos>
        <cfdi:Traslados>
          <cfdi:Traslado Base=""2155.17"" Impuesto=""002"" TipoFactor=""Tasa"" TasaOCuota=""0.160000"" Importe=""344.83"" />
        </cfdi:Traslados>
      </cfdi:Impuestos>
    </cfdi:Concepto>
    <cfdi:Concepto ClaveProdServ=""46181504"" NoIdentificacion=""PROV-998"" Cantidad=""10"" ClaveUnidad=""PR"" Unidad=""Par"" Descripcion=""Guante de Nitrilo Industrial Anticorte"" ValorUnitario=""45.00"" Importe=""450.00"" ObjetoImp=""02"">
      <cfdi:Impuestos>
        <cfdi:Traslados>
          <cfdi:Traslado Base=""450.00"" Impuesto=""002"" TipoFactor=""Tasa"" TasaOCuota=""0.160000"" Importe=""72.00"" />
        </cfdi:Traslados>
      </cfdi:Impuestos>
    </cfdi:Concepto>
  </cfdi:Conceptos>
  <cfdi:Impuestos TotalImpuestosTrasladados=""616.83"">
    <cfdi:Traslados>
      <cfdi:Traslado Base=""3855.17"" Impuesto=""002"" TipoFactor=""Tasa"" TasaOCuota=""0.160000"" Importe=""616.83"" />
    </cfdi:Traslados>
  </cfdi:Impuestos>
  <cfdi:Complemento>
    <tfd:TimbreFiscalDigital Version=""1.1"" UUID=""A1B2C3D4-E5F6-4789-9012-3456789ABCDE"" FechaTimbrado=""2026-10-14T09:16:12"" RfcProvCertif=""SAT970701NN3"" />
  </cfdi:Complemento>
</cfdi:Comprobante>";
            File.WriteAllText(demoPath, xml);
        }
        return demoPath;
    }

    private static string ObtenerAtributo(XElement el, string nombre, string defecto = "")
    {
        var attr = el.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        return attr?.Value?.Trim() ?? defecto;
    }

    private static decimal ObtenerDecimal(XElement el, string nombre, decimal defecto)
    {
        string txt = ObtenerAtributo(el, nombre);
        if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return val;
        return defecto;
    }
}
