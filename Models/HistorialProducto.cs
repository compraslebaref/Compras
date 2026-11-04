namespace SistemaComprasAlmacen.Models;

/// <summary>Fila de la pestaña "Entradas" del historial detallado de un producto.</summary>
public class FilaHistorialEntrada
{
    public string Fecha { get; set; } = string.Empty;
    public string Folio { get; set; } = string.Empty;
    public string OrdenCompra { get; set; } = string.Empty;
    public string Proveedor { get; set; } = string.Empty;
    public string Cantidad { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
}

/// <summary>Fila de la pestaña "Salidas y devoluciones" del historial detallado de un producto.</summary>
public class FilaHistorialSalida
{
    public string Fecha { get; set; } = string.Empty;
    public string Folio { get; set; } = string.Empty;
    public string Recibe { get; set; } = string.Empty;
    public string Proyecto { get; set; } = string.Empty;
    public decimal Entregado { get; set; }
    public decimal Devuelto { get; set; }
    public decimal Neto => System.Math.Max(0m, Entregado - Devuelto);
}

/// <summary>Fila de la pestaña "Préstamos" del historial detallado de una herramienta.</summary>
public class FilaHistorialPrestamo
{
    public string FechaSalida { get; set; } = string.Empty;
    public string Folio { get; set; } = string.Empty;
    public string CodigoHerramienta { get; set; } = string.Empty;
    public string Recibe { get; set; } = string.Empty;
    public string Proyecto { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string FechaDevuelto { get; set; } = string.Empty;
}
