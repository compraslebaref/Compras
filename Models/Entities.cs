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

    public override string ToString() => $"{CodigoInterno} - {Nombre}";
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
    public override string ToString() => $"{Folio} | {ProyectoNombre} ({DepartamentoNombre} - {EncargadoNombre})";
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

    // Proveedor 1
    public int Proveedor1Id { get; set; }
    public decimal PrecioSinIva1 { get; set; }
    public decimal PrecioConIva1 => Math.Round(PrecioSinIva1 * 1.16m, 2);
    public decimal TotalConIva1 => Math.Round(Cantidad * PrecioSinIva1 * 1.16m, 2);

    // Proveedor 2
    public int Proveedor2Id { get; set; }
    public decimal PrecioSinIva2 { get; set; }
    public decimal PrecioConIva2 => Math.Round(PrecioSinIva2 * 1.16m, 2);
    public decimal TotalConIva2 => Math.Round(Cantidad * PrecioSinIva2 * 1.16m, 2);

    // Proveedor 3
    public int Proveedor3Id { get; set; }
    public decimal PrecioSinIva3 { get; set; }
    public decimal PrecioConIva3 => Math.Round(PrecioSinIva3 * 1.16m, 2);
    public decimal TotalConIva3 => Math.Round(Cantidad * PrecioSinIva3 * 1.16m, 2);

    // Proveedor ganador elegido para esta partida (1, 2 o 3)
    public int ProveedorGanadorIndice { get; set; } = 1;
    public string ProveedorGanadorNombre { get; set; } = string.Empty;
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
