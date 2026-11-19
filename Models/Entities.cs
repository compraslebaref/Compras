using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using SistemaComprasAlmacen.Services;

namespace SistemaComprasAlmacen.Models;

public class Departamento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public override string ToString() => $"{Codigo} - {Nombre}";
}

public class Encargado
{
    public int Id { get; set; }
    public int DepartamentoId { get; set; }
    public string DepartamentoNombre { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Puesto { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public override string ToString() => $"{Nombre} ({DepartamentoNombre})";
}

public class Proyecto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string ClienteArea { get; set; } = string.Empty;
    public string Estatus { get; set; } = "Activo"; // Activo, Pausado, Cerrado
    public bool EsStockGeneral { get; set; }
    public override string ToString() => EsStockGeneral ? "[STOCK] Almacén General" : $"{Codigo} - {Nombre}";
}

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Icono { get; set; } = "Box24";
    public string ColorHex { get; set; } = "#0078D4";
    public string EtiquetaCampo1 { get; set; } = "Capacidad Eléctrica / Tipo";
    public string EtiquetaCampo2 { get; set; } = "Especificación / Material";
    public string EtiquetaCampo3 { get; set; } = "Detalle Adicional / Color";
    public string CamposResumen => $"{EtiquetaCampo1} | {EtiquetaCampo2} | {EtiquetaCampo3}";
    public override string ToString() => Nombre;
}

public class Producto : INotifyPropertyChanged
{
    private bool _seleccionado;
    private string _imagenRuta = string.Empty;

    public int Id { get; set; }
    public string CodigoInterno { get; set; } = string.Empty; // Ej. PRD-0001
    public string CodigoBarras { get; set; } = string.Empty;  // EAN/UPC o igual al interno
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int CategoriaId { get; set; }
    public string CategoriaNombre { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;       // Ej. Jabón y Limpiadores, Gas Refrigerante R-32, Cinta de Aislar
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public string Medidas { get; set; } = string.Empty;
    public string CapacidadElectrica { get; set; } = string.Empty; // Campo dinámico 1 según categoría
    public string Especificacion1 { get; set; } = string.Empty;    // Campo dinámico 2 según categoría
    public string Especificacion2 { get; set; } = string.Empty;    // Campo dinámico 3 según categoría
    public string UnidadCompra { get; set; } = "Pieza";       // Ej. Caja, Paquete, Rollo, Pieza
    public string UnidadAlmacen { get; set; } = "Pieza";      // Ej. Pieza, Metro, Par
    public decimal FactorConversion { get; set; } = 1m;       // Ej. 1 Caja = 100 Piezas
    public decimal StockActual { get; set; }                  // En UnidadAlmacen
    public decimal StockReservadoProyecto { get; set; }       // En UnidadAlmacen
    public decimal StockMinimo { get; set; } = 0m;
    public decimal UltimoCostoCompra { get; set; }            // Por UnidadCompra (con IVA)
    public decimal CostoUnitarioAlmacen => FactorConversion > 0 ? Math.Round(UltimoCostoCompra / FactorConversion, 2) : UltimoCostoCompra;
    private string _ubicacion = "A-01";
    public string Ubicacion
    {
        get => _ubicacion;
        set
        {
            string val = value ?? string.Empty;
            if (_ubicacion != val)
            {
                _ubicacion = val;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ubicacion)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UbicacionBadgeTexto)));
            }
        }
    }
    public string UbicacionBadgeTexto => string.IsNullOrWhiteSpace(_ubicacion) ? "📍 Sin ubic." : $"📍 {_ubicacion.Trim()}";
    public string ImagenRuta
    {
        get => _imagenRuta;
        set
        {
            if (_imagenRuta != value)
            {
                _imagenRuta = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImagenRuta)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImagenMiniatura)));
            }
        }
    }
    public ImageSource? ImagenMiniatura => ImageStandardizerService.ObtenerMiniaturaCacheada(_imagenRuta);
    public bool EsHerramienta { get; set; }
    public decimal StockLibre => Math.Max(0, StockActual - StockReservadoProyecto);
    public bool StockBajo => StockMinimo > StockActual;
    public decimal FaltanteStock => StockMinimo > StockActual ? StockMinimo - StockActual : 0m;
    public string PrioridadAlertaStock => StockMinimo <= StockActual
        ? "Normal"
        : (StockActual <= 0
            ? "🔴 Crítica (Agotado)"
            : (StockActual <= StockMinimo * 0.5m ? "🟠 Alta (≤50%)" : "🟡 Media"));
    public string ConversionTexto => FactorConversion > 1
        ? $"1 {UnidadCompra} = {FactorConversion:0.##} {UnidadAlmacen}"
        : $"1 {UnidadAlmacen}";
    public string FichaTecnicaResumen
    {
        get
        {
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(Familia)) partes.Add($"[{Familia}]");
            if (!string.IsNullOrWhiteSpace(Marca)) partes.Add(Marca);
            if (!string.IsNullOrWhiteSpace(Modelo)) partes.Add($"Mod: {Modelo}");
            if (!string.IsNullOrWhiteSpace(Medidas)) partes.Add(Medidas);
            if (!string.IsNullOrWhiteSpace(CapacidadElectrica)) partes.Add(CapacidadElectrica);
            if (!string.IsNullOrWhiteSpace(Especificacion1)) partes.Add(Especificacion1);
            if (!string.IsNullOrWhiteSpace(Especificacion2)) partes.Add(Especificacion2);
            return partes.Count > 0 ? string.Join(" • ", partes) : CategoriaNombre;
        }
    }
    public string CodigoEfectivo => string.IsNullOrWhiteSpace(CodigoBarras) ? CodigoInterno : CodigoBarras;
    public bool Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (_seleccionado != value)
            {
                _seleccionado = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Seleccionado)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() =>
        !string.IsNullOrWhiteSpace(Marca) || !string.IsNullOrWhiteSpace(Modelo)
            ? $"{CodigoInterno} - {Nombre} [{Marca} {Modelo}]".Trim()
            : $"{CodigoInterno} - {Nombre}";
}

public class HerramientaActivo
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public string CodigoIndividual { get; set; } = string.Empty; // HER-0001
    public string NumeroSerie { get; set; } = string.Empty;
    public string MarcaModelo { get; set; } = string.Empty;
    public string Estado { get; set; } = "Disponible"; // Disponible, En Préstamo, Dañada, Extraviada / Baja
    public string Ubicacion { get; set; } = "Gabinete H-1";
    public string EncargadoActual { get; set; } = string.Empty;
    public string ProyectoActual { get; set; } = string.Empty;
    public string NotasEstado { get; set; } = string.Empty;
    public string FechaCompra { get; set; } = string.Empty;
    public string FechaFinGarantia { get; set; } = string.Empty;
    public decimal CostoAdquisicion { get; set; }
    public decimal GastoMantenimientoAcumulado { get; set; }

    public bool EnGarantia =>
        !string.IsNullOrWhiteSpace(FechaFinGarantia) &&
        DateTime.TryParse(FechaFinGarantia, out var dtFin) &&
        dtFin.Date >= DateTime.Today;

    public string EstadoGarantiaTexto
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FechaFinGarantia) || !DateTime.TryParse(FechaFinGarantia, out var dtFin))
                return "Sin registro";
            int dias = (dtFin.Date - DateTime.Today).Days;
            return dias >= 0
                ? $"En Garantía ({dias}d rest. · {dtFin:yyyy-MM-dd})"
                : $"Vencida ({dtFin:yyyy-MM-dd})";
        }
    }

    public decimal PorcentajeMantenimientoSobreCosto =>
        CostoAdquisicion > 0m ? Math.Round((GastoMantenimientoAcumulado / CostoAdquisicion) * 100m, 1) : 0m;

    public bool AlertaReemplazoTco =>
        CostoAdquisicion > 0m && GastoMantenimientoAcumulado >= (CostoAdquisicion * 0.50m);

    public string ResumenTcoTexto =>
        CostoAdquisicion <= 0m && GastoMantenimientoAcumulado <= 0m
            ? "S/D"
            : $"Costo: {CostoAdquisicion:C0} | Mant: {GastoMantenimientoAcumulado:C0} ({PorcentajeMantenimientoSobreCosto:0.#}%)" +
              (AlertaReemplazoTco ? " [REEMPLAZAR >=50%]" : "");

    public override string ToString() => $"{CodigoIndividual} - {ProductoNombre} ({MarcaModelo}) [{Estado}]";
}

public class Proveedor
{
    public int Id { get; set; }
    public string Rfc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string NombreComercial { get; set; } = string.Empty;
    public string Contacto { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public int DiasEntregaPromedio { get; set; } = 3;
    public string CondicionesPago { get; set; } = "Contado";
    public string Notas { get; set; } = string.Empty;

    public string NombreMostrar
    {
        get
        {
            string com = (NombreComercial ?? string.Empty).Trim();
            string raz = (RazonSocial ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(com) && !string.Equals(com, raz, StringComparison.OrdinalIgnoreCase))
                return com;
            string baseNom = !string.IsNullOrWhiteSpace(com) ? com : raz;
            return ExtraerNombreComercialCorto(baseNom);
        }
    }

    public static string ExtraerNombreComercialCorto(string? nombreLegal)
    {
        if (string.IsNullOrWhiteSpace(nombreLegal))
            return string.Empty;

        string s = nombreLegal.Trim();
        string[] sufijosRegimen =
        {
            ", S.A.P.I. DE C.V.", " S.A.P.I. DE C.V.", ", SAPI DE CV", " SAPI DE CV",
            ", S. DE R.L. DE C.V.", " S. DE R.L. DE C.V.", ", S DE RL DE CV", " S DE RL DE CV",
            ", S. DE R.L.", " S. DE R.L.", ", S DE RL", " S DE RL",
            ", S.A.B. DE C.V.", " S.A.B. DE C.V.", ", SAB DE CV", " SAB DE CV",
            ", S.A. DE C.V.", " S.A. DE C.V.", ", SA DE CV", " SA DE CV",
            ", S.A.S. DE C.V.", " S.A.S. DE C.V.", ", SAS DE CV", " SAS DE CV",
            ", S.C. DE R.L. DE C.V.", " S.C. DE R.L. DE C.V.",
            ", S.C.", " S.C.", ", S.A.", " S.A.", ", A.C.", " A.C."
        };

        foreach (var suf in sufijosRegimen)
        {
            if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^suf.Length].Trim(' ', ',', '.', '-');
                break;
            }
        }

        return string.IsNullOrWhiteSpace(s) ? nombreLegal.Trim() : s;
    }

    public override string ToString() =>
        !string.IsNullOrWhiteSpace(Rfc) && !string.Equals(Rfc.Trim(), "XAXX010101000", StringComparison.OrdinalIgnoreCase)
            ? $"{NombreMostrar} ({Rfc})"
            : NombreMostrar;

    public string EtiquetaSelectorComparativa => ToString();
}

public class Solicitud
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // SOL-2026-0001
    public string Fecha { get; set; } = string.Empty;
    public int DepartamentoId { get; set; }
    public string DepartamentoNombre { get; set; } = string.Empty;
    public int EncargadoId { get; set; }
    public string EncargadoNombre { get; set; } = string.Empty;
    public int ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public string ProyectoMostrar => string.IsNullOrWhiteSpace(ProyectoNombre) ? "Consumo Interno de Departamento" : ProyectoNombre;
    public string TipoSolicitud { get; set; } = "Requerimiento de Compra"; // Requerimiento de Compra, Solo Cotización / Presupuesto
    public bool EsSoloCotizacion => string.Equals(TipoSolicitud, "Solo Cotización / Presupuesto", StringComparison.OrdinalIgnoreCase);
    public string Origen { get; set; } = "Catálogo"; // Catálogo, WhatsApp, Plantilla Excel
    public string Estatus { get; set; } = "Pendiente"; // Pendiente, Autorizada para Surtir Stock, En Comparativa, Cotización Entregada, OC Generada, Atendida
    public string Notas { get; set; } = string.Empty;
    public bool UsaPartidas { get; set; }
    public List<SolicitudPartida> Partidas { get; set; } = new();
    // Pausa por dependencia externa y cierre manual (no alteran el Estatus del flujo)
    public string EsperandoRespuestaDe { get; set; } = string.Empty;
    public string EsperandoDesde { get; set; } = string.Empty;
    public bool ConcluidoManual { get; set; }
    public bool EstaEsperandoRespuesta => !string.IsNullOrWhiteSpace(EsperandoRespuestaDe) && !ConcluidoManual;
    public string EstatusMostrar => ConcluidoManual ? "Concluida" : (EstaEsperandoRespuesta ? "Esperando respuesta" : Estatus);
    public IReadOnlyList<SolicitudPartida> PartidasParaComparativa => UsaPartidas
        ? Partidas
        : Partidas.Any(p => p.IncluirEnCotizacion || p.TieneOcGenerada)
            ? Partidas.Where(p => p.IncluirEnCotizacion || p.TieneOcGenerada).ToList()
            : Partidas;

    public int TotalSkus => Partidas.Count;
    public decimal TotalArticulos => Partidas.Count > 0 ? Partidas.Sum(x => x.CantidadSolicitada) : 0m;
    public string ResumenArticulos => $"{TotalSkus} SKUs ({TotalArticulos:0.##} pzas)";
    public bool EsEditable => Id == 0 || string.Equals(Estatus, "Pendiente", StringComparison.OrdinalIgnoreCase);
    public bool PermiteEditarUnidadYQuitarConceptos =>
        EsEditable ||
        string.Equals(Estatus, "En Comparativa", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Estatus, "Parcial en OC", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Estatus, "Cotización Entregada", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => Id == 0
        ? "📋 [Nueva Solicitud en Captura]"
        : $"{Folio} · {(string.IsNullOrWhiteSpace(ProyectoNombre) ? DepartamentoNombre : ProyectoNombre)} ({TotalSkus} art. · {EncargadoNombre})";
}

public class SolicitudPartida
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string DescripcionLibre { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;
    public bool EsConceptoLibre => ProductoId <= 0;
    public string UnidadCompra { get; set; } = "Pieza";
    public decimal CantidadSolicitada { get; set; } = 1m;
    public decimal StockActualAlmacen { get; set; }
    public bool IncluirEnCotizacion { get; set; } = true;
    public int DiasVigenciaCotizacion { get; set; } = 15;
    public string FechaCotizacion { get; set; } = string.Empty;
    public string NotasPartida { get; set; } = string.Empty;
    public string NombrePartida { get; set; } = string.Empty;
    public string EncabezadoPartida => string.IsNullOrWhiteSpace(NombrePartida) ? "Sin partida asignada" : NombrePartida.Trim().ToUpperInvariant();

    // Cotización guardada por producto (3 opciones + ganador + OC generada)
    public int Proveedor1Id { get; set; }
    public decimal PrecioSinIva1 { get; set; }
    public int DiasEntrega1 { get; set; } = 2;
    public decimal FleteConIva1 { get; set; }
    public string NotasCotizacion1 { get; set; } = string.Empty;
    public int DiasVigencia1 { get; set; } = 15;
    public string FechaCotizacion1 { get; set; } = string.Empty;

    public int Proveedor2Id { get; set; }
    public decimal PrecioSinIva2 { get; set; }
    public int DiasEntrega2 { get; set; } = 3;
    public decimal FleteConIva2 { get; set; }
    public string NotasCotizacion2 { get; set; } = string.Empty;
    public int DiasVigencia2 { get; set; } = 15;
    public string FechaCotizacion2 { get; set; } = string.Empty;

    public int Proveedor3Id { get; set; }
    public decimal PrecioSinIva3 { get; set; }
    public int DiasEntrega3 { get; set; } = 1;
    public decimal FleteConIva3 { get; set; }
    public string NotasCotizacion3 { get; set; } = string.Empty;
    public int DiasVigencia3 { get; set; } = 15;
    public string FechaCotizacion3 { get; set; } = string.Empty;

    public int ProveedorGanadorIndice { get; set; } // 0 = Sin decidir, 1 = Opción 1, 2 = Opción 2, 3 = Opción 3
    public int OrdenCompraId { get; set; }
    public string OrdenCompraFolio { get; set; } = string.Empty;
    public bool TieneOcGenerada => OrdenCompraId > 0 || !string.IsNullOrWhiteSpace(OrdenCompraFolio);
}

public class CotizacionComparativa
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // COT-2026-0001
    public int SolicitudId { get; set; }
    public string SolicitudFolio { get; set; } = string.Empty;
    public string ProyectoNombre { get; set; } = string.Empty;
    public string DepartamentoNombre { get; set; } = string.Empty;
    public string EncargadoNombre { get; set; } = string.Empty;
    public string Fecha { get; set; } = string.Empty;
    public string Estatus { get; set; } = "Abierta"; // Abierta, Cerrada con OC
    public string Notas { get; set; } = string.Empty;
}

public class CotizacionProveedorExtra
{
    public int Id { get; set; }
    public int SolicitudPartidaId { get; set; }
    public int NumeroOpcion { get; set; } // 4, 5, 6...
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva { get; set; }
    public decimal PrecioConIva => Math.Round(PrecioSinIva * 1.16m, 2);
    public int DiasEntrega { get; set; } = 2;
    public decimal FleteConIva { get; set; }
    public int DiasVigencia { get; set; } = 15;
    public string FechaCotizacion { get; set; } = string.Empty;
    public string NotasCotizacion { get; set; } = string.Empty;
    public override string ToString() => $"Op.{NumeroOpcion} {ProveedorNombre}: ${PrecioConIva:N2} c/IVA ({DiasEntrega}d)";
}

public class CotizacionRenglonComparativo
{
    public string NombrePartida { get; set; } = string.Empty;
    public int SolicitudPartidaId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string DescripcionLibre { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;
    public string UnidadCompra { get; set; } = "Pieza";
    public decimal Cantidad { get; set; } = 1m;
    public decimal StockActualAlmacen { get; set; }
    public bool IncluirEnCotizacion { get; set; } = true;
    public int DiasVigenciaCotizacion { get; set; } = 15;
    public string FechaCotizacion { get; set; } = string.Empty;
    public decimal UltimoCostoCompraConIva { get; set; }
    public string ReferenciaHistorica { get; set; } = string.Empty;
    public List<CotizacionProveedorExtra> ProveedoresExtra { get; set; } = new();

    public bool EsConceptoLibre => ProductoId <= 0;

    public static string FormatearVigencia(string? fechaCotizacion, int diasVigencia, bool detallado = false)
    {
        int dv = diasVigencia > 0 ? diasVigencia : 15;
        if (string.IsNullOrWhiteSpace(fechaCotizacion))
            return $"Vigencia: {dv} días";
        if (DateTime.TryParse(fechaCotizacion, out DateTime dtCot))
        {
            DateTime vence = dtCot.Date.AddDays(dv);
            int diasRestantes = (vence - DateTime.Today).Days;
            if (diasRestantes >= 0)
                return detallado
                    ? $"🟢 Vigente hasta {vence:dd/MM/yyyy} ({diasRestantes}d rest.)"
                    : $"🟢 Vigente ({diasRestantes}d rest.)";
            return detallado
                ? $"🔴 Vencida hace {Math.Abs(diasRestantes)}d (Venció {vence:dd/MM/yyyy} — Preguntar a proveedor)"
                : $"🔴 Vencida (hace {Math.Abs(diasRestantes)}d - Recotizar)";
        }
        return $"Vigencia: {dv} días";
    }

    public string VigenciaEstadoTexto
    {
        get
        {
            if (ProveedorGanadorIndice == 1)
                return FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion1) ? FechaCotizacion1 : FechaCotizacion, DiasVigencia1 > 0 ? DiasVigencia1 : DiasVigenciaCotizacion);
            if (ProveedorGanadorIndice == 2)
                return FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion2) ? FechaCotizacion2 : FechaCotizacion, DiasVigencia2 > 0 ? DiasVigencia2 : DiasVigenciaCotizacion);
            if (ProveedorGanadorIndice == 3)
                return FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion3) ? FechaCotizacion3 : FechaCotizacion, DiasVigencia3 > 0 ? DiasVigencia3 : DiasVigenciaCotizacion);
            if (ProveedorGanadorIndice >= 4)
            {
                var ex = ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == ProveedorGanadorIndice);
                if (ex != null)
                    return FormatearVigencia(!string.IsNullOrWhiteSpace(ex.FechaCotizacion) ? ex.FechaCotizacion : FechaCotizacion, ex.DiasVigencia > 0 ? ex.DiasVigencia : DiasVigenciaCotizacion);
            }
            return FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion1) ? FechaCotizacion1 : FechaCotizacion, DiasVigencia1 > 0 ? DiasVigencia1 : DiasVigenciaCotizacion);
        }
    }

    // Opción 1
    public int Proveedor1Id { get; set; }
    public string Proveedor1Nombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva1 { get; set; }
    public decimal PrecioConIva1 => Math.Round(PrecioSinIva1 * 1.16m, 2);
    public int DiasEntrega1 { get; set; } = 2;
    public int DiasVigencia1 { get; set; } = 15;
    public string FechaCotizacion1 { get; set; } = string.Empty;
    public decimal FleteConIva1 { get; set; }
    public string NotasCotizacion1 { get; set; } = string.Empty;
    public decimal TotalPartidaConIva1 => Math.Round(Cantidad * PrecioSinIva1 * 1.16m, 2);
    public decimal TotalConFlete1 => Math.Round(TotalPartidaConIva1 + FleteConIva1, 2);
    public decimal ImporteTotal1 => TotalConFlete1;
    public string EstadoVigencia1 => FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion1) ? FechaCotizacion1 : FechaCotizacion, DiasVigencia1, true);
    public string ResumenOpcion1 => Proveedor1Id > 0 && PrecioSinIva1 > 0
        ? $"{Proveedor.ExtraerNombreComercialCorto(Proveedor1Nombre)}: ${PrecioConIva1:N2} c/u ({DiasEntrega1}d · Vig.{DiasVigencia1}d)"
        : (Proveedor1Id > 0 && !string.IsNullOrWhiteSpace(Proveedor1Nombre) && Proveedor1Nombre != "Sin asignar"
            ? $"{Proveedor.ExtraerNombreComercialCorto(Proveedor1Nombre)}: Sin precio ($0.00)"
            : "Sin cotizar");

    // Opción 2
    public int Proveedor2Id { get; set; }
    public string Proveedor2Nombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva2 { get; set; }
    public decimal PrecioConIva2 => Math.Round(PrecioSinIva2 * 1.16m, 2);
    public int DiasEntrega2 { get; set; } = 3;
    public int DiasVigencia2 { get; set; } = 15;
    public string FechaCotizacion2 { get; set; } = string.Empty;
    public decimal FleteConIva2 { get; set; }
    public string NotasCotizacion2 { get; set; } = string.Empty;
    public decimal TotalPartidaConIva2 => Math.Round(Cantidad * PrecioSinIva2 * 1.16m, 2);
    public decimal TotalConFlete2 => Math.Round(TotalPartidaConIva2 + FleteConIva2, 2);
    public decimal ImporteTotal2 => TotalConFlete2;
    public string EstadoVigencia2 => FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion2) ? FechaCotizacion2 : FechaCotizacion, DiasVigencia2, true);
    public string ResumenOpcion2 => Proveedor2Id > 0 && PrecioSinIva2 > 0
        ? $"{Proveedor.ExtraerNombreComercialCorto(Proveedor2Nombre)}: ${PrecioConIva2:N2} c/u ({DiasEntrega2}d · Vig.{DiasVigencia2}d)"
        : (Proveedor2Id > 0 && NumeroOpcionesVisibles >= 2 && !string.IsNullOrWhiteSpace(Proveedor2Nombre) && Proveedor2Nombre != "Sin asignar"
            ? $"{Proveedor.ExtraerNombreComercialCorto(Proveedor2Nombre)}: Sin precio ($0.00)"
            : "Sin cotizar");

    // Opción 3
    public int Proveedor3Id { get; set; }
    public string Proveedor3Nombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva3 { get; set; }
    public decimal PrecioConIva3 => Math.Round(PrecioSinIva3 * 1.16m, 2);
    public int DiasEntrega3 { get; set; } = 1;
    public int DiasVigencia3 { get; set; } = 15;
    public string FechaCotizacion3 { get; set; } = string.Empty;
    public decimal FleteConIva3 { get; set; }
    public string NotasCotizacion3 { get; set; } = string.Empty;
    public decimal TotalPartidaConIva3 => Math.Round(Cantidad * PrecioSinIva3 * 1.16m, 2);
    public decimal TotalConFlete3 => Math.Round(TotalPartidaConIva3 + FleteConIva3, 2);
    public decimal ImporteTotal3 => TotalConFlete3;
    public string EstadoVigencia3 => FormatearVigencia(!string.IsNullOrWhiteSpace(FechaCotizacion3) ? FechaCotizacion3 : FechaCotizacion, DiasVigencia3, true);
    public string ResumenOpcion3
    {
        get
        {
            string baseOp3 = Proveedor3Id > 0 && PrecioSinIva3 > 0
                ? $"{Proveedor.ExtraerNombreComercialCorto(Proveedor3Nombre)}: ${PrecioConIva3:N2} c/u ({DiasEntrega3}d · Vig.{DiasVigencia3}d)"
                : (Proveedor3Id > 0 && NumeroOpcionesVisibles >= 3 && !string.IsNullOrWhiteSpace(Proveedor3Nombre) && Proveedor3Nombre != "Sin asignar"
                    ? $"{Proveedor.ExtraerNombreComercialCorto(Proveedor3Nombre)}: Sin precio ($0.00)"
                    : "Sin cotizar");
            if (ProveedoresExtra.Count == 0)
                return baseOp3;
            string extras = string.Join(" | ", ProveedoresExtra.Select(x =>
                x.PrecioSinIva > 0
                    ? $"Op.{x.NumeroOpcion} {Proveedor.ExtraerNombreComercialCorto(x.ProveedorNombre)}: ${x.PrecioConIva:N2} ({x.DiasEntrega}d · Vig.{x.DiasVigencia}d)"
                    : $"Op.{x.NumeroOpcion} {Proveedor.ExtraerNombreComercialCorto(x.ProveedorNombre)}: Sin precio ($0.00)"));
            return baseOp3 == "Sin cotizar" ? extras : $"{baseOp3} | {extras}";
        }
    }

    // Proveedor ganador elegido para este producto (0 = Sin decidir, 1 = Opción 1, 2 = Opción 2, 3 = Opción 3, 4+ = ProveedoresExtra)
    public int ProveedorGanadorIndice { get; set; }
    public int NumeroOpcionesVisibles { get; set; } = 1;
    public int OrdenCompraId { get; set; }
    public string OrdenCompraFolio { get; set; } = string.Empty;
    public bool TieneOcGenerada => OrdenCompraId > 0 || !string.IsNullOrWhiteSpace(OrdenCompraFolio);

    public int ProveedorGanadorId
    {
        get
        {
            if (ProveedorGanadorIndice == 1) return Proveedor1Id;
            if (ProveedorGanadorIndice == 2) return Proveedor2Id;
            if (ProveedorGanadorIndice == 3) return Proveedor3Id;
            if (ProveedorGanadorIndice >= 4)
                return ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == ProveedorGanadorIndice)?.ProveedorId ?? 0;
            return 0;
        }
    }

    public string ProveedorGanadorNombre
    {
        get
        {
            if (ProveedorGanadorIndice == 1) return Proveedor1Nombre;
            if (ProveedorGanadorIndice == 2) return Proveedor2Nombre;
            if (ProveedorGanadorIndice == 3) return Proveedor3Nombre;
            if (ProveedorGanadorIndice >= 4)
                return ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == ProveedorGanadorIndice)?.ProveedorNombre ?? "Sin decidir";
            return "Sin decidir";
        }
    }

    public decimal PrecioUnitarioGanadorSinIva
    {
        get
        {
            if (ProveedorGanadorIndice == 1) return PrecioSinIva1;
            if (ProveedorGanadorIndice == 2) return PrecioSinIva2;
            if (ProveedorGanadorIndice == 3) return PrecioSinIva3;
            if (ProveedorGanadorIndice >= 4)
                return ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == ProveedorGanadorIndice)?.PrecioSinIva ?? 0m;
            return 0m;
        }
    }

    public decimal PrecioUnitarioGanadorConIva => Math.Round(PrecioUnitarioGanadorSinIva * 1.16m, 2);

    public decimal FleteGanadorConIva
    {
        get
        {
            if (ProveedorGanadorIndice == 1) return FleteConIva1;
            if (ProveedorGanadorIndice == 2) return FleteConIva2;
            if (ProveedorGanadorIndice == 3) return FleteConIva3;
            if (ProveedorGanadorIndice >= 4)
                return ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == ProveedorGanadorIndice)?.FleteConIva ?? 0m;
            return 0m;
        }
    }

    public int DiasEntregaGanador
    {
        get
        {
            if (ProveedorGanadorIndice == 1) return DiasEntrega1;
            if (ProveedorGanadorIndice == 2) return DiasEntrega2;
            if (ProveedorGanadorIndice == 3) return DiasEntrega3;
            if (ProveedorGanadorIndice >= 4)
                return ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == ProveedorGanadorIndice)?.DiasEntrega ?? 2;
            return 2;
        }
    }

    public decimal TotalGanadorConIva => Math.Round(Cantidad * PrecioUnitarioGanadorSinIva * 1.16m, 2);

    public string EtiquetaSelectorDetalle => $"{(string.IsNullOrWhiteSpace(ProductoCodigo) ? "CONCEPTO" : ProductoCodigo)} — {ProductoNombre} ({Cantidad:0.##} {UnidadCompra})";

    public string EstadoDecisionTexto
    {
        get
        {
            if (TieneOcGenerada)
                return $"OC: {OrdenCompraFolio} ({Proveedor.ExtraerNombreComercialCorto(ProveedorGanadorNombre)})";
            if (ProveedorGanadorIndice >= 1 && ProveedorGanadorId > 0)
                return $"★ {Proveedor.ExtraerNombreComercialCorto(ProveedorGanadorNombre)}";
            return "Pendiente de elegir";
        }
    }
}

public class ResumenOcPrevia
{
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;
    public int CantidadPartidas { get; set; }
    public string ProductosResumen { get; set; } = string.Empty;
    public int DiasEntregaMax { get; set; }
    public decimal SubtotalSinIva { get; set; }
    public decimal FleteConIva { get; set; }
    public decimal IvaTotal { get; set; }
    public decimal TotalConIva { get; set; }
    public string EstadoEmision { get; set; } = "Lista para emitir OC";
}

public class OrdenCompra
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // OC-2026-0001
    public string Fecha { get; set; } = string.Empty;
    public int SolicitudId { get; set; }
    public string SolicitudFolio { get; set; } = string.Empty;
    public string CotizacionFolio { get; set; } = string.Empty;
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;
    public string ProveedorRfc { get; set; } = string.Empty;
    public int ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public string DepartamentoNombre { get; set; } = string.Empty;
    public string EncargadoNombre { get; set; } = string.Empty;
    public int TiempoEntregaDias { get; set; } = 3;
    public decimal CostoEnvioConIva { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal TotalConIva { get; set; }
    public string CondicionPago { get; set; } = "Contado (Transferencia previa)";
    public string FechaProgramadaPago { get; set; } = string.Empty;
    public string Estatus { get; set; } = "Emitida"; // Pendiente Vo.Bo. Dirección, Autorizada / Pendiente Pago Finanzas, Pagada / Lista para Almacén, Emitida, Recibida Parcialmente, Recibida Completa, Cerrada Parcial (Admin), Cancelada
    public string Notas { get; set; } = string.Empty;
    public List<OrdenCompraPartida> Partidas { get; set; } = new();
    // Pausa por dependencia externa y cierre manual (no alteran el Estatus del flujo)
    public string EsperandoRespuestaDe { get; set; } = string.Empty;
    public string EsperandoDesde { get; set; } = string.Empty;
    public bool ConcluidoManual { get; set; }
    public bool EstaEsperandoRespuesta => !string.IsNullOrWhiteSpace(EsperandoRespuestaDe) && !ConcluidoManual;
    public string EstatusMostrar => ConcluidoManual ? "Concluida" : (EstaEsperandoRespuesta ? "Esperando respuesta" : Estatus);
    public decimal TotalUnidadesPedidas => Partidas.Count > 0 ? Partidas.Sum(x => x.CantidadPedida) : 0m;
    public decimal TotalUnidadesRecibidas => Partidas.Count > 0 ? Partidas.Sum(x => x.CantidadRecibida) : 0m;
    public string AvanceRecepcionTexto
    {
        get
        {
            if (TotalUnidadesPedidas <= 0)
                return Estatus == "Recibida Completa" ? "100%" : "0%";
            decimal pct = Math.Min(100m, Math.Round((TotalUnidadesRecibidas / TotalUnidadesPedidas) * 100m, 0));
            return $"{pct:0}% ({TotalUnidadesRecibidas:0.##}/{TotalUnidadesPedidas:0.##})";
        }
    }
    public override string ToString() => Id == 0 ? Folio : $"{Folio} - {ProveedorNombre} ({Estatus}) - ${TotalConIva:N2}";
}

public class OrdenCompraPartida : INotifyPropertyChanged
{
    private decimal _cantidadRecibirAhora;
    private bool _seleccionadoRecibir;
    private string _ubicacionDestino = "Almacén General";

    public int Id { get; set; }
    public int OrdenCompraId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string DescripcionLibre { get; set; } = string.Empty;
    public string UnidadCompra { get; set; } = "Pieza";
    public string UnidadAlmacen { get; set; } = "Pieza";
    private decimal _factorConversion = 1m;
    public decimal FactorConversion
    {
        get => _factorConversion;
        set
        {
            decimal ajustado = Math.Max(0.0001m, Math.Round(value, 4));
            if (_factorConversion != ajustado)
            {
                _factorConversion = ajustado;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FactorConversion)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalUnidadesAlmacenRecibirHoy)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResumenConversionAlmacen)));
            }
        }
    }
    public decimal CantidadPedida { get; set; }
    public decimal CantidadRecibida { get; set; }
    public decimal CantidadPendiente => Math.Max(0, CantidadPedida - CantidadRecibida);
    public bool TienePendientePorRecibir => CantidadPendiente > 0m;
    public decimal TotalUnidadesAlmacenRecibirHoy => Math.Round(CantidadRecibirAhora * Math.Max(0.0001m, FactorConversion), 2);
    public string ResumenConversionAlmacen => $"{TotalUnidadesAlmacenRecibirHoy:0.##} {UnidadAlmacen}";

    public bool SeleccionadoRecibir
    {
        get => _seleccionadoRecibir;
        set
        {
            if (_seleccionadoRecibir != value)
            {
                _seleccionadoRecibir = value;
                if (_seleccionadoRecibir && _cantidadRecibirAhora <= 0m && CantidadPendiente > 0m)
                {
                    _cantidadRecibirAhora = CantidadPendiente;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadRecibirAhora)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalUnidadesAlmacenRecibirHoy)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResumenConversionAlmacen)));
                }
                else if (!_seleccionadoRecibir && _cantidadRecibirAhora > 0m)
                {
                    _cantidadRecibirAhora = 0m;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadRecibirAhora)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalUnidadesAlmacenRecibirHoy)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResumenConversionAlmacen)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoRecibir)));
            }
        }
    }

    public decimal CantidadRecibirAhora
    {
        get => _cantidadRecibirAhora;
        set
        {
            decimal max = CantidadPendiente > 0m ? CantidadPendiente : Math.Max(0m, value);
            decimal ajustado = Math.Clamp(value, 0m, max);
            if (_cantidadRecibirAhora != ajustado)
            {
                _cantidadRecibirAhora = ajustado;
                bool debeSel = _cantidadRecibirAhora > 0m;
                if (_seleccionadoRecibir != debeSel)
                {
                    _seleccionadoRecibir = debeSel;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoRecibir)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadRecibirAhora)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalUnidadesAlmacenRecibirHoy)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResumenConversionAlmacen)));
            }
        }
    }

    public string UbicacionDestino
    {
        get => _ubicacionDestino;
        set
        {
            string val = ExtraerCodigoUbicacionSimple(value);
            if (_ubicacionDestino != val)
            {
                _ubicacionDestino = val;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UbicacionDestino)));
            }
        }
    }

    private static string ExtraerCodigoUbicacionSimple(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "Almacén General";
        string s = texto.Trim();
        int idx = s.IndexOf(" — ", StringComparison.Ordinal);
        if (idx > 0) s = s[..idx].Trim();
        return s;
    }

    public decimal PrecioUnitarioSinIva { get; set; }
    public decimal PrecioUnitarioConIva => Math.Round(PrecioUnitarioSinIva * 1.16m, 2);
    public decimal ImporteSubtotal => Math.Round(CantidadPedida * PrecioUnitarioSinIva, 2);
    public decimal ImporteConIva => Math.Round(ImporteSubtotal * 1.16m, 2);

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class EntradaAlmacen
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // ENT-2026-0001
    public string Fecha { get; set; } = string.Empty;
    public int? OrdenCompraId { get; set; }
    public string OrdenCompraFolio { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;
    public string ProyectoNombre { get; set; } = string.Empty;
    public string TipoEntrada { get; set; } = "Recepción OC"; // Recepción OC, Recepción Parcial OC, Entrada Directa / Ajuste
    public string FacturaUuid { get; set; } = string.Empty;
    public string Notas { get; set; } = string.Empty;
    public string FacturaReferencia => !string.IsNullOrWhiteSpace(FacturaUuid) ? FacturaUuid : Notas;
    public List<EntradaPartida> Partidas { get; set; } = new();
    public override string ToString() =>
        $"{Folio} — Prov: {ProveedorNombre} | OC: {(string.IsNullOrWhiteSpace(OrdenCompraFolio) ? "Directa" : OrdenCompraFolio)} | Proy: {ProyectoNombre} ({Fecha})";
}

public class EntradaPartida : INotifyPropertyChanged
{
    private bool _seleccionadoAccion = true;
    private decimal _cantidadParaSalida;
    private string _ubicacionAlmacen = "Almacén General";

    public int Id { get; set; }
    public int EntradaAlmacenId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public decimal CantidadCompraRecibida { get; set; }
    public string UnidadCompra { get; set; } = "Pieza";
    public decimal FactorConversion { get; set; } = 1m;
    public decimal CantidadIngresadaAlmacen { get; set; }
    public string UnidadAlmacen { get; set; } = "Pieza";
    public decimal CostoUnitarioCompraConIva { get; set; }

    public bool SeleccionadoAccion
    {
        get => _seleccionadoAccion;
        set
        {
            if (_seleccionadoAccion != value)
            {
                _seleccionadoAccion = value;
                if (_seleccionadoAccion && _cantidadParaSalida <= 0m && CantidadIngresadaAlmacen > 0m)
                {
                    _cantidadParaSalida = CantidadIngresadaAlmacen;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadParaSalida)));
                }
                else if (!_seleccionadoAccion && _cantidadParaSalida > 0m)
                {
                    _cantidadParaSalida = 0m;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadParaSalida)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoAccion)));
            }
        }
    }

    public decimal CantidadParaSalida
    {
        get => _cantidadParaSalida;
        set
        {
            decimal ajustado = Math.Clamp(value, 0m, Math.Max(CantidadIngresadaAlmacen, value));
            if (_cantidadParaSalida != ajustado)
            {
                _cantidadParaSalida = ajustado;
                bool debeSel = _cantidadParaSalida > 0m;
                if (_seleccionadoAccion != debeSel)
                {
                    _seleccionadoAccion = debeSel;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoAccion)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadParaSalida)));
            }
        }
    }

    public string UbicacionAlmacen
    {
        get => _ubicacionAlmacen;
        set
        {
            string s = (value ?? string.Empty).Trim();
            int idx = s.IndexOf(" — ", StringComparison.Ordinal);
            if (idx > 0) s = s[..idx].Trim();
            if (_ubicacionAlmacen != s)
            {
                _ubicacionAlmacen = s;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UbicacionAlmacen)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class SalidaAlmacen
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // SAL-2026-0001
    public string Fecha { get; set; } = string.Empty;
    public int DepartamentoId { get; set; }
    public string DepartamentoNombre { get; set; } = string.Empty;
    public int EncargadoId { get; set; }
    public string EncargadoNombre { get; set; } = string.Empty;
    public string PersonaRecibe { get; set; } = string.Empty;
    public string PersonaRecibeEfectiva => !string.IsNullOrWhiteSpace(PersonaRecibe) ? PersonaRecibe : EncargadoNombre;
    public int ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public decimal CostoTotalEstimado { get; set; }
    public string Notas { get; set; } = string.Empty;
    public List<SalidaPartida> Partidas { get; set; } = new();
    public decimal TotalUnidadesEntregadas => Partidas.Count > 0 ? Partidas.Sum(x => x.CantidadEntregada) : 0m;
    public decimal TotalUnidadesDevueltas => Partidas.Count > 0 ? Partidas.Sum(x => x.CantidadDevuelta) : 0m;
    public string EstatusDevolucionTexto
    {
        get
        {
            if (TotalUnidadesDevueltas <= 0m) return "Entregado (Sin devolución)";
            if (TotalUnidadesDevueltas >= TotalUnidadesEntregadas - 0.001m) return "Devuelto Completo";
            return $"Devuelto Parcial ({TotalUnidadesDevueltas:0.##}/{TotalUnidadesEntregadas:0.##})";
        }
    }

    public override string ToString() =>
        $"{Folio} — Recibió: {PersonaRecibeEfectiva} ({DepartamentoNombre}) | Proy: {ProyectoNombre} [{EstatusDevolucionTexto}]";
}

public class SalidaPartida : INotifyPropertyChanged
{
    private decimal _cantidadDevolverAhora;
    private bool _seleccionadoDevolver;

    public int Id { get; set; }
    public int SalidaAlmacenId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public decimal CantidadEntregada { get; set; }
    public decimal CantidadDevuelta { get; set; }
    public decimal CantidadNetaConsumida => Math.Max(0m, CantidadEntregada - CantidadDevuelta);
    public decimal CantidadPendienteDevolver => Math.Max(0m, CantidadEntregada - CantidadDevuelta);
    public bool TienePendientePorDevolver => CantidadPendienteDevolver > 0m;

    public bool SeleccionadoDevolver
    {
        get => _seleccionadoDevolver;
        set
        {
            if (_seleccionadoDevolver != value)
            {
                _seleccionadoDevolver = value;
                if (_seleccionadoDevolver && _cantidadDevolverAhora <= 0m && CantidadPendienteDevolver > 0m)
                {
                    _cantidadDevolverAhora = CantidadPendienteDevolver;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadDevolverAhora)));
                }
                else if (!_seleccionadoDevolver && _cantidadDevolverAhora > 0m)
                {
                    _cantidadDevolverAhora = 0m;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadDevolverAhora)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoDevolver)));
            }
        }
    }

    public decimal CantidadDevolverAhora
    {
        get => _cantidadDevolverAhora;
        set
        {
            decimal ajustado = Math.Clamp(value, 0m, CantidadPendienteDevolver > 0m ? CantidadPendienteDevolver : Math.Max(0m, value));
            if (_cantidadDevolverAhora != ajustado)
            {
                _cantidadDevolverAhora = ajustado;
                bool debeEstarSel = _cantidadDevolverAhora > 0m;
                if (_seleccionadoDevolver != debeEstarSel)
                {
                    _seleccionadoDevolver = debeEstarSel;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoDevolver)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CantidadDevolverAhora)));
            }
        }
    }

    public string UnidadAlmacen { get; set; } = "Pieza";
    public decimal CostoUnitarioAlmacen { get; set; }
    public decimal ImporteCosto => Math.Round(CantidadNetaConsumida * CostoUnitarioAlmacen, 2);

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class PrestamoHerramienta
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // PRE-2026-0001
    public string FechaSalida { get; set; } = string.Empty;
    public string FechaDevolucion { get; set; } = string.Empty;
    public string FechaCompromiso { get; set; } = string.Empty;
    public int DepartamentoId { get; set; }
    public string DepartamentoNombre { get; set; } = string.Empty;
    public int EncargadoId { get; set; }
    public string EncargadoNombre { get; set; } = string.Empty;
    public string PersonaRecibe { get; set; } = string.Empty;
    public string PersonaRecibeEfectiva => !string.IsNullOrWhiteSpace(PersonaRecibe) ? PersonaRecibe : EncargadoNombre;
    public int ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public string Estatus { get; set; } = "Activo"; // Activo, Devuelto Completo, Devuelto con Incidencia
    public string Notas { get; set; } = string.Empty;
    public List<PrestamoPartida> Partidas { get; set; } = new();
    public string ResumenHerramientas => Partidas.Count > 0 ? string.Join(", ", Partidas.Select(p => $"{p.CodigoIndividual} {p.ProductoNombre}")) : Notas;
    public int TotalHerramientas => Partidas.Count;
    public int PendientesDevolver => Partidas.Count(p => string.Equals(p.EstadoDevolucion, "Pendiente", StringComparison.OrdinalIgnoreCase));
    public override string ToString() =>
        $"{Folio} — Recibió: {PersonaRecibeEfectiva} ({DepartamentoNombre}) | Pendientes: {PendientesDevolver}/{TotalHerramientas} [{Estatus}]";
}

public class PrestamoPartida : INotifyPropertyChanged
{
    private bool _seleccionadoDevolver;
    private string _estadoRetornoSeleccionado = "Bueno (Completo)";
    private string _observacionesRetorno = string.Empty;

    public int Id { get; set; }
    public int PrestamoHerramientaId { get; set; }
    public int HerramientaActivoId { get; set; }
    public string CodigoIndividual { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string MarcaModelo { get; set; } = string.Empty;
    public string NumeroSerie { get; set; } = string.Empty;
    public string EstadoSalida { get; set; } = "Bueno";
    public string EstadoDevolucion { get; set; } = "Pendiente"; // Pendiente, Bueno, Dañado, Extraviado
    public string FechaDevuelto { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;

    public bool EstaPendienteDevolucion => string.Equals(EstadoDevolucion, "Pendiente", StringComparison.OrdinalIgnoreCase);

    public bool SeleccionadoDevolver
    {
        get => _seleccionadoDevolver;
        set
        {
            if (_seleccionadoDevolver != value)
            {
                _seleccionadoDevolver = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeleccionadoDevolver)));
            }
        }
    }

    public string EstadoRetornoSeleccionado
    {
        get => _estadoRetornoSeleccionado;
        set
        {
            if (_estadoRetornoSeleccionado != value)
            {
                _estadoRetornoSeleccionado = value ?? "Bueno (Completo)";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstadoRetornoSeleccionado)));
            }
        }
    }

    public string ObservacionesRetorno
    {
        get => _observacionesRetorno;
        set
        {
            if (_observacionesRetorno != value)
            {
                _observacionesRetorno = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ObservacionesRetorno)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class FacturaXml
{
    public int Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string SerieFolio { get; set; } = string.Empty;
    public string FechaTimbrado { get; set; } = string.Empty;
    public string EmisorRfc { get; set; } = string.Empty;
    public string EmisorNombre { get; set; } = string.Empty;
    public string Moneda { get; set; } = "MXN";
    public decimal TipoCambio { get; set; } = 1m;
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public int? OrdenCompraId { get; set; }
    public string OrdenCompraFolio { get; set; } = string.Empty;
    public int? ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public string RutaArchivoXml { get; set; } = string.Empty;
    public bool EsNotaSinFactura =>
        Uuid.StartsWith("NOTA-", StringComparison.OrdinalIgnoreCase) ||
        Uuid.StartsWith("SIN-FACTURA", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(RutaArchivoXml, "SIN_FACTURA", StringComparison.OrdinalIgnoreCase);
    public string TipoComprobante => EsNotaSinFactura ? "Nota / Sin Factura" : "Factura CFDI XML";
    public List<FacturaConcepto> Conceptos { get; set; } = new();
}

public class FacturaConcepto
{
    public int Id { get; set; }
    public int FacturaXmlId { get; set; }
    public string ClaveProdServ { get; set; } = string.Empty;
    public string NoIdentificacion { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal Importe { get; set; }
    public decimal IvaConcepto { get; set; }
    public decimal TotalConIva => Math.Round(Importe + IvaConcepto, 2);
    public int? ProductoIdVinculado { get; set; }
    public string ProductoVinculadoNombre { get; set; } = string.Empty;
}

public class EventoCalendario
{
    public int Id { get; set; }
    public string Fecha { get; set; } = string.Empty; // yyyy-MM-dd
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = "Recordatorio"; // Recordatorio, Entrega OC, Herramienta, Vencimiento Pago, Inventario, Cotización por Vencer
    public string Prioridad { get; set; } = "Media";        // Alta, Media, Baja
    public bool Completado { get; set; }
    public bool Oculto { get; set; }
    public string Notas { get; set; } = string.Empty;
    public bool EsAutomatico { get; set; }
    public bool EsManual => !EsAutomatico;
    public string OrigenFolio { get; set; } = string.Empty;
    public string ReferenciaFolio
    {
        get => !string.IsNullOrWhiteSpace(OrigenFolio) ? OrigenFolio : string.Empty;
        set => OrigenFolio = value;
    }
    public string ClaveUnicaAutomatico => $"AUTO:{Categoria}:{ReferenciaFolio}";
    public string EstadoTiempoTexto { get; set; } = string.Empty;
    public string EstatusMostrar => Completado ? "Completado" : EstadoTiempoTexto;
    public string EstatusTexto => EstatusMostrar;
    public string TextoBotonCompletar => Completado ? "Reabrir" : "✓ Listo";
    public bool TieneDocumentoVinculado =>
        !string.IsNullOrWhiteSpace(ReferenciaFolio) &&
        (ReferenciaFolio.StartsWith("OC-", StringComparison.OrdinalIgnoreCase) ||
         ReferenciaFolio.StartsWith("SOL-", StringComparison.OrdinalIgnoreCase) ||
         ReferenciaFolio.StartsWith("PRE-", StringComparison.OrdinalIgnoreCase) ||
         ReferenciaFolio.StartsWith("ENT-", StringComparison.OrdinalIgnoreCase) ||
         ReferenciaFolio.StartsWith("PRD-", StringComparison.OrdinalIgnoreCase));
}

public class PendienteOperativoCompra
{
    public string Tipo { get; set; } = string.Empty;        // Solicitud, Factura XML, Cotización por Vencer
    public string Folio { get; set; } = string.Empty;       // SOL-2026-0001, ENT-2026-0001, COT-...
    public string Fecha { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public string TextoAccionPrincipal { get; set; } = "Atender";
    public int EntidadId { get; set; }
}

public class PuntoHistorialCompraProducto
{
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Now;
    public string FechaTexto { get; set; } = string.Empty;
    public DateTime FechaOrden { get; set; }
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;
    public string FolioDocumento { get; set; } = string.Empty;
    public string TipoDocumento { get; set; } = "OC";
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "Pieza";
    public decimal PrecioUnitarioSinIva { get; set; }
    public decimal PrecioUnitarioConIva => Math.Round(PrecioUnitarioSinIva * 1.16m, 2);
    public decimal ImporteSinIva => Math.Round(Cantidad * PrecioUnitarioSinIva, 2);
    public decimal ImporteConIva => Math.Round(ImporteSinIva * 1.16m, 2);
}

public class PartidaCompraSinFactura
{
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string UnidadCompra { get; set; } = "Pieza";
    public string Unidad => UnidadCompra;
    public decimal Cantidad { get; set; } = 1m;
    public decimal PrecioUnitarioSinIva { get; set; }
    public decimal PrecioUnitario
    {
        get => PrecioUnitarioSinIva;
        set => PrecioUnitarioSinIva = value;
    }
    public decimal PrecioUnitarioConIva => Math.Round(PrecioUnitarioSinIva * 1.16m, 2);
    public decimal ImporteSubtotal => Math.Round(Cantidad * PrecioUnitarioSinIva, 2);
    public decimal ImporteSinIva => ImporteSubtotal;
    public decimal Importe => ImporteSubtotal;
    public decimal ImporteConIva => Math.Round(ImporteSubtotal * 1.16m, 2);
}

public class ReferenciaSimilarHistorica
{
    public int IdBancoPrecio { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;
    public decimal PrecioSinIva { get; set; }
    public decimal PrecioConIva => Math.Round(PrecioSinIva * 1.16m, 2);
    public int DiasEntrega { get; set; } = 2;
    public int DiasVigencia { get; set; } = 15;
    public string FechaCotizacion { get; set; } = string.Empty;
    public string NotasCotizacion { get; set; } = string.Empty;
    public string ReferenciaOrigen { get; set; } = string.Empty;
    public string EstadoVigenciaCorto => !string.IsNullOrWhiteSpace(FechaCotizacion)
        ? CotizacionRenglonComparativo.FormatearVigencia(FechaCotizacion, DiasVigencia)
        : "Compra previa";
    public string EtiquetaCombo => $"[{EstadoVigenciaCorto}] {ProveedorNombre} | ${PrecioSinIva:N2} s/IVA (${PrecioConIva:N2} c/IVA) — [{ProductoCodigo}] {ProductoNombre} ({ReferenciaOrigen})";
    public override string ToString() => EtiquetaCombo;
}

public class RegistroCotizacionVigencia
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string DescripcionConcepto { get; set; } = string.Empty;
    public string Familia { get; set; } = string.Empty;
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;
    public decimal PrecioSinIva { get; set; }
    public decimal PrecioConIva => Math.Round(PrecioSinIva * 1.16m, 2);
    public int DiasEntrega { get; set; } = 2;
    public int DiasVigencia { get; set; } = 15;
    public string FechaCotizacion { get; set; } = string.Empty;
    public string OrigenFolio { get; set; } = string.Empty;
    public string NotasCotizacion { get; set; } = string.Empty;
    public bool FueGanador { get; set; }
    public int SolicitudPartidaId { get; set; }
    public bool EsLineaAbierta => ProductoId <= 0;
    public string TipoPartidaTexto => EsLineaAbierta ? "Línea Abierta (Sin SKU)" : $"SKU [{ProductoCodigo}]";
    public string EstadoVigenciaTexto => CotizacionRenglonComparativo.FormatearVigencia(FechaCotizacion, DiasVigencia, true);
    public string EstadoVigenciaCorto => CotizacionRenglonComparativo.FormatearVigencia(FechaCotizacion, DiasVigencia, false);
    public bool EstaVigente
    {
        get
        {
            if (DateTime.TryParse(FechaCotizacion, out DateTime dt))
                return (dt.Date.AddDays(DiasVigencia > 0 ? DiasVigencia : 15) - DateTime.Today).Days >= 0;
            return true;
        }
    }
}

public class MaterialRankingAnalisis
{
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string CategoriaNombre { get; set; } = string.Empty;
    public string UnidadAlmacen { get; set; } = "Pieza";
    public int FrecuenciaSolicitudes { get; set; }
    public decimal CantidadTotalSolicitada { get; set; }
    public int FrecuenciaSalidas { get; set; }
    public decimal CantidadTotalConsumida { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal UltimoPrecioConIva { get; set; }
    public string UltimoProveedorNombre { get; set; } = "Sin registro";
    public string FechaUltimaCotizacion { get; set; } = string.Empty;
    public string VigenciaPrecioTexto { get; set; } = "Sin cotización reciente";
    public decimal SugerenciaCompra => Math.Max(0m, Math.Round((CantidadTotalConsumida > 0 ? CantidadTotalConsumida * 0.35m : StockMinimo * 1.5m) - StockActual, 0));
    public string PrioridadAbasto
    {
        get
        {
            if (StockMinimo > StockActual && StockActual <= 0)
                return "🔴 Crítica (Agotado / Bajo Mín.)";
            if (StockMinimo > StockActual)
                return "🟠 Alta (Stock Mín. > Actual)";
            if (StockActual <= 0 && (FrecuenciaSolicitudes >= 2 || FrecuenciaSalidas >= 2))
                return "Alta (Alta rotación sin stock)";
            if (VigenciaPrecioTexto.StartsWith("Vencid", StringComparison.OrdinalIgnoreCase))
                return "Actualizar Precio";
            return "Normal";
        }
    }
}

public class NotificacionCheckpoint
{
    public int Id { get; set; }
    public string FechaHora { get; set; } = string.Empty;
    public string Etapa { get; set; } = string.Empty;            // Ej. Etapa 1: Solicitud, Etapa 4: Recepción
    public string CheckpointCodigo { get; set; } = string.Empty; // Ej. CP-1A, CP-1B, CP-2B, CP-5B, MSG
    public string FolioReferencia { get; set; } = string.Empty;  // Ej. SOL-2026-0001, OC-2026-0001
    public string EstatusAnterior { get; set; } = string.Empty;
    public string EstatusNuevo { get; set; } = string.Empty;
    public string RolOrigen { get; set; } = "Compras";           // Compras, Almacén
    public string RolesDestino { get; set; } = "Almacén";        // Compras, Almacén, Solicitante, Dirección, Finanzas
    public string UsuarioOrigen { get; set; } = string.Empty;
    public string UsuarioDestino { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public bool Leida { get; set; }
    public string RemitenteTexto => !string.IsNullOrWhiteSpace(UsuarioOrigen)
        ? $"{UsuarioOrigen} ({RolOrigen})"
        : RolOrigen;
    public string DestinatarioTexto => !string.IsNullOrWhiteSpace(UsuarioDestino)
        ? $"@{UsuarioDestino} ({RolesDestino})"
        : RolesDestino;
    public string TransicionTexto => string.IsNullOrWhiteSpace(EstatusAnterior)
        ? EstatusNuevo
        : $"{EstatusAnterior} → {EstatusNuevo}";
}

public class UsuarioSistema
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Nombre
    {
        get => NombreCompleto;
        set => NombreCompleto = value;
    }
    public string Puesto { get; set; } = string.Empty;
    public string Rol { get; set; } = "Responsable de Compras"; // Responsable de Compras, Auxiliar de Compras, Auxiliar de Almacén, Dirección, Finanzas, Solicitante
    public string CorreoErp { get; set; } = string.Empty;
    public string RolPredeterminado
    {
        get => VistaOperativaBase;
        set => Rol = value;
    }
    public bool Activo { get; set; } = true;
    public string FechaAlta { get; set; } = string.Empty;
    public string EstadoTexto => Activo ? "Activo" : "Inactivo";
    public string CorreoErpMostrar => string.IsNullOrWhiteSpace(CorreoErp) ? "Solo local" : CorreoErp;

    public string TipoUsuarioNormalizado => NormalizarTipoUsuario(Rol, Username, Puesto);

    public string VistaOperativaBase => TipoUsuarioNormalizado switch
    {
        "Auxiliar de Almacén" => "Almacén",
        "Solicitante" => "Almacén",
        _ => "Compras"
    };

    public static string NormalizarTipoUsuario(string? rolRaw, string? username = null, string? puesto = null)
    {
        string r = (rolRaw ?? string.Empty).Trim();
        string u = (username ?? string.Empty).Trim().ToLowerInvariant();
        string p = (puesto ?? string.Empty).Trim();

        if (r.Equals("Responsable de Compras", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Administrador", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return "Responsable de Compras";

        if (r.Equals("Auxiliar de Compras", StringComparison.OrdinalIgnoreCase))
            return "Auxiliar de Compras";

        if (r.Equals("Auxiliar de Almacén", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Auxiliar de Almacen", StringComparison.OrdinalIgnoreCase))
            return "Auxiliar de Almacén";

        if (r.Equals("Dirección", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Direccion", StringComparison.OrdinalIgnoreCase))
            return "Dirección";

        if (r.Equals("Finanzas", StringComparison.OrdinalIgnoreCase))
            return "Finanzas";

        if (r.Equals("Solicitante", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Operativo", StringComparison.OrdinalIgnoreCase))
            return "Solicitante";

        // Compatibilidad con registros heredados ("Compras" / "Almacén")
        if (r.Equals("Almacén", StringComparison.OrdinalIgnoreCase) || r.Equals("Almacen", StringComparison.OrdinalIgnoreCase))
        {
            if (u == "operativo" || p.Contains("Cuadrilla", StringComparison.OrdinalIgnoreCase) || p.Contains("Solicitante", StringComparison.OrdinalIgnoreCase))
                return "Solicitante";
            return "Auxiliar de Almacén";
        }

        if (u == "direccion" || p.Contains("Dirección", StringComparison.OrdinalIgnoreCase) || p.Contains("Direccion", StringComparison.OrdinalIgnoreCase))
            return "Dirección";
        if (u == "finanzas" || p.Contains("Finanzas", StringComparison.OrdinalIgnoreCase) || p.Contains("Tesorer", StringComparison.OrdinalIgnoreCase))
            return "Finanzas";
        if (u == "oscar" || p.Contains("Responsable", StringComparison.OrdinalIgnoreCase) || p.Contains("Jefe", StringComparison.OrdinalIgnoreCase) || p.Contains("Admin", StringComparison.OrdinalIgnoreCase))
            return "Responsable de Compras";

        return "Auxiliar de Compras";
    }

    public override string ToString() => $"{NombreCompleto} (@{Username} · {TipoUsuarioNormalizado})";
}

public class RegistroBitacoraUsuario
{
    public int Id { get; set; }
    public string FechaHora { get; set; } = string.Empty;
    public int UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string RolOperativo => Rol;
    public string Modulo { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string FolioReferencia { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}

public class ResumenFamiliaCatalogo
{
    public string Familia { get; set; } = string.Empty;
    public string CategoriaNombre { get; set; } = string.Empty;
    public int CantidadMarcasOpciones { get; set; }
    public decimal StockTotalFamilia { get; set; }
    public string UnidadComun { get; set; } = "Pieza";
    public string UnidadBase => UnidadComun;
    public decimal MejorCostoConIva { get; set; }
    public string MarcasDisponiblesResumen { get; set; } = string.Empty;
    public string MarcasResumen => MarcasDisponiblesResumen;
    public Producto? ProductoSugerido { get; set; }
    public int ProductoSugeridoId => ProductoSugerido?.Id ?? 0;
    public List<Producto> ProductosMiembros { get; set; } = new();
    public override string ToString() => $"[FAMILIA] {Familia} ({CantidadMarcasOpciones} opciones · Stock: {StockTotalFamilia:0.##} {UnidadComun})";
}

public class KitHerramienta
{
    public int Id { get; set; }
    public string CodigoKit { get; set; } = string.Empty; // Ej. KIT-0001
    public string Nombre { get; set; } = string.Empty;    // Ej. Maletín Eléctrico Cuadrilla 1
    public string Especialidad { get; set; } = "General";
    public string Ubicacion { get; set; } = "GABINETE-1";
    public string ResponsableActual { get; set; } = string.Empty;
    public string ProyectoActual { get; set; } = string.Empty;
    public string UltimoValePrestamoFolio { get; set; } = string.Empty;
    public string Estatus { get; set; } = "Disponible";   // Disponible, En Obra / Prestado, Incompleto / Con Incidencia
    public string Notas { get; set; } = string.Empty;
    public List<KitHerramientaPartida> Partidas { get; set; } = new();

    public int TotalHerramientas => Partidas.Count;
    public int DisponiblesCount => Partidas.Count(p => string.Equals(p.EstadoActual, "Disponible", StringComparison.OrdinalIgnoreCase));
    public int EnPrestamoCount => Partidas.Count(p => string.Equals(p.EstadoActual, "En Préstamo", StringComparison.OrdinalIgnoreCase));
    public string ResumenHerramientas => Partidas.Count == 0
        ? "Sin herramientas asignadas"
        : string.Join(", ", Partidas.Select(p => $"{p.CodigoIndividual} ({p.HerramientaNombre})"));
    public string DisponibilidadTexto => $"{DisponiblesCount}/{TotalHerramientas} disp.";
    public string ResguardoMostrar => string.IsNullOrWhiteSpace(ResponsableActual)
        ? $"En Almacén ({Ubicacion})"
        : $"{ResponsableActual}" + (!string.IsNullOrWhiteSpace(ProyectoActual) ? $" · {ProyectoActual}" : "");

    public override string ToString() => $"{CodigoKit} — {Nombre} ({DisponiblesCount}/{TotalHerramientas} disp.)";
}

public class KitHerramientaPartida : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int KitId { get; set; }
    public int HerramientaActivoId { get; set; }
    public string CodigoIndividual { get; set; } = string.Empty;
    public string HerramientaNombre { get; set; } = string.Empty;
    public string MarcaModelo { get; set; } = string.Empty;
    public string NumeroSerie { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string EstadoActual { get; set; } = "Disponible";
    public string EncargadoActual { get; set; } = string.Empty;

    private bool _regresoOk = true;
    public bool RegresoOk
    {
        get => _regresoOk;
        set
        {
            if (_regresoOk == value) return;
            _regresoOk = value;
            if (_regresoOk && _estadoRetornoElegido != "Bueno (Completo)")
                _estadoRetornoElegido = "Bueno (Completo)";
            else if (!_regresoOk && _estadoRetornoElegido == "Bueno (Completo)")
                _estadoRetornoElegido = "Se queda en obra (Continúa préstamo)";
            OnPropertyChanged();
            OnPropertyChanged(nameof(EstadoRetornoElegido));
        }
    }

    private string _estadoRetornoElegido = "Bueno (Completo)";
    public string EstadoRetornoElegido
    {
        get => _estadoRetornoElegido;
        set
        {
            if (_estadoRetornoElegido == value) return;
            _estadoRetornoElegido = value ?? "Bueno (Completo)";
            _regresoOk = string.Equals(_estadoRetornoElegido, "Bueno (Completo)", StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RegresoOk));
        }
    }

    private string _observacionesRetorno = string.Empty;
    public string ObservacionesRetorno
    {
        get => _observacionesRetorno;
        set
        {
            if (_observacionesRetorno == value) return;
            _observacionesRetorno = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VehiculoFlotilla
{
    public int Id { get; set; }
    public string CodigoVehiculo { get; set; } = string.Empty; // Ej. VEH-0001
    public string Placas { get; set; } = string.Empty;
    public string MarcaModelo { get; set; } = string.Empty;    // Ej. Nissan NP300 Estacas 2023
    public int Anio { get; set; } = 2024;
    public string ResponsableHabitual { get; set; } = string.Empty;
    public int KilometrajeInicial { get; set; }
    public int KilometrajeActual { get; set; }
    public int ProximoServicioKm { get; set; }
    public int IntervaloServicioKm { get; set; } = 10000;      // Cada cuántos km se configura el servicio preventivo
    public int UltimoServicioKm { get; set; }                  // Odómetro del último mantenimiento realizado
    public decimal CapacidadTanqueLitros { get; set; } = 60m;
    public string TipoCombustible { get; set; } = "Gasolina Regular";
    public string VigenciaSeguro { get; set; } = string.Empty; // yyyy-MM-dd
    public string Estatus { get; set; } = "Activo en Operación"; // Activo en Operación, En Taller / Servicio, Fuera de Servicio
    public string Notas { get; set; } = string.Empty;

    public decimal GastoCombustibleAcumulado { get; set; }
    public decimal LitrosCombustibleAcumulados { get; set; }
    public decimal GastoMantenimientoAcumulado { get; set; }
    public decimal GastoOtrosAcumulado { get; set; }
    public decimal GastoTotalAcumulado => GastoCombustibleAcumulado + GastoMantenimientoAcumulado + GastoOtrosAcumulado;
    public List<RegistroGastoVehiculo> HistorialGastos { get; set; } = new();
    public List<RevisionSemanalVehiculo> HistorialRevisiones { get; set; } = new();

    public int KilometrosRecorridosPeriodo => Math.Max(0, KilometrajeActual - KilometrajeInicial);

    public decimal CostoPorKilometro =>
        KilometrosRecorridosPeriodo > 0
            ? Math.Round(GastoTotalAcumulado / KilometrosRecorridosPeriodo, 2)
            : 0m;

    public string CostoPorKilometroTexto =>
        KilometrosRecorridosPeriodo > 0 && GastoTotalAcumulado > 0m
            ? $"${CostoPorKilometro:0.00} / km"
            : "S/D";

    public decimal RendimientoKmPorLitro =>
        LitrosCombustibleAcumulados > 0 && KilometrosRecorridosPeriodo > 0
            ? Math.Round(KilometrosRecorridosPeriodo / LitrosCombustibleAcumulados, 2)
            : 0m;

    public string RendimientoTexto =>
        RendimientoKmPorLitro > 0
            ? $"{RendimientoKmPorLitro:0.0} km/L ({LitrosCombustibleAcumulados:0.#} L)"
            : (LitrosCombustibleAcumulados > 0 ? $"{LitrosCombustibleAcumulados:0.#} L cargados" : "Sin cargas L");

    public int IntervaloEfectivoKm => IntervaloServicioKm > 0 ? IntervaloServicioKm : 10000;

    public int MetaServicioCalculadaKm =>
        ProximoServicioKm > 0
            ? ProximoServicioKm
            : (UltimoServicioKm > 0 ? UltimoServicioKm + IntervaloEfectivoKm : KilometrajeInicial + IntervaloEfectivoKm);

    public int KmRestantesServicio => MetaServicioCalculadaKm - KilometrajeActual;

    public decimal PromedioKmPorDia
    {
        get
        {
            var puntos = new List<(DateTime fecha, int km)>();
            foreach (var g in HistorialGastos.Where(x => x.KilometrajeRegistro > 0))
            {
                if (DateTime.TryParse(g.Fecha, out var dt))
                    puntos.Add((dt.Date, g.KilometrajeRegistro));
            }
            foreach (var rev in HistorialRevisiones.Where(x => x.KilometrajeRegistrado > 0))
            {
                if (DateTime.TryParse(rev.Fecha, out var dt))
                    puntos.Add((dt.Date, rev.KilometrajeRegistrado));
            }
            if (puntos.Count >= 2)
            {
                var minP = puntos.OrderBy(p => p.fecha).ThenBy(p => p.km).First();
                var maxP = puntos.OrderByDescending(p => p.fecha).ThenByDescending(p => p.km).First();
                double dias = Math.Max(1.0, (maxP.fecha - minP.fecha).TotalDays);
                int deltaKm = maxP.km - minP.km;
                if (deltaKm > 0)
                    return Math.Round((decimal)(deltaKm / dias), 1);
            }
            if (KilometrosRecorridosPeriodo > 0)
                return Math.Round(KilometrosRecorridosPeriodo / 30m, 1);
            return 45m; // Ritmo estándar de obra por defecto (45 km/día)
        }
    }

    public string EstimacionProximoServicioFechaTexto
    {
        get
        {
            int rest = KmRestantesServicio;
            if (rest <= 0)
                return $"Vencido por {Math.Abs(rest):N0} km (Atender ya)";
            decimal ritmoDia = Math.Max(10m, PromedioKmPorDia);
            int diasEst = (int)Math.Ceiling(rest / ritmoDia);
            DateTime fechaEst = DateTime.Today.AddDays(Math.Min(diasEst, 730));
            return $"~{fechaEst:dd/MMM/yyyy} (en ~{diasEst} días a {ritmoDia:0.#} km/día)";
        }
    }

    public bool RequiereServicioPronto =>
        MetaServicioCalculadaKm > 0 && KilometrajeActual >= (MetaServicioCalculadaKm - 800);

    public string AlertaServicioTexto
    {
        get
        {
            int meta = MetaServicioCalculadaKm;
            if (meta <= 0) return "Sin programar";
            int rest = meta - KilometrajeActual;
            return rest <= 0
                ? $"VENCIDO por {Math.Abs(rest):N0} km (Meta: {meta:N0} km)"
                : (rest <= 800 ? $"Próximo en {rest:N0} km ({meta:N0} km)" : $"En {rest:N0} km (Meta: {meta:N0} km · c/{IntervaloEfectivoKm:N0})");
        }
    }

    public override string ToString() => $"{CodigoVehiculo} — {MarcaModelo} ({Placas})";
}

public class RegistroGastoVehiculo
{
    public int Id { get; set; }
    public int VehiculoId { get; set; }
    public string CodigoVehiculo { get; set; } = string.Empty;
    public string VehiculoDescripcion { get; set; } = string.Empty;
    public string Fecha { get; set; } = string.Empty;
    public string TipoGasto { get; set; } = "Gasolina / Combustible"; // Gasolina / Combustible, Mantenimiento Preventivo, Reparación / Taller, Llantas / Refacciones, Seguro / Peajes
    public int KilometrajeRegistro { get; set; }
    public decimal LitrosCargados { get; set; }
    public decimal PrecioPorLitro { get; set; }
    public bool TanqueLleno { get; set; } = true;
    public decimal MontoConIva { get; set; }
    public string ProveedorTaller { get; set; } = string.Empty;
    public string FolioReferencia { get; set; } = string.Empty; // Factura XML, OC o Nota
    public string ProyectoNombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    public string LitrosYPrecioTexto =>
        LitrosCargados > 0
            ? $"{LitrosCargados:0.##} L (@ ${PrecioPorLitro:0.00}/L)"
            : "-";
}

public class RevisionSemanalVehiculo
{
    public int Id { get; set; }
    public int VehiculoId { get; set; }
    public string CodigoVehiculo { get; set; } = string.Empty;
    public string VehiculoDescripcion { get; set; } = string.Empty;
    public string Placas { get; set; } = string.Empty;
    public string FolioRevision { get; set; } = string.Empty; // Ej. REV-0001
    public string Fecha { get; set; } = string.Empty;
    public string ResponsableInspector { get; set; } = string.Empty;
    public int KilometrajeRegistrado { get; set; }
    public string NivelCombustible { get; set; } = "3/4 Tanque";
    public string EstadoLlantas { get; set; } = "OK";
    public string EstadoNivelesFluidos { get; set; } = "OK";
    public string EstadoFrenosLuces { get; set; } = "OK";
    public string EstadoCarroceriaLimpieza { get; set; } = "OK";
    public string Observaciones { get; set; } = string.Empty;

    public string ResumenEstadoFisico =>
        $"Llantas: {EstadoLlantas} | Fluidos: {EstadoNivelesFluidos} | Frenos/Luces: {EstadoFrenosLuces} | Carrocería: {EstadoCarroceriaLimpieza}";
}

public class RenglonHojaCotizacionProveedor : INotifyPropertyChanged
{
    public CotizacionRenglonComparativo RenglonBase { get; }
    public int IndiceOpcion { get; } // 1, 2, 3 o 4+
    public int ProveedorHojaId { get; set; }
    public string ProveedorHojaNombre { get; set; } = string.Empty;
    public Action<RenglonHojaCotizacionProveedor>? AlModificar { get; set; }

    public RenglonHojaCotizacionProveedor(CotizacionRenglonComparativo baseRow, int indiceOpcion, Action<RenglonHojaCotizacionProveedor>? onModify = null, int proveedorHojaId = 0, string proveedorHojaNombre = "")
    {
        RenglonBase = baseRow;
        IndiceOpcion = Math.Max(1, indiceOpcion);
        ProveedorHojaId = proveedorHojaId;
        ProveedorHojaNombre = proveedorHojaNombre ?? string.Empty;
        AlModificar = onModify;
    }

    public int ObtenerSlotEfectivoLectura()
    {
        if (ProveedorHojaId > 0)
        {
            if (RenglonBase.Proveedor1Id == ProveedorHojaId) return 1;
            if (RenglonBase.Proveedor2Id == ProveedorHojaId) return 2;
            if (RenglonBase.Proveedor3Id == ProveedorHojaId) return 3;
            var ex = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.ProveedorId == ProveedorHojaId);
            if (ex != null) return ex.NumeroOpcion;
            return 0;
        }

        if (IndiceOpcion == 1 && RenglonBase.Proveedor1Id <= 0) return 1;
        if (IndiceOpcion == 2 && RenglonBase.Proveedor2Id <= 0) return 2;
        if (IndiceOpcion == 3 && RenglonBase.Proveedor3Id <= 0) return 3;
        return 0;
    }

    public bool ConservarProveedoresAsignados { get; init; }

    private bool SlotDisponible(int proveedorId, decimal precio) =>
        proveedorId <= 0 || (!ConservarProveedoresAsignados && precio <= 0);

    public int AsegurarSlotParaEscritura()
    {
        int existente = ObtenerSlotEfectivoLectura();
        if (existente > 0)
        {
            if (existente <= 3 && RenglonBase.NumeroOpcionesVisibles < existente)
                RenglonBase.NumeroOpcionesVisibles = existente;
            return existente;
        }

        string nomProv = string.IsNullOrWhiteSpace(ProveedorHojaNombre) ? "Sin asignar" : ProveedorHojaNombre;

        if (ProveedorHojaId > 0)
        {
            // 1. Preferir el slot de esta columna si está libre o sin precio
            if (IndiceOpcion == 1 && SlotDisponible(RenglonBase.Proveedor1Id, RenglonBase.PrecioSinIva1))
            {
                RenglonBase.Proveedor1Id = ProveedorHojaId;
                RenglonBase.Proveedor1Nombre = nomProv;
                return 1;
            }
            if (IndiceOpcion == 2 && SlotDisponible(RenglonBase.Proveedor2Id, RenglonBase.PrecioSinIva2))
            {
                RenglonBase.Proveedor2Id = ProveedorHojaId;
                RenglonBase.Proveedor2Nombre = nomProv;
                if (RenglonBase.NumeroOpcionesVisibles < 2) RenglonBase.NumeroOpcionesVisibles = 2;
                return 2;
            }
            if (IndiceOpcion == 3 && SlotDisponible(RenglonBase.Proveedor3Id, RenglonBase.PrecioSinIva3))
            {
                RenglonBase.Proveedor3Id = ProveedorHojaId;
                RenglonBase.Proveedor3Nombre = nomProv;
                if (RenglonBase.NumeroOpcionesVisibles < 3) RenglonBase.NumeroOpcionesVisibles = 3;
                return 3;
            }

            // 2. Si el slot preferido ya tiene precio de otro proveedor (o es slot 4+), buscar cualquier slot 1..3 vacío o sin precio
            if (SlotDisponible(RenglonBase.Proveedor1Id, RenglonBase.PrecioSinIva1))
            {
                RenglonBase.Proveedor1Id = ProveedorHojaId;
                RenglonBase.Proveedor1Nombre = nomProv;
                return 1;
            }
            if (SlotDisponible(RenglonBase.Proveedor2Id, RenglonBase.PrecioSinIva2))
            {
                RenglonBase.Proveedor2Id = ProveedorHojaId;
                RenglonBase.Proveedor2Nombre = nomProv;
                if (RenglonBase.NumeroOpcionesVisibles < 2) RenglonBase.NumeroOpcionesVisibles = 2;
                return 2;
            }
            if (SlotDisponible(RenglonBase.Proveedor3Id, RenglonBase.PrecioSinIva3))
            {
                RenglonBase.Proveedor3Id = ProveedorHojaId;
                RenglonBase.Proveedor3Nombre = nomProv;
                if (RenglonBase.NumeroOpcionesVisibles < 3) RenglonBase.NumeroOpcionesVisibles = 3;
                return 3;
            }

            // 3. Si los 3 slots principales ya tienen cotizaciones activas de otros proveedores, crear CotizacionProveedorExtra (Opción 4+)
            int numOp = RenglonBase.ProveedoresExtra.Count > 0
                ? Math.Max(4, RenglonBase.ProveedoresExtra.Max(x => x.NumeroOpcion) + 1)
                : 4;
            var nuevoEx = new CotizacionProveedorExtra
            {
                SolicitudPartidaId = RenglonBase.SolicitudPartidaId,
                NumeroOpcion = numOp,
                ProveedorId = ProveedorHojaId,
                ProveedorNombre = nomProv,
                DiasEntrega = 2,
                DiasVigencia = 15,
                FechaCotizacion = DateTime.Today.ToString("yyyy-MM-dd")
            };
            RenglonBase.ProveedoresExtra.Add(nuevoEx);
            if (RenglonBase.NumeroOpcionesVisibles < 3) RenglonBase.NumeroOpcionesVisibles = 3;
            return numOp;
        }

        int slotFallback = Math.Clamp(IndiceOpcion, 1, 3);
        if (RenglonBase.NumeroOpcionesVisibles < slotFallback)
            RenglonBase.NumeroOpcionesVisibles = slotFallback;
        return slotFallback;
    }

    public int SolicitudPartidaId => RenglonBase.SolicitudPartidaId;
    public int ProductoId => RenglonBase.ProductoId;
    public string ProductoCodigo => RenglonBase.ProductoCodigo;
    public string ProductoNombre => RenglonBase.ProductoNombre;
    public string UnidadCompra
    {
        get => RenglonBase.UnidadCompra;
        set
        {
            string val = string.IsNullOrWhiteSpace(value) ? "Pieza" : value.Trim();
            if (!string.Equals(RenglonBase.UnidadCompra, val, StringComparison.Ordinal))
            {
                RenglonBase.UnidadCompra = val;
                NotificarTodo();
                AlModificar?.Invoke(this);
            }
        }
    }
    public decimal Cantidad => RenglonBase.Cantidad;
    public decimal StockActualAlmacen => RenglonBase.StockActualAlmacen;
    public string ReferenciaHistoricaCorta => RenglonBase.UltimoCostoCompraConIva > 0
        ? $"Últ.: {RenglonBase.UltimoCostoCompraConIva:C2} c/IVA"
        : (!string.IsNullOrWhiteSpace(RenglonBase.ReferenciaHistorica) && !RenglonBase.ReferenciaHistorica.StartsWith("Sin compras", StringComparison.OrdinalIgnoreCase)
            ? RenglonBase.ReferenciaHistorica
            : "Sin compra previa");

    public decimal PrecioSinIva
    {
        get
        {
            int slot = ObtenerSlotEfectivoLectura();
            if (slot == 1) return RenglonBase.PrecioSinIva1;
            if (slot == 2) return RenglonBase.PrecioSinIva2;
            if (slot == 3) return RenglonBase.PrecioSinIva3;
            if (slot >= 4) return RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot)?.PrecioSinIva ?? 0m;
            return 0m;
        }
        set
        {
            decimal val = Math.Max(0m, Math.Round(value, 2));
            if (val <= 0m && ObtenerSlotEfectivoLectura() == 0)
                return;

            int slot = AsegurarSlotParaEscritura();
            string hoy = DateTime.Today.ToString("yyyy-MM-dd");
            if (slot == 1)
            {
                RenglonBase.PrecioSinIva1 = val;
                if (val > 0 && string.IsNullOrWhiteSpace(RenglonBase.FechaCotizacion1)) RenglonBase.FechaCotizacion1 = hoy;
            }
            else if (slot == 2)
            {
                RenglonBase.PrecioSinIva2 = val;
                if (val > 0 && string.IsNullOrWhiteSpace(RenglonBase.FechaCotizacion2)) RenglonBase.FechaCotizacion2 = hoy;
            }
            else if (slot == 3)
            {
                RenglonBase.PrecioSinIva3 = val;
                if (val > 0 && string.IsNullOrWhiteSpace(RenglonBase.FechaCotizacion3)) RenglonBase.FechaCotizacion3 = hoy;
            }
            else if (slot >= 4)
            {
                var ex = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null)
                {
                    ex.PrecioSinIva = val;
                    if (val > 0 && string.IsNullOrWhiteSpace(ex.FechaCotizacion)) ex.FechaCotizacion = hoy;
                }
            }
            NotificarTodo();
            AlModificar?.Invoke(this);
        }
    }

    public decimal PrecioConIva
    {
        get => Math.Round(PrecioSinIva * 1.16m, 2);
        set
        {
            decimal sinIva = Math.Max(0m, Math.Round(value / 1.16m, 2));
            PrecioSinIva = sinIva;
        }
    }

    public decimal ImporteSubtotalSinIva => Math.Round(Cantidad * PrecioSinIva, 2);
    public decimal ImporteTotalConIva => Math.Round(ImporteSubtotalSinIva * 1.16m + FleteConIva, 2);

    public decimal FleteConIva
    {
        get
        {
            int slot = ObtenerSlotEfectivoLectura();
            if (slot == 1) return RenglonBase.FleteConIva1;
            if (slot == 2) return RenglonBase.FleteConIva2;
            if (slot == 3) return RenglonBase.FleteConIva3;
            if (slot >= 4) return RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot)?.FleteConIva ?? 0m;
            return 0m;
        }
        set
        {
            decimal val = Math.Max(0m, Math.Round(value, 2));
            if (val <= 0m && ObtenerSlotEfectivoLectura() == 0)
                return;

            int slot = AsegurarSlotParaEscritura();
            if (slot == 1) RenglonBase.FleteConIva1 = val;
            else if (slot == 2) RenglonBase.FleteConIva2 = val;
            else if (slot == 3) RenglonBase.FleteConIva3 = val;
            else if (slot >= 4)
            {
                var ex = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null) ex.FleteConIva = val;
            }
            NotificarTodo();
            AlModificar?.Invoke(this);
        }
    }

    public int DiasEntrega
    {
        get
        {
            int slot = ObtenerSlotEfectivoLectura();
            if (slot == 1) return RenglonBase.DiasEntrega1;
            if (slot == 2) return RenglonBase.DiasEntrega2;
            if (slot == 3) return RenglonBase.DiasEntrega3;
            if (slot >= 4) return RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot)?.DiasEntrega ?? 2;
            return 2;
        }
        set
        {
            int val = Math.Max(1, value);
            int slot = AsegurarSlotParaEscritura();
            if (slot == 1) RenglonBase.DiasEntrega1 = val;
            else if (slot == 2) RenglonBase.DiasEntrega2 = val;
            else if (slot == 3) RenglonBase.DiasEntrega3 = val;
            else if (slot >= 4)
            {
                var ex = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null) ex.DiasEntrega = val;
            }
            NotificarTodo();
            AlModificar?.Invoke(this);
        }
    }

    public int DiasVigencia
    {
        get
        {
            int slot = ObtenerSlotEfectivoLectura();
            if (slot == 1) return RenglonBase.DiasVigencia1 > 0 ? RenglonBase.DiasVigencia1 : 15;
            if (slot == 2) return RenglonBase.DiasVigencia2 > 0 ? RenglonBase.DiasVigencia2 : 15;
            if (slot == 3) return RenglonBase.DiasVigencia3 > 0 ? RenglonBase.DiasVigencia3 : 15;
            if (slot >= 4)
            {
                int dv = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot)?.DiasVigencia ?? 15;
                return dv > 0 ? dv : 15;
            }
            return 15;
        }
        set
        {
            int val = Math.Max(1, value);
            int slot = AsegurarSlotParaEscritura();
            if (slot == 1) RenglonBase.DiasVigencia1 = val;
            else if (slot == 2) RenglonBase.DiasVigencia2 = val;
            else if (slot == 3) RenglonBase.DiasVigencia3 = val;
            else if (slot >= 4)
            {
                var ex = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null) ex.DiasVigencia = val;
            }
            NotificarTodo();
            AlModificar?.Invoke(this);
        }
    }

    public string NotasCotizacion
    {
        get
        {
            int slot = ObtenerSlotEfectivoLectura();
            if (slot == 1) return RenglonBase.NotasCotizacion1;
            if (slot == 2) return RenglonBase.NotasCotizacion2;
            if (slot == 3) return RenglonBase.NotasCotizacion3;
            if (slot >= 4) return RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot)?.NotasCotizacion ?? string.Empty;
            return string.Empty;
        }
        set
        {
            string val = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(val) && ObtenerSlotEfectivoLectura() == 0)
                return;

            int slot = AsegurarSlotParaEscritura();
            if (slot == 1) RenglonBase.NotasCotizacion1 = val;
            else if (slot == 2) RenglonBase.NotasCotizacion2 = val;
            else if (slot == 3) RenglonBase.NotasCotizacion3 = val;
            else if (slot >= 4)
            {
                var ex = RenglonBase.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null) ex.NotasCotizacion = val;
            }
            NotificarTodo();
            AlModificar?.Invoke(this);
        }
    }

    public bool EsGanadorEnEstaOpcion
    {
        get
        {
            int slot = ObtenerSlotEfectivoLectura();
            return slot > 0 && RenglonBase.ProveedorGanadorIndice == slot;
        }
        set
        {
            int slot = value ? AsegurarSlotParaEscritura() : ObtenerSlotEfectivoLectura();
            if (value && slot > 0)
                RenglonBase.ProveedorGanadorIndice = slot;
            else if (!value && slot > 0 && RenglonBase.ProveedorGanadorIndice == slot)
                RenglonBase.ProveedorGanadorIndice = 0;
            NotificarTodo();
            AlModificar?.Invoke(this);
        }
    }

    public void NotificarTodo()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnidadCompra)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrecioSinIva)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrecioConIva)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImporteSubtotalSinIva)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImporteTotalConIva)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FleteConIva)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiasEntrega)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiasVigencia)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotasCotizacion)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsGanadorEnEstaOpcion)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class RenglonAnalisisPrecioPartida
{
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public int OpcionesCotizadas { get; set; }
    public decimal MejorPrecioTotalConIva { get; set; }
    public decimal PeorPrecioTotalConIva { get; set; }
    public decimal PromedioTotalConIva { get; set; }
    public decimal ElegidoTotalConIva { get; set; }
    public string ProveedorElegido { get; set; } = "Sin adjudicar";
    public decimal AhorroVsMaximo { get; set; }
    public decimal PorcentajeAhorroVsMaximo { get; set; }
    public string AhorroVsMaximoTexto => AhorroVsMaximo > 0
        ? $"{AhorroVsMaximo:C2} ({PorcentajeAhorroVsMaximo:0.0}%)"
        : "$0.00 (0.0%)";
    public string DiagnosticoDiferencia { get; set; } = "Pendiente";
}

// ==================== FASE 4: ECONOMÍA INDUSTRIAL (KRALJIC, PPV E INFLACIÓN) Y AUDITORÍA DE AGENCIA ====================

public class ItemMatrizKraljic
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string Familia { get; set; } = "General";
    public string Categoria { get; set; } = string.Empty;
    public decimal GastoAcumulado { get; set; }
    public int FrecuenciaCompra { get; set; }
    public int ProveedoresDisponibles { get; set; }
    public decimal MejorPrecioHistoricoSinIva { get; set; }
    public int MejorProveedorId { get; set; }
    public string MejorProveedorNombre { get; set; } = "Sin historial";
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public string CuadranteKraljic { get; set; } = "Rutinario / No Crítico";
    public string NivelImpactoFinanciero { get; set; } = "Bajo";
    public string NivelRiesgoSuministro { get; set; } = "Bajo";
    public string EstrategiaRecomendada { get; set; } = string.Empty;
}

public class ItemVariacionPrecioPpv
{
    public int ProductoId { get; set; }
    public string Familia { get; set; } = "General";
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public decimal PrecioPromedioHistorico { get; set; }
    public decimal UltimoPrecioPagado { get; set; }
    public decimal CantidadCompradaReciente { get; set; }
    public string UltimoProveedor { get; set; } = string.Empty;
    public string UltimaFechaCompra { get; set; } = string.Empty;
    public decimal VariacionUnitaria => UltimoPrecioPagado - PrecioPromedioHistorico;
    public decimal VariacionUnitariaPct => PrecioPromedioHistorico > 0m
        ? Math.Round(((UltimoPrecioPagado - PrecioPromedioHistorico) / PrecioPromedioHistorico) * 100m, 1)
        : 0m;
    // PPV positivo = Ahorro vs promedio histórico; PPV negativo = Sobreprecio / Inflación
    public decimal AhorroOSobreprecioTotalPpv => Math.Round((PrecioPromedioHistorico - UltimoPrecioPagado) * CantidadCompradaReciente, 2);

    public string VariacionPctTexto => VariacionUnitariaPct > 0m
        ? $"+{VariacionUnitariaPct:0.0}% (Alza)"
        : VariacionUnitariaPct < 0m
            ? $"{VariacionUnitariaPct:0.0}% (Baja)"
            : "0.0% (Estable)";

    public string PpvResultadoTexto => AhorroOSobreprecioTotalPpv > 0.01m
        ? $"Ahorro: +{AhorroOSobreprecioTotalPpv:C2}"
        : AhorroOSobreprecioTotalPpv < -0.01m
            ? $"Sobreprecio: {AhorroOSobreprecioTotalPpv:C2}"
            : "$0.00 (En línea)";

    public string DiagnosticoInflacion { get; set; } = "Estable";
}

public class ItemAuditoriaCuadrilla
{
    public string ResponsableOCuadrilla { get; set; } = string.Empty;
    public string ProyectoReciente { get; set; } = string.Empty;
    public int ValesSalidaCount { get; set; }
    public decimal UnidadesRetiradas { get; set; }
    public decimal UnidadesSobranteReingresadas { get; set; }
    public int KitsDespachados { get; set; }
    public int HerramientasEnResguardoActivo { get; set; }
    public int IncidenciasOMermasRegistradas { get; set; }
    public decimal CostoEstimadoRetirado { get; set; }
    public string EstadoControlAgencia { get; set; } = "Controlado";
}

// ==================== NOTIFICACIONES POR CORREO A GERENTES (GMAIL SMTP -> CORREO CORPORATIVO) ====================

public class ConfiguracionSmtpCorreo
{
    public bool Activo { get; set; } = true;
    public string ServidorSmtp { get; set; } = "smtp.gmail.com";
    public int PuertoSmtp { get; set; } = 587;
    public bool UsarSsl { get; set; } = true;
    public string CorreoRemitenteGmail { get; set; } = string.Empty;
    public string NombreMostrarRemitente { get; set; } = "Compras y Almacén Lebaref";
    public string ContrasenaAplicacionGmail { get; set; } = string.Empty;
    public string CorreoCopiaFijaGerencia { get; set; } = string.Empty;
    public string TiempoAproxRegistroDefault { get; set; } = "24 a 48 hrs";
    public string TiempoAproxComparativaDefault { get; set; } = "2 a 4 días hábiles";
    public bool ImapRecepcionActiva { get; set; } = true;
    public bool ImapAutoCrearPorAsunto { get; set; } = true;
    public string PalabrasClaveAsuntoImap { get; set; } = "SOLICITUD, COTIZAR, COTIZACION, PEDIDO, REQUERIMIENTO";

    public bool TieneCredencialesCompletas =>
        !string.IsNullOrWhiteSpace(ServidorSmtp) &&
        PuertoSmtp > 0 &&
        !string.IsNullOrWhiteSpace(CorreoRemitenteGmail) &&
        !string.IsNullOrWhiteSpace(ContrasenaAplicacionGmail);
}

public class RegistroNotificacionCorreo
{
    public int Id { get; set; }
    public string FechaHora { get; set; } = string.Empty;
    public string SolicitudFolio { get; set; } = string.Empty;
    public string OrdenCompraFolio { get; set; } = string.Empty;
    public string EtapaClave { get; set; } = string.Empty;
    public string EtapaNombre { get; set; } = string.Empty;
    public string GerenteNombre { get; set; } = string.Empty;
    public string CorreoDestino { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string MensajePrincipal { get; set; } = string.Empty;
    public string CuerpoHtml { get; set; } = string.Empty;
    public string TiempoOLugarTexto { get; set; } = string.Empty;
    public string EstadoEnvio { get; set; } = "Pendiente";
    public string DetalleResultado { get; set; } = string.Empty;
}

public class CorreoEntranteSolicitud
{
    public int Id { get; set; }
    public string UidImap { get; set; } = string.Empty;
    public string FechaCorreo { get; set; } = string.Empty;
    public string RemitenteNombre { get; set; } = string.Empty;
    public string RemitenteCorreo { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string CuerpoTexto { get; set; } = string.Empty;
    public string NombreAdjuntoCsv { get; set; } = string.Empty;
    public string ContenidoAdjuntoCsv { get; set; } = string.Empty;
    public bool CoincidePalabraClaveAsunto { get; set; }
    public int EncargadoDetectadoId { get; set; }
    public string EncargadoDetectadoNombre { get; set; } = string.Empty;
    public int DepartamentoDetectadoId { get; set; }
    public string DepartamentoDetectadoNombre { get; set; } = string.Empty;
    public int ProyectoDetectadoId { get; set; }
    public string ProyectoDetectadoNombre { get; set; } = string.Empty;
    public int PartidasDetectadasCount { get; set; }
    public string ResumenPartidas { get; set; } = string.Empty;
    public string EstadoBandeja { get; set; } = "En Bandeja (Por Revisar)";
    public int SolicitudCreadaId { get; set; }
    public string SolicitudCreadaFolio { get; set; } = string.Empty;
    public bool EsPendienteRevision => string.Equals(EstadoBandeja, "En Bandeja (Por Revisar)", StringComparison.OrdinalIgnoreCase);
}
