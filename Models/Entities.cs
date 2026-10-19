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
    public decimal StockMinimo { get; set; } = 5m;
    public decimal UltimoCostoCompra { get; set; }            // Por UnidadCompra (con IVA)
    public decimal CostoUnitarioAlmacen => FactorConversion > 0 ? Math.Round(UltimoCostoCompra / FactorConversion, 2) : UltimoCostoCompra;
    public string Ubicacion { get; set; } = "A-01";
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
    public bool StockBajo => StockActual <= StockMinimo;
    public string ConversionTexto => FactorConversion > 1
        ? $"1 {UnidadCompra} = {FactorConversion:0.##} {UnidadAlmacen}"
        : $"1 {UnidadAlmacen}";
    public string FichaTecnicaResumen
    {
        get
        {
            var partes = new List<string>();
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
    public string NombreMostrar => string.IsNullOrWhiteSpace(NombreComercial) ? RazonSocial : NombreComercial;
    public override string ToString() => $"{NombreMostrar} ({Rfc})";
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
    public string Origen { get; set; } = "Catálogo"; // Catálogo, WhatsApp, Plantilla Excel
    public string Estatus { get; set; } = "Pendiente"; // Pendiente, En Comparativa, OC Generada, Atendida
    public string Notas { get; set; } = string.Empty;
    public List<SolicitudPartida> Partidas { get; set; } = new();
    public int TotalSkus => Partidas.Count;
    public decimal TotalArticulos => Partidas.Count > 0 ? Partidas.Sum(x => x.CantidadSolicitada) : 0m;
    public string ResumenArticulos => $"{TotalSkus} SKUs ({TotalArticulos:0.##} pzas)";
    public bool EsEditable => !string.Equals(Estatus, "OC Generada", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(Estatus, "Atendida", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => Id == 0
        ? "[Nuevo Carrito Temporal] (Sin guardar aún)"
        : $"{Folio} | {ProyectoNombre} ({TotalSkus} art. - {EncargadoNombre})";
}

public class SolicitudPartida
{
    public int Id { get; set; }
    public int SolicitudId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string UnidadCompra { get; set; } = "Pieza";
    public decimal CantidadSolicitada { get; set; } = 1m;
    public decimal StockActualAlmacen { get; set; }
    public string NotasPartida { get; set; } = string.Empty;

    // Cotización guardada por producto (3 opciones + ganador + OC generada)
    public int Proveedor1Id { get; set; }
    public decimal PrecioSinIva1 { get; set; }
    public int DiasEntrega1 { get; set; } = 2;
    public decimal FleteConIva1 { get; set; }
    public string NotasCotizacion1 { get; set; } = string.Empty;

    public int Proveedor2Id { get; set; }
    public decimal PrecioSinIva2 { get; set; }
    public int DiasEntrega2 { get; set; } = 3;
    public decimal FleteConIva2 { get; set; }
    public string NotasCotizacion2 { get; set; } = string.Empty;

    public int Proveedor3Id { get; set; }
    public decimal PrecioSinIva3 { get; set; }
    public int DiasEntrega3 { get; set; } = 1;
    public decimal FleteConIva3 { get; set; }
    public string NotasCotizacion3 { get; set; } = string.Empty;

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

public class CotizacionRenglonComparativo
{
    public int SolicitudPartidaId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string UnidadCompra { get; set; } = "Pieza";
    public decimal Cantidad { get; set; } = 1m;
    public decimal StockActualAlmacen { get; set; }
    public decimal UltimoCostoCompraConIva { get; set; }
    public string ReferenciaHistorica { get; set; } = string.Empty;

    // Opción 1
    public int Proveedor1Id { get; set; }
    public string Proveedor1Nombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva1 { get; set; }
    public decimal PrecioConIva1 => Math.Round(PrecioSinIva1 * 1.16m, 2);
    public int DiasEntrega1 { get; set; } = 2;
    public decimal FleteConIva1 { get; set; }
    public string NotasCotizacion1 { get; set; } = string.Empty;
    public decimal TotalPartidaConIva1 => Math.Round(Cantidad * PrecioSinIva1 * 1.16m, 2);
    public decimal TotalConFlete1 => Math.Round(TotalPartidaConIva1 + FleteConIva1, 2);
    public string ResumenOpcion1 => Proveedor1Id > 0 && PrecioSinIva1 > 0
        ? $"{Proveedor1Nombre}: ${PrecioConIva1:N2} c/u ({DiasEntrega1}d)"
        : "Sin cotizar";

    // Opción 2
    public int Proveedor2Id { get; set; }
    public string Proveedor2Nombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva2 { get; set; }
    public decimal PrecioConIva2 => Math.Round(PrecioSinIva2 * 1.16m, 2);
    public int DiasEntrega2 { get; set; } = 3;
    public decimal FleteConIva2 { get; set; }
    public string NotasCotizacion2 { get; set; } = string.Empty;
    public decimal TotalPartidaConIva2 => Math.Round(Cantidad * PrecioSinIva2 * 1.16m, 2);
    public decimal TotalConFlete2 => Math.Round(TotalPartidaConIva2 + FleteConIva2, 2);
    public string ResumenOpcion2 => Proveedor2Id > 0 && PrecioSinIva2 > 0
        ? $"{Proveedor2Nombre}: ${PrecioConIva2:N2} c/u ({DiasEntrega2}d)"
        : "Sin cotizar";

    // Opción 3
    public int Proveedor3Id { get; set; }
    public string Proveedor3Nombre { get; set; } = "Sin asignar";
    public decimal PrecioSinIva3 { get; set; }
    public decimal PrecioConIva3 => Math.Round(PrecioSinIva3 * 1.16m, 2);
    public int DiasEntrega3 { get; set; } = 1;
    public decimal FleteConIva3 { get; set; }
    public string NotasCotizacion3 { get; set; } = string.Empty;
    public decimal TotalPartidaConIva3 => Math.Round(Cantidad * PrecioSinIva3 * 1.16m, 2);
    public decimal TotalConFlete3 => Math.Round(TotalPartidaConIva3 + FleteConIva3, 2);
    public string ResumenOpcion3 => Proveedor3Id > 0 && PrecioSinIva3 > 0
        ? $"{Proveedor3Nombre}: ${PrecioConIva3:N2} c/u ({DiasEntrega3}d)"
        : "Sin cotizar";

    // Proveedor ganador elegido para este producto (0 = Sin decidir, 1 = Opción 1, 2 = Opción 2, 3 = Opción 3)
    public int ProveedorGanadorIndice { get; set; }
    public int OrdenCompraId { get; set; }
    public string OrdenCompraFolio { get; set; } = string.Empty;
    public bool TieneOcGenerada => OrdenCompraId > 0 || !string.IsNullOrWhiteSpace(OrdenCompraFolio);

    public int ProveedorGanadorId => ProveedorGanadorIndice switch
    {
        1 => Proveedor1Id,
        2 => Proveedor2Id,
        3 => Proveedor3Id,
        _ => 0
    };

    public string ProveedorGanadorNombre => ProveedorGanadorIndice switch
    {
        1 => Proveedor1Nombre,
        2 => Proveedor2Nombre,
        3 => Proveedor3Nombre,
        _ => "Sin decidir"
    };

    public decimal PrecioUnitarioGanadorSinIva => ProveedorGanadorIndice switch
    {
        1 => PrecioSinIva1,
        2 => PrecioSinIva2,
        3 => PrecioSinIva3,
        _ => 0m
    };

    public decimal PrecioUnitarioGanadorConIva => Math.Round(PrecioUnitarioGanadorSinIva * 1.16m, 2);

    public decimal FleteGanadorConIva => ProveedorGanadorIndice switch
    {
        1 => FleteConIva1,
        2 => FleteConIva2,
        3 => FleteConIva3,
        _ => 0m
    };

    public int DiasEntregaGanador => ProveedorGanadorIndice switch
    {
        1 => DiasEntrega1,
        2 => DiasEntrega2,
        3 => DiasEntrega3,
        _ => 2
    };

    public decimal TotalGanadorConIva => ProveedorGanadorIndice switch
    {
        1 => TotalPartidaConIva1,
        2 => TotalPartidaConIva2,
        3 => TotalPartidaConIva3,
        _ => 0m
    };

    public string EstadoDecisionTexto
    {
        get
        {
            if (TieneOcGenerada)
                return $"OC: {OrdenCompraFolio} ({ProveedorGanadorNombre})";
            if (ProveedorGanadorIndice >= 1 && ProveedorGanadorIndice <= 3 && ProveedorGanadorId > 0)
                return $"Op.{ProveedorGanadorIndice}: {ProveedorGanadorNombre}";
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
    public string Estatus { get; set; } = "Emitida"; // Emitida, Recibida Parcialmente, Recibida Completa, Cancelada
    public string Notas { get; set; } = string.Empty;
    public List<OrdenCompraPartida> Partidas { get; set; } = new();
    public override string ToString() => Id == 0 ? Folio : $"{Folio} - {ProveedorNombre} ({Estatus}) - ${TotalConIva:N2}";
}

public class OrdenCompraPartida
{
    public int Id { get; set; }
    public int OrdenCompraId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string UnidadCompra { get; set; } = "Pieza";
    public string UnidadAlmacen { get; set; } = "Pieza";
    public decimal FactorConversion { get; set; } = 1m;
    public decimal CantidadPedida { get; set; }
    public decimal CantidadRecibida { get; set; }
    public decimal CantidadPendiente => Math.Max(0, CantidadPedida - CantidadRecibida);
    public decimal CantidadRecibirAhora { get; set; } // Para pantalla de recepción parcial
    public decimal PrecioUnitarioSinIva { get; set; }
    public decimal PrecioUnitarioConIva => Math.Round(PrecioUnitarioSinIva * 1.16m, 2);
    public decimal ImporteSubtotal => Math.Round(CantidadPedida * PrecioUnitarioSinIva, 2);
    public decimal ImporteConIva => Math.Round(ImporteSubtotal * 1.16m, 2);
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
    public List<EntradaPartida> Partidas { get; set; } = new();
}

public class EntradaPartida
{
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
    public int ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public decimal CostoTotalEstimado { get; set; }
    public string Notas { get; set; } = string.Empty;
    public List<SalidaPartida> Partidas { get; set; } = new();
}

public class SalidaPartida
{
    public int Id { get; set; }
    public int SalidaAlmacenId { get; set; }
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public decimal CantidadEntregada { get; set; }
    public string UnidadAlmacen { get; set; } = "Pieza";
    public decimal CostoUnitarioAlmacen { get; set; }
    public decimal ImporteCosto => Math.Round(CantidadEntregada * CostoUnitarioAlmacen, 2);
}

public class PrestamoHerramienta
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty; // PRE-2026-0001
    public string FechaSalida { get; set; } = string.Empty;
    public string FechaDevolucion { get; set; } = string.Empty;
    public int DepartamentoId { get; set; }
    public string DepartamentoNombre { get; set; } = string.Empty;
    public int EncargadoId { get; set; }
    public string EncargadoNombre { get; set; } = string.Empty;
    public int ProyectoId { get; set; }
    public string ProyectoNombre { get; set; } = string.Empty;
    public string Estatus { get; set; } = "Activo"; // Activo, Devuelto Completo, Devuelto con Incidencia
    public string Notas { get; set; } = string.Empty;
    public List<PrestamoPartida> Partidas { get; set; } = new();
}

public class PrestamoPartida
{
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
