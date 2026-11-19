using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen.Data;

public static class DatabaseService
{
    public static readonly string BaseFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SistemaComprasAlmacen");

    public static readonly string ImagesFolder = Path.Combine(BaseFolder, "ImagenesCatalogo");
    public static readonly string XmlFolder = Path.Combine(BaseFolder, "FacturasXml");
    public static readonly string BackupsFolder = Path.Combine(BaseFolder, "Respaldos");
    public static readonly string LogsFolder = Path.Combine(BaseFolder, "Logs");
    public static readonly string DbPath = Path.Combine(BaseFolder, "compras_almacen.db");

    private const string PinSalt = "Lebaref_WMS_Salt_2026#";
    private static string ConnectionString => $"Data Source={DbPath};Default Timeout=5";

    public static void Initialize()
    {
        Directory.CreateDirectory(BaseFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(XmlFolder);
        Directory.CreateDirectory(BackupsFolder);
        Directory.CreateDirectory(LogsFolder);

        RestaurarDatosRepositorioSiEsNecesario();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA busy_timeout = 5000;";
            pragmaCmd.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Departamentos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Codigo TEXT NOT NULL,
    Nombre TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Encargados (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DepartamentoId INTEGER NOT NULL,
    Nombre TEXT NOT NULL,
    Puesto TEXT NOT NULL DEFAULT '',
    Telefono TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS Proyectos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Codigo TEXT NOT NULL,
    Nombre TEXT NOT NULL,
    ClienteArea TEXT NOT NULL DEFAULT '',
    Estatus TEXT NOT NULL DEFAULT 'Activo',
    EsStockGeneral INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Categorias (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Nombre TEXT NOT NULL,
    Icono TEXT NOT NULL DEFAULT 'Box24',
    ColorHex TEXT NOT NULL DEFAULT '#0078D4',
    EtiquetaCampo1 TEXT NOT NULL DEFAULT 'Capacidad Eléctrica / Tipo',
    EtiquetaCampo2 TEXT NOT NULL DEFAULT 'Especificación / Material',
    EtiquetaCampo3 TEXT NOT NULL DEFAULT 'Detalle Adicional / Color'
);

CREATE TABLE IF NOT EXISTS Productos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CodigoInterno TEXT NOT NULL UNIQUE,
    CodigoBarras TEXT NOT NULL DEFAULT '',
    Nombre TEXT NOT NULL,
    Descripcion TEXT NOT NULL DEFAULT '',
    CategoriaId INTEGER NOT NULL,
    Marca TEXT NOT NULL DEFAULT '',
    Modelo TEXT NOT NULL DEFAULT '',
    Medidas TEXT NOT NULL DEFAULT '',
    CapacidadElectrica TEXT NOT NULL DEFAULT '',
    Especificacion1 TEXT NOT NULL DEFAULT '',
    Especificacion2 TEXT NOT NULL DEFAULT '',
    UnidadCompra TEXT NOT NULL DEFAULT 'Pieza',
    UnidadAlmacen TEXT NOT NULL DEFAULT 'Pieza',
    FactorConversion REAL NOT NULL DEFAULT 1,
    StockActual REAL NOT NULL DEFAULT 0,
    StockReservadoProyecto REAL NOT NULL DEFAULT 0,
    StockMinimo REAL NOT NULL DEFAULT 0,
    UltimoCostoCompra REAL NOT NULL DEFAULT 0,
    Ubicacion TEXT NOT NULL DEFAULT 'A-01',
    ImagenRuta TEXT NOT NULL DEFAULT '',
    EsHerramienta INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS HerramientasActivos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductoId INTEGER NOT NULL,
    CodigoIndividual TEXT NOT NULL UNIQUE,
    NumeroSerie TEXT NOT NULL DEFAULT '',
    MarcaModelo TEXT NOT NULL DEFAULT '',
    Estado TEXT NOT NULL DEFAULT 'Disponible',
    Ubicacion TEXT NOT NULL DEFAULT 'Gabinete H-1',
    EncargadoActual TEXT NOT NULL DEFAULT '',
    ProyectoActual TEXT NOT NULL DEFAULT '',
    NotasEstado TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS Proveedores (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Rfc TEXT NOT NULL,
    RazonSocial TEXT NOT NULL,
    NombreComercial TEXT NOT NULL DEFAULT '',
    Contacto TEXT NOT NULL DEFAULT '',
    Telefono TEXT NOT NULL DEFAULT '',
    Correo TEXT NOT NULL DEFAULT '',
    DiasEntregaPromedio INTEGER NOT NULL DEFAULT 3,
    CondicionesPago TEXT NOT NULL DEFAULT 'Contado',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS ProductoProveedorMapa (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EmisorRfc TEXT NOT NULL,
    ClaveProveedor TEXT NOT NULL,
    DescripcionProveedor TEXT NOT NULL,
    ProductoId INTEGER NOT NULL,
    UltimoPrecioSinIva REAL NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Solicitudes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Folio TEXT NOT NULL UNIQUE,
    Fecha TEXT NOT NULL,
    DepartamentoId INTEGER NOT NULL,
    EncargadoId INTEGER NOT NULL,
    ProyectoId INTEGER NOT NULL,
    Origen TEXT NOT NULL DEFAULT 'Catálogo',
    Estatus TEXT NOT NULL DEFAULT 'Pendiente',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS SolicitudPartidas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SolicitudId INTEGER NOT NULL,
    ProductoId INTEGER NOT NULL,
    CantidadSolicitada REAL NOT NULL DEFAULT 1,
    NotasPartida TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS OrdenesCompra (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Folio TEXT NOT NULL UNIQUE,
    Fecha TEXT NOT NULL,
    SolicitudId INTEGER NOT NULL DEFAULT 0,
    CotizacionFolio TEXT NOT NULL DEFAULT '',
    ProveedorId INTEGER NOT NULL,
    ProyectoId INTEGER NOT NULL,
    DepartamentoNombre TEXT NOT NULL DEFAULT '',
    EncargadoNombre TEXT NOT NULL DEFAULT '',
    TiempoEntregaDias INTEGER NOT NULL DEFAULT 3,
    CostoEnvioConIva REAL NOT NULL DEFAULT 0,
    Subtotal REAL NOT NULL DEFAULT 0,
    Iva REAL NOT NULL DEFAULT 0,
    TotalConIva REAL NOT NULL DEFAULT 0,
    Estatus TEXT NOT NULL DEFAULT 'Emitida',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS OrdenCompraPartidas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OrdenCompraId INTEGER NOT NULL,
    ProductoId INTEGER NOT NULL,
    CantidadPedida REAL NOT NULL DEFAULT 1,
    CantidadRecibida REAL NOT NULL DEFAULT 0,
    PrecioUnitarioSinIva REAL NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS EntradasAlmacen (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Folio TEXT NOT NULL UNIQUE,
    Fecha TEXT NOT NULL,
    OrdenCompraId INTEGER,
    OrdenCompraFolio TEXT NOT NULL DEFAULT '',
    ProveedorNombre TEXT NOT NULL DEFAULT '',
    ProyectoNombre TEXT NOT NULL DEFAULT '',
    TipoEntrada TEXT NOT NULL DEFAULT 'Recepción OC',
    FacturaUuid TEXT NOT NULL DEFAULT '',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS EntradaPartidas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntradaAlmacenId INTEGER NOT NULL,
    ProductoId INTEGER NOT NULL,
    CantidadCompraRecibida REAL NOT NULL,
    UnidadCompra TEXT NOT NULL,
    FactorConversion REAL NOT NULL,
    CantidadIngresadaAlmacen REAL NOT NULL,
    UnidadAlmacen TEXT NOT NULL,
    CostoUnitarioCompraConIva REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS SalidasAlmacen (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Folio TEXT NOT NULL UNIQUE,
    Fecha TEXT NOT NULL,
    DepartamentoId INTEGER NOT NULL,
    EncargadoId INTEGER NOT NULL,
    ProyectoId INTEGER NOT NULL,
    CostoTotalEstimado REAL NOT NULL DEFAULT 0,
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS SalidaPartidas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SalidaAlmacenId INTEGER NOT NULL,
    ProductoId INTEGER NOT NULL,
    CantidadEntregada REAL NOT NULL,
    UnidadAlmacen TEXT NOT NULL,
    CostoUnitarioAlmacen REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS PrestamosHerramienta (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Folio TEXT NOT NULL UNIQUE,
    FechaSalida TEXT NOT NULL,
    FechaDevolucion TEXT NOT NULL DEFAULT '',
    DepartamentoId INTEGER NOT NULL,
    EncargadoId INTEGER NOT NULL,
    ProyectoId INTEGER NOT NULL,
    Estatus TEXT NOT NULL DEFAULT 'Activo',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS PrestamoPartidas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PrestamoHerramientaId INTEGER NOT NULL,
    HerramientaActivoId INTEGER NOT NULL,
    EstadoSalida TEXT NOT NULL DEFAULT 'Bueno',
    EstadoDevolucion TEXT NOT NULL DEFAULT 'Pendiente',
    FechaDevuelto TEXT NOT NULL DEFAULT '',
    Observaciones TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS FacturasXml (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Uuid TEXT NOT NULL UNIQUE,
    SerieFolio TEXT NOT NULL DEFAULT '',
    FechaTimbrado TEXT NOT NULL DEFAULT '',
    EmisorRfc TEXT NOT NULL,
    EmisorNombre TEXT NOT NULL,
    Moneda TEXT NOT NULL DEFAULT 'MXN',
    TipoCambio REAL NOT NULL DEFAULT 1,
    Subtotal REAL NOT NULL DEFAULT 0,
    Descuento REAL NOT NULL DEFAULT 0,
    Iva REAL NOT NULL DEFAULT 0,
    Total REAL NOT NULL DEFAULT 0,
    OrdenCompraId INTEGER,
    OrdenCompraFolio TEXT NOT NULL DEFAULT '',
    RutaArchivoXml TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS FacturaConceptos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FacturaXmlId INTEGER NOT NULL,
    ClaveProdServ TEXT NOT NULL DEFAULT '',
    NoIdentificacion TEXT NOT NULL DEFAULT '',
    Descripcion TEXT NOT NULL DEFAULT '',
    Unidad TEXT NOT NULL DEFAULT '',
    Cantidad REAL NOT NULL DEFAULT 1,
    ValorUnitario REAL NOT NULL DEFAULT 0,
    Importe REAL NOT NULL DEFAULT 0,
    IvaConcepto REAL NOT NULL DEFAULT 0,
    ProductoIdVinculado INTEGER
);

CREATE TABLE IF NOT EXISTS ConfiguracionSistema (
    Clave TEXT PRIMARY KEY,
    Valor TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS EventosCalendario (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Fecha TEXT NOT NULL,
    Titulo TEXT NOT NULL,
    Descripcion TEXT NOT NULL DEFAULT '',
    Categoria TEXT NOT NULL DEFAULT 'Recordatorio',
    Prioridad TEXT NOT NULL DEFAULT 'Normal',
    Completado INTEGER NOT NULL DEFAULT 0,
    Notas TEXT NOT NULL DEFAULT '',
    ReferenciaFolio TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS BitacoraAdministrador (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Fecha TEXT NOT NULL,
    Accion TEXT NOT NULL,
    Referencia TEXT NOT NULL DEFAULT '',
    Motivo TEXT NOT NULL DEFAULT '',
    Detalle TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS CotizacionesPartidaProveedor (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SolicitudPartidaId INTEGER NOT NULL,
    NumeroOpcion INTEGER NOT NULL DEFAULT 4,
    ProveedorId INTEGER NOT NULL,
    PrecioSinIva REAL NOT NULL DEFAULT 0,
    DiasEntrega INTEGER NOT NULL DEFAULT 2,
    FleteConIva REAL NOT NULL DEFAULT 0,
    DiasVigencia INTEGER NOT NULL DEFAULT 15,
    FechaCotizacion TEXT NOT NULL DEFAULT '',
    NotasCotizacion TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS BancoPreciosHistorico (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductoId INTEGER NOT NULL DEFAULT 0,
    DescripcionConcepto TEXT NOT NULL DEFAULT '',
    Familia TEXT NOT NULL DEFAULT '',
    ProveedorId INTEGER NOT NULL,
    PrecioSinIva REAL NOT NULL DEFAULT 0,
    DiasEntrega INTEGER NOT NULL DEFAULT 2,
    DiasVigencia INTEGER NOT NULL DEFAULT 15,
    FechaCotizacion TEXT NOT NULL,
    OrigenFolio TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS NotificacionesCheckpoint (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FechaHora TEXT NOT NULL,
    Etapa TEXT NOT NULL,
    CheckpointCodigo TEXT NOT NULL,
    FolioReferencia TEXT NOT NULL,
    EstatusAnterior TEXT NOT NULL DEFAULT '',
    EstatusNuevo TEXT NOT NULL DEFAULT '',
    RolOrigen TEXT NOT NULL DEFAULT 'Compras',
    RolesDestino TEXT NOT NULL DEFAULT 'Almacén',
    UsuarioOrigen TEXT NOT NULL DEFAULT '',
    UsuarioDestino TEXT NOT NULL DEFAULT '',
    Mensaje TEXT NOT NULL,
    Leida INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS UsuariosSistema (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    NombreCompleto TEXT NOT NULL,
    Puesto TEXT NOT NULL DEFAULT '',
    Rol TEXT NOT NULL DEFAULT 'Compras',
    PinHash TEXT NOT NULL DEFAULT '',
    Activo INTEGER NOT NULL DEFAULT 1,
    FechaAlta TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS BitacoraUsuario (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FechaHora TEXT NOT NULL,
    UsuarioId INTEGER NOT NULL DEFAULT 0,
    UsuarioNombre TEXT NOT NULL DEFAULT '',
    Rol TEXT NOT NULL DEFAULT '',
    Modulo TEXT NOT NULL DEFAULT '',
    Accion TEXT NOT NULL DEFAULT '',
    FolioReferencia TEXT NOT NULL DEFAULT '',
    Detalle TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS KitsHerramientas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CodigoKit TEXT NOT NULL UNIQUE,
    Nombre TEXT NOT NULL,
    Especialidad TEXT NOT NULL DEFAULT 'General',
    Ubicacion TEXT NOT NULL DEFAULT 'GABINETE-1',
    ResponsableActual TEXT NOT NULL DEFAULT '',
    ProyectoActual TEXT NOT NULL DEFAULT '',
    UltimoValePrestamoFolio TEXT NOT NULL DEFAULT '',
    Estatus TEXT NOT NULL DEFAULT 'Disponible',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS KitHerramientasPartidas (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    KitId INTEGER NOT NULL,
    HerramientaActivoId INTEGER NOT NULL,
    UNIQUE(KitId, HerramientaActivoId)
);

CREATE TABLE IF NOT EXISTS VehiculosFlotilla (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CodigoVehiculo TEXT NOT NULL UNIQUE,
    Placas TEXT NOT NULL DEFAULT '',
    MarcaModelo TEXT NOT NULL,
    Anio INTEGER NOT NULL DEFAULT 2024,
    ResponsableHabitual TEXT NOT NULL DEFAULT '',
    KilometrajeInicial INTEGER NOT NULL DEFAULT 0,
    KilometrajeActual INTEGER NOT NULL DEFAULT 0,
    ProximoServicioKm INTEGER NOT NULL DEFAULT 0,
    VigenciaSeguro TEXT NOT NULL DEFAULT '',
    Estatus TEXT NOT NULL DEFAULT 'Activo en Operación',
    Notas TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS BitacoraGastosVehiculo (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VehiculoId INTEGER NOT NULL,
    Fecha TEXT NOT NULL,
    TipoGasto TEXT NOT NULL DEFAULT 'Gasolina / Combustible',
    KilometrajeRegistro INTEGER NOT NULL DEFAULT 0,
    LitrosCargados REAL NOT NULL DEFAULT 0,
    PrecioPorLitro REAL NOT NULL DEFAULT 0,
    TanqueLleno INTEGER NOT NULL DEFAULT 1,
    MontoConIva REAL NOT NULL DEFAULT 0,
    ProveedorTaller TEXT NOT NULL DEFAULT '',
    FolioReferencia TEXT NOT NULL DEFAULT '',
    ProyectoNombre TEXT NOT NULL DEFAULT '',
    Descripcion TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS RevisionesSemanalesVehiculo (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VehiculoId INTEGER NOT NULL,
    FolioRevision TEXT NOT NULL UNIQUE,
    Fecha TEXT NOT NULL,
    ResponsableInspector TEXT NOT NULL DEFAULT '',
    KilometrajeRegistrado INTEGER NOT NULL DEFAULT 0,
    NivelCombustible TEXT NOT NULL DEFAULT '3/4 Tanque',
    EstadoLlantas TEXT NOT NULL DEFAULT 'OK',
    EstadoNivelesFluidos TEXT NOT NULL DEFAULT 'OK',
    EstadoFrenosLuces TEXT NOT NULL DEFAULT 'OK',
    EstadoCarroceriaLimpieza TEXT NOT NULL DEFAULT 'OK',
    Observaciones TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS IDX_Productos_CodigoInterno ON Productos(CodigoInterno);
CREATE INDEX IF NOT EXISTS IDX_Productos_Nombre ON Productos(Nombre);
CREATE INDEX IF NOT EXISTS IDX_Productos_CategoriaId ON Productos(CategoriaId);
CREATE INDEX IF NOT EXISTS IDX_Productos_Ubicacion ON Productos(Ubicacion);
CREATE INDEX IF NOT EXISTS IDX_Herramientas_Ubicacion ON HerramientasActivos(Ubicacion);
CREATE INDEX IF NOT EXISTS IDX_SolicitudPartidas_SolId ON SolicitudPartidas(SolicitudId);
CREATE INDEX IF NOT EXISTS IDX_CotizacionesPartidaProveedor_PartId ON CotizacionesPartidaProveedor(SolicitudPartidaId);
CREATE INDEX IF NOT EXISTS IDX_OrdenCompraPartidas_OcId ON OrdenCompraPartidas(OrdenCompraId);
CREATE INDEX IF NOT EXISTS IDX_OrdenCompraPartidas_ProdId ON OrdenCompraPartidas(ProductoId);
CREATE INDEX IF NOT EXISTS IDX_EntradaPartidas_EntId ON EntradaPartidas(EntradaAlmacenId);
CREATE INDEX IF NOT EXISTS IDX_SalidaPartidas_SalId ON SalidaPartidas(SalidaAlmacenId);
CREATE INDEX IF NOT EXISTS IDX_FacturaConceptos_FacId ON FacturaConceptos(FacturaXmlId);
CREATE INDEX IF NOT EXISTS IDX_ProductoProveedorMapa_RfcClave ON ProductoProveedorMapa(EmisorRfc, ClaveProveedor);
CREATE INDEX IF NOT EXISTS IDX_NotificacionesCheckpoint_Folio ON NotificacionesCheckpoint(FolioReferencia);
CREATE INDEX IF NOT EXISTS IDX_BitacoraUsuario_Fecha ON BitacoraUsuario(FechaHora);
CREATE INDEX IF NOT EXISTS IDX_BitacoraUsuario_Folio ON BitacoraUsuario(FolioReferencia);

CREATE TABLE IF NOT EXISTS NotificacionesCorreo (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FechaHora TEXT NOT NULL,
    SolicitudFolio TEXT NOT NULL DEFAULT '',
    OrdenCompraFolio TEXT NOT NULL DEFAULT '',
    EtapaClave TEXT NOT NULL DEFAULT '',
    EtapaNombre TEXT NOT NULL DEFAULT '',
    GerenteNombre TEXT NOT NULL DEFAULT '',
    CorreoDestino TEXT NOT NULL DEFAULT '',
    Asunto TEXT NOT NULL DEFAULT '',
    MensajePrincipal TEXT NOT NULL DEFAULT '',
    CuerpoHtml TEXT NOT NULL DEFAULT '',
    TiempoOLugarTexto TEXT NOT NULL DEFAULT '',
    EstadoEnvio TEXT NOT NULL DEFAULT 'Pendiente',
    DetalleResultado TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS IDX_NotificacionesCorreo_Folio ON NotificacionesCorreo(SolicitudFolio);

CREATE TABLE IF NOT EXISTS CorreosEntrantesSolicitud (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UidImap TEXT NOT NULL UNIQUE,
    FechaCorreo TEXT NOT NULL DEFAULT '',
    RemitenteNombre TEXT NOT NULL DEFAULT '',
    RemitenteCorreo TEXT NOT NULL DEFAULT '',
    Asunto TEXT NOT NULL DEFAULT '',
    CuerpoTexto TEXT NOT NULL DEFAULT '',
    NombreAdjuntoCsv TEXT NOT NULL DEFAULT '',
    ContenidoAdjuntoCsv TEXT NOT NULL DEFAULT '',
    CoincidePalabraClaveAsunto INTEGER NOT NULL DEFAULT 0,
    EncargadoDetectadoId INTEGER NOT NULL DEFAULT 0,
    EncargadoDetectadoNombre TEXT NOT NULL DEFAULT '',
    DepartamentoDetectadoId INTEGER NOT NULL DEFAULT 0,
    DepartamentoDetectadoNombre TEXT NOT NULL DEFAULT '',
    ProyectoDetectadoId INTEGER NOT NULL DEFAULT 0,
    ProyectoDetectadoNombre TEXT NOT NULL DEFAULT '',
    PartidasDetectadasCount INTEGER NOT NULL DEFAULT 0,
    ResumenPartidas TEXT NOT NULL DEFAULT '',
    EstadoBandeja TEXT NOT NULL DEFAULT 'En Bandeja (Por Revisar)',
    SolicitudCreadaId INTEGER NOT NULL DEFAULT 0,
    SolicitudCreadaFolio TEXT NOT NULL DEFAULT ''
);
";
        cmd.ExecuteNonQuery();

        AsegurarColumnaExiste(conn, "Encargados", "Correo", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Categorias", "EtiquetaCampo1", "TEXT NOT NULL DEFAULT 'Capacidad Eléctrica / Tipo'");
        AsegurarColumnaExiste(conn, "Categorias", "EtiquetaCampo2", "TEXT NOT NULL DEFAULT 'Especificación / Material'");
        AsegurarColumnaExiste(conn, "Categorias", "EtiquetaCampo3", "TEXT NOT NULL DEFAULT 'Detalle Adicional / Color'");

        AsegurarColumnaExiste(conn, "Productos", "Familia", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Marca", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Modelo", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Medidas", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "CapacidadElectrica", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Especificacion1", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Especificacion2", "TEXT NOT NULL DEFAULT ''");

        // Columnas para conceptos libres (sin SKU previo) en SolicitudPartidas y OrdenCompraPartidas
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DescripcionLibre", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "UnidadLibre", "TEXT NOT NULL DEFAULT 'Pieza'");
        AsegurarColumnaExiste(conn, "OrdenCompraPartidas", "DescripcionLibre", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "OrdenCompraPartidas", "UnidadLibre", "TEXT NOT NULL DEFAULT 'Pieza'");

        AsegurarColumnaExiste(conn, "Solicitudes", "UsaPartidas", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "NombrePartida", "TEXT NOT NULL DEFAULT ''");

        // Columnas de cotización comparativa por producto en SolicitudPartidas
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "Proveedor1Id", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "PrecioSinIva1", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasEntrega1", "INTEGER NOT NULL DEFAULT 2");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FleteConIva1", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "NotasCotizacion1", "TEXT NOT NULL DEFAULT ''");

        AsegurarColumnaExiste(conn, "SolicitudPartidas", "Proveedor2Id", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "PrecioSinIva2", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasEntrega2", "INTEGER NOT NULL DEFAULT 3");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FleteConIva2", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "NotasCotizacion2", "TEXT NOT NULL DEFAULT ''");

        AsegurarColumnaExiste(conn, "SolicitudPartidas", "Proveedor3Id", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "PrecioSinIva3", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasEntrega3", "INTEGER NOT NULL DEFAULT 1");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FleteConIva3", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "NotasCotizacion3", "TEXT NOT NULL DEFAULT ''");

        AsegurarColumnaExiste(conn, "SolicitudPartidas", "ProveedorGanadorIndice", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "OrdenCompraId", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "OrdenCompraFolio", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "IncluirEnCotizacion", "INTEGER NOT NULL DEFAULT 1");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasVigenciaCotizacion", "INTEGER NOT NULL DEFAULT 15");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FechaCotizacion", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasVigencia1", "INTEGER NOT NULL DEFAULT 15");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FechaCotizacion1", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasVigencia2", "INTEGER NOT NULL DEFAULT 15");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FechaCotizacion2", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "DiasVigencia3", "INTEGER NOT NULL DEFAULT 15");
        AsegurarColumnaExiste(conn, "SolicitudPartidas", "FechaCotizacion3", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "BancoPreciosHistorico", "NotasCotizacion", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "BancoPreciosHistorico", "FueGanador", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "BancoPreciosHistorico", "SolicitudPartidaId", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "Solicitudes", "TipoSolicitud", "TEXT NOT NULL DEFAULT 'Requerimiento de Compra'");
        AsegurarColumnaExiste(conn, "OrdenesCompra", "CondicionPago", "TEXT NOT NULL DEFAULT 'Contado (Transferencia previa)'");
        AsegurarColumnaExiste(conn, "OrdenesCompra", "FechaProgramadaPago", "TEXT NOT NULL DEFAULT ''");
        foreach (var tablaSeg in new[] { "Solicitudes", "OrdenesCompra" })
        {
            AsegurarColumnaExiste(conn, tablaSeg, "EsperandoRespuestaDe", "TEXT NOT NULL DEFAULT ''");
            AsegurarColumnaExiste(conn, tablaSeg, "EsperandoDesde", "TEXT NOT NULL DEFAULT ''");
            AsegurarColumnaExiste(conn, tablaSeg, "ConcluidoManual", "INTEGER NOT NULL DEFAULT 0");
        }

        // Columnas de persona abierta que recibe y fecha compromiso en Salidas y Préstamos
        AsegurarColumnaExiste(conn, "SalidasAlmacen", "PersonaRecibe", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SalidaPartidas", "CantidadDevuelta", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "PrestamosHerramienta", "PersonaRecibe", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "PrestamosHerramienta", "FechaCompromiso", "TEXT NOT NULL DEFAULT ''");

        // Columnas de EventosCalendario y BitacoraAdministrador
        AsegurarColumnaExiste(conn, "EventosCalendario", "Descripcion", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "EventosCalendario", "ReferenciaFolio", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "EventosCalendario", "Oculto", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "EventosCalendario", "ClaveAutomatica", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "BitacoraAdministrador", "Referencia", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "BitacoraAdministrador", "Motivo", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "NotificacionesCheckpoint", "UsuarioOrigen", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "NotificacionesCheckpoint", "UsuarioDestino", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "HerramientasActivos", "FechaCompra", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "HerramientasActivos", "FechaFinGarantia", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "HerramientasActivos", "CostoAdquisicion", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "HerramientasActivos", "GastoMantenimientoAcumulado", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "VehiculosFlotilla", "IntervaloServicioKm", "INTEGER NOT NULL DEFAULT 10000");
        AsegurarColumnaExiste(conn, "VehiculosFlotilla", "UltimoServicioKm", "INTEGER NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "VehiculosFlotilla", "CapacidadTanqueLitros", "REAL NOT NULL DEFAULT 60");
        AsegurarColumnaExiste(conn, "VehiculosFlotilla", "TipoCombustible", "TEXT NOT NULL DEFAULT 'Gasolina Regular'");
        AsegurarColumnaExiste(conn, "BitacoraGastosVehiculo", "LitrosCargados", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "BitacoraGastosVehiculo", "PrecioPorLitro", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "BitacoraGastosVehiculo", "TanqueLleno", "INTEGER NOT NULL DEFAULT 1");
        AsegurarColumnaExiste(conn, "UsuariosSistema", "CorreoErp", "TEXT NOT NULL DEFAULT ''");
        SeedUsuariosSistemaSiVacio(conn);
        MigrarUsuariosYRolesBase(conn);
        SeedKitsHerramientasSiVacio(conn);
        SeedVehiculosFlotillaYGarantiasSiVacio(conn);

        using (var pinCmd = conn.CreateCommand())
        {
            pinCmd.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'AdminPin' LIMIT 1;";
            var pinActual = pinCmd.ExecuteScalar()?.ToString();
            if (string.IsNullOrWhiteSpace(pinActual))
            {
                using var insPin = conn.CreateCommand();
                insPin.CommandText = "INSERT OR REPLACE INTO ConfiguracionSistema (Clave, Valor) VALUES ('AdminPin', $hash);";
                insPin.Parameters.AddWithValue("$hash", CalcularHashPinAdministrador("1234"));
                insPin.ExecuteNonQuery();
            }
            else if (!pinActual.StartsWith("SHA256:", StringComparison.Ordinal))
            {
                using var migPin = conn.CreateCommand();
                migPin.CommandText = "UPDATE ConfiguracionSistema SET Valor = $hash WHERE Clave = 'AdminPin';";
                migPin.Parameters.AddWithValue("$hash", CalcularHashPinAdministrador(pinActual.Trim()));
                migPin.ExecuteNonQuery();
            }
        }

        SeedInitialDataIfEmpty(conn);
        SeedEventosCalendarioSiVacio(conn);
        MigrarCategoriasYReclasificarCatalogo(conn);
        AutoAsignarFamiliasInicialesSiVacio(conn);
        EstandarizarUnidadesMedidaEnBaseDatos(conn);
        SincronizarHerramientasCatalogoConDirectorio(conn);
        NormalizarNombresComercialesProveedores(conn);
        conn.Close();
        SincronizarFacturasYOrdenesExistentes();
    }

    private static void NormalizarNombresComercialesProveedores(SqliteConnection conn)
    {
        var porActualizar = new List<(int id, string nuevoComercial)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, RazonSocial, NombreComercial FROM Proveedores";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                string rs = (r.IsDBNull(1) ? "" : r.GetString(1)).Trim();
                string nc = (r.IsDBNull(2) ? "" : r.GetString(2)).Trim();
                if (string.IsNullOrWhiteSpace(nc) || string.Equals(nc, rs, StringComparison.OrdinalIgnoreCase))
                {
                    string limpio = Proveedor.ExtraerNombreComercialCorto(!string.IsNullOrWhiteSpace(nc) ? nc : rs);
                    if (!string.IsNullOrWhiteSpace(limpio) && !string.Equals(limpio, nc, StringComparison.Ordinal))
                        porActualizar.Add((id, limpio));
                }
            }
        }

        foreach (var (id, nuevoComercial) in porActualizar)
        {
            using var up = conn.CreateCommand();
            up.CommandText = "UPDATE Proveedores SET NombreComercial = $nc WHERE Id = $id";
            up.Parameters.AddWithValue("$nc", nuevoComercial);
            up.Parameters.AddWithValue("$id", id);
            up.ExecuteNonQuery();
        }
    }

    public static readonly IReadOnlyList<string> UnidadesMedidaEstandar = new[]
    {
        "Pieza",
        "Metro",
        "Kilogramo",
        "Gramo",
        "Litro",
        "Mililitro",
        "Galón",
        "Cubeta",
        "Bote",
        "Cartucho",
        "Paquete",
        "Caja",
        "Rollo",
        "Bolsa",
        "Bulto",
        "Juego",
        "Par",
        "Barra",
        "Hoja",
        "Metro cuadrado",
        "Metro cúbico",
        "Servicio"
    };

    public static string NormalizarUnidadMedida(string? unidadRaw, string? claveUnidadSat = null)
    {
        string raw = (unidadRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            raw = (claveUnidadSat ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return "Pieza";

        // 1. Quitar textos entre paréntesis o corchetes, ej. "Caja (100 pzas)" -> "Caja"
        int idxPar = raw.IndexOf('(');
        if (idxPar > 0) raw = raw[..idxPar].Trim();
        int idxCor = raw.IndexOf('[');
        if (idxCor > 0) raw = raw[..idxCor].Trim();

        // 2. Separar sufijos o prefijos de clave SAT con guion, ej. "PIEZA-H87", "KILOGRAMO-KGM"
        string partePrincipal = raw;
        string parteSecundaria = (claveUnidadSat ?? string.Empty).Trim();
        if (raw.Contains('-'))
        {
            var partes = raw.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (partes.Length >= 2)
            {
                partePrincipal = partes[0];
                if (string.IsNullOrWhiteSpace(parteSecundaria))
                    parteSecundaria = partes[^1];
            }
        }

        string u = LimpiarTextoUnidad(partePrincipal);
        string sat = LimpiarTextoUnidad(parteSecundaria);

        string? mapeada = MapearClaveOSinonimoUnidad(u) ?? MapearClaveOSinonimoUnidad(sat);
        if (!string.IsNullOrEmpty(mapeada))
            return mapeada;

        // Coincidencia parcial segura
        if (u.StartsWith("PIEZ") || u.StartsWith("PZA") || u == "H87") return "Pieza";
        if (u.StartsWith("KILO") || u.StartsWith("KG")) return "Kilogramo";
        if (u.StartsWith("GRAM")) return "Gramo";
        if (u.StartsWith("METRO CUAD") || u.StartsWith("MT2") || u.StartsWith("M2")) return "Metro cuadrado";
        if (u.StartsWith("METRO CUB") || u.StartsWith("MT3") || u.StartsWith("M3")) return "Metro cúbico";
        if (u.StartsWith("METR") || u.StartsWith("MTR")) return "Metro";
        if (u.StartsWith("MILILIT")) return "Mililitro";
        if (u.StartsWith("LITR") || u.StartsWith("LTR")) return "Litro";
        if (u.StartsWith("GAL")) return "Galón";
        if (u.StartsWith("CUBET")) return "Cubeta";
        if (u.StartsWith("PAQ") || u.StartsWith("PACK")) return "Paquete";
        if (u.StartsWith("CAJ")) return "Caja";
        if (u.StartsWith("ROLL") || u.StartsWith("BOBIN") || u.StartsWith("CARRET")) return "Rollo";
        if (u.StartsWith("BOLS")) return "Bolsa";
        if (u.StartsWith("BULT") || u.StartsWith("SAC") || u.StartsWith("COSTAL")) return "Bulto";
        if (u.StartsWith("JUEG") || u.StartsWith("JGO") || u.StartsWith("KIT") || u.StartsWith("CONJUNT")) return "Juego";
        if (u.StartsWith("CARTUCH") || u.StartsWith("TUB")) return "Cartucho";
        if (u.StartsWith("BOT") || u.StartsWith("LAT") || u.StartsWith("ENVAS")) return "Bote";
        if (u.StartsWith("BARR") || u.StartsWith("TRAM") || u.StartsWith("VARILL")) return "Barra";
        if (u.StartsWith("HOJ") || u.StartsWith("PLIEG") || u.StartsWith("LAMIN") || u.StartsWith("PLAC")) return "Hoja";
        if (u.StartsWith("SERV") || u.StartsWith("ACTIV")) return "Servicio";

        return "Pieza";
    }

    private static string LimpiarTextoUnidad(string? txt)
    {
        if (string.IsNullOrWhiteSpace(txt)) return string.Empty;
        string s = txt.Trim().ToUpperInvariant()
            .Replace(".", "")
            .Replace(",", "")
            .Replace("Á", "A")
            .Replace("É", "E")
            .Replace("Í", "I")
            .Replace("Ó", "O")
            .Replace("Ú", "U")
            .Replace("\uFFFD", "O"); // Corrige "GALÓN" con caracter corrupto "GALN"
        while (s.Contains("  "))
            s = s.Replace("  ", " ");
        return s.Trim();
    }

    private static string? MapearClaveOSinonimoUnidad(string u)
    {
        if (string.IsNullOrWhiteSpace(u)) return null;
        return u switch
        {
            "PIEZA" or "PIEZAS" or "PZA" or "PZAS" or "PZ" or "PZS" or "H87" or "XUN" or "EA" or "UN" or "UND" or "UNI" or "UNIDAD" or "UNIDADES" or "C62" => "Pieza",
            "METRO" or "METROS" or "METRO LINEAL" or "METROS LINEALES" or "MTR" or "MT" or "MTS" or "M" or "ML" or "LM" => "Metro",
            "KILOGRAMO" or "KILOGRAMOS" or "KILO" or "KILOS" or "KGM" or "KG" or "KGS" => "Kilogramo",
            "GRAMO" or "GRAMOS" or "GRM" or "GR" or "GRS" or "G" => "Gramo",
            "LITRO" or "LITROS" or "LTR" or "LT" or "LTS" or "L" => "Litro",
            "MILILITRO" or "MILILITROS" or "MLT" => "Mililitro",
            "GALON" or "GALONES" or "GAL" or "GLL" or "GL" => "Galón",
            "CUBETA" or "CUBETAS" or "CUB" => "Cubeta",
            "BOTE" or "BOTES" or "LATA" or "LATAS" or "ENVASE" or "AEROSOL" or "SPRAY" => "Bote",
            "CARTUCHO" or "CARTUCHOS" or "CRT" or "TUBO" => "Cartucho",
            "PAQUETE" or "PAQUETES" or "PAQ" or "PQT" or "PQ" or "PK" or "XPK" or "PACK" or "BLISTER" => "Paquete",
            "CAJA" or "CAJAS" or "CJ" or "CJA" or "BX" or "XBX" => "Caja",
            "ROLLO" or "ROLLOS" or "RO" or "XRO" or "BOBINA" or "CARRETE" => "Rollo",
            "BOLSA" or "BOLSAS" or "BG" or "XBG" => "Bolsa",
            "BULTO" or "BULTOS" or "SACO" or "SACOS" or "COSTAL" or "COSTALES" => "Bulto",
            "JUEGO" or "JUEGOS" or "JGO" or "JGOS" or "KIT" or "KITS" or "SET" or "KT" or "CONJUNTO" => "Juego",
            "PAR" or "PARES" or "PR" or "2P" => "Par",
            "BARRA" or "BARRAS" or "TRAMO" or "TRAMOS" or "VARILLA" or "PERFIL" => "Barra",
            "HOJA" or "HOJAS" or "PLIEGO" or "PLIEGOS" or "PLACA" or "LAMINA" => "Hoja",
            "METRO CUADRADO" or "METROS CUADRADOS" or "M2" or "M²" or "MTK" or "MT2" => "Metro cuadrado",
            "METRO CUBICO" or "METROS CUBICOS" or "M3" or "M³" or "MTQ" or "MT3" => "Metro cúbico",
            "SERVICIO" or "SERVICIOS" or "SER" or "SRV" or "E48" or "ACT" or "ACTIVIDAD" or "LOTE" or "XLT" => "Servicio",
            _ => null
        };
    }

    public static void EstandarizarUnidadesMedidaEnBaseDatos(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();

        // 1. Productos (UnidadCompra y UnidadAlmacen)
        var prods = new List<(int Id, string Uc, string Ua)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, UnidadCompra, UnidadAlmacen FROM Productos;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                prods.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2)));
            }
        }
        foreach (var (id, uc, ua) in prods)
        {
            string normUc = NormalizarUnidadMedida(uc);
            string normUa = NormalizarUnidadMedida(ua);
            if (normUc != uc || normUa != ua)
            {
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE Productos SET UnidadCompra = $uc, UnidadAlmacen = $ua WHERE Id = $id;";
                upd.Parameters.AddWithValue("$uc", normUc);
                upd.Parameters.AddWithValue("$ua", normUa);
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
            }
        }

        // 2. FacturaConceptos (Unidad)
        var unidadesFac = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT DISTINCT Unidad FROM FacturaConceptos;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!r.IsDBNull(0)) unidadesFac.Add(r.GetString(0));
            }
        }
        foreach (var uOriginal in unidadesFac)
        {
            string norm = NormalizarUnidadMedida(uOriginal);
            if (norm != uOriginal)
            {
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE FacturaConceptos SET Unidad = $norm WHERE Unidad = $orig;";
                upd.Parameters.AddWithValue("$norm", norm);
                upd.Parameters.AddWithValue("$orig", uOriginal);
                upd.ExecuteNonQuery();
            }
        }

        // 3. EntradaPartidas (UnidadCompra y UnidadAlmacen)
        var entradasU = new List<(int Id, string Uc, string Ua)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, UnidadCompra, UnidadAlmacen FROM EntradaPartidas;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                entradasU.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2)));
            }
        }
        foreach (var (id, uc, ua) in entradasU)
        {
            string normUc = NormalizarUnidadMedida(uc);
            string normUa = NormalizarUnidadMedida(ua);
            if (normUc != uc || normUa != ua)
            {
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE EntradaPartidas SET UnidadCompra = $uc, UnidadAlmacen = $ua WHERE Id = $id;";
                upd.Parameters.AddWithValue("$uc", normUc);
                upd.Parameters.AddWithValue("$ua", normUa);
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
            }
        }

        // 4. SalidaPartidas (UnidadAlmacen)
        var unidadesSal = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT DISTINCT UnidadAlmacen FROM SalidaPartidas;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!r.IsDBNull(0)) unidadesSal.Add(r.GetString(0));
            }
        }
        foreach (var uOriginal in unidadesSal)
        {
            string norm = NormalizarUnidadMedida(uOriginal);
            if (norm != uOriginal)
            {
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE SalidaPartidas SET UnidadAlmacen = $norm WHERE UnidadAlmacen = $orig;";
                upd.Parameters.AddWithValue("$norm", norm);
                upd.Parameters.AddWithValue("$orig", uOriginal);
                upd.ExecuteNonQuery();
            }
        }

        // 5. SolicitudPartidas y OrdenCompraPartidas (UnidadLibre)
        foreach (var tabla in new[] { "SolicitudPartidas", "OrdenCompraPartidas" })
        {
            var unidadesLibres = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"SELECT DISTINCT UnidadLibre FROM {tabla};";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!r.IsDBNull(0)) unidadesLibres.Add(r.GetString(0));
                }
            }
            foreach (var uOriginal in unidadesLibres)
            {
                string norm = NormalizarUnidadMedida(uOriginal);
                if (norm != uOriginal)
                {
                    using var upd = conn.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText = $"UPDATE {tabla} SET UnidadLibre = $norm WHERE UnidadLibre = $orig;";
                    upd.Parameters.AddWithValue("$norm", norm);
                    upd.Parameters.AddWithValue("$orig", uOriginal);
                    upd.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
    }

    private static void AsegurarColumnaExiste(SqliteConnection conn, string tabla, string columna, string definicionSql)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tabla});";
        using var r = check.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase))
                return;
        }
        r.Close();

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tabla} ADD COLUMN {columna} {definicionSql};";
        alter.ExecuteNonQuery();
    }

    private static void RestaurarDatosRepositorioSiEsNecesario()
    {
        try
        {
            string? repoDataDir = null;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Data", "compras_almacen.db");
                if (File.Exists(candidate))
                {
                    repoDataDir = Path.Combine(dir.FullName, "Data");
                    break;
                }
                dir = dir.Parent;
            }

            if (repoDataDir == null)
                return;

            string repoDb = Path.Combine(repoDataDir, "compras_almacen.db");
            if (!File.Exists(DbPath) && File.Exists(repoDb))
            {
                File.Copy(repoDb, DbPath, overwrite: false);
            }

            string repoImgs = Path.Combine(repoDataDir, "ImagenesCatalogo");
            if (Directory.Exists(repoImgs))
            {
                foreach (var imgFile in Directory.GetFiles(repoImgs))
                {
                    string destImg = Path.Combine(ImagesFolder, Path.GetFileName(imgFile));
                    if (!File.Exists(destImg))
                    {
                        File.Copy(imgFile, destImg, overwrite: false);
                    }
                }
            }
        }
        catch
        {
            // Continuar normalmente si ocurre algún error de permisos
        }
    }

    private static void SeedInitialDataIfEmpty(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = @"
SELECT
    (SELECT COUNT(*) FROM ConfiguracionSistema WHERE Clave = 'SeedInicialAplicado') +
    (SELECT COUNT(*) FROM Departamentos) +
    (SELECT COUNT(*) FROM Productos) +
    (SELECT COUNT(*) FROM HerramientasActivos) +
    (SELECT COUNT(*) FROM Proveedores) +
    (SELECT COUNT(*) FROM Proyectos);";
        var count = Convert.ToInt32(check.ExecuteScalar());
        if (count > 0)
        {
            using var markCmd = conn.CreateCommand();
            markCmd.CommandText = "INSERT OR IGNORE INTO ConfiguracionSistema (Clave, Valor) VALUES ('SeedInicialAplicado', '1');";
            markCmd.ExecuteNonQuery();
            return;
        }

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = @"
INSERT OR IGNORE INTO ConfiguracionSistema (Clave, Valor) VALUES ('SeedInicialAplicado', '1');

INSERT OR IGNORE INTO Departamentos (Codigo, Nombre) VALUES
('ING-MEC', 'Ingeniería Mecánica'),
('ING-ELE', 'Ingeniería Eléctrica y Control'),
('ING-AUT', 'Ingeniería de Automatización'),
('ING-PRO', 'Ingeniería de Procesos / Manufactura'),
('ALM-GEN', 'Almacén y Mantenimiento');

INSERT INTO Encargados (DepartamentoId, Nombre, Puesto, Telefono) VALUES
(1, 'Ing. Carlos Mendoza', 'Líder de Diseño Mecánico', '55-1020-3040'),
(1, 'Ing. Sofía Ramírez', 'Proyectista Mecánico', '55-1020-3041'),
(2, 'Ing. Roberto Vega', 'Especialista en Control y PLC', '55-2030-4050'),
(2, 'Ing. Daniel Torres', 'Ingeniero Eléctrico', '55-2030-4051'),
(3, 'Ing. Mariana Castro', 'Líder de Robótica e Integración', '55-3040-5060'),
(4, 'Ing. Jorge Luna', 'Ingeniero de Planta', '55-4050-6070');

INSERT INTO Proyectos (Codigo, Nombre, ClienteArea, Estatus, EsStockGeneral) VALUES
('STOCK-ALM', 'Reposición de Stock General de Almacén', 'Almacén Interno', 'Activo', 1),
('PRY-2026-01', 'Celda de Ensamble Automatizado Línea 3', 'Planta Norte', 'Activo', 0),
('PRY-2026-02', 'Tablero de Control Principal Horno B', 'Área Térmica', 'Activo', 0),
('PRY-2026-03', 'Transportador de Rodillos Inoxidable', 'Empaque', 'Activo', 0);

INSERT INTO Categorias (Nombre, Icono, ColorHex) VALUES
('Eléctrico y Control', 'Flash24', '#0078D4'),
('Tornillería y Fijación', 'Wrench24', '#498205'),
('Neumática e Hidráulica', 'Gauge24', '#881798'),
('Consumibles y Abrasivos', 'Flame24', '#D83B01'),
('Seguridad y EPP', 'ShieldCheckmark24', '#008272'),
('Herramientas y Equipos', 'Toolbox24', '#5C2D91');

INSERT INTO Proveedores (Rfc, RazonSocial, NombreComercial, Contacto, Telefono, Correo, DiasEntregaPromedio, CondicionesPago, Notas) VALUES
('FIE920315AB1', 'Ferretería Industrial y Eléctrica del Centro S.A. de C.V.', 'FerreIndustrial Centro', 'Lic. Pedro Gómez', '55-4455-6677', 'ventas@ferreindustrial.mx', 2, 'Crédito 15 días', 'Entrega sin costo en pedidos mayores a $2,500'),
('AUT010820XY9', 'Automatización y Suministros Industriales S.A. de C.V.', 'AutoSum Industrial', 'Ing. Laura Nava', '55-8899-0011', 'cotizaciones@autosum.com.mx', 4, 'Crédito 30 días', 'Especialistas en eléctrico, sensores y neumática'),
('MAY150510KL4', 'Mayorista de Tornillería y Herramientas S.A. de C.V.', 'TorniMax Mayorista', 'Sr. Arturo Rivas', '55-2233-4455', 'pedidos@tornimax.mx', 1, 'Contado', 'Mejor precio en cajas de tornillería y abrasivos');

INSERT INTO Productos (CodigoInterno, CodigoBarras, Nombre, Descripcion, CategoriaId, UnidadCompra, UnidadAlmacen, FactorConversion, StockActual, StockReservadoProyecto, StockMinimo, UltimoCostoCompra, Ubicacion, EsHerramienta) VALUES
('PRD-0001', '750100010001', 'Tornillo Hexagonal Inox 1/4 x 1 pulg', 'Acero inoxidable 304 rosca estándar UNC', 2, 'Caja (100 pzas)', 'Pieza', 100, 340, 50, 100, 290.00, 'A-01-01', 0),
('PRD-0002', '750100010002', 'Disco de Corte Fino 4-1/2 pulg Inox', 'Disco abrasivo 1mm para acero inoxidable', 4, 'Paquete (25 pzas)', 'Pieza', 25, 42, 0, 15, 625.00, 'B-02-01', 0),
('PRD-0003', '750100010003', 'Cable Uso Rudo Calibre 12 AWG Negro', 'Cable de cobre flexible aislamiento THHW-LS 600V', 1, 'Rollo (100 m)', 'Metro', 100, 85, 20, 30, 1850.00, 'C-01-02', 0),
('PRD-0004', '750100010004', 'Relevador Encapsulado 24VDC 8 Pines + Base', 'Con indicador LED y base para riel DIN', 1, 'Caja (10 pzas)', 'Pieza', 10, 8, 0, 10, 1160.00, 'C-02-04', 0),
('PRD-0005', '750100010005', 'Manguera Neumática Poliuretano 8mm Azul', 'Alta flexibilidad 10 bar', 3, 'Rollo (100 m)', 'Metro', 100, 120, 0, 25, 980.00, 'D-01-01', 0),
('PRD-0006', '750100010006', 'Cinta de Aislar Super 33+ 3M', 'Uso profesional alta adherencia 19mm x 20m', 1, 'Paquete (10 pzas)', 'Pieza', 10, 14, 0, 8, 950.00, 'C-01-05', 0),
('PRD-0007', '750100010007', 'Guante de Nitrilo Anticorte Nivel 5', 'Guante de seguridad industrial palma recubierta', 5, 'Paquete (12 pares)', 'Par', 12, 6, 0, 10, 840.00, 'E-01-01', 0),
('PRD-0008', 'PRD-0008', 'Taladro Rotomartillo Industrial 1/2 pulg', 'Taladro percutor 850W velocidad variable', 6, 'Pieza', 'Pieza', 1, 2, 0, 1, 2850.00, 'Gabinete H-1', 1),
('PRD-0009', 'PRD-0009', 'Multímetro Digital True RMS Industrial', 'Categoría IV 600V medición temperatura y frecuencia', 6, 'Pieza', 'Pieza', 1, 2, 0, 1, 3400.00, 'Gabinete H-2', 1),
('PRD-0010', 'PRD-0010', 'Esmeriladora Angular 4-1/2 pulg 1100W', 'Con guarda de ajuste rápido y mango antivibración', 6, 'Pieza', 'Pieza', 1, 2, 0, 1, 2150.00, 'Gabinete H-1', 1);

INSERT INTO HerramientasActivos (ProductoId, CodigoIndividual, NumeroSerie, MarcaModelo, Estado, Ubicacion, EncargadoActual, ProyectoActual, NotasEstado) VALUES
(8, 'HER-0001', 'BOS-882910', 'Bosch GSB 16 RE', 'Disponible', 'Gabinete H-1', '', '', 'En maletín completo con llave de mandril'),
(8, 'HER-0002', 'BOS-882955', 'Bosch GSB 16 RE', 'En Préstamo', 'En campo', 'Ing. Carlos Mendoza', 'PRY-2026-01', 'Prestado para montaje en Línea 3'),
(9, 'HER-0003', 'FLU-117-091', 'Fluke 179 True RMS', 'Disponible', 'Gabinete H-2', '', '', 'Con puntas de prueba y termopar'),
(9, 'HER-0004', 'FLU-117-094', 'Fluke 179 True RMS', 'Disponible', 'Gabinete H-2', '', '', 'Calibrado Oct 2026'),
(10, 'HER-0005', 'DEW-402-112', 'DeWalt DWE402', 'Disponible', 'Gabinete H-1', '', '', 'Con llave de brida'),
(10, 'HER-0006', 'DEW-402-119', 'DeWalt DWE402', 'Dañada', 'Taller Mtto', '', '', 'Cable de alimentación requiere reemplazo');

INSERT INTO Solicitudes (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, Origen, Estatus, Notas) VALUES
('SOL-2026-0001', '2026-10-12 09:30', 2, 3, 3, 'WhatsApp', 'Pendiente', 'Urgente para armado de gabinete eléctrico Horno B'),
('SOL-2026-0002', '2026-10-13 11:15', 1, 1, 2, 'Catálogo', 'OC Generada', 'Insumos de fijación y corte para estructura Línea 3');

INSERT INTO SolicitudPartidas (SolicitudId, ProductoId, CantidadSolicitada, NotasPartida) VALUES
(1, 3, 2, '2 Rollos de 100m para alimentación principal'),
(1, 4, 3, '3 Cajas de 10 relevadores para control'),
(1, 6, 2, '2 Paquetes de cinta Super 33+'),
(2, 1, 5, '5 Cajas de tornillo inox 1/4'),
(2, 2, 4, '4 Paquetes de discos de corte inox');

INSERT INTO OrdenesCompra (Folio, Fecha, SolicitudId, CotizacionFolio, ProveedorId, ProyectoId, DepartamentoNombre, EncargadoNombre, TiempoEntregaDias, CostoEnvioConIva, Subtotal, Iva, TotalConIva, Estatus, Notas) VALUES
('OC-2026-0001', '2026-10-13 14:20', 2, 'COT-2026-0001', 3, 2, 'Ingeniería Mecánica', 'Ing. Carlos Mendoza', 2, 150.00, 3405.17, 544.83, 4100.00, 'Recibida Parcialmente', 'Primera entrega recibida, faltan 2 paquetes de discos');

INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva) VALUES
(1, 1, 5, 5, 250.00),
(1, 2, 4, 2, 538.79);

INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas) VALUES
('ENT-2026-0001', '2026-10-14 08:45', 1, 'OC-2026-0001', 'TorniMax Mayorista', 'PRY-2026-01 - Celda de Ensamble Automatizado Línea 3', 'Recepción Parcial OC', '', 'Llegaron las 5 cajas de tornillos completas y 2 de los 4 paquetes de discos');

INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva) VALUES
(1, 1, 5, 'Caja (100 pzas)', 100, 500, 'Pieza', 290.00),
(1, 2, 2, 'Paquete (25 pzas)', 25, 50, 'Pieza', 625.00);

INSERT INTO PrestamosHerramienta (Folio, FechaSalida, FechaDevolucion, DepartamentoId, EncargadoId, ProyectoId, Estatus, Notas) VALUES
('PRE-2026-0001', '2026-10-14 08:00', '', 1, 1, 2, 'Activo', 'Préstamo para instalación de estructura en Línea 3');

INSERT INTO PrestamoPartidas (PrestamoHerramientaId, HerramientaActivoId, EstadoSalida, EstadoDevolucion, FechaDevuelto, Observaciones) VALUES
(1, 2, 'Bueno', 'Pendiente', '', 'Entregado con maletín y brocas');
";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public static string GenerarSiguienteFolio(string prefijo, string tabla)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {tabla}";
        int next = Convert.ToInt32(cmd.ExecuteScalar()) + 1;
        while (true)
        {
            string candidate = $"{prefijo}-{DateTime.Now:yyyy}-{next:D4}";
            using var chk = conn.CreateCommand();
            chk.CommandText = $"SELECT COUNT(*) FROM {tabla} WHERE Folio = $f";
            chk.Parameters.AddWithValue("$f", candidate);
            if (Convert.ToInt32(chk.ExecuteScalar()) == 0)
                return candidate;
            next++;
        }
    }

    public static string GenerarSiguienteCodigoProducto(string prefijo, string tabla, string campo)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {tabla}";
        int next = Convert.ToInt32(cmd.ExecuteScalar()) + 1;
        while (true)
        {
            string candidate = $"{prefijo}-{next:D4}";
            using var chk = conn.CreateCommand();
            chk.CommandText = $"SELECT COUNT(*) FROM {tabla} WHERE {campo} = $c";
            chk.Parameters.AddWithValue("$c", candidate);
            if (Convert.ToInt32(chk.ExecuteScalar()) == 0)
                return candidate;
            next++;
        }
    }

    // ==================== CATÁLOGOS BASE ====================

    public static List<Departamento> ObtenerDepartamentos()
    {
        var list = new List<Departamento>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Codigo, Nombre FROM Departamentos ORDER BY Nombre";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Departamento
            {
                Id = r.GetInt32(0),
                Codigo = r.GetString(1),
                Nombre = r.GetString(2)
            });
        }
        return list;
    }

    public static void GuardarDepartamento(string codigo, string nombre, int id = 0)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (id > 0)
        {
            cmd.CommandText = "UPDATE Departamentos SET Codigo=$c, Nombre=$n WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Departamentos (Codigo, Nombre) VALUES ($c, $n)";
        }
        cmd.Parameters.AddWithValue("$c", codigo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$n", nombre.Trim());
        cmd.ExecuteNonQuery();
    }

    public static void EliminarDepartamento(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Departamentos WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<Encargado> ObtenerEncargados(int? departamentoId = null)
    {
        var list = new List<Encargado>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT e.Id, e.DepartamentoId, d.Nombre, e.Nombre, e.Puesto, e.Telefono, COALESCE(e.Correo, '')
FROM Encargados e
LEFT JOIN Departamentos d ON d.Id = e.DepartamentoId
WHERE ($depId IS NULL OR e.DepartamentoId = $depId)
ORDER BY e.Nombre";
        cmd.Parameters.AddWithValue("$depId", departamentoId.HasValue ? departamentoId.Value : DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Encargado
            {
                Id = r.GetInt32(0),
                DepartamentoId = r.GetInt32(1),
                DepartamentoNombre = r.IsDBNull(2) ? "Sin departamento" : r.GetString(2),
                Nombre = r.GetString(3),
                Puesto = r.GetString(4),
                Telefono = r.GetString(5),
                Correo = r.GetString(6)
            });
        }
        return list;
    }

    public static void GuardarEncargado(int departamentoId, string nombre, string puesto, string telefono, int id = 0, string correo = "")
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (id > 0)
        {
            cmd.CommandText = "UPDATE Encargados SET DepartamentoId=$d, Nombre=$n, Puesto=$p, Telefono=$t, Correo=$c WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Encargados (DepartamentoId, Nombre, Puesto, Telefono, Correo) VALUES ($d, $n, $p, $t, $c)";
        }
        cmd.Parameters.AddWithValue("$d", departamentoId);
        cmd.Parameters.AddWithValue("$n", nombre.Trim());
        cmd.Parameters.AddWithValue("$p", puesto.Trim());
        cmd.Parameters.AddWithValue("$t", telefono.Trim());
        cmd.Parameters.AddWithValue("$c", (correo ?? string.Empty).Trim());
        cmd.ExecuteNonQuery();
    }

    public static void EliminarEncargado(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Encargados WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<Proyecto> ObtenerProyectos()
    {
        var list = new List<Proyecto>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Codigo, Nombre, ClienteArea, Estatus, EsStockGeneral FROM Proyectos ORDER BY EsStockGeneral DESC, Id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Proyecto
            {
                Id = r.GetInt32(0),
                Codigo = r.GetString(1),
                Nombre = r.GetString(2),
                ClienteArea = r.GetString(3),
                Estatus = r.GetString(4),
                EsStockGeneral = r.GetInt32(5) == 1
            });
        }
        return list;
    }

    public static void GuardarProyecto(string codigo, string nombre, string area, string estatus = "Activo", int id = 0)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (id > 0)
        {
            cmd.CommandText = "UPDATE Proyectos SET Codigo=$c, Nombre=$n, ClienteArea=$a, Estatus=$e WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Proyectos (Codigo, Nombre, ClienteArea, Estatus, EsStockGeneral) VALUES ($c, $n, $a, $e, 0)";
        }
        cmd.Parameters.AddWithValue("$c", codigo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$n", nombre.Trim());
        cmd.Parameters.AddWithValue("$a", area.Trim());
        cmd.Parameters.AddWithValue("$e", string.IsNullOrWhiteSpace(estatus) ? "Activo" : estatus.Trim());
        cmd.ExecuteNonQuery();
    }

    public static void EliminarProyecto(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Proyectos WHERE Id=$id AND EsStockGeneral=0";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<Categoria> ObtenerCategorias()
    {
        var list = new List<Categoria>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Nombre, Icono, ColorHex, EtiquetaCampo1, EtiquetaCampo2, EtiquetaCampo3 FROM Categorias ORDER BY Id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Categoria
            {
                Id = r.GetInt32(0),
                Nombre = r.GetString(1),
                Icono = r.GetString(2),
                ColorHex = r.GetString(3),
                EtiquetaCampo1 = r.IsDBNull(4) || string.IsNullOrWhiteSpace(r.GetString(4)) ? "Capacidad Eléctrica / Tipo" : r.GetString(4),
                EtiquetaCampo2 = r.IsDBNull(5) || string.IsNullOrWhiteSpace(r.GetString(5)) ? "Especificación / Material" : r.GetString(5),
                EtiquetaCampo3 = r.IsDBNull(6) || string.IsNullOrWhiteSpace(r.GetString(6)) ? "Detalle Adicional / Color" : r.GetString(6)
            });
        }
        return list;
    }

    public static void GuardarCategoria(string nombre, int id = 0, string? etiqueta1 = null, string? etiqueta2 = null, string? etiqueta3 = null)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        string e1 = string.IsNullOrWhiteSpace(etiqueta1) ? "Capacidad Eléctrica / Tipo" : etiqueta1.Trim();
        string e2 = string.IsNullOrWhiteSpace(etiqueta2) ? "Especificación / Material" : etiqueta2.Trim();
        string e3 = string.IsNullOrWhiteSpace(etiqueta3) ? "Detalle Adicional / Color" : etiqueta3.Trim();

        if (id > 0)
        {
            cmd.CommandText = "UPDATE Categorias SET Nombre=$n, EtiquetaCampo1=$e1, EtiquetaCampo2=$e2, EtiquetaCampo3=$e3 WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Categorias (Nombre, Icono, ColorHex, EtiquetaCampo1, EtiquetaCampo2, EtiquetaCampo3) VALUES ($n, 'Box24', '#0078D4', $e1, $e2, $e3)";
        }
        cmd.Parameters.AddWithValue("$n", nombre.Trim());
        cmd.Parameters.AddWithValue("$e1", e1);
        cmd.Parameters.AddWithValue("$e2", e2);
        cmd.Parameters.AddWithValue("$e3", e3);
        cmd.ExecuteNonQuery();
    }

    public static void EliminarCategoria(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Categorias WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<Proveedor> ObtenerProveedores()
    {
        var list = new List<Proveedor>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Rfc, RazonSocial, NombreComercial, Contacto, Telefono, Correo, DiasEntregaPromedio, CondicionesPago, Notas FROM Proveedores ORDER BY NombreComercial, RazonSocial";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Proveedor
            {
                Id = r.GetInt32(0),
                Rfc = r.GetString(1),
                RazonSocial = r.GetString(2),
                NombreComercial = r.GetString(3),
                Contacto = r.GetString(4),
                Telefono = r.GetString(5),
                Correo = r.GetString(6),
                DiasEntregaPromedio = r.GetInt32(7),
                CondicionesPago = r.GetString(8),
                Notas = r.GetString(9)
            });
        }
        return list;
    }

    public static int GuardarProveedor(Proveedor p)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (p.Id > 0)
        {
            cmd.CommandText = @"
UPDATE Proveedores SET Rfc=$rfc, RazonSocial=$rs, NombreComercial=$nc, Contacto=$con, Telefono=$tel, Correo=$cor, DiasEntregaPromedio=$dias, CondicionesPago=$cond, Notas=$not
WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", p.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO Proveedores (Rfc, RazonSocial, NombreComercial, Contacto, Telefono, Correo, DiasEntregaPromedio, CondicionesPago, Notas)
VALUES ($rfc, $rs, $nc, $con, $tel, $cor, $dias, $cond, $not);
SELECT last_insert_rowid();";
        }
        cmd.Parameters.AddWithValue("$rfc", p.Rfc.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$rs", p.RazonSocial.Trim());
        cmd.Parameters.AddWithValue("$nc", p.NombreComercial.Trim());
        cmd.Parameters.AddWithValue("$con", p.Contacto.Trim());
        cmd.Parameters.AddWithValue("$tel", p.Telefono.Trim());
        cmd.Parameters.AddWithValue("$cor", p.Correo.Trim());
        cmd.Parameters.AddWithValue("$dias", p.DiasEntregaPromedio);
        cmd.Parameters.AddWithValue("$cond", p.CondicionesPago.Trim());
        cmd.Parameters.AddWithValue("$not", p.Notas.Trim());

        if (p.Id > 0)
        {
            cmd.ExecuteNonQuery();
            return p.Id;
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void EliminarProveedor(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Proveedores WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== PRODUCTOS Y HERRAMIENTAS ====================

    public static List<Producto> ObtenerProductos(string? filtro = null, int? categoriaId = null)
    {
        var list = new List<Producto>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT p.Id, p.CodigoInterno, p.CodigoBarras, p.Nombre, p.Descripcion, p.CategoriaId, c.Nombre,
       p.UnidadCompra, p.UnidadAlmacen, p.FactorConversion, p.StockActual, p.StockReservadoProyecto,
       p.StockMinimo, p.UltimoCostoCompra, p.Ubicacion, p.ImagenRuta, p.EsHerramienta,
       p.Marca, p.Modelo, p.Medidas, p.CapacidadElectrica, p.Especificacion1, p.Especificacion2, COALESCE(p.Familia, '')
FROM Productos p
LEFT JOIN Categorias c ON c.Id = p.CategoriaId
WHERE ($cat IS NULL OR p.CategoriaId = $cat)
  AND ($f IS NULL OR p.Nombre LIKE '%' || $f || '%'
       OR p.CodigoInterno LIKE '%' || $f || '%'
       OR p.CodigoBarras LIKE '%' || $f || '%'
       OR p.Descripcion LIKE '%' || $f || '%'
       OR p.Familia LIKE '%' || $f || '%'
       OR p.Marca LIKE '%' || $f || '%'
       OR p.Modelo LIKE '%' || $f || '%'
       OR p.Medidas LIKE '%' || $f || '%'
       OR p.CapacidadElectrica LIKE '%' || $f || '%'
       OR p.Especificacion1 LIKE '%' || $f || '%'
       OR p.Especificacion2 LIKE '%' || $f || '%')
ORDER BY p.CodigoInterno";
        cmd.Parameters.AddWithValue("$cat", categoriaId.HasValue && categoriaId.Value > 0 ? categoriaId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$f", string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : filtro.Trim());
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Producto
            {
                Id = r.GetInt32(0),
                CodigoInterno = r.GetString(1),
                CodigoBarras = r.GetString(2),
                Nombre = r.GetString(3),
                Descripcion = r.GetString(4),
                CategoriaId = r.GetInt32(5),
                CategoriaNombre = r.IsDBNull(6) ? "General" : r.GetString(6),
                UnidadCompra = r.GetString(7),
                UnidadAlmacen = r.GetString(8),
                FactorConversion = Convert.ToDecimal(r.GetDouble(9)),
                StockActual = Convert.ToDecimal(r.GetDouble(10)),
                StockReservadoProyecto = Convert.ToDecimal(r.GetDouble(11)),
                StockMinimo = Convert.ToDecimal(r.GetDouble(12)),
                UltimoCostoCompra = Convert.ToDecimal(r.GetDouble(13)),
                Ubicacion = r.GetString(14),
                ImagenRuta = r.GetString(15),
                EsHerramienta = r.GetInt32(16) == 1,
                Marca = r.IsDBNull(17) ? string.Empty : r.GetString(17),
                Modelo = r.IsDBNull(18) ? string.Empty : r.GetString(18),
                Medidas = r.IsDBNull(19) ? string.Empty : r.GetString(19),
                CapacidadElectrica = r.IsDBNull(20) ? string.Empty : r.GetString(20),
                Especificacion1 = r.IsDBNull(21) ? string.Empty : r.GetString(21),
                Especificacion2 = r.IsDBNull(22) ? string.Empty : r.GetString(22),
                Familia = r.IsDBNull(23) ? string.Empty : r.GetString(23)
            });
        }
        return list;
    }

    public static List<string> ObtenerFamiliasProductos()
    {
        var baseFamilias = new List<string>
        {
            "Gas Refrigerante R-410A",
            "Gas Refrigerante R-32",
            "Gas Refrigerante R-404A",
            "Gas Propileno / Soldadura",
            "Aceites y Químicos HVAC",
            "Capacitores HVAC",
            "Contactores y Relevadores",
            "Sensores y Controles HVAC",
            "Filtros Deshidratadores",
            "Válvulas y Accesorios HVAC",
            "Soldadura de Plata y Fundente",
            "Cinta de Aislar",
            "Cinta de Montaje / Ducto / Teflón",
            "Focos y Luminarias LED",
            "Cable y Conductores Eléctricos",
            "Pilas y Baterías",
            "Tubería y Conexiones PVC",
            "Mangueras y Conexiones Hidráulicas",
            "Tornillos y Tuercas",
            "Pijas y Taquetes",
            "Discos de Corte y Lija",
            "Brocas y Puntas",
            "Herramienta Manual",
            "Impermeabilizantes y Pintura",
            "Brochas, Rodillos y Felpas",
            "Selladores, Silicones y Pegamentos",
            "Guantes de Seguridad",
            "Cascos y Protección Cabeza",
            "Botas y Calzado de Seguridad",
            "Lentes y Protección Visual",
            "Chalecos y Señalización",
            "Jabón, Detergentes y Limpiadores",
            "Toallas, Papel y Microfibras",
            "Escobas, Cepillos y Trapeadores",
            "Bolsas para Basura",
            "Insecticidas y Plagas",
            "Papelería y Artículos de Oficina"
        };

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var cmdCustom = conn.CreateCommand())
        {
            cmdCustom.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'FamiliasPersonalizadas' LIMIT 1";
            var val = cmdCustom.ExecuteScalar()?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
            {
                foreach (var f in val.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    string clean = f.Trim();
                    if (!string.IsNullOrWhiteSpace(clean) && !baseFamilias.Any(x => x.Equals(clean, StringComparison.OrdinalIgnoreCase)))
                    {
                        baseFamilias.Add(clean);
                    }
                }
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT TRIM(Familia) FROM Productos WHERE TRIM(COALESCE(Familia, '')) <> '' ORDER BY Familia";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string fam = r.GetString(0);
            if (!string.IsNullOrWhiteSpace(fam) && !baseFamilias.Any(x => x.Equals(fam, StringComparison.OrdinalIgnoreCase)))
            {
                baseFamilias.Add(fam);
            }
        }

        return baseFamilias.OrderBy(x => x).ToList();
    }

    public static void GuardarFamiliaPersonalizada(string? familia)
    {
        string clean = (familia ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(clean)) return;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        var actuales = new List<string>();
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'FamiliasPersonalizadas' LIMIT 1";
            var val = sel.ExecuteScalar()?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
            {
                actuales.AddRange(val.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }
        }

        if (!actuales.Any(x => x.Equals(clean, StringComparison.OrdinalIgnoreCase)))
        {
            actuales.Add(clean);
            using var up = conn.CreateCommand();
            up.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ('FamiliasPersonalizadas', $val)
ON CONFLICT(Clave) DO UPDATE SET Valor = $val";
            up.Parameters.AddWithValue("$val", string.Join("|", actuales));
            up.ExecuteNonQuery();
        }
    }

    public static int RenombrarFamiliaMasivo(string familiaActual, string nuevaFamilia)
    {
        if (string.IsNullOrWhiteSpace(familiaActual))
            return 0;

        string nuevaClean = (nuevaFamilia ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(nuevaClean))
        {
            GuardarFamiliaPersonalizada(nuevaClean);
        }

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'FamiliasPersonalizadas' LIMIT 1";
            var val = sel.ExecuteScalar()?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
            {
                var lista = val.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().Equals(familiaActual.Trim(), StringComparison.OrdinalIgnoreCase) ? nuevaClean : x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                using var upConf = conn.CreateCommand();
                upConf.CommandText = "UPDATE ConfiguracionSistema SET Valor = $v WHERE Clave = 'FamiliasPersonalizadas'";
                upConf.Parameters.AddWithValue("$v", string.Join("|", lista));
                upConf.ExecuteNonQuery();
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Productos SET Familia = $nueva WHERE LOWER(TRIM(Familia)) = LOWER(TRIM($actual))";
        cmd.Parameters.AddWithValue("$nueva", nuevaClean);
        cmd.Parameters.AddWithValue("$actual", familiaActual.Trim());
        return cmd.ExecuteNonQuery();
    }

    public static string ExtraerFamiliaSugerida(string? nombreRaw)
    {
        string u = (nombreRaw ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(u)) return string.Empty;

        if (u.Contains("R-410") || u.Contains("R410")) return "Gas Refrigerante R-410A";
        if (u.Contains("R-32") || u.Contains("R32")) return "Gas Refrigerante R-32";
        if (u.Contains("R-404") || u.Contains("R404")) return "Gas Refrigerante R-404A";
        if (u.Contains("PROPILENO") || u.Contains("MAP-PRO")) return "Gas Propileno / Soldadura";
        if (ContieneAlguno(u, "ACEITE", "COIL CLENER", "COIL CLEANER", "DESINCRUSTANTE", "DIELECTRICO")) return "Aceites y Químicos HVAC";
        if (u.Contains("CAPACITOR")) return "Capacitores HVAC";
        if (ContieneAlguno(u, "CONTACTOR", "RELEVADOR", "PASTILLA TERMOMAG", "INTERRUPTOR")) return "Contactores y Relevadores";
        if (ContieneAlguno(u, "SENSOR", "TERMOSTATO", "CONTROL UNIVERSAL", "CONTROLADOR")) return "Sensores y Controles HVAC";
        if (u.Contains("FILTRO DESHIDRATADOR")) return "Filtros Deshidratadores";
        if (ContieneAlguno(u, "VALVULA", "VÁLVULA", "BOMBA DE CONDENSADO", "BOMBA DE DRENAJE")) return "Válvulas y Accesorios HVAC";
        if (ContieneAlguno(u, "SOLDADURA DE PLATA", "VARITA DE PLATA", "VARILLA SOLDADURA", "FUNDENTE")) return "Soldadura de Plata y Fundente";
        if (ContieneAlguno(u, "CINTA AISLAR", "CINTA AISLANTE")) return "Cinta de Aislar";
        if (ContieneAlguno(u, "CINTA TEFLON", "CINTA PARA DUCTO", "CINTA DE ALUMINIO", "CINTA DE MONTAJE", "CINTA AZUL", "CINTA MASKING")) return "Cinta de Montaje / Ducto / Teflón";
        if (ContieneAlguno(u, "FOCO", "LAMPARA", "LÁMPARA", "REFLECTOR", "LINTERNA")) return "Focos y Luminarias LED";
        if (ContieneAlguno(u, "PILA ", "PILAS ", "PILAN ", "BATERIA", "BATERÍA")) return "Pilas y Baterías";
        if (ContieneAlguno(u, "GUANTE", "GUANTES")) return "Guantes de Seguridad";
        if (ContieneAlguno(u, "CASCO", "BARBOQUEJO")) return "Cascos y Protección Cabeza";
        if (ContieneAlguno(u, "BOTA", "BORCEGUI", "CALZADO")) return "Botas y Calzado de Seguridad";
        if (ContieneAlguno(u, "LENTE", "GAFA")) return "Lentes y Protección Visual";
        if (ContieneAlguno(u, "CHALECO", "CONO", "SEÑAL")) return "Chalecos y Señalización";
        if (ContieneAlguno(u, "JABON", "JABÓN", "DETERGENTE", "ARIEL", "AXION", "LAVATRASTES", "FABULOSO", "PINOL", "CLORALEX", "LYSOL", "WINDEX", "LIMPIADOR", "DESINFECTANTE")) return "Jabón, Detergentes y Limpiadores";
        if (ContieneAlguno(u, "MICROFIBRA", "TOALLA", "PAPEL HIGI", "SERVILLETA", "ESTOPA")) return "Toallas, Papel y Microfibras";
        if (ContieneAlguno(u, "ESCOBA", "TRAPEADOR", "CEPILLO", "RECOGEDOR", "FIBRA MULTIUSOS")) return "Escobas, Cepillos y Trapeadores";
        if (ContieneAlguno(u, "BOLSA NEGRA", "BOLS BAS", "BOLSA VERDE", "COSTAL")) return "Bolsas para Basura";
        if (ContieneAlguno(u, "INSECTICIDA", "INSEC ", "H-24", "H24", "MOSQUITO", "FUMIGADOR")) return "Insecticidas y Plagas";
        if (ContieneAlguno(u, "PIJA", "TAQUETE")) return "Pijas y Taquetes";
        if (ContieneAlguno(u, "TORNILLO", "TUERCA", "ARANDELA", "RONDANA", "ESPÁRRAGO")) return "Tornillos y Tuercas";
        if (ContieneAlguno(u, "DISCO", "LIJA", "SEGUETA")) return "Discos de Corte y Lija";
        if (ContieneAlguno(u, "BROCA", "PUNTA", "DADO", "BROQUERO")) return "Brocas y Puntas";
        if (ContieneAlguno(u, "IMPERMEABILI", "IMPERCOAT", "IMPERPRIM", "FESTER", "PINTURA", "ESMALTE", "THINNER", "AGUARRAS")) return "Impermeabilizantes y Pintura";
        if (ContieneAlguno(u, "BROCHA", "RODILLO", "FELPA", "MANERAL")) return "Brochas, Rodillos y Felpas";
        if (ContieneAlguno(u, "SILICON", "SILICÓN", "SELLADOR", "DURETAN", "TANGIT", "PEGAMENTO", "ADHECRETO")) return "Selladores, Silicones y Pegamentos";
        if (ContieneAlguno(u, "PVC", "CODO", "TEE", "YEE", "COPLE", "REDUCCION", "TAPON", "NIPLE", "TUBERIA", "TUBERÍA")) return "Tubería y Conexiones PVC";
        if (ContieneAlguno(u, "MANGUERA", "CONECTOR RÁPIDO", "CONECTOR RAPIDO", "LLAVE MANGUERA")) return "Mangueras y Conexiones Hidráulicas";

        return string.Empty;
    }

    private static void AutoAsignarFamiliasInicialesSiVacio(SqliteConnection conn)
    {
        var pendientes = new List<(int Id, string Nombre)>();
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT Id, Nombre FROM Productos WHERE TRIM(COALESCE(Familia, '')) = ''";
            using var r = sel.ExecuteReader();
            while (r.Read())
            {
                pendientes.Add((r.GetInt32(0), r.GetString(1)));
            }
        }

        if (pendientes.Count == 0) return;

        using var tx = conn.BeginTransaction();
        foreach (var (id, nom) in pendientes)
        {
            string fam = ExtraerFamiliaSugerida(nom);
            if (!string.IsNullOrWhiteSpace(fam))
            {
                using var up = conn.CreateCommand();
                up.Transaction = tx;
                up.CommandText = "UPDATE Productos SET Familia = $fam WHERE Id = $id";
                up.Parameters.AddWithValue("$fam", fam);
                up.Parameters.AddWithValue("$id", id);
                up.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private static string NormalizarTextoCodigo(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        string s = input.Trim().ToUpperInvariant()
            .Replace('\'', '-')
            .Replace('’', '-')
            .Replace('´', '-')
            .Replace('`', '-')
            .Replace('/', '-')
            .Replace('?', '-')
            .Replace('_', '-');

        while (s.Contains("--"))
            s = s.Replace("--", "-");

        return s;
    }

    public static Producto? BuscarProductoPorCodigo(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        string clean = codigo.Trim();
        string norm = NormalizarTextoCodigo(clean);
        string cleanSinCeros = clean.TrimStart('0');
        string normSinCeros = norm.TrimStart('0');

        var productos = ObtenerProductos();

        // 1. Coincidencia exacta o normalizada por CodigoInterno, CodigoBarras o CodigoEfectivo
        var matchDirecto = productos.FirstOrDefault(p =>
            string.Equals(p.CodigoInterno?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.CodigoBarras?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.CodigoEfectivo?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizarTextoCodigo(p.CodigoInterno), norm, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizarTextoCodigo(p.CodigoBarras), norm, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizarTextoCodigo(p.CodigoEfectivo), norm, StringComparison.OrdinalIgnoreCase));

        if (matchDirecto != null)
            return matchDirecto;

        // 2. Coincidencia ignorando ceros a la izquierda en códigos de barras numéricos (EAN/UPC)
        if (!string.IsNullOrWhiteSpace(cleanSinCeros) && cleanSinCeros.Length >= 4)
        {
            var matchSinCeros = productos.FirstOrDefault(p =>
                (!string.IsNullOrWhiteSpace(p.CodigoBarras) &&
                 string.Equals(p.CodigoBarras.Trim().TrimStart('0'), cleanSinCeros, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.CodigoBarras) &&
                 string.Equals(NormalizarTextoCodigo(p.CodigoBarras).TrimStart('0'), normSinCeros, StringComparison.OrdinalIgnoreCase)));

            if (matchSinCeros != null)
                return matchSinCeros;
        }

        // 3. Coincidencia flexible tipo PRD-1 -> PRD-0001 o escribir solo el número "441" -> PRD-0441
        var partesC = norm.Split('-', StringSplitOptions.RemoveEmptyEntries);
        int numBuscado = -1;
        bool buscarPorNumeroPrd =
            (partesC.Length == 2 && partesC[0].Equals("PRD", StringComparison.OrdinalIgnoreCase) && int.TryParse(partesC[1], out numBuscado)) ||
            (partesC.Length == 1 && clean.Length <= 4 && int.TryParse(clean, out numBuscado) && numBuscado > 0);

        if (buscarPorNumeroPrd && numBuscado > 0)
        {
            var matchPrd = productos.FirstOrDefault(p =>
            {
                var partesP = NormalizarTextoCodigo(p.CodigoInterno).Split('-', StringSplitOptions.RemoveEmptyEntries);
                return partesP.Length == 2 &&
                       partesP[0].Equals("PRD", StringComparison.OrdinalIgnoreCase) &&
                       int.TryParse(partesP[1], out int numP) &&
                       numP == numBuscado;
            });
            if (matchPrd != null)
                return matchPrd;
        }

        // 4. Coincidencia por ClaveProveedor en ProductoProveedorMapa (ej. código de fabricante escaneado)
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ProductoId FROM ProductoProveedorMapa
WHERE UPPER(TRIM(ClaveProveedor)) = $c OR UPPER(TRIM(ClaveProveedor)) = $n
LIMIT 1";
        cmd.Parameters.AddWithValue("$c", clean.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$n", norm);
        var pidObj = cmd.ExecuteScalar();
        if (pidObj != null && pidObj != DBNull.Value)
        {
            int pid = Convert.ToInt32(pidObj);
            return productos.FirstOrDefault(p => p.Id == pid);
        }

        return null;
    }

    public static int GuardarProducto(Producto p)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(p.CodigoInterno))
            p.CodigoInterno = GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
        if (string.IsNullOrWhiteSpace(p.CodigoBarras))
            p.CodigoBarras = p.CodigoInterno;

        if (string.IsNullOrWhiteSpace(p.Familia))
            p.Familia = ExtraerFamiliaSugerida(p.Nombre);

        if (p.Id > 0)
        {
            cmd.CommandText = @"
UPDATE Productos SET
    CodigoInterno=$ci, CodigoBarras=$cb, Nombre=$nom, Descripcion=$desc, CategoriaId=$cat, Familia=$fam,
    Marca=$mar, Modelo=$mod, Medidas=$med, CapacidadElectrica=$cap, Especificacion1=$esp1, Especificacion2=$esp2,
    UnidadCompra=$uc, UnidadAlmacen=$ua, FactorConversion=$fc, StockActual=$sa,
    StockMinimo=$sm, UltimoCostoCompra=$ucc, Ubicacion=$ubi, ImagenRuta=$img, EsHerramienta=$her
WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", p.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO Productos (CodigoInterno, CodigoBarras, Nombre, Descripcion, CategoriaId, Familia, Marca, Modelo, Medidas, CapacidadElectrica, Especificacion1, Especificacion2, UnidadCompra, UnidadAlmacen, FactorConversion, StockActual, StockReservadoProyecto, StockMinimo, UltimoCostoCompra, Ubicacion, ImagenRuta, EsHerramienta)
VALUES ($ci, $cb, $nom, $desc, $cat, $fam, $mar, $mod, $med, $cap, $esp1, $esp2, $uc, $ua, $fc, $sa, 0, $sm, $ucc, $ubi, $img, $her);
SELECT last_insert_rowid();";
        }

        cmd.Parameters.AddWithValue("$ci", p.CodigoInterno.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$cb", p.CodigoBarras.Trim());
        cmd.Parameters.AddWithValue("$nom", p.Nombre.Trim());
        cmd.Parameters.AddWithValue("$desc", (p.Descripcion ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$cat", p.CategoriaId > 0 ? p.CategoriaId : 1);
        cmd.Parameters.AddWithValue("$fam", (p.Familia ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$mar", (p.Marca ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$mod", (p.Modelo ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$med", (p.Medidas ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$cap", (p.CapacidadElectrica ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$esp1", (p.Especificacion1 ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$esp2", (p.Especificacion2 ?? string.Empty).Trim());
        p.UnidadCompra = NormalizarUnidadMedida(p.UnidadCompra);
        p.UnidadAlmacen = NormalizarUnidadMedida(p.UnidadAlmacen);
        cmd.Parameters.AddWithValue("$uc", p.UnidadCompra);
        cmd.Parameters.AddWithValue("$ua", p.UnidadAlmacen);
        cmd.Parameters.AddWithValue("$fc", (double)Math.Max(0.0001m, p.FactorConversion));
        cmd.Parameters.AddWithValue("$sa", (double)p.StockActual);
        cmd.Parameters.AddWithValue("$sm", (double)p.StockMinimo);
        cmd.Parameters.AddWithValue("$ucc", (double)p.UltimoCostoCompra);
        cmd.Parameters.AddWithValue("$ubi", p.Ubicacion.Trim());
        cmd.Parameters.AddWithValue("$img", p.ImagenRuta ?? string.Empty);
        cmd.Parameters.AddWithValue("$her", p.EsHerramienta ? 1 : 0);

        if (p.Id > 0)
        {
            cmd.ExecuteNonQuery();
            return p.Id;
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void MigrarCategoriasYReclasificarCatalogo(SqliteConnection conn)
    {
        var definicionesCategorias = new (int Id, string Nombre, string Icono, string Color, string E1, string E2, string E3)[]
        {
            (1,  "Refrigeración y Aire Acondicionado (HVAC)", "Temperature24",   "#0284C7", "Capacidad / Tonelaje",         "Capacidad Eléctrica (V/F/Hz)", "Gas / Valor Técnico / Equipo"),
            (2,  "Eléctrico, Iluminación y Control",          "Flash24",         "#EAB308", "Capacidad Eléctrica / Voltaje", "Corriente / Potencia",         "Polos / Calibre / Protección IP"),
            (3,  "Plomería, Tubería y Conexiones",            "Water24",         "#0EA5E9", "Diámetro / Conexión",           "Material y Cédula",            "Tipo de Unión / Presión"),
            (4,  "Herramientas, Medición y Maquinaria",       "Wrench24",        "#EA580C", "Alimentación / Tipo",           "Potencia / Capacidad",         "Encastre / Punta / Especificación"),
            (5,  "Pintura, Impermeabilizantes y Obra",        "PaintBrush24",    "#8B5CF6", "Color / Acabado",               "Contenido / Rendimiento",      "Base / Superficie / Durabilidad"),
            (6,  "Tornillería, Fijación y Herrajes",          "Settings24",      "#64748B", "Paso / Calibre / Tipo",         "Material / Acabado",           "Capacidad / Resistencia"),
            (7,  "Soldadura, Químicos y Consumibles Técnicos","Beaker24",        "#D97706", "Contenido / Presentación",      "Aplicación / Especificación",  "Grano / Pureza / Temperatura"),
            (8,  "Seguridad Industrial y EPP",                "ShieldCheckmark24","#16A34A","Talla",                         "Material / Recubrimiento",     "Color / Norma / Protección"),
            (9,  "Limpieza, Higiene y Jardinería",            "Sparkle24",       "#14B8A6", "Material / Tipo",               "Contenido / Presentación",     "Color / Aroma"),
            (10, "Papelería, Oficina y Comedor",              "Document24",      "#6366F1", "Tamaño / Formato",              "Color / Presentación",         "Detalle / Conectividad"),
            (11, "Servicios, Peajes y Gastos Operativos",     "Receipt24",       "#78716C", "Tipo de Servicio / Gasto",      "Ruta / Referencia",            "Vigencia / Observación")
        };

        using var tx = conn.BeginTransaction();

        foreach (var c in definicionesCategorias)
        {
            using var upCat = conn.CreateCommand();
            upCat.Transaction = tx;
            upCat.CommandText = @"
INSERT INTO Categorias (Id, Nombre, Icono, ColorHex, EtiquetaCampo1, EtiquetaCampo2, EtiquetaCampo3)
VALUES ($id, $n, $i, $col, $e1, $e2, $e3)
ON CONFLICT(Id) DO UPDATE SET
    Nombre = excluded.Nombre,
    Icono = excluded.Icono,
    ColorHex = excluded.ColorHex,
    EtiquetaCampo1 = excluded.EtiquetaCampo1,
    EtiquetaCampo2 = excluded.EtiquetaCampo2,
    EtiquetaCampo3 = excluded.EtiquetaCampo3;";
            upCat.Parameters.AddWithValue("$id", c.Id);
            upCat.Parameters.AddWithValue("$n", c.Nombre);
            upCat.Parameters.AddWithValue("$i", c.Icono);
            upCat.Parameters.AddWithValue("$col", c.Color);
            upCat.Parameters.AddWithValue("$e1", c.E1);
            upCat.Parameters.AddWithValue("$e2", c.E2);
            upCat.Parameters.AddWithValue("$e3", c.E3);
            upCat.ExecuteNonQuery();
        }

        using var checkMig = conn.CreateCommand();
        checkMig.Transaction = tx;
        checkMig.CommandText = "SELECT COUNT(*) FROM ConfiguracionSistema WHERE Clave = 'Reclasificacion11CategoriasV1';";
        bool yaReclasificado = Convert.ToInt32(checkMig.ExecuteScalar()) > 0;

        if (!yaReclasificado)
        {
            var productosExistentes = new List<(int Id, string Nombre, string Descripcion)>();
            using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT Id, Nombre, Descripcion FROM Productos;";
                using var r = sel.ExecuteReader();
                while (r.Read())
                {
                    productosExistentes.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? string.Empty : r.GetString(2)));
                }
            }

            foreach (var item in productosExistentes)
            {
                var info = ClasificarYExtraerEspecificacionesProducto(item.Nombre, item.Descripcion);
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = @"
UPDATE Productos SET
    CategoriaId = $cat,
    Marca = $mar,
    Modelo = $mod,
    Medidas = $med,
    CapacidadElectrica = $cap,
    Especificacion1 = $esp1,
    Especificacion2 = $esp2,
    EsHerramienta = CASE WHEN $cat = 4 AND EsHerramienta = 1 THEN 1 ELSE EsHerramienta END
WHERE Id = $id;";
                upd.Parameters.AddWithValue("$id", item.Id);
                upd.Parameters.AddWithValue("$cat", info.CategoriaId);
                upd.Parameters.AddWithValue("$mar", info.Marca);
                upd.Parameters.AddWithValue("$mod", info.Modelo);
                upd.Parameters.AddWithValue("$med", info.Medidas);
                upd.Parameters.AddWithValue("$cap", info.CapacidadElectrica);
                upd.Parameters.AddWithValue("$esp1", info.Especificacion1);
                upd.Parameters.AddWithValue("$esp2", info.Especificacion2);
                upd.ExecuteNonQuery();
            }

            using var mark = conn.CreateCommand();
            mark.Transaction = tx;
            mark.CommandText = "INSERT OR REPLACE INTO ConfiguracionSistema (Clave, Valor) VALUES ('Reclasificacion11CategoriasV1', '1');";
            mark.ExecuteNonQuery();
        }

        // Asegurar que artículos consumibles, EPP o limpieza que no tienen activos individuales HER-xxxx no queden marcados como EsHerramienta=1 por error,
        // pero conservando cualquier artículo que el usuario haya marcado legítimamente como herramienta.
        using (var fixHer = conn.CreateCommand())
        {
            fixHer.Transaction = tx;
            fixHer.CommandText = @"
UPDATE Productos
SET CategoriaId = 8, EsHerramienta = 0
WHERE (UPPER(Nombre) LIKE '%CASCO%'
    OR UPPER(Nombre) LIKE '%GUANTE%'
    OR UPPER(Nombre) LIKE '%CHALECO%'
    OR UPPER(Nombre) LIKE '%ALTA VISIBILIDAD%'
    OR UPPER(Nombre) LIKE '%ALTA VISIBILIAD%'
    OR UPPER(Nombre) LIKE '%CONO DE PRECAUCI%'
    OR UPPER(Nombre) LIKE '%RESPIRADOR%')
  AND Id NOT IN (SELECT DISTINCT ProductoId FROM HerramientasActivos);

UPDATE Productos
SET EsHerramienta = 0
WHERE UPPER(Nombre) LIKE '%FUMIGADOR%'
  AND Id NOT IN (SELECT DISTINCT ProductoId FROM HerramientasActivos);

UPDATE Productos
SET CategoriaId = 4
WHERE (UPPER(Nombre) LIKE '%VACUOMETRO%'
    OR UPPER(Nombre) LIKE '%VACUÓMETRO%'
    OR UPPER(Nombre) LIKE '%ESCALERA%'
    OR UPPER(Nombre) LIKE '%MEDIDOR DE TEMPERATURA%'
    OR UPPER(Nombre) LIKE '%HIDROLAVADORA%');";
            fixHer.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static void SincronizarHerramientasCatalogoConDirectorio(SqliteConnection? connExterna = null)
    {
        bool cerrar = false;
        SqliteConnection conn;
        if (connExterna != null)
        {
            conn = connExterna;
        }
        else
        {
            conn = new SqliteConnection(ConnectionString);
            conn.Open();
            cerrar = true;
        }

        try
        {
            // 1. Limpieza única de artículos cuyo StockActual fue incrementado artificialmente al abrir/guardar el modal de herramienta sin compra/existencia real
            using (var chkClean = conn.CreateCommand())
            {
                chkClean.CommandText = "SELECT COUNT(*) FROM ConfiguracionSistema WHERE Clave = 'LimpiezaStockHerramientasModalV2';";
                bool yaLimpiado = Convert.ToInt32(chkClean.ExecuteScalar()) > 0;
                if (!yaLimpiado)
                {
                    using var fixCmd = conn.CreateCommand();
                    fixCmd.CommandText = @"
-- PRD-0137 y PRD-0316 tenían StockActual = 0 antes de marcarse como herramienta (creó 1 existencia al abrir/guardar el modal)
UPDATE Productos SET StockActual = 0 WHERE CodigoInterno IN ('PRD-0137', 'PRD-0316') AND StockActual = 1;

-- PRD-0182 (Casco de seguridad color naranja) sí cuenta con 8 unidades reales
UPDATE Productos SET StockActual = 8 WHERE CodigoInterno = 'PRD-0182';
INSERT OR IGNORE INTO HerramientasActivos (Id, ProductoId, CodigoIndividual, NumeroSerie, MarcaModelo, Estado, Ubicacion, EncargadoActual, ProyectoActual, NotasEstado) VALUES
(46, 192, 'HER-0037', 'TCASN-1', 'CASCO DE SEGURIDAD COLOR NARANJA', 'Disponible', 'Almacén General', '', '', 'Clave SAT: 46181704 | ID Prov: TCASN | Creado desde Factura U-243917'),
(47, 192, 'HER-0038', 'TCASN-2', 'CASCO DE SEGURIDAD COLOR NARANJA', 'Disponible', 'Almacén General', '', '', 'Clave SAT: 46181704 | ID Prov: TCASN | Creado desde Factura U-243917'),
(48, 192, 'HER-0039', 'TCASN-3', 'CASCO DE SEGURIDAD COLOR NARANJA', 'Disponible', 'Almacén General', '', '', 'Clave SAT: 46181704 | ID Prov: TCASN | Creado desde Factura U-243917'),
(49, 192, 'HER-0040', 'TCASN-4', 'CASCO DE SEGURIDAD COLOR NARANJA', 'Disponible', 'Almacén General', '', '', 'Clave SAT: 46181704 | ID Prov: TCASN | Creado desde Factura U-243917');

INSERT OR REPLACE INTO ConfiguracionSistema (Clave, Valor) VALUES ('LimpiezaStockHerramientasModalV2', '1');";
                    fixCmd.ExecuteNonQuery();
                }
            }

            // 2. Eliminar fichas HER-xxxx sobrantes que estén 'Disponible' y sin historial en PrestamoPartidas cuando superan el StockActual real del producto
            var stockPorProducto = new Dictionary<int, int>();
            using (var cmdSt = conn.CreateCommand())
            {
                cmdSt.CommandText = "SELECT Id, COALESCE(StockActual, 0) FROM Productos;";
                using var rs = cmdSt.ExecuteReader();
                while (rs.Read())
                {
                    stockPorProducto[rs.GetInt32(0)] = Math.Max(0, (int)Math.Round(rs.GetDouble(1)));
                }
            }

            var fichasPorProducto = new Dictionary<int, List<(int HerId, string Estado, int NumPrestamos)>>();
            using (var cmdF = conn.CreateCommand())
            {
                cmdF.CommandText = @"
SELECT h.Id, h.ProductoId, COALESCE(h.Estado, 'Disponible'),
       (SELECT COUNT(*) FROM PrestamoPartidas pp WHERE pp.HerramientaActivoId = h.Id)
FROM HerramientasActivos h
ORDER BY h.Id DESC;";
                using var rf = cmdF.ExecuteReader();
                while (rf.Read())
                {
                    int hid = rf.GetInt32(0);
                    int pid = rf.GetInt32(1);
                    string est = rf.GetString(2);
                    int nprest = rf.GetInt32(3);
                    if (!fichasPorProducto.TryGetValue(pid, out var list))
                    {
                        list = new List<(int, string, int)>();
                        fichasPorProducto[pid] = list;
                    }
                    list.Add((hid, est, nprest));
                }
            }

            var idsHerParaEliminar = new List<int>();
            foreach (var kv in fichasPorProducto)
            {
                int pid = kv.Key;
                int stockEsperado = stockPorProducto.TryGetValue(pid, out int st) ? st : 0;
                var activas = kv.Value
                    .Where(x => !string.Equals(x.Estado, "Extraviada / Baja", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int exceso = activas.Count - stockEsperado;
                if (exceso > 0)
                {
                    foreach (var ficha in activas)
                    {
                        if (exceso <= 0) break;
                        if (string.Equals(ficha.Estado, "Disponible", StringComparison.OrdinalIgnoreCase) && ficha.NumPrestamos == 0)
                        {
                            idsHerParaEliminar.Add(ficha.HerId);
                            exceso--;
                        }
                    }
                }
            }

            if (idsHerParaEliminar.Count > 0)
            {
                foreach (int hid in idsHerParaEliminar)
                {
                    using var delHer = conn.CreateCommand();
                    delHer.CommandText = "DELETE FROM HerramientasActivos WHERE Id = $id;";
                    delHer.Parameters.AddWithValue("$id", hid);
                    delHer.ExecuteNonQuery();
                }
            }

            int maxNum = 0;
            var conteoPorProducto = new Dictionary<int, int>();

            using (var cmdHer = conn.CreateCommand())
            {
                cmdHer.CommandText = "SELECT ProductoId, CodigoIndividual, COALESCE(Estado, 'Disponible') FROM HerramientasActivos;";
                using var r = cmdHer.ExecuteReader();
                while (r.Read())
                {
                    int pid = r.GetInt32(0);
                    string cod = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                    string est = r.IsDBNull(2) ? "Disponible" : r.GetString(2);

                    if (!string.Equals(est, "Extraviada / Baja", StringComparison.OrdinalIgnoreCase))
                    {
                        conteoPorProducto[pid] = conteoPorProducto.TryGetValue(pid, out int c) ? c + 1 : 1;
                    }

                    if (cod.StartsWith("HER-", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(cod[4..].Trim(), out int n) && n > maxNum)
                    {
                        maxNum = n;
                    }
                }
            }

            var herramientasPorCrear = new List<(int Id, string Nombre, string Marca, string Modelo, string Ubicacion, int CantidadFaltante)>();
            using (var cmdProd = conn.CreateCommand())
            {
                cmdProd.CommandText = @"
SELECT Id, Nombre, COALESCE(Marca, ''), COALESCE(Modelo, ''), COALESCE(Ubicacion, 'Gabinete H-1'), COALESCE(StockActual, 0)
FROM Productos
WHERE EsHerramienta = 1
ORDER BY Id;";
                using var rp = cmdProd.ExecuteReader();
                while (rp.Read())
                {
                    int pid = rp.GetInt32(0);
                    double stock = rp.GetDouble(5);
                    int registradasActivas = conteoPorProducto.TryGetValue(pid, out int c) ? c : 0;
                    int esperadas = Math.Max(0, (int)Math.Round(stock));

                    if (esperadas > registradasActivas)
                    {
                        int faltantes = Math.Min(50, esperadas - registradasActivas);
                        herramientasPorCrear.Add((
                            pid,
                            rp.GetString(1),
                            rp.GetString(2),
                            rp.GetString(3),
                            rp.GetString(4),
                            faltantes));
                    }
                }
            }

            if (herramientasPorCrear.Count == 0)
                return;

            using var tx = conn.BeginTransaction();
            foreach (var h in herramientasPorCrear)
            {
                string marcaModelo = $"{h.Marca} {h.Modelo}".Trim();
                if (string.IsNullOrWhiteSpace(marcaModelo))
                    marcaModelo = h.Nombre;
                string ubicacion = string.IsNullOrWhiteSpace(h.Ubicacion) || h.Ubicacion == "A-01"
                    ? "Gabinete H-1"
                    : h.Ubicacion;

                for (int i = 0; i < h.CantidadFaltante; i++)
                {
                    maxNum++;
                    string codigo = $"HER-{maxNum:D4}";

                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
INSERT INTO HerramientasActivos (ProductoId, CodigoIndividual, NumeroSerie, MarcaModelo, Estado, Ubicacion, EncargadoActual, ProyectoActual, NotasEstado)
VALUES ($pid, $cod, 'S/N', $mm, 'Disponible', $ub, '', '', 'Sincronizada desde Catálogo de Herramientas');";
                    ins.Parameters.AddWithValue("$pid", h.Id);
                    ins.Parameters.AddWithValue("$cod", codigo);
                    ins.Parameters.AddWithValue("$mm", marcaModelo);
                    ins.Parameters.AddWithValue("$ub", ubicacion);
                    ins.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        finally
        {
            if (cerrar)
                conn.Close();
        }
    }

    public static (int CategoriaId, string Marca, string Modelo, string Medidas, string CapacidadElectrica, string Especificacion1, string Especificacion2)
        ClasificarYExtraerEspecificacionesProducto(string? nombreRaw, string? descripcionRaw = null)
    {
        string nombre = (nombreRaw ?? string.Empty).Trim();
        string u = nombre.ToUpperInvariant();

        // 1. Determinar Categoría (1 a 11)
        int catId = DeterminarCategoriaIdPorNombre(u);

        // 2. Extraer Marca conocida
        string marca = ExtraerMarcaConocida(u);

        // 3. Extraer Modelo / Número de parte
        string modelo = ExtraerModeloConocido(nombre, u);

        // 4. Extraer Medidas / Dimensiones físicas
        string medidas = ExtraerMedidasConocidas(u);

        // 5. Extraer Campos Dinámicos 1, 2 y 3 según la Categoría
        var (c1, c2, c3) = ExtraerCamposEspecificosPorCategoria(catId, nombre, u);

        return (catId, marca, modelo, medidas, c1, c2, c3);
    }

    private static int DeterminarCategoriaIdPorNombre(string u)
    {
        if (ContieneAlguno(u, "AUTOVÍA", "AUTOVIA", "PEAJE", "NRU ", "CASETA", "FLETE"))
            return 11; // Servicios, Peajes y Gastos Operativos

        if (ContieneAlguno(u, "CASCO DE SEGURIDAD", "BARBOQUEJO", "LENTES DE SEGURIDAD", "LENTE CON RECUBRIMIENTO", "GUANTE", "PONCHO UNITALLA", "BANDA DE PLÁSTICOPROHIBIDO", "PROHIBIDO", "CHALECO", "ALTA VISIBILIDAD", "ALTA VISIBILIAD", "CONO DE PRECAUCIÓN", "CONO DE PRECAUCION", "RESPIRADOR"))
            return 8; // Seguridad Industrial y EPP

        if (ContieneAlguno(u, "CARPETA", "FOLDER", "ENGARGO", "OPALINA", "BOLIG", "LECTOR TARJETAS CLIP", "STARLINK", "GATORADE", "SPLENDA", "TENEDORES", "CUCHASOP", "SERVILLETA", "SERVI BOLSA"))
            return 10; // Papelería, Oficina y Comedor

        if (ContieneAlguno(u, "MICROFIBRA", "PANO MICROFIBRA", "TRAPEADOR", "ESCOBA", "CEPILLO", "FIBRA MULTIUSOS", "BOLSA NEGRA", "BOLS BAS", "BOLSA VERDE", "BOLSA REUTILIZABLE", "COSTALITOS", "CLORALEX", "BLANQ", "FABULOSO", "PINOL", "LYSOL", "DESINFECTANTE", "WINDEX", "LIMPIADOR", "ARIEL", "AXION", "LAVATRASTES", "AIR WICK", "GLD AER", "PAPEL HIGIÉNICO", "PAPEL HIGIENICO", "TOALLA SCOTT", "BANOS CLX", "PROTECTO ES MED", "PRICESA 200G", "INSECTICIDA", "INSEC H24", "REP.DE MOSQUITOS", "TRAMPA H-24", "HERBICESPED", "FUMIGADOR", "CUBETA 4L")
            && !ContieneAlguno(u, "ESCOBA METÁLICA", "ESCOBA JAR", "MICROFIBRA 3/8"))
            return 9; // Limpieza, Higiene y Jardinería

        if (ContieneAlguno(u, "MINISPLIT", "MINI SPLIT", "CONDENSADOR", "EVAPORADOR", "COMPRESOR", "REFRIGERANTE", "FAN COIL", "BOMBA DE DRENAJE", "BOMBA DE CONDENSADO", "VALVULA DE EXPANSION", "VALVULA SERVICIO", "FILTRO DESHIDRATADOR", "SENSOR TRIPLE", "SENSOR DOBLE", "SENSOR DE AMBIENTE", "SENSOR DE POZO", "SENSOR TRANE", "PRESOSTATO", "CONTROLADOR ELECTRONICO DIGITAL LG", "CONTROL UNIVERSAL MIRAGE", "TUBERIA DE EXPANSION", "CHAROLA DE DRENAJE", "TURBINA PARA FAN"))
            return 1; // Refrigeración y Aire Acondicionado (HVAC)

        if (ContieneAlguno(u, "IMPERMEABILI", "IMPERCOAT", "IMPERPRIM", "FESTERMIX", "NAYA SBS", "LAVILUX", "LAVIFLEX", "PINT HGAR", "BROCHA", "RODILLO", "MANERAL", "FELPA", "PLASTIPROTECTOR", "CINTA AZUL", "CINTA MASKING", "DURETAN", "SILICON", "CEMENTO BLANCO", "THINNER", "THINER", "AGUARRAS", "GUARDAPOLVOS"))
            return 5; // Pintura, Impermeabilizantes y Obra

        if (ContieneAlguno(u, "HIDRAULIC", "PVC SAN", "YEE PVC", "CODO 90", "TAPON MACHO", "TAPÓN MACHO", "TAPON HIDRAULICO", "TAPON INSERCCION", "NIPLE GALVANIZADO", "REDUCCION BUSHIN", "BRIDA DE", "TUERCA UNION", "MANGUERA", "LLAVE P/MANGUERA", "LLAVE MANGUERA", "CONECTOR RÁPIDO", "CONECTORES RÁPIDOS", "CONECTORE HEMBRA PLÁSTICO", "CONECTOR PLÁSTICO, HEMBRA", "BOQUILLA MINI")
            && !u.Contains("LAVA-1500P"))
            return 3; // Plomería, Tubería y Conexiones

        if (ContieneAlguno(u, "TORNILLO", "PIJA", "ARANDELA", "TAQUETE", "MÉNSULA", "MENSULA", "ESQUINERO DE UNION", "CONECTOR MET", "CABLE DE ACERO", "NUDO PARA CABLE", "GUARDACABO", "TENSORES ELASTICOS", "SUJETADORES CON MATRACA", "BANDA GATES", "RODAMIENTO DE BOLAS"))
            return 6; // Tornillería, Fijación y Herrajes

        if (ContieneAlguno(u, "VARITA DE PLATA", "SOLDADURA DE PLATA", "PASTA FUNDENTE", "FUNDENTE", "TANGIT", "PEGAMENTO PARA PVC", "LIJA", "DISCOS DE LIJA", "CINTA TEFLON", "CINTA PARA DUCTO", "CINTA DE ALUMINIO", "SCOTCH CINTA DE MONTAJE", "SOLVENTE DIELECTRICO", "ALCOHOL ISOPROPILICO", "LUBRICANTE", "GRASA AUTOMOTRIZ", "ÁCIDO MURIATICO", "ACIDO MURIATICO", "ESTOPA", "PASTILLAS SAL"))
            return 7; // Soldadura, Químicos y Consumibles Técnicos

        if (ContieneAlguno(u, "FLEXOMETRO", "VENTILADOR PEDESTAL", "SOPLADORA", "DESARMADOR", "ASPIRADORA", "HIDROLAVADORA", "LAVA-1500P", "ESCOBA JAR", "ESCOBA METÁLICA", "PUNTAS DE IMPACTO", "DADOS MAG", "SEGUETA", "BROQUERO", "BROCA", "MULTÍMETRO", "MULTIMETRO", "VACUOMETRO", "VACUÓMETRO", "MANOMETRO", "MANÓMETRO", "MEDIDOR DE TEMPERATURA", "ESCALERA", "TALADRO", "ROTOMARTILLO", "ESMERIL", "CABLES DE PRUEBA", "MACHETE", "PALA CUADRADA", "LINTERNA", "LAMPARA PORTATIL", "LAMPARA DE TRABAJO", "LÁMPARA DE TRABAJO"))
            return 4; // Herramientas, Medición y Maquinaria

        return 2; // Eléctrico, Iluminación y Control
    }

    private static bool ContieneAlguno(string textoUpper, params string[] terminos)
    {
        foreach (var t in terminos)
        {
            if (textoUpper.Contains(t, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ExtraerMarcaConocida(string u)
    {
        var marcas = new (string Patron, string NombreMarca)[]
        {
            (" LG ", "LG"), ("PANASONIC", "Panasonic"), ("TRANE", "Trane"), ("MIRAGE", "Mirage"),
            ("EMERSON", "Emerson"), ("SANHUA", "Sanhua"), ("GENETRON", "Genetron"), ("MYVENT", "MyVent"),
            ("TECNOLITE", "Tecnolite"), ("VOLTECK", "Volteck"), ("PRETUL", "Pretul"), ("FIERO", "Fiero"),
            ("TRUPER", "Truper"), ("BTDIN", "Bticino"), ("ARGOS", "Argos"), ("3M", "3M"), ("TUK", "Tuk"),
            ("OSEL", "Osel"), ("LAVILUX", "Lavilux"), ("LAVIFLEX", "Laviflex"), ("IMPERCOAT", "Impercoat"),
            ("FESTERMIX", "Fester"), ("IMPERPRIM", "Fester"), ("DURETAN", "Duretan / Pennsylvania"),
            ("NAYA SBS", "Naya"), ("ADHECRETO", "Adhecreto"), ("TANGIT", "Tangit / Henkel"),
            ("SISTA", "Sista"), ("SCOTCH", "Scotch / 3M"), ("GATES", "Gates"), ("OLMECA", "Olmeca"),
            ("GIM", "GIM"), ("SPOC", "Spoc"), ("CARRAND", "Carrand"), ("REYNERA", "Reynera"),
            ("CLORALEX", "Cloralex"), ("FABULOSO", "Fabuloso"), ("PINOL", "Pinol"), ("LYSOL", "Lysol"),
            ("WINDEX", "Windex"), ("ARIEL", "Ariel"), ("AXION", "Axion"), ("AIR WICK", "Air Wick"),
            ("GLD AER", "Glade"), ("SCOTT", "Scott"), ("SUAVEL", "Suavel"), ("FANCY", "Fancy"),
            ("H-24", "H-24"), ("H24", "H-24"), ("BIC", "BIC"), ("STARLINK", "Starlink"),
            ("CLIP PLUS", "Clip"), ("SPLENDA", "Splenda"), ("GATORADE", "Gatorade"), ("UNISAL", "Unisal"),
            ("AUTOVÍA DEL MAYAB", "Autovía del Mayab")
        };

        string padded = $" {u} ";
        foreach (var (patron, marca) in marcas)
        {
            if (padded.Contains(patron, StringComparison.OrdinalIgnoreCase))
                return marca;
        }
        return string.Empty;
    }

    private static string ExtraerModeloConocido(string nombre, string u)
    {
        var regexModelos = new[]
        {
            @"\b(EB[GRD]\d{8}|AKB\d{8}|EA[PU]\d{8}|AHA\d{8}|AGK\d{8}|AJR\d{8}|MJX\d{8}|4681A\d{5}[A-Z]?)\b",
            @"\b(CSHW\w+|C-SBP\w+|OTP\d+\w+|IU2C\d+\w*|MVPP\d+|FEFL\d+|ER\d+-\w+|MRC4K2E|APFD-\w+|6625-90-247|100UFOLED\w+|150LQLED\w+|LAVA-1500P|ASPI-03|EP-22P|607-ZZ|D245)\b",
            @"\b(GATES\s*[A-Z]{1,2}\d+)\b"
        };

        foreach (var pat in regexModelos)
        {
            var m = System.Text.RegularExpressions.Regex.Match(u, pat);
            if (m.Success)
                return m.Groups[1].Value.Trim();
        }
        return string.Empty;
    }

    private static string ExtraerMedidasConocidas(string u)
    {
        var patronesMedida = new[]
        {
            @"\b(\d+(?:-\d+/\d+|\s+\d+/\d+|/\d+)?\s*[""']?\s*X\s*\d+(?:\.\d+|\s+\d+/\d+|/\d+)?\s*(?:MM|CM|M|MT|MTS|[""'])?)\b",
            @"\b(LARGO\s+\d+\s*CM(?:\s+POR\s+DIAMETRO\s+\d+\s*CM)?)\b",
            @"\b(\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)\s*(?:PULG|[""']))\b",
            @"\b(\d+\s*(?:MT|MTS|M)\b)",
            @"\b(\d+(?:\.\d+)?\s*(?:MM|CM)\b)"
        };

        foreach (var pat in patronesMedida)
        {
            var m = System.Text.RegularExpressions.Regex.Match(u, pat);
            if (m.Success)
                return m.Groups[1].Value.Trim();
        }
        return string.Empty;
    }

    private static (string C1, string C2, string C3) ExtraerCamposEspecificosPorCategoria(int catId, string nombre, string u)
    {
        string volt = ExtraerPatron(u, @"\b(\d+(?:\s*A\s*\d+|-\d+)?\s*V(?:DC|AC|\s+DC|\s+AC)?|\d+\s*VOLTS?)\b");
        string ampWatt = ExtraerPatron(u, @"\b(\d+(?:\.\d+)?\s*(?:AMP|A|W|KW|MAH|MFD|HP|LM))\b");
        string ton = ExtraerPatron(u, @"\b(\d+(?:\.\d+)?\s*TONELADAS?|\d+/\d+\s*HP|\d+(?:\.\d+)?\s*HP|\d+\s*LITROS?)\b");
        string gas = ExtraerPatron(u, @"\b(R-?\d{2,3}[A-Z]?)\b");
        string ohm = ExtraerPatron(u, @"\b(\d+K(?:\s*,\s*(?:POZO\s*\w+\s*)?\d+K)*)\b");
        string psi = ExtraerPatron(u, @"\b(\d+(?:\.\d+)?\s*PSI(?:\s*\(\s*\d+\s*BAR\s*\))?)\b");
        string contenido = ExtraerPatron(u, @"\b(\d+(?:\.\d+)?\s*(?:LTS?|LITROS?|ML|GAL(?:ON|ONES)?|GR|G|KG|KL))\b");
        string color = ExtraerPatron(u, @"\b(BLANCO|NEGRO(?:\s+MATE)?|ROJO|AZUL|AMARILLO|NARANJA|VERDE|GRIS|CAFE|TRANSPARENTE)\b");

        switch (catId)
        {
            case 1: // Refrigeración y Aire Acondicionado (HVAC)
            {
                string c1 = !string.IsNullOrEmpty(ton) ? ton : (!string.IsNullOrEmpty(ohm) ? $"Sensor {ohm} Ohm" : psi);
                string fase = u.Contains("TRIFASICO") || u.Contains("3PH") ? " Trifásico" : string.Empty;
                string c2 = (volt + (string.IsNullOrEmpty(ampWatt) ? "" : $" / {ampWatt}") + fase).Trim(' ', '/');
                string c3 = !string.IsNullOrEmpty(gas) ? $"Gas {gas}" : (u.Contains("INVERTER") ? "Sistema Inverter" : (u.Contains("CONDENSADORA") ? "Para Condensadora" : (u.Contains("EVAPORADOR") ? "Para Evaporador" : string.Empty)));
                return (c1, c2, c3);
            }
            case 2: // Eléctrico, Iluminación y Control
            {
                string c1 = volt;
                string c2 = ampWatt;
                string polosCal = ExtraerPatron(u, @"\b(\d+\s*POLOS?|\d+P|CALIBRE\s*\d+|\d+X\d+\s*AWG|IP\d{2}|2X\d+)\b");
                return (c1, c2, polosCal);
            }
            case 3: // Plomería, Tubería y Conexiones
            {
                string diam = ExtraerPatron(u, @"\b(\d+\s+\d+/\d+|\d+/\d+|\d+\s*MM|\d+\s*[""'])\b");
                string mat = u.Contains("GALVANIZAD") ? "Acero Galvanizado" : (u.Contains("PVC SAN") ? "PVC Sanitario" : (u.Contains("C-40") || u.Contains("C- 40") || u.Contains("HIDRAULIC") ? "PVC Hidráulico Cédula 40" : (u.Contains("ALUMINIO") ? "Aluminio" : "Plástico / Reforzado")));
                string tipo = u.Contains("MACHO") ? "Rosca Macho" : (u.Contains("HEMBRA") ? "Conexión Hembra" : (u.Contains("RÁPIDO") || u.Contains("RAPIDO") ? "Acoplamiento Rápido" : "Conexión Hidráulica"));
                return (diam, mat, tipo);
            }
            case 4: // Herramientas, Medición y Maquinaria
            {
                string alim = !string.IsNullOrEmpty(volt) ? $"Eléctrica {volt}" : (u.Contains("RECARGABLE") ? "Batería Recargable" : (u.Contains("PILA") ? "A Baterías" : "Manual / Uso Industrial"));
                string cap = !string.IsNullOrEmpty(ampWatt) ? ampWatt : psi;
                string esp = ExtraerPatron(u, @"\b(SDS\s*PLUS|ALTA\s*VELOCIDAD|IMPACTO|BIMETÁLICA|ANTI\s*IMPACTO|CONFORT\s*GRIP|MAGNETICOS?)\b");
                return (alim, cap, esp);
            }
            case 5: // Pintura, Impermeabilizantes y Obra
            {
                string dur = ExtraerPatron(u, @"\b(\d+\s*AÑOS|FIBRATADO|ACRILICO|POLIURETANO|PELO\s*DE\s*CAMELLO|MICROFIBRA|POLIESTER)\b");
                return (color, contenido, dur);
            }
            case 6: // Tornillería, Fijación y Herrajes
            {
                string cal = ExtraerPatron(u, @"\b(#\d+|\d+/\d+[""']?)\b");
                string mat = u.Contains("GALVANIZADO") ? "Acero Galvanizado" : (u.Contains("ACERO") ? "Acero" : (u.Contains("PLASTICO") || u.Contains("PLÁSTICO") ? "Plástico / Nylon" : "Metal"));
                string res = ExtraerPatron(u, @"\b(\d+\s*(?:LB|KG|PZAS|PIEZAS))\b");
                return (cal, mat, res);
            }
            case 7: // Soldadura, Químicos y Consumibles Técnicos
            {
                string aplic = u.Contains("PLATA") ? "Soldadura de Plata" : (u.Contains("PVC") ? "Cementante PVC" : (u.Contains("DIELECTRICO") ? "Limpieza Dieléctrica" : (u.Contains("ALTAS TEMPERATURAS") ? "Alta Temperatura" : "Uso Técnico")));
                string grano = ExtraerPatron(u, @"\b(GRANO\s*\d+|0%\s*AG|TODA\s*PRESION|BASE\s*BENTONA)\b");
                return (contenido, aplic, grano);
            }
            case 8: // Seguridad Industrial y EPP
            {
                string talla = u.Contains("XCH") ? "Extra Chica (XCH)" : (u.Contains("CHICOS") ? "Chica (CH)" : (u.Contains("MEDIANOS") ? "Mediana (M)" : (u.Contains("GRANDE") ? "Grande (G)" : "Unitalla")));
                string mat = u.Contains("NITRILO") ? "Nylon con recubrimiento Nitrilo" : (u.Contains("PUNTOS PVC") ? "Algodón con puntos PVC" : (u.Contains("PVC") ? "PVC Impermeable" : "Policarbonato / Polietileno"));
                string prot = !string.IsNullOrEmpty(color) ? color : (u.Contains("ANTIEMPAÑE") ? "Antiempañe" : "Protección Industrial");
                return (talla, mat, prot);
            }
            case 9: // Limpieza, Higiene y Jardinería
            {
                string tipo = u.Contains("MICROFIBRA") ? "Paño Microfibra" : (u.Contains("ALGODON") ? "Algodón" : (u.Contains("ROLLO") || u.Contains("BOLSA") ? "Bolsa Polietileno" : "Limpieza / Higiene"));
                return (tipo, contenido, color);
            }
            case 10: // Papelería, Oficina y Comedor
            {
                string tam = u.Contains("CARTA") || u.Contains("CTA") ? "Tamaño Carta" : (u.Contains("SATELITAL") ? "Kit Antena + Router WiFi" : "Estándar");
                return (tam, !string.IsNullOrEmpty(color) ? color : contenido, string.Empty);
            }
            case 11: // Servicios, Peajes y Gastos Operativos
            {
                string refNru = ExtraerPatron(nombre, @"\b(NRU\s*\w+)\b");
                string fecha = ExtraerPatron(nombre, @"(\(\d{2}/\d{2}/\d{4}\))");
                return ("Peaje / Caseta Autopista", refNru, fecha);
            }
            default:
                return (volt, ampWatt, color);
        }
    }

    private static string ExtraerPatron(string texto, string regex)
    {
        var m = System.Text.RegularExpressions.Regex.Match(texto, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
    }

    public static List<HerramientaActivo> ObtenerHerramientasIndividuales() => ObtenerHerramientas();

    public static List<HerramientaActivo> ObtenerHerramientas()
    {
        var list = new List<HerramientaActivo>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT h.Id, h.ProductoId, COALESCE(p.Nombre, 'Herramienta'), h.CodigoIndividual, h.NumeroSerie, h.MarcaModelo,
       h.Estado, h.Ubicacion, h.EncargadoActual, h.ProyectoActual, h.NotasEstado,
       COALESCE(h.FechaCompra, ''), COALESCE(h.FechaFinGarantia, ''),
       COALESCE(h.CostoAdquisicion, COALESCE(p.UltimoCostoCompra, 0)),
       COALESCE(h.GastoMantenimientoAcumulado, 0)
FROM HerramientasActivos h
LEFT JOIN Productos p ON p.Id = h.ProductoId
ORDER BY h.CodigoIndividual";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new HerramientaActivo
            {
                Id = r.GetInt32(0),
                ProductoId = r.GetInt32(1),
                ProductoNombre = r.GetString(2),
                CodigoIndividual = r.GetString(3),
                NumeroSerie = r.GetString(4),
                MarcaModelo = r.GetString(5),
                Estado = r.GetString(6),
                Ubicacion = r.GetString(7),
                EncargadoActual = r.GetString(8),
                ProyectoActual = r.GetString(9),
                NotasEstado = r.GetString(10),
                FechaCompra = r.GetString(11),
                FechaFinGarantia = r.GetString(12),
                CostoAdquisicion = Convert.ToDecimal(r.GetDouble(13)),
                GastoMantenimientoAcumulado = Convert.ToDecimal(r.GetDouble(14))
            });
        }
        return list;
    }

    public static void GuardarHerramientaActivo(HerramientaActivo h)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        if (string.IsNullOrWhiteSpace(h.CodigoIndividual))
            h.CodigoIndividual = GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual");

        using var cmd = conn.CreateCommand();
        if (h.Id > 0)
        {
            cmd.CommandText = @"
UPDATE HerramientasActivos
SET ProductoId=$pid, CodigoIndividual=$ci, NumeroSerie=$ns, MarcaModelo=$mm, Estado=$est, Ubicacion=$ubi, NotasEstado=$not,
    FechaCompra=$fc, FechaFinGarantia=$fg, CostoAdquisicion=$ca, GastoMantenimientoAcumulado=$gm
WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", h.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO HerramientasActivos (ProductoId, CodigoIndividual, NumeroSerie, MarcaModelo, Estado, Ubicacion, EncargadoActual, ProyectoActual, NotasEstado, FechaCompra, FechaFinGarantia, CostoAdquisicion, GastoMantenimientoAcumulado)
VALUES ($pid, $ci, $ns, $mm, $est, $ubi, '', '', $not, $fc, $fg, $ca, $gm)";
        }
        cmd.Parameters.AddWithValue("$pid", h.ProductoId);
        cmd.Parameters.AddWithValue("$ci", h.CodigoIndividual.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$ns", h.NumeroSerie.Trim());
        cmd.Parameters.AddWithValue("$mm", h.MarcaModelo.Trim());
        cmd.Parameters.AddWithValue("$est", h.Estado);
        cmd.Parameters.AddWithValue("$ubi", h.Ubicacion.Trim());
        cmd.Parameters.AddWithValue("$not", h.NotasEstado.Trim());
        cmd.Parameters.AddWithValue("$fc", h.FechaCompra?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$fg", h.FechaFinGarantia?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$ca", (double)h.CostoAdquisicion);
        cmd.Parameters.AddWithValue("$gm", (double)h.GastoMantenimientoAcumulado);
        cmd.ExecuteNonQuery();

        if (h.ProductoId > 0)
        {
            using var upProd = conn.CreateCommand();
            upProd.CommandText = "UPDATE Productos SET EsHerramienta = 1 WHERE Id = $pid;";
            upProd.Parameters.AddWithValue("$pid", h.ProductoId);
            upProd.ExecuteNonQuery();
        }
    }

    public static void EliminarHerramientaActivo(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HerramientasActivos WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void EliminarProducto(int id)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE FacturaConceptos SET ProductoIdVinculado = -1 WHERE ProductoIdVinculado = $id;
DELETE FROM ProductoProveedorMapa WHERE ProductoId = $id;
DELETE FROM OrdenCompraPartidas WHERE ProductoId = $id;
DELETE FROM SolicitudPartidas WHERE ProductoId = $id;
DELETE FROM EntradaPartidas WHERE ProductoId = $id;
DELETE FROM SalidaPartidas WHERE ProductoId = $id;
DELETE FROM HerramientasActivos WHERE ProductoId = $id;
DELETE FROM Productos WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();

        tx.Commit();
    }

    // ==================== SOLICITUDES DE INGENIERÍA ====================

    public static List<Solicitud> ObtenerSolicitudes()
    {
        var list = new List<Solicitud>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT s.Id, s.Folio, s.Fecha, s.DepartamentoId, COALESCE(d.Nombre, 'Sin depto'), s.EncargadoId, COALESCE(e.Nombre, 'Sin encargado'),
       s.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Consumo Interno de Departamento'), s.Origen, s.Estatus, s.Notas,
       COALESCE(NULLIF(s.TipoSolicitud, ''), 'Requerimiento de Compra'), s.UsaPartidas
FROM Solicitudes s
LEFT JOIN Departamentos d ON d.Id = s.DepartamentoId
LEFT JOIN Encargados e ON e.Id = s.EncargadoId
LEFT JOIN Proyectos pr ON pr.Id = s.ProyectoId
ORDER BY s.Id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Solicitud
            {
                Id = r.GetInt32(0),
                Folio = r.GetString(1),
                Fecha = r.GetString(2),
                DepartamentoId = r.GetInt32(3),
                DepartamentoNombre = r.GetString(4),
                EncargadoId = r.GetInt32(5),
                EncargadoNombre = r.GetString(6),
                ProyectoId = r.GetInt32(7),
                ProyectoNombre = r.GetString(8),
                Origen = r.GetString(9),
                Estatus = r.GetString(10),
                Notas = r.GetString(11),
                TipoSolicitud = r.GetString(12),
                UsaPartidas = r.GetInt32(13) != 0
            });
        }

        foreach (var sol in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT sp.Id, sp.SolicitudId, sp.ProductoId, COALESCE(p.CodigoInterno, 'CONCEPTO'),
       CASE WHEN sp.ProductoId > 0 AND p.Nombre IS NOT NULL THEN p.Nombre ELSE COALESCE(NULLIF(sp.DescripcionLibre, ''), 'Concepto Abierto') END,
       COALESCE(NULLIF(TRIM(sp.UnidadLibre), ''), p.UnidadCompra, 'Pieza'),
       sp.CantidadSolicitada, COALESCE(p.StockActual, 0), sp.NotasPartida,
       COALESCE(sp.Proveedor1Id, 0), COALESCE(sp.PrecioSinIva1, 0), COALESCE(sp.DiasEntrega1, 2), COALESCE(sp.FleteConIva1, 0), COALESCE(sp.NotasCotizacion1, ''),
       COALESCE(sp.Proveedor2Id, 0), COALESCE(sp.PrecioSinIva2, 0), COALESCE(sp.DiasEntrega2, 3), COALESCE(sp.FleteConIva2, 0), COALESCE(sp.NotasCotizacion2, ''),
       COALESCE(sp.Proveedor3Id, 0), COALESCE(sp.PrecioSinIva3, 0), COALESCE(sp.DiasEntrega3, 1), COALESCE(sp.FleteConIva3, 0), COALESCE(sp.NotasCotizacion3, ''),
       COALESCE(sp.ProveedorGanadorIndice, 0), COALESCE(sp.OrdenCompraId, 0), COALESCE(sp.OrdenCompraFolio, ''),
       COALESCE(sp.DescripcionLibre, ''), COALESCE(p.Familia, ''),
       COALESCE(sp.IncluirEnCotizacion, 1), COALESCE(sp.DiasVigenciaCotizacion, 15), COALESCE(sp.FechaCotizacion, ''),
       COALESCE(sp.DiasVigencia1, 0), COALESCE(sp.FechaCotizacion1, ''),
       COALESCE(sp.DiasVigencia2, 0), COALESCE(sp.FechaCotizacion2, ''),
       COALESCE(sp.DiasVigencia3, 0), COALESCE(sp.FechaCotizacion3, ''),
       sp.NombrePartida
FROM SolicitudPartidas sp
LEFT JOIN Productos p ON p.Id = sp.ProductoId
WHERE sp.SolicitudId = $sid";
            pCmd.Parameters.AddWithValue("$sid", sol.Id);
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                int prodId = pr.GetInt32(2);
                string nomMostrar = pr.GetString(4);
                string descLibre = pr.GetString(27);
                string fam = pr.GetString(28);
                if (string.IsNullOrWhiteSpace(fam))
                    fam = ExtraerFamiliaSugerida(!string.IsNullOrWhiteSpace(descLibre) ? descLibre : nomMostrar);

                int vigGen = pr.GetInt32(30) > 0 ? pr.GetInt32(30) : 15;
                string fecGen = pr.GetString(31);
                int vig1 = pr.GetInt32(32) > 0 ? pr.GetInt32(32) : vigGen;
                string fec1 = !string.IsNullOrWhiteSpace(pr.GetString(33)) ? pr.GetString(33) : fecGen;
                int vig2 = pr.GetInt32(34) > 0 ? pr.GetInt32(34) : vigGen;
                string fec2 = !string.IsNullOrWhiteSpace(pr.GetString(35)) ? pr.GetString(35) : fecGen;
                int vig3 = pr.GetInt32(36) > 0 ? pr.GetInt32(36) : vigGen;
                string fec3 = !string.IsNullOrWhiteSpace(pr.GetString(37)) ? pr.GetString(37) : fecGen;

                sol.Partidas.Add(new SolicitudPartida
                {
                    Id = pr.GetInt32(0),
                    SolicitudId = pr.GetInt32(1),
                    ProductoId = prodId,
                    ProductoCodigo = prodId > 0 ? pr.GetString(3) : "CONCEPTO",
                    ProductoNombre = nomMostrar,
                    DescripcionLibre = !string.IsNullOrWhiteSpace(descLibre) ? descLibre : nomMostrar,
                    Familia = fam,
                    UnidadCompra = pr.GetString(5),
                    CantidadSolicitada = Convert.ToDecimal(pr.GetDouble(6)),
                    StockActualAlmacen = Convert.ToDecimal(pr.GetDouble(7)),
                    NotasPartida = pr.GetString(8),
                    Proveedor1Id = pr.GetInt32(9),
                    PrecioSinIva1 = Convert.ToDecimal(pr.GetDouble(10)),
                    DiasEntrega1 = pr.GetInt32(11),
                    FleteConIva1 = Convert.ToDecimal(pr.GetDouble(12)),
                    NotasCotizacion1 = pr.GetString(13),
                    DiasVigencia1 = vig1,
                    FechaCotizacion1 = fec1,
                    Proveedor2Id = pr.GetInt32(14),
                    PrecioSinIva2 = Convert.ToDecimal(pr.GetDouble(15)),
                    DiasEntrega2 = pr.GetInt32(16),
                    FleteConIva2 = Convert.ToDecimal(pr.GetDouble(17)),
                    NotasCotizacion2 = pr.GetString(18),
                    DiasVigencia2 = vig2,
                    FechaCotizacion2 = fec2,
                    Proveedor3Id = pr.GetInt32(19),
                    PrecioSinIva3 = Convert.ToDecimal(pr.GetDouble(20)),
                    DiasEntrega3 = pr.GetInt32(21),
                    FleteConIva3 = Convert.ToDecimal(pr.GetDouble(22)),
                    NotasCotizacion3 = pr.GetString(23),
                    DiasVigencia3 = vig3,
                    FechaCotizacion3 = fec3,
                    ProveedorGanadorIndice = pr.GetInt32(24),
                    OrdenCompraId = pr.GetInt32(25),
                    OrdenCompraFolio = pr.GetString(26),
                    IncluirEnCotizacion = pr.GetInt32(29) != 0,
                    NombrePartida = pr.GetString(38),
                    DiasVigenciaCotizacion = vigGen,
                    FechaCotizacion = fecGen
                });
            }
        }

        // Seguimiento: "Esperando respuesta" y cierre manual
        var solDict = list.ToDictionary(s => s.Id);
        using (var sCmd = conn.CreateCommand())
        {
            sCmd.CommandText = "SELECT Id, COALESCE(EsperandoRespuestaDe,''), COALESCE(EsperandoDesde,''), COALESCE(ConcluidoManual,0) FROM Solicitudes";
            using var sr = sCmd.ExecuteReader();
            while (sr.Read())
            {
                if (solDict.TryGetValue(sr.GetInt32(0), out var sS))
                {
                    sS.EsperandoRespuestaDe = sr.GetString(1);
                    sS.EsperandoDesde = sr.GetString(2);
                    sS.ConcluidoManual = sr.GetInt32(3) != 0;
                }
            }
        }
        return list;
    }

    public static Solicitud CrearSolicitud(
        int departamentoId,
        int encargadoId,
        int proyectoId,
        string origen,
        string notas,
        List<SolicitudPartida> partidas,
        string tipoSolicitud = "Requerimiento de Compra",
        string? folioPersonalizado = null,
        string? tiempoAproxRespuesta = null,
        bool notificarCorreo = true,
        bool usaPartidas = false)
    {
        string folio = string.IsNullOrWhiteSpace(folioPersonalizado)
            ? GenerarSiguienteFolio("SOL", "Solicitudes")
            : folioPersonalizado.Trim();
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string tipoLim = string.IsNullOrWhiteSpace(tipoSolicitud) ? "Requerimiento de Compra" : tipoSolicitud.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO Solicitudes (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, Origen, Estatus, Notas, TipoSolicitud, UsaPartidas)
VALUES ($f, $fe, $d, $e, $p, $or, 'Pendiente', $n, $ts, $usaPartidas);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$f", folio);
        cmd.Parameters.AddWithValue("$fe", fecha);
        cmd.Parameters.AddWithValue("$d", departamentoId);
        cmd.Parameters.AddWithValue("$e", encargadoId);
        cmd.Parameters.AddWithValue("$p", proyectoId);
        cmd.Parameters.AddWithValue("$or", origen);
        cmd.Parameters.AddWithValue("$n", notas.Trim());
        cmd.Parameters.AddWithValue("$ts", tipoLim);
        cmd.Parameters.AddWithValue("$usaPartidas", usaPartidas);
        int solId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var item in partidas)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO SolicitudPartidas (SolicitudId, ProductoId, DescripcionLibre, UnidadLibre, CantidadSolicitada, NotasPartida, PrecioSinIva1, NotasCotizacion1, IncluirEnCotizacion, DiasVigenciaCotizacion, NombrePartida)
VALUES ($sid, $pid, $dlib, $ulib, $cant, $not, $pr1, $nc1, $inc, $vig, $nombrePartida)";
            pCmd.Parameters.AddWithValue("$nombrePartida", usaPartidas ? item.NombrePartida.Trim() : string.Empty);
            pCmd.Parameters.AddWithValue("$sid", solId);
            pCmd.Parameters.AddWithValue("$pid", item.ProductoId);
            pCmd.Parameters.AddWithValue("$dlib", !string.IsNullOrWhiteSpace(item.DescripcionLibre) ? item.DescripcionLibre.Trim() : (item.ProductoNombre ?? string.Empty).Trim());
            pCmd.Parameters.AddWithValue("$ulib", string.IsNullOrWhiteSpace(item.UnidadCompra) ? "Pieza" : item.UnidadCompra.Trim());
            pCmd.Parameters.AddWithValue("$cant", (double)item.CantidadSolicitada);
            pCmd.Parameters.AddWithValue("$not", item.NotasPartida ?? string.Empty);
            pCmd.Parameters.AddWithValue("$pr1", (double)item.PrecioSinIva1);
            pCmd.Parameters.AddWithValue("$nc1", item.NotasCotizacion1 ?? string.Empty);
            pCmd.Parameters.AddWithValue("$inc", item.IncluirEnCotizacion ? 1 : 0);
            pCmd.Parameters.AddWithValue("$vig", item.DiasVigenciaCotizacion > 0 ? item.DiasVigenciaCotizacion : 15);
            pCmd.ExecuteNonQuery();
        }

        tx.Commit();
        var solCreada = ObtenerSolicitudes().First(s => s.Id == solId);
        if (notificarCorreo && solCreada.Partidas.Count > 0)
        {
            NotificarCambioEstadoCotizacionGerente(solCreada.Id, null, "1_CREADA", tiempoAproxRespuesta);
        }
        return solCreada;
    }

    public static Solicitud ActualizarSolicitudExistente(
        int solicitudId,
        int departamentoId,
        int encargadoId,
        int proyectoId,
        string origen,
        string notas,
        List<SolicitudPartida> partidas,
        string tipoSolicitud = "Requerimiento de Compra",
        string? tiempoAproxRespuesta = null,
        bool notificarCorreo = true,
        bool? usaPartidas = null)
    {
        var solPrevia = ObtenerSolicitudes().FirstOrDefault(s => s.Id == solicitudId);
        if (solPrevia != null && solPrevia.Estatus == "OC Generada")
        {
            throw new InvalidOperationException("No se puede modificar una Solicitud que ya tiene todas sus Órdenes de Compra generadas.");
        }

        bool conPartidas = usaPartidas ?? solPrevia?.UsaPartidas ?? false;
        static string ClavePartida(SolicitudPartida sp) => sp.Id > 0 ? $"ID:{sp.Id}" :
            sp.ProductoId > 0 ? $"PID:{sp.ProductoId}" : $"LIB:{(sp.DescripcionLibre ?? sp.ProductoNombre ?? "").Trim().ToUpperInvariant()}";

        var prevPorClave = solPrevia?.Partidas
            .GroupBy(ClavePartida)
            .ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<string, SolicitudPartida>();

        string tipoLim = string.IsNullOrWhiteSpace(tipoSolicitud) ? (solPrevia?.TipoSolicitud ?? "Requerimiento de Compra") : tipoSolicitud.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE Solicitudes
SET DepartamentoId = $d, EncargadoId = $e, ProyectoId = $p, Origen = $or, Notas = $n, TipoSolicitud = $ts, UsaPartidas = $usaPartidas
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$d", departamentoId);
            cmd.Parameters.AddWithValue("$e", encargadoId);
            cmd.Parameters.AddWithValue("$p", proyectoId);
            cmd.Parameters.AddWithValue("$or", origen);
            cmd.Parameters.AddWithValue("$n", (notas ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("$ts", tipoLim);
            cmd.Parameters.AddWithValue("$usaPartidas", conPartidas);
            cmd.Parameters.AddWithValue("$id", solicitudId);
            cmd.ExecuteNonQuery();
        }

        // Eliminar solo partidas que no tengan Orden de Compra emitida
        using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM SolicitudPartidas WHERE SolicitudId = $sid AND COALESCE(OrdenCompraId, 0) = 0";
            delCmd.Parameters.AddWithValue("$sid", solicitudId);
            delCmd.ExecuteNonQuery();
        }

        foreach (var item in partidas)
        {
            prevPorClave.TryGetValue(ClavePartida(item), out var prev);
            if (prev != null && prev.TieneOcGenerada)
            {
                // Esa partida ya fue emitida en una OC; se conserva intacta en la BD
                continue;
            }

            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO SolicitudPartidas (
    SolicitudId, ProductoId, DescripcionLibre, UnidadLibre, CantidadSolicitada, NotasPartida,
    Proveedor1Id, PrecioSinIva1, DiasEntrega1, FleteConIva1, NotasCotizacion1, DiasVigencia1, FechaCotizacion1,
    Proveedor2Id, PrecioSinIva2, DiasEntrega2, FleteConIva2, NotasCotizacion2, DiasVigencia2, FechaCotizacion2,
    Proveedor3Id, PrecioSinIva3, DiasEntrega3, FleteConIva3, NotasCotizacion3, DiasVigencia3, FechaCotizacion3,
    ProveedorGanadorIndice, OrdenCompraId, OrdenCompraFolio,
    IncluirEnCotizacion, DiasVigenciaCotizacion, FechaCotizacion, NombrePartida
)
VALUES (
    $sid, $pid, $dlib, $ulib, $cant, $not,
    $pv1, $pr1, $d1, $fl1, $nc1, $dv1, $fc1,
    $pv2, $pr2, $d2, $fl2, $nc2, $dv2, $fc2,
    $pv3, $pr3, $d3, $fl3, $nc3, $dv3, $fc3,
    $gan, 0, '',
    $inc, $vig, $fcot, $nombrePartida
)";
            string nombrePartida = conPartidas ? item.NombrePartida.Trim() : string.Empty;
            pCmd.Parameters.AddWithValue("$nombrePartida", nombrePartida);
            pCmd.Parameters.AddWithValue("$sid", solicitudId);
            pCmd.Parameters.AddWithValue("$pid", item.ProductoId);
            pCmd.Parameters.AddWithValue("$dlib", !string.IsNullOrWhiteSpace(item.DescripcionLibre) ? item.DescripcionLibre.Trim() : (item.ProductoNombre ?? string.Empty).Trim());
            pCmd.Parameters.AddWithValue("$ulib", string.IsNullOrWhiteSpace(item.UnidadCompra) ? "Pieza" : item.UnidadCompra.Trim());
            pCmd.Parameters.AddWithValue("$cant", (double)item.CantidadSolicitada);
            pCmd.Parameters.AddWithValue("$not", item.NotasPartida ?? string.Empty);

            pCmd.Parameters.AddWithValue("$pv1", prev?.Proveedor1Id ?? item.Proveedor1Id);
            pCmd.Parameters.AddWithValue("$pr1", (double)(prev?.PrecioSinIva1 ?? item.PrecioSinIva1));
            pCmd.Parameters.AddWithValue("$d1", prev?.DiasEntrega1 ?? (item.DiasEntrega1 > 0 ? item.DiasEntrega1 : 2));
            pCmd.Parameters.AddWithValue("$fl1", (double)(prev?.FleteConIva1 ?? item.FleteConIva1));
            pCmd.Parameters.AddWithValue("$nc1", prev?.NotasCotizacion1 ?? item.NotasCotizacion1 ?? string.Empty);
            pCmd.Parameters.AddWithValue("$dv1", prev?.DiasVigencia1 ?? (item.DiasVigencia1 > 0 ? item.DiasVigencia1 : 15));
            pCmd.Parameters.AddWithValue("$fc1", prev?.FechaCotizacion1 ?? item.FechaCotizacion1 ?? string.Empty);

            pCmd.Parameters.AddWithValue("$pv2", prev?.Proveedor2Id ?? item.Proveedor2Id);
            pCmd.Parameters.AddWithValue("$pr2", (double)(prev?.PrecioSinIva2 ?? item.PrecioSinIva2));
            pCmd.Parameters.AddWithValue("$d2", prev?.DiasEntrega2 ?? (item.DiasEntrega2 > 0 ? item.DiasEntrega2 : 3));
            pCmd.Parameters.AddWithValue("$fl2", (double)(prev?.FleteConIva2 ?? item.FleteConIva2));
            pCmd.Parameters.AddWithValue("$nc2", prev?.NotasCotizacion2 ?? item.NotasCotizacion2 ?? string.Empty);
            pCmd.Parameters.AddWithValue("$dv2", prev?.DiasVigencia2 ?? (item.DiasVigencia2 > 0 ? item.DiasVigencia2 : 15));
            pCmd.Parameters.AddWithValue("$fc2", prev?.FechaCotizacion2 ?? item.FechaCotizacion2 ?? string.Empty);

            pCmd.Parameters.AddWithValue("$pv3", prev?.Proveedor3Id ?? item.Proveedor3Id);
            pCmd.Parameters.AddWithValue("$pr3", (double)(prev?.PrecioSinIva3 ?? item.PrecioSinIva3));
            pCmd.Parameters.AddWithValue("$d3", prev?.DiasEntrega3 ?? (item.DiasEntrega3 > 0 ? item.DiasEntrega3 : 1));
            pCmd.Parameters.AddWithValue("$fl3", (double)(prev?.FleteConIva3 ?? item.FleteConIva3));
            pCmd.Parameters.AddWithValue("$nc3", prev?.NotasCotizacion3 ?? item.NotasCotizacion3 ?? string.Empty);
            pCmd.Parameters.AddWithValue("$dv3", prev?.DiasVigencia3 ?? (item.DiasVigencia3 > 0 ? item.DiasVigencia3 : 15));
            pCmd.Parameters.AddWithValue("$fc3", prev?.FechaCotizacion3 ?? item.FechaCotizacion3 ?? string.Empty);

            pCmd.Parameters.AddWithValue("$gan", prev?.ProveedorGanadorIndice ?? item.ProveedorGanadorIndice);
            pCmd.Parameters.AddWithValue("$inc", item.IncluirEnCotizacion ? 1 : 0);
            pCmd.Parameters.AddWithValue("$vig", prev?.DiasVigenciaCotizacion ?? (item.DiasVigenciaCotizacion > 0 ? item.DiasVigenciaCotizacion : 15));
            pCmd.Parameters.AddWithValue("$fcot", prev?.FechaCotizacion ?? item.FechaCotizacion ?? string.Empty);
            pCmd.ExecuteNonQuery();

            if (prev != null && prev.Id > 0)
            {
                using var idCmd = conn.CreateCommand();
                idCmd.Transaction = tx;
                idCmd.CommandText = "SELECT last_insert_rowid();";
                int nuevoSpId = Convert.ToInt32(idCmd.ExecuteScalar());
                if (nuevoSpId > 0)
                {
                    using var relinkCmd = conn.CreateCommand();
                    relinkCmd.Transaction = tx;
                    relinkCmd.CommandText = "UPDATE CotizacionesPartidaProveedor SET SolicitudPartidaId = $newId WHERE SolicitudPartidaId = $oldId";
                    relinkCmd.Parameters.AddWithValue("$newId", nuevoSpId);
                    relinkCmd.Parameters.AddWithValue("$oldId", prev.Id);
                    relinkCmd.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
        var solActualizada = ObtenerSolicitudes().First(s => s.Id == solicitudId);
        if (notificarCorreo && solActualizada.Partidas.Count > 0 && !YaSeEnvioNotificacionEtapa(solActualizada.Folio, "1_CREADA"))
        {
            NotificarCambioEstadoCotizacionGerente(solActualizada.Id, null, "1_CREADA", tiempoAproxRespuesta);
        }
        return solActualizada;
    }

    public static void ActualizarTipoYEstatusSolicitud(int solicitudId, string tipoSolicitud, string estatus)
    {
        if (solicitudId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Solicitudes SET TipoSolicitud = $ts, Estatus = $est WHERE Id = $id";
        cmd.Parameters.AddWithValue("$ts", tipoSolicitud);
        cmd.Parameters.AddWithValue("$est", estatus);
        cmd.Parameters.AddWithValue("$id", solicitudId);
        cmd.ExecuteNonQuery();
    }

    public static void ActualizarEstatusSolicitud(int solicitudId, string estatus)
    {
        if (solicitudId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Solicitudes SET Estatus = $est WHERE Id = $id";
        cmd.Parameters.AddWithValue("$est", estatus);
        cmd.Parameters.AddWithValue("$id", solicitudId);
        cmd.ExecuteNonQuery();
    }

    public static void AsignarPartidaAArticulo(int solicitudId, int articuloId, string nombrePartida)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE SolicitudPartidas SET NombrePartida = $nombre
WHERE Id = $id AND SolicitudId = $sid AND OrdenCompraId = 0
  AND EXISTS (SELECT 1 FROM Solicitudes s WHERE s.Id = $sid AND s.UsaPartidas = 1
              AND s.ConcluidoManual = 0
              AND s.Estatus IN ('En Comparativa', 'Parcial en OC', 'Cotización Entregada', 'Cotizada (Presupuesto)'));";
        cmd.Parameters.AddWithValue("$nombre", nombrePartida.Trim());
        cmd.Parameters.AddWithValue("$id", articuloId);
        cmd.Parameters.AddWithValue("$sid", solicitudId);
        cmd.ExecuteNonQuery();
    }

    public static void ActualizarUnidadPartidaEnSolicitud(int solicitudPartidaId, int solicitudId, int productoId, string? descripcionLibre, string nuevaUnidad)
    {
        string uNorm = NormalizarUnidadMedida(nuevaUnidad);
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (solicitudPartidaId > 0)
        {
            cmd.CommandText = @"UPDATE SolicitudPartidas
SET UnidadLibre = $u
WHERE Id = $id AND SolicitudId = $sid AND OrdenCompraId = 0";
            cmd.Parameters.AddWithValue("$sid", solicitudId);
            cmd.Parameters.AddWithValue("$u", uNorm);
            cmd.Parameters.AddWithValue("$id", solicitudPartidaId);
            cmd.ExecuteNonQuery();
        }
        else if (solicitudId > 0)
        {
            cmd.CommandText = @"
UPDATE SolicitudPartidas
SET UnidadLibre = $u
WHERE SolicitudId = $sid
  AND COALESCE(OrdenCompraId, 0) = 0
  AND (($pid > 0 AND ProductoId = $pid) OR ($pid <= 0 AND UPPER(TRIM(DescripcionLibre)) = UPPER(TRIM($dlib))))";
            cmd.Parameters.AddWithValue("$u", uNorm);
            cmd.Parameters.AddWithValue("$sid", solicitudId);
            cmd.Parameters.AddWithValue("$pid", productoId);
            cmd.Parameters.AddWithValue("$dlib", (descripcionLibre ?? string.Empty).Trim());
            cmd.ExecuteNonQuery();
        }
    }

    public static void EliminarPartidaEspecificaDeSolicitud(int solicitudPartidaId, int solicitudId, int productoId, string? descripcionLibre)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        int targetId = solicitudPartidaId;
        if (targetId <= 0 && solicitudId > 0)
        {
            using var findCmd = conn.CreateCommand();
            findCmd.Transaction = tx;
            findCmd.CommandText = @"
SELECT Id FROM SolicitudPartidas
WHERE SolicitudId = $sid
  AND COALESCE(OrdenCompraId, 0) = 0
  AND (($pid > 0 AND ProductoId = $pid) OR ($pid <= 0 AND UPPER(TRIM(DescripcionLibre)) = UPPER(TRIM($dlib))))
LIMIT 1";
            findCmd.Parameters.AddWithValue("$sid", solicitudId);
            findCmd.Parameters.AddWithValue("$pid", productoId);
            findCmd.Parameters.AddWithValue("$dlib", (descripcionLibre ?? string.Empty).Trim());
            var obj = findCmd.ExecuteScalar();
            if (obj != null && obj != DBNull.Value)
                targetId = Convert.ToInt32(obj);
        }

        if (targetId > 0)
        {
            using (var delExtra = conn.CreateCommand())
            {
                delExtra.Transaction = tx;
                delExtra.CommandText = "DELETE FROM CotizacionesPartidaProveedor WHERE SolicitudPartidaId = $spid";
                delExtra.Parameters.AddWithValue("$spid", targetId);
                delExtra.ExecuteNonQuery();
            }

            using (var delSp = conn.CreateCommand())
            {
                delSp.Transaction = tx;
                delSp.CommandText = "DELETE FROM SolicitudPartidas WHERE Id = $spid AND COALESCE(OrdenCompraId, 0) = 0";
                delSp.Parameters.AddWithValue("$spid", targetId);
                delSp.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    public static EntradaAlmacen RegistrarEntradaDirectaSinOc(
        string proveedorNombre,
        string proyectoNombre,
        string folioDocumento,
        string notas,
        List<EntradaPartida> partidas)
    {
        string folioEnt = GenerarSiguienteFolio("ENT", "EntradasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas)
VALUES ($fol, $fec, NULL, 'SIN-OC', $prov, $proy, 'Entrada sin OC (Por Regularizar)', $fdoc, $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folioEnt);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$prov", proveedorNombre.Trim());
        cmd.Parameters.AddWithValue("$proy", proyectoNombre.Trim());
        cmd.Parameters.AddWithValue("$fdoc", folioDocumento.Trim());
        cmd.Parameters.AddWithValue("$not", notas.Trim());
        int entId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var p in partidas)
        {
            decimal factor = Math.Max(1m, p.FactorConversion);
            decimal cantAlmacen = p.CantidadIngresadaAlmacen > 0 ? p.CantidadIngresadaAlmacen : Math.Round(p.CantidadCompraRecibida * factor, 2);

            using (var epCmd = conn.CreateCommand())
            {
                epCmd.Transaction = tx;
                epCmd.CommandText = @"
INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva)
VALUES ($eid, $pid, $cc, $uc, $fc, $ca, $ua, $costo)";
                epCmd.Parameters.AddWithValue("$eid", entId);
                epCmd.Parameters.AddWithValue("$pid", p.ProductoId);
                epCmd.Parameters.AddWithValue("$cc", (double)p.CantidadCompraRecibida);
                epCmd.Parameters.AddWithValue("$uc", string.IsNullOrWhiteSpace(p.UnidadCompra) ? "Pieza" : p.UnidadCompra);
                epCmd.Parameters.AddWithValue("$fc", (double)factor);
                epCmd.Parameters.AddWithValue("$ca", (double)cantAlmacen);
                epCmd.Parameters.AddWithValue("$ua", string.IsNullOrWhiteSpace(p.UnidadAlmacen) ? "Pieza" : p.UnidadAlmacen);
                epCmd.Parameters.AddWithValue("$costo", (double)p.CostoUnitarioCompraConIva);
                epCmd.ExecuteNonQuery();
            }

            using (var upStock = conn.CreateCommand())
            {
                upStock.Transaction = tx;
                upStock.CommandText = "UPDATE Productos SET StockActual = StockActual + $ca, Ubicacion = CASE WHEN $ub <> '' THEN $ub ELSE Ubicacion END WHERE Id = $pid";
                upStock.Parameters.AddWithValue("$ca", (double)cantAlmacen);
                upStock.Parameters.AddWithValue("$ub", (p.UbicacionAlmacen ?? string.Empty).Trim());
                upStock.Parameters.AddWithValue("$pid", p.ProductoId);
                upStock.ExecuteNonQuery();
            }
        }

        tx.Commit();
        return ObtenerEntradasAlmacen().First(e => e.Id == entId);
    }

    public static Solicitud AgregarOIncrementarPartidaEnSolicitud(int solicitudId, int productoId, decimal cantidadAgregar, string notasPartida = "", string nombrePartida = "")
    {
        if (cantidadAgregar <= 0) cantidadAgregar = 1;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT Estatus FROM Solicitudes WHERE Id = $id";
            chk.Parameters.AddWithValue("$id", solicitudId);
            string estatus = Convert.ToString(chk.ExecuteScalar()) ?? "";
            if (estatus == "OC Generada")
            {
                throw new InvalidOperationException("No se pueden agregar artículos a una Solicitud que ya tiene Orden de Compra generada.");
            }
        }

        conn.CreateCollation("PARTIDA_NOCASE", StringComparer.OrdinalIgnoreCase.Compare);
        nombrePartida = nombrePartida.Trim();
        int? partidaExistenteId = null;
        decimal cantExistente = 0m;
        using (var findCmd = conn.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = "SELECT Id, CantidadSolicitada FROM SolicitudPartidas WHERE SolicitudId = $sid AND ProductoId = $pid AND NombrePartida COLLATE PARTIDA_NOCASE = $nombrePartida AND OrdenCompraId = 0 LIMIT 1";
            findCmd.Parameters.AddWithValue("$sid", solicitudId);
            findCmd.Parameters.AddWithValue("$pid", productoId);
            findCmd.Parameters.AddWithValue("$nombrePartida", nombrePartida);
            using var r = findCmd.ExecuteReader();
            if (r.Read())
            {
                partidaExistenteId = r.GetInt32(0);
                cantExistente = Convert.ToDecimal(r.GetDouble(1));
            }
        }

        if (partidaExistenteId.HasValue)
        {
            using var upCmd = conn.CreateCommand();
            upCmd.Transaction = tx;
            upCmd.CommandText = "UPDATE SolicitudPartidas SET CantidadSolicitada = $cant WHERE Id = $id";
            upCmd.Parameters.AddWithValue("$cant", (double)(cantExistente + cantidadAgregar));
            upCmd.Parameters.AddWithValue("$id", partidaExistenteId.Value);
            upCmd.ExecuteNonQuery();
        }
        else
        {
            using var insCmd = conn.CreateCommand();
            insCmd.Transaction = tx;
            insCmd.CommandText = @"
INSERT INTO SolicitudPartidas (SolicitudId, ProductoId, CantidadSolicitada, NotasPartida, NombrePartida)
VALUES ($sid, $pid, $cant, $not, $nombrePartida)";
            insCmd.Parameters.AddWithValue("$sid", solicitudId);
            insCmd.Parameters.AddWithValue("$pid", productoId);
            insCmd.Parameters.AddWithValue("$cant", (double)cantidadAgregar);
            insCmd.Parameters.AddWithValue("$not", notasPartida ?? string.Empty);
            insCmd.Parameters.AddWithValue("$nombrePartida", nombrePartida);
            insCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return ObtenerSolicitudes().First(s => s.Id == solicitudId);
    }

    public static Solicitud QuitarPartidaDeSolicitud(int solicitudId, int productoId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT Estatus FROM Solicitudes WHERE Id = $id";
            chk.Parameters.AddWithValue("$id", solicitudId);
            string estatus = Convert.ToString(chk.ExecuteScalar()) ?? "";
            if (estatus == "OC Generada")
            {
                throw new InvalidOperationException("No se pueden quitar artículos de una Solicitud que ya tiene Orden de Compra generada.");
            }
        }

        using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM SolicitudPartidas WHERE SolicitudId = $sid AND ProductoId = $pid";
            delCmd.Parameters.AddWithValue("$sid", solicitudId);
            delCmd.Parameters.AddWithValue("$pid", productoId);
            delCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return ObtenerSolicitudes().First(s => s.Id == solicitudId);
    }

    public static void EliminarSolicitud(int solicitudId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT Estatus FROM Solicitudes WHERE Id = $id";
            chk.Parameters.AddWithValue("$id", solicitudId);
            string estatus = Convert.ToString(chk.ExecuteScalar()) ?? "";
            if (estatus == "OC Generada")
            {
                throw new InvalidOperationException("No se puede eliminar una Solicitud que ya tiene Orden de Compra generada.");
            }
        }

        using (var delPartidas = conn.CreateCommand())
        {
            delPartidas.Transaction = tx;
            delPartidas.CommandText = "DELETE FROM SolicitudPartidas WHERE SolicitudId = $sid";
            delPartidas.Parameters.AddWithValue("$sid", solicitudId);
            delPartidas.ExecuteNonQuery();
        }

        using (var delSol = conn.CreateCommand())
        {
            delSol.Transaction = tx;
            delSol.CommandText = "DELETE FROM Solicitudes WHERE Id = $sid";
            delSol.Parameters.AddWithValue("$sid", solicitudId);
            delSol.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // ==================== ÓRDENES DE COMPRA Y RECEPCIONES PARCIALES ====================

    public static List<OrdenCompra> ObtenerOrdenesCompra()
    {
        var list = new List<OrdenCompra>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT oc.Id, oc.Folio, oc.Fecha, oc.SolicitudId, COALESCE(s.Folio, ''), oc.CotizacionFolio,
       oc.ProveedorId, COALESCE(pv.RazonSocial, 'Proveedor'), COALESCE(pv.NombreComercial, 'Proveedor'), COALESCE(pv.Rfc, ''),
       oc.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Consumo Interno de Departamento'), oc.DepartamentoNombre, oc.EncargadoNombre,
       oc.TiempoEntregaDias, oc.CostoEnvioConIva, oc.Subtotal, oc.Iva, oc.TotalConIva, oc.Estatus, oc.Notas,
       COALESCE(NULLIF(oc.CondicionPago, ''), 'Contado (Transferencia previa)'), COALESCE(oc.FechaProgramadaPago, '')
FROM OrdenesCompra oc
LEFT JOIN Proveedores pv ON pv.Id = oc.ProveedorId
LEFT JOIN Proyectos pr ON pr.Id = oc.ProyectoId
LEFT JOIN Solicitudes s ON s.Id = oc.SolicitudId
ORDER BY oc.Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string razon = r.GetString(7);
                string com = r.GetString(8);
                list.Add(new OrdenCompra
                {
                    Id = r.GetInt32(0),
                    Folio = r.GetString(1),
                    Fecha = r.GetString(2),
                    SolicitudId = r.GetInt32(3),
                    SolicitudFolio = r.GetString(4),
                    CotizacionFolio = r.GetString(5),
                    ProveedorId = r.GetInt32(6),
                    ProveedorNombre = string.IsNullOrWhiteSpace(com) ? razon : com,
                    ProveedorRfc = r.GetString(9),
                    ProyectoId = r.GetInt32(10),
                    ProyectoNombre = r.GetString(11),
                    DepartamentoNombre = r.GetString(12),
                    EncargadoNombre = r.GetString(13),
                    TiempoEntregaDias = r.GetInt32(14),
                    CostoEnvioConIva = Convert.ToDecimal(r.GetDouble(15)),
                    Subtotal = Convert.ToDecimal(r.GetDouble(16)),
                    Iva = Convert.ToDecimal(r.GetDouble(17)),
                    TotalConIva = Convert.ToDecimal(r.GetDouble(18)),
                    Estatus = r.GetString(19),
                    Notas = r.GetString(20),
                    CondicionPago = r.GetString(21),
                    FechaProgramadaPago = r.GetString(22)
                });
            }
        }

        if (list.Count == 0)
            return list;

        var ocDict = list.ToDictionary(o => o.Id);
        using (var pCmd = conn.CreateCommand())
        {
            pCmd.CommandText = @"
SELECT ocp.Id, ocp.OrdenCompraId, ocp.ProductoId, COALESCE(p.CodigoInterno, 'CONCEPTO'),
       CASE WHEN ocp.ProductoId > 0 AND p.Nombre IS NOT NULL THEN p.Nombre ELSE COALESCE(NULLIF(ocp.DescripcionLibre, ''), 'Concepto Abierto') END,
       COALESCE(NULLIF(TRIM(ocp.UnidadLibre), ''), p.UnidadCompra, 'Pieza'),
       COALESCE(p.UnidadAlmacen, COALESCE(NULLIF(TRIM(ocp.UnidadLibre), ''), 'Pieza')), COALESCE(p.FactorConversion, 1),
       ocp.CantidadPedida, ocp.CantidadRecibida, ocp.PrecioUnitarioSinIva, COALESCE(ocp.DescripcionLibre, ''),
       COALESCE(p.UnidadCompra, 'Pieza')
FROM OrdenCompraPartidas ocp
LEFT JOIN Productos p ON p.Id = ocp.ProductoId";
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                int ocId = pr.GetInt32(1);
                if (!ocDict.TryGetValue(ocId, out var oc))
                    continue;

                int prodId = pr.GetInt32(2);
                string nom = pr.GetString(4);
                string dLib = pr.GetString(11);
                string unidadCompraOc = pr.GetString(5);
                string unidadAlmacenProd = pr.GetString(6);
                decimal factorCatalogo = Convert.ToDecimal(pr.GetDouble(7));
                string unidadCatalogo = pr.GetString(12);
                decimal factorEfectivo;
                if (string.Equals(unidadCompraOc.Trim(), unidadAlmacenProd.Trim(), StringComparison.OrdinalIgnoreCase))
                    factorEfectivo = 1m;
                else if (string.Equals(unidadCompraOc.Trim(), unidadCatalogo.Trim(), StringComparison.OrdinalIgnoreCase))
                    factorEfectivo = factorCatalogo > 0m ? factorCatalogo : 1m;
                else
                    factorEfectivo = 1m;

                var part = new OrdenCompraPartida
                {
                    Id = pr.GetInt32(0),
                    OrdenCompraId = ocId,
                    ProductoId = prodId,
                    ProductoCodigo = prodId > 0 ? pr.GetString(3) : "CONCEPTO",
                    ProductoNombre = nom,
                    DescripcionLibre = !string.IsNullOrWhiteSpace(dLib) ? dLib : nom,
                    UnidadCompra = unidadCompraOc,
                    UnidadAlmacen = unidadAlmacenProd,
                    FactorConversion = factorEfectivo,
                    CantidadPedida = Convert.ToDecimal(pr.GetDouble(8)),
                    CantidadRecibida = Convert.ToDecimal(pr.GetDouble(9)),
                    PrecioUnitarioSinIva = Convert.ToDecimal(pr.GetDouble(10))
                };
                part.CantidadRecibirAhora = part.CantidadPendiente;
                oc.Partidas.Add(part);
            }
        }

        // Seguimiento: "Esperando respuesta" y cierre manual
        using (var sCmd = conn.CreateCommand())
        {
            sCmd.CommandText = "SELECT Id, COALESCE(EsperandoRespuestaDe,''), COALESCE(EsperandoDesde,''), COALESCE(ConcluidoManual,0) FROM OrdenesCompra";
            using var sr = sCmd.ExecuteReader();
            while (sr.Read())
            {
                if (ocDict.TryGetValue(sr.GetInt32(0), out var ocS))
                {
                    ocS.EsperandoRespuestaDe = sr.GetString(1);
                    ocS.EsperandoDesde = sr.GetString(2);
                    ocS.ConcluidoManual = sr.GetInt32(3) != 0;
                }
            }
        }

        return list;
    }

    /// <summary>Marca (o quita, con quien = vacío) la pausa "Esperando respuesta" de una Solicitud u Orden de Compra.</summary>
    public static void EstablecerEsperandoRespuesta(bool esOrdenCompra, int id, string quien)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        string tabla = esOrdenCompra ? "OrdenesCompra" : "Solicitudes";
        cmd.CommandText = $"UPDATE {tabla} SET EsperandoRespuestaDe = $q, EsperandoDesde = $d WHERE Id = $id";
        cmd.Parameters.AddWithValue("$q", quien ?? string.Empty);
        cmd.Parameters.AddWithValue("$d", string.IsNullOrWhiteSpace(quien) ? string.Empty : DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Marca o desmarca una Solicitud u Orden de Compra como concluida manualmente (oculta el "Siguiente paso").</summary>
    public static void EstablecerConcluidoManual(bool esOrdenCompra, int id, bool concluido)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        string tabla = esOrdenCompra ? "OrdenesCompra" : "Solicitudes";
        cmd.CommandText = $"UPDATE {tabla} SET ConcluidoManual = $c, EsperandoRespuestaDe = CASE WHEN $c = 1 THEN '' ELSE EsperandoRespuestaDe END WHERE Id = $id";
        cmd.Parameters.AddWithValue("$c", concluido ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<ReferenciaSimilarHistorica> ObtenerReferenciasSimilaresParaComparativa(int productoId, string? nombreOConcepto = null, string? familia = null)
    {
        var resultados = new List<ReferenciaSimilarHistorica>();
        var todosProductos = ObtenerProductos();
        var prodDict = todosProductos.ToDictionary(p => p.Id);
        var proveedores = ObtenerProveedores();
        var provDict = proveedores.ToDictionary(p => p.Id);
        var provPorRfc = proveedores
            .Where(p => !string.IsNullOrWhiteSpace(p.Rfc))
            .GroupBy(p => p.Rfc.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        Producto? prodBase = productoId > 0 && prodDict.TryGetValue(productoId, out var pb) ? pb : null;
        string textoBase = !string.IsNullOrWhiteSpace(nombreOConcepto) ? nombreOConcepto.Trim() : (prodBase?.Nombre ?? string.Empty);
        string famBase = !string.IsNullOrWhiteSpace(familia) ? familia.Trim() : (prodBase?.Familia ?? string.Empty);
        if (string.IsNullOrWhiteSpace(famBase))
            famBase = ExtraerFamiliaSugerida(textoBase);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DE", "LA", "EL", "LOS", "LAS", "PARA", "CON", "SIN", "POR", "DEL", "PZA", "PZAS", "PIEZA", "PIEZAS",
            "CAJA", "PAQ", "PAQUETE", "MOD", "MODELO", "TIPO", "COLOR", "USO", "PACK", "CONCEPTO", "ABIERTO"
        };

        var palabrasClave = textoBase
            .Split(new[] { ' ', ',', '.', '/', '-', '(', ')', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var idsCandidatos = new HashSet<int>();
        if (productoId > 0)
            idsCandidatos.Add(productoId);

        foreach (var p in todosProductos)
        {
            if (!string.IsNullOrWhiteSpace(famBase) &&
                !string.IsNullOrWhiteSpace(p.Familia) &&
                p.Familia.Equals(famBase, StringComparison.OrdinalIgnoreCase))
            {
                idsCandidatos.Add(p.Id);
                continue;
            }

            if (palabrasClave.Count > 0)
            {
                int coincidencias = palabrasClave.Count(w =>
                    p.Nombre.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                    p.Familia.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                    p.Marca.Contains(w, StringComparison.OrdinalIgnoreCase));

                if ((palabrasClave.Count == 1 && coincidencias >= 1) ||
                    (palabrasClave.Count >= 2 && coincidencias >= Math.Min(2, palabrasClave.Count)))
                {
                    idsCandidatos.Add(p.Id);
                }
            }
        }

        if (idsCandidatos.Count == 0 && palabrasClave.Count == 0 && string.IsNullOrWhiteSpace(famBase))
            return resultados;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // 1. Primero buscar en BancoPreciosHistorico (cotizaciones registradas con vigencia, tanto SKUs como Líneas Abiertas)
        using (var cmdBp = conn.CreateCommand())
        {
            cmdBp.CommandText = @"
SELECT Id, ProductoId, DescripcionConcepto, Familia, ProveedorId, PrecioSinIva, DiasEntrega, DiasVigencia, FechaCotizacion, OrigenFolio, COALESCE(NotasCotizacion, ''), COALESCE(FueGanador, 0)
FROM BancoPreciosHistorico
WHERE PrecioSinIva > 0 AND ProveedorId > 0
ORDER BY Id DESC";
            using var rb = cmdBp.ExecuteReader();
            while (rb.Read())
            {
                int idBp = rb.GetInt32(0);
                int pid = rb.GetInt32(1);
                string descC = rb.GetString(2);
                string famC = rb.GetString(3);
                int pvId = rb.GetInt32(4);
                if (!provDict.TryGetValue(pvId, out var prv))
                    continue;

                bool coincide = false;
                if (pid > 0 && idsCandidatos.Contains(pid))
                    coincide = true;
                else if (!string.IsNullOrWhiteSpace(textoBase) && descC.Equals(textoBase, StringComparison.OrdinalIgnoreCase))
                    coincide = true;
                else if (!string.IsNullOrWhiteSpace(famBase) && famC.Equals(famBase, StringComparison.OrdinalIgnoreCase))
                    coincide = true;
                else if (palabrasClave.Count > 0 && palabrasClave.Any(w => descC.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    coincide = true;

                if (!coincide)
                    continue;

                prodDict.TryGetValue(pid, out var prd);
                string codMostrar = prd != null ? prd.CodigoInterno : "LÍNEA ABIERTA";
                string nomMostrar = prd != null ? prd.Nombre : (string.IsNullOrWhiteSpace(descC) ? "Concepto Abierto" : descC);

                if (resultados.Any(x => x.ProductoId == pid && x.ProveedorId == pvId && x.ProductoNombre.Equals(nomMostrar, StringComparison.OrdinalIgnoreCase)))
                    continue;

                decimal precio = Math.Round(Convert.ToDecimal(rb.GetDouble(5)), 2);
                int diasEnt = rb.GetInt32(6);
                int diasVig = rb.GetInt32(7) > 0 ? rb.GetInt32(7) : 15;
                string fCot = rb.GetString(8);
                string origFol = rb.GetString(9);
                string notasCot = rb.GetString(10);
                bool fueGan = rb.GetInt32(11) != 0;

                string vigTag = CotizacionRenglonComparativo.FormatearVigencia(fCot, diasVig, detallado: false);
                string tagGan = fueGan ? " · Adjudicada" : "";

                resultados.Add(new ReferenciaSimilarHistorica
                {
                    IdBancoPrecio = idBp,
                    ProductoId = pid,
                    ProductoCodigo = codMostrar,
                    ProductoNombre = nomMostrar,
                    Familia = !string.IsNullOrWhiteSpace(famC) ? famC : (prd?.Familia ?? ""),
                    ProveedorId = pvId,
                    ProveedorNombre = prv.NombreMostrar,
                    PrecioSinIva = precio,
                    DiasEntrega = diasEnt > 0 ? diasEnt : 2,
                    DiasVigencia = diasVig,
                    FechaCotizacion = fCot,
                    NotasCotizacion = notasCot,
                    ReferenciaOrigen = $"Cotización {origFol}{tagGan} [{vigTag}] ({fCot})"
                });
            }
        }

        if (idsCandidatos.Count > 0)
        {
            // 2. Complementar con Órdenes de Compra de todos los productos candidatos (familia / similares)
            using (var cmdOc = conn.CreateCommand())
            {
                cmdOc.CommandText = $@"
SELECT ocp.ProductoId, oc.ProveedorId, ocp.PrecioUnitarioSinIva, COALESCE(oc.TiempoEntregaDias, 2), oc.Folio, oc.Fecha
FROM OrdenCompraPartidas ocp
INNER JOIN OrdenesCompra oc ON oc.Id = ocp.OrdenCompraId
WHERE ocp.ProductoId IN ({string.Join(",", idsCandidatos)})
  AND oc.ProveedorId > 0
  AND ocp.PrecioUnitarioSinIva > 0
  AND oc.Estatus <> 'Cancelada'
ORDER BY ocp.Id DESC";
                using var r = cmdOc.ExecuteReader();
                while (r.Read())
                {
                    int pid = r.GetInt32(0);
                    int pvId = r.GetInt32(1);
                    if (!prodDict.TryGetValue(pid, out var prd) || !provDict.TryGetValue(pvId, out var prv))
                        continue;

                    if (resultados.Any(x => x.ProductoId == pid && x.ProveedorId == pvId))
                        continue;

                    decimal precio = Math.Round(Convert.ToDecimal(r.GetDouble(2)), 2);
                    int dias = r.GetInt32(3);
                    string folioOc = r.GetString(4);
                    string fechaOc = r.GetString(5);
                    string fechaCorta = fechaOc.Length >= 10 ? fechaOc.Substring(0, 10) : fechaOc;

                    resultados.Add(new ReferenciaSimilarHistorica
                    {
                        ProductoId = pid,
                        ProductoCodigo = prd.CodigoInterno,
                        ProductoNombre = prd.Nombre,
                        Familia = prd.Familia,
                        ProveedorId = pvId,
                        ProveedorNombre = prv.NombreMostrar,
                        PrecioSinIva = precio,
                        DiasEntrega = dias > 0 ? dias : 2,
                        DiasVigencia = 15,
                        FechaCotizacion = fechaCorta,
                        ReferenciaOrigen = $"OC Histórica {folioOc} ({fechaCorta}) [{prd.CodigoInterno}]"
                    });
                }
            }

            // 3. Complementar con ProductoProveedorMapa (Facturas XML)
            using (var cmdXml = conn.CreateCommand())
            {
                cmdXml.CommandText = $@"
SELECT ProductoId, EmisorRfc, UltimoPrecioSinIva, ClaveProveedor
FROM ProductoProveedorMapa
WHERE ProductoId IN ({string.Join(",", idsCandidatos)})
  AND UltimoPrecioSinIva > 0
ORDER BY Id DESC";
                using var rx = cmdXml.ExecuteReader();
                while (rx.Read())
                {
                    int pid = rx.GetInt32(0);
                    string rfc = (rx.GetString(1) ?? "").Trim().ToUpperInvariant();
                    if (!prodDict.TryGetValue(pid, out var prd) || !provPorRfc.TryGetValue(rfc, out var prv))
                        continue;

                    if (resultados.Any(x => x.ProductoId == pid && x.ProveedorId == prv.Id))
                        continue;

                    decimal precio = Math.Round(Convert.ToDecimal(rx.GetDouble(2)), 2);
                    string clave = rx.GetString(3);

                    resultados.Add(new ReferenciaSimilarHistorica
                    {
                        ProductoId = pid,
                        ProductoCodigo = prd.CodigoInterno,
                        ProductoNombre = prd.Nombre,
                        Familia = prd.Familia,
                        ProveedorId = prv.Id,
                        ProveedorNombre = prv.NombreMostrar,
                        PrecioSinIva = precio,
                        DiasEntrega = prv.DiasEntregaPromedio > 0 ? prv.DiasEntregaPromedio : 3,
                        DiasVigencia = 30,
                        FechaCotizacion = "",
                        ReferenciaOrigen = $"Factura XML ({clave} - [{prd.CodigoInterno}] {prd.Nombre})"
                    });
                }
            }
        }

        // Ordenar priorizando el mismo producto/concepto primero y luego el mejor precio unitario sin IVA
        return resultados
            .OrderByDescending(x => productoId > 0 && x.ProductoId == productoId ? 2 : (x.ProductoNombre.Equals(textoBase, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            .ThenBy(x => x.PrecioSinIva)
            .ToList();
    }

    public static (List<(int proveedorId, decimal precioSinIva, int diasEntrega, string notaSugerida)> opciones, string referenciaHistorica) ObtenerSugerenciasCotizacionPorProducto(int productoId, string? nombreOConcepto = null, string? familia = null)
    {
        var similares = ObtenerReferenciasSimilaresParaComparativa(productoId, nombreOConcepto, familia);
        var opciones = similares
            .GroupBy(s => s.ProveedorId)
            .Select(g =>
            {
                var best = g.OrderBy(x => x.PrecioSinIva).First();
                return (best.ProveedorId, best.PrecioSinIva, best.DiasEntrega, best.ReferenciaOrigen);
            })
            .OrderBy(x => x.PrecioSinIva)
            .Take(3)
            .ToList();

        string refHist;
        if (similares.Count > 0)
        {
            var mejor = similares[0];
            int numProvs = similares.Select(x => x.ProveedorId).Distinct().Count();
            int numProds = similares.Select(x => x.ProductoId).Distinct().Count();
            refHist = $"Mejor histórico ({numProds} SKU(s) similares / {numProvs} prov.): {mejor.ProveedorNombre} a ${mejor.PrecioSinIva:N2} s/IVA (${mejor.PrecioConIva:N2} c/IVA) [{mejor.ProductoCodigo}]";
        }
        else
        {
            refHist = "Sin compras previas registradas para este artículo o similares (asigne proveedor a cotizar)";
        }

        return (opciones, refHist);
    }

    public static List<CotizacionProveedorExtra> ObtenerProveedoresExtraPartida(int solicitudPartidaId)
    {
        var list = new List<CotizacionProveedorExtra>();
        if (solicitudPartidaId <= 0) return list;

        var provDict = ObtenerProveedores().ToDictionary(p => p.Id);
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, SolicitudPartidaId, NumeroOpcion, ProveedorId, PrecioSinIva, DiasEntrega, FleteConIva, DiasVigencia, FechaCotizacion, NotasCotizacion
FROM CotizacionesPartidaProveedor
WHERE SolicitudPartidaId = $spid
ORDER BY NumeroOpcion ASC, Id ASC";
        cmd.Parameters.AddWithValue("$spid", solicitudPartidaId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int pvId = r.GetInt32(3);
            string pvNom = provDict.TryGetValue(pvId, out var pv) ? pv.NombreMostrar : "Proveedor";
            list.Add(new CotizacionProveedorExtra
            {
                Id = r.GetInt32(0),
                SolicitudPartidaId = r.GetInt32(1),
                NumeroOpcion = r.GetInt32(2),
                ProveedorId = pvId,
                ProveedorNombre = pvNom,
                PrecioSinIva = Convert.ToDecimal(r.GetDouble(4)),
                DiasEntrega = r.GetInt32(5),
                FleteConIva = Convert.ToDecimal(r.GetDouble(6)),
                DiasVigencia = r.GetInt32(7) > 0 ? r.GetInt32(7) : 15,
                FechaCotizacion = r.GetString(8),
                NotasCotizacion = r.GetString(9)
            });
        }
        return list;
    }

    public static void GuardarProveedorExtraPartida(CotizacionProveedorExtra extra)
    {
        if (extra.SolicitudPartidaId <= 0 || extra.ProveedorId <= 0) return;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var permiso = conn.CreateCommand())
        {
            permiso.CommandText = @"SELECT COUNT(*) FROM SolicitudPartidas sp
JOIN Solicitudes s ON s.Id = sp.SolicitudId
WHERE sp.Id = $id AND (s.UsaPartidas = 0 OR sp.OrdenCompraId = 0);";
            permiso.Parameters.AddWithValue("$id", extra.SolicitudPartidaId);
            if (Convert.ToInt32(permiso.ExecuteScalar()) == 0) return;
        }

        if (extra.NumeroOpcion < 4)
        {
            using var maxCmd = conn.CreateCommand();
            maxCmd.CommandText = "SELECT COALESCE(MAX(NumeroOpcion), 3) FROM CotizacionesPartidaProveedor WHERE SolicitudPartidaId = $spid";
            maxCmd.Parameters.AddWithValue("$spid", extra.SolicitudPartidaId);
            extra.NumeroOpcion = Convert.ToInt32(maxCmd.ExecuteScalar()) + 1;
        }

        string fCot = string.IsNullOrWhiteSpace(extra.FechaCotizacion) ? DateTime.Today.ToString("yyyy-MM-dd") : extra.FechaCotizacion;
        using var cmd = conn.CreateCommand();
        if (extra.Id > 0)
        {
            cmd.CommandText = @"
UPDATE CotizacionesPartidaProveedor
SET ProveedorId = $pv, PrecioSinIva = $pr, DiasEntrega = $de, FleteConIva = $fl, DiasVigencia = $dv, FechaCotizacion = $fc, NotasCotizacion = $not
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", extra.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO CotizacionesPartidaProveedor (SolicitudPartidaId, NumeroOpcion, ProveedorId, PrecioSinIva, DiasEntrega, FleteConIva, DiasVigencia, FechaCotizacion, NotasCotizacion)
VALUES ($spid, $num, $pv, $pr, $de, $fl, $dv, $fc, $not);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$spid", extra.SolicitudPartidaId);
            cmd.Parameters.AddWithValue("$num", extra.NumeroOpcion);
        }
        cmd.Parameters.AddWithValue("$pv", extra.ProveedorId);
        cmd.Parameters.AddWithValue("$pr", (double)extra.PrecioSinIva);
        cmd.Parameters.AddWithValue("$de", Math.Max(1, extra.DiasEntrega));
        cmd.Parameters.AddWithValue("$fl", (double)extra.FleteConIva);
        cmd.Parameters.AddWithValue("$dv", extra.DiasVigencia > 0 ? extra.DiasVigencia : 15);
        cmd.Parameters.AddWithValue("$fc", fCot);
        cmd.Parameters.AddWithValue("$not", extra.NotasCotizacion ?? string.Empty);
        if (extra.Id > 0)
            cmd.ExecuteNonQuery();
        else
            extra.Id = Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void EliminarProveedorExtraPartida(int extraId)
    {
        if (extraId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CotizacionesPartidaProveedor WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", extraId);
        cmd.ExecuteNonQuery();
    }

    public static void RegistrarPrecioEnBancoHistorico(
        int productoId,
        string descripcionConcepto,
        string familia,
        int proveedorId,
        decimal precioSinIva,
        int diasEntrega,
        int diasVigencia,
        string origenFolio,
        string? fechaCotizacion = null,
        string? notasCotizacion = null,
        bool fueGanador = false,
        int solicitudPartidaId = 0)
    {
        if (proveedorId <= 0 || precioSinIva <= 0m) return;
        string fechaUsar = string.IsNullOrWhiteSpace(fechaCotizacion) ? DateTime.Today.ToString("yyyy-MM-dd") : fechaCotizacion.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var del = conn.CreateCommand())
        {
            del.CommandText = @"
DELETE FROM BancoPreciosHistorico
WHERE ProveedorId = $pv AND FechaCotizacion = $fc AND OrigenFolio = $fol AND (
    ($pid > 0 AND ProductoId = $pid) OR
    ($pid <= 0 AND UPPER(TRIM(DescripcionConcepto)) = UPPER(TRIM($desc)))
)";
            del.Parameters.AddWithValue("$pv", proveedorId);
            del.Parameters.AddWithValue("$fc", fechaUsar);
            del.Parameters.AddWithValue("$fol", string.IsNullOrWhiteSpace(origenFolio) ? "COT" : origenFolio.Trim());
            del.Parameters.AddWithValue("$pid", productoId);
            del.Parameters.AddWithValue("$desc", descripcionConcepto ?? string.Empty);
            del.ExecuteNonQuery();
        }

        using var ins = conn.CreateCommand();
        ins.CommandText = @"
INSERT INTO BancoPreciosHistorico (ProductoId, DescripcionConcepto, Familia, ProveedorId, PrecioSinIva, DiasEntrega, DiasVigencia, FechaCotizacion, OrigenFolio, NotasCotizacion, FueGanador, SolicitudPartidaId)
VALUES ($pid, $desc, $fam, $pv, $pr, $de, $dv, $fc, $fol, $not, $gan, $spid)";
        ins.Parameters.AddWithValue("$pid", productoId);
        ins.Parameters.AddWithValue("$desc", (descripcionConcepto ?? string.Empty).Trim());
        ins.Parameters.AddWithValue("$fam", (familia ?? string.Empty).Trim());
        ins.Parameters.AddWithValue("$pv", proveedorId);
        ins.Parameters.AddWithValue("$pr", (double)precioSinIva);
        ins.Parameters.AddWithValue("$de", Math.Max(1, diasEntrega));
        ins.Parameters.AddWithValue("$dv", diasVigencia > 0 ? diasVigencia : 15);
        ins.Parameters.AddWithValue("$fc", fechaUsar);
        ins.Parameters.AddWithValue("$fol", string.IsNullOrWhiteSpace(origenFolio) ? "COT" : origenFolio.Trim());
        ins.Parameters.AddWithValue("$not", (notasCotizacion ?? string.Empty).Trim());
        ins.Parameters.AddWithValue("$gan", fueGanador ? 1 : 0);
        ins.Parameters.AddWithValue("$spid", solicitudPartidaId);
        ins.ExecuteNonQuery();
    }

    public static void GuardarCotizacionPartida(CotizacionRenglonComparativo r, string origenFolio = "COT")
    {
        if (r.SolicitudPartidaId <= 0) return;

        string hoy = DateTime.Today.ToString("yyyy-MM-dd");
        bool tiene1 = r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0;
        bool tiene2 = r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0;
        bool tiene3 = r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0;
        bool tieneAlgunaCotizacion = tiene1 || tiene2 || tiene3 ||
            r.ProveedoresExtra.Any(x => x.ProveedorId > 0 && x.PrecioSinIva > 0);

        if (tieneAlgunaCotizacion && string.IsNullOrWhiteSpace(r.FechaCotizacion))
            r.FechaCotizacion = hoy;
        if (tiene1 && string.IsNullOrWhiteSpace(r.FechaCotizacion1))
            r.FechaCotizacion1 = !string.IsNullOrWhiteSpace(r.FechaCotizacion) ? r.FechaCotizacion : hoy;
        if (tiene2 && string.IsNullOrWhiteSpace(r.FechaCotizacion2))
            r.FechaCotizacion2 = !string.IsNullOrWhiteSpace(r.FechaCotizacion) ? r.FechaCotizacion : hoy;
        if (tiene3 && string.IsNullOrWhiteSpace(r.FechaCotizacion3))
            r.FechaCotizacion3 = !string.IsNullOrWhiteSpace(r.FechaCotizacion) ? r.FechaCotizacion : hoy;

        int vigencia = r.DiasVigenciaCotizacion > 0 ? r.DiasVigenciaCotizacion : 15;
        int vig1 = r.DiasVigencia1 > 0 ? r.DiasVigencia1 : vigencia;
        int vig2 = r.DiasVigencia2 > 0 ? r.DiasVigencia2 : vigencia;
        int vig3 = r.DiasVigencia3 > 0 ? r.DiasVigencia3 : vigencia;
        r.DiasVigencia1 = vig1;
        r.DiasVigencia2 = vig2;
        r.DiasVigencia3 = vig3;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE SolicitudPartidas
SET CantidadSolicitada = $cant,
    UnidadLibre = $ulib,
    Proveedor1Id = $pv1, PrecioSinIva1 = $pr1, DiasEntrega1 = $d1, FleteConIva1 = $fl1, NotasCotizacion1 = $nc1, DiasVigencia1 = $dv1, FechaCotizacion1 = $fc1,
    Proveedor2Id = $pv2, PrecioSinIva2 = $pr2, DiasEntrega2 = $d2, FleteConIva2 = $fl2, NotasCotizacion2 = $nc2, DiasVigencia2 = $dv2, FechaCotizacion2 = $fc2,
    Proveedor3Id = $pv3, PrecioSinIva3 = $pr3, DiasEntrega3 = $d3, FleteConIva3 = $fl3, NotasCotizacion3 = $nc3, DiasVigencia3 = $dv3, FechaCotizacion3 = $fc3,
    ProveedorGanadorIndice = $gan,
    DiasVigenciaCotizacion = $vig,
    FechaCotizacion = $fcot
WHERE Id = $id AND OrdenCompraId = 0";
        cmd.Parameters.AddWithValue("$cant", (double)r.Cantidad);
        cmd.Parameters.AddWithValue("$ulib", NormalizarUnidadMedida(r.UnidadCompra));
        cmd.Parameters.AddWithValue("$pv1", r.Proveedor1Id);
        cmd.Parameters.AddWithValue("$pr1", (double)r.PrecioSinIva1);
        cmd.Parameters.AddWithValue("$d1", r.DiasEntrega1);
        cmd.Parameters.AddWithValue("$fl1", (double)r.FleteConIva1);
        cmd.Parameters.AddWithValue("$nc1", r.NotasCotizacion1 ?? string.Empty);
        cmd.Parameters.AddWithValue("$dv1", vig1);
        cmd.Parameters.AddWithValue("$fc1", r.FechaCotizacion1 ?? string.Empty);

        cmd.Parameters.AddWithValue("$pv2", r.Proveedor2Id);
        cmd.Parameters.AddWithValue("$pr2", (double)r.PrecioSinIva2);
        cmd.Parameters.AddWithValue("$d2", r.DiasEntrega2);
        cmd.Parameters.AddWithValue("$fl2", (double)r.FleteConIva2);
        cmd.Parameters.AddWithValue("$nc2", r.NotasCotizacion2 ?? string.Empty);
        cmd.Parameters.AddWithValue("$dv2", vig2);
        cmd.Parameters.AddWithValue("$fc2", r.FechaCotizacion2 ?? string.Empty);

        cmd.Parameters.AddWithValue("$pv3", r.Proveedor3Id);
        cmd.Parameters.AddWithValue("$pr3", (double)r.PrecioSinIva3);
        cmd.Parameters.AddWithValue("$d3", r.DiasEntrega3);
        cmd.Parameters.AddWithValue("$fl3", (double)r.FleteConIva3);
        cmd.Parameters.AddWithValue("$nc3", r.NotasCotizacion3 ?? string.Empty);
        cmd.Parameters.AddWithValue("$dv3", vig3);
        cmd.Parameters.AddWithValue("$fc3", r.FechaCotizacion3 ?? string.Empty);

        cmd.Parameters.AddWithValue("$gan", r.ProveedorGanadorIndice);
        cmd.Parameters.AddWithValue("$vig", vigencia);
        cmd.Parameters.AddWithValue("$fcot", r.FechaCotizacion ?? string.Empty);
        cmd.Parameters.AddWithValue("$id", r.SolicitudPartidaId);
        if (cmd.ExecuteNonQuery() == 0) return;

        string descConcepto = !string.IsNullOrWhiteSpace(r.DescripcionLibre) ? r.DescripcionLibre : r.ProductoNombre;
        if (tiene1)
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, r.Proveedor1Id, r.PrecioSinIva1, r.DiasEntrega1, vig1, origenFolio, r.FechaCotizacion1, r.NotasCotizacion1, r.ProveedorGanadorIndice == 1, r.SolicitudPartidaId);
        if (tiene2)
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, r.Proveedor2Id, r.PrecioSinIva2, r.DiasEntrega2, vig2, origenFolio, r.FechaCotizacion2, r.NotasCotizacion2, r.ProveedorGanadorIndice == 2, r.SolicitudPartidaId);
        if (tiene3)
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, r.Proveedor3Id, r.PrecioSinIva3, r.DiasEntrega3, vig3, origenFolio, r.FechaCotizacion3, r.NotasCotizacion3, r.ProveedorGanadorIndice == 3, r.SolicitudPartidaId);

        foreach (var ex in r.ProveedoresExtra.Where(x => x.ProveedorId > 0 && x.PrecioSinIva > 0))
        {
            int vigEx = ex.DiasVigencia > 0 ? ex.DiasVigencia : vigencia;
            string fecEx = !string.IsNullOrWhiteSpace(ex.FechaCotizacion) ? ex.FechaCotizacion : (r.FechaCotizacion ?? hoy);
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, ex.ProveedorId, ex.PrecioSinIva, ex.DiasEntrega, vigEx, origenFolio, fecEx, ex.NotasCotizacion, r.ProveedorGanadorIndice == ex.NumeroOpcion, r.SolicitudPartidaId);
        }
    }

    public static string CalcularTiempoRelativoEntregaComparativa(IEnumerable<CotizacionRenglonComparativo>? renglones)
    {
        var cfg = ObtenerConfiguracionSmtpCorreo();
        string fallback = string.IsNullOrWhiteSpace(cfg.TiempoAproxComparativaDefault) ? "1 a 2 días hábiles" : cfg.TiempoAproxComparativaDefault.Trim();
        if (renglones == null) return fallback;

        var lista = renglones.ToList();
        // Priorizar días de entrega de proveedores ganadores si ya se eligieron; si no, de todos los cotizados
        var diasGanadores = lista
            .Where(r => r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .Select(r =>
            {
                if (r.ProveedorGanadorIndice == 1) return r.DiasEntrega1;
                if (r.ProveedorGanadorIndice == 2) return r.DiasEntrega2;
                if (r.ProveedorGanadorIndice == 3) return r.DiasEntrega3;
                var ex = r.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == r.ProveedorGanadorIndice);
                return ex?.DiasEntrega ?? 0;
            })
            .Where(d => d > 0)
            .ToList();

        var diasCotizados = diasGanadores.Count > 0
            ? diasGanadores
            : lista.SelectMany(r => new[]
            {
                r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0 ? r.DiasEntrega1 : 0,
                r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0 ? r.DiasEntrega2 : 0,
                r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0 ? r.DiasEntrega3 : 0
            }.Concat(r.ProveedoresExtra.Where(x => x.ProveedorId > 0 && x.PrecioSinIva > 0).Select(x => x.DiasEntrega)))
            .Where(d => d > 0)
            .ToList();

        if (diasCotizados.Count == 0) return fallback;
        int minD = diasCotizados.Min();
        int maxD = diasCotizados.Max();
        return minD == maxD
            ? (minD == 1 ? "1 día hábil" : $"{minD} días hábiles")
            : $"{minD} a {maxD} días hábiles";
    }

    public static void GuardarBorradorComparativa(
        int solicitudId,
        List<CotizacionRenglonComparativo> renglones,
        string folioComparativa = "COT",
        string? tiempoAproxEntregaPersonalizado = null,
        bool? forzarNotificacionCorreo = null)
    {
        if (renglones.Count == 0 && ObtenerSolicitudes().Any(s => s.Id == solicitudId && s.UsaPartidas))
            return;

        foreach (var r in renglones)
        {
            if (!r.TieneOcGenerada)
            {
                GuardarCotizacionPartida(r, folioComparativa);
            }
        }

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE Solicitudes
SET Estatus = CASE
    WHEN Estatus = 'Pendiente' THEN 'En Comparativa'
    ELSE Estatus
END
WHERE Id = $sid";
        cmd.Parameters.AddWithValue("$sid", solicitudId);
        cmd.ExecuteNonQuery();

        var solComp = ObtenerSolicitudes().FirstOrDefault(s => s.Id == solicitudId);
        bool debeNotificar = forzarNotificacionCorreo ?? (solComp != null && !YaSeEnvioNotificacionEtapa(solComp.Folio, "2_COMPARATIVA"));
        if (solComp != null && debeNotificar)
        {
            string tiempoEstimadoComp = !string.IsNullOrWhiteSpace(tiempoAproxEntregaPersonalizado)
                ? tiempoAproxEntregaPersonalizado.Trim()
                : CalcularTiempoRelativoEntregaComparativa(renglones);
            NotificarCambioEstadoCotizacionGerente(solicitudId, null, "2_COMPARATIVA", tiempoEstimadoComp);
        }
    }

    public static List<RegistroCotizacionVigencia> ObtenerCotizacionesBancoHistorico(int? productoId = null, int? proveedorId = null, string? filtroTexto = null)
    {
        var lista = new List<RegistroCotizacionVigencia>();
        var prodDict = ObtenerProductos().ToDictionary(p => p.Id);
        var provDict = ObtenerProveedores().ToDictionary(p => p.Id);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT Id, ProductoId, DescripcionConcepto, Familia, ProveedorId, PrecioSinIva, DiasEntrega, DiasVigencia, FechaCotizacion, OrigenFolio,
       COALESCE(NotasCotizacion, ''), COALESCE(FueGanador, 0), COALESCE(SolicitudPartidaId, 0)
FROM BancoPreciosHistorico
WHERE PrecioSinIva > 0 AND ProveedorId > 0
ORDER BY Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                int pid = r.GetInt32(1);
                string desc = r.GetString(2);
                string fam = r.GetString(3);
                int pvId = r.GetInt32(4);

                if (productoId.HasValue && pid != productoId.Value)
                    continue;
                if (proveedorId.HasValue && pvId != proveedorId.Value)
                    continue;

                prodDict.TryGetValue(pid, out var prd);
                provDict.TryGetValue(pvId, out var prv);

                string codProd = prd != null ? prd.CodigoInterno : "LÍNEA ABIERTA";
                string nomProd = prd != null ? prd.Nombre : (string.IsNullOrWhiteSpace(desc) ? "Concepto Abierto" : desc);
                string nomProv = prv != null ? prv.NombreMostrar : $"Proveedor #{pvId}";

                if (!string.IsNullOrWhiteSpace(filtroTexto))
                {
                    string f = filtroTexto.Trim();
                    if (!nomProd.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                        !codProd.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                        !nomProv.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                        !desc.Contains(f, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                lista.Add(new RegistroCotizacionVigencia
                {
                    Id = id,
                    ProductoId = pid,
                    ProductoCodigo = codProd,
                    DescripcionConcepto = nomProd,
                    Familia = !string.IsNullOrWhiteSpace(fam) ? fam : (prd?.Familia ?? ""),
                    ProveedorId = pvId,
                    ProveedorNombre = nomProv,
                    PrecioSinIva = Math.Round(Convert.ToDecimal(r.GetDouble(5)), 2),
                    DiasEntrega = r.GetInt32(6) > 0 ? r.GetInt32(6) : 2,
                    DiasVigencia = r.GetInt32(7) > 0 ? r.GetInt32(7) : 15,
                    FechaCotizacion = r.GetString(8),
                    OrigenFolio = r.GetString(9),
                    NotasCotizacion = r.GetString(10),
                    FueGanador = r.GetInt32(11) != 0,
                    SolicitudPartidaId = r.GetInt32(12)
                });
            }
        }

        // Complementar con partidas de SolicitudPartidas cotizadas que aún no estén en BancoPreciosHistorico
        using (var cmdSp = conn.CreateCommand())
        {
            cmdSp.CommandText = @"
SELECT sp.Id, sp.ProductoId, COALESCE(sp.DescripcionLibre, ''), s.Folio, s.Fecha,
       COALESCE(sp.Proveedor1Id, 0), COALESCE(sp.PrecioSinIva1, 0), COALESCE(sp.DiasEntrega1, 2), COALESCE(sp.DiasVigencia1, sp.DiasVigenciaCotizacion, 15), COALESCE(NULLIF(sp.FechaCotizacion1, ''), NULLIF(sp.FechaCotizacion, ''), SUBSTR(s.Fecha, 1, 10)), COALESCE(sp.NotasCotizacion1, ''),
       COALESCE(sp.Proveedor2Id, 0), COALESCE(sp.PrecioSinIva2, 0), COALESCE(sp.DiasEntrega2, 3), COALESCE(sp.DiasVigencia2, sp.DiasVigenciaCotizacion, 15), COALESCE(NULLIF(sp.FechaCotizacion2, ''), NULLIF(sp.FechaCotizacion, ''), SUBSTR(s.Fecha, 1, 10)), COALESCE(sp.NotasCotizacion2, ''),
       COALESCE(sp.Proveedor3Id, 0), COALESCE(sp.PrecioSinIva3, 0), COALESCE(sp.DiasEntrega3, 1), COALESCE(sp.DiasVigencia3, sp.DiasVigenciaCotizacion, 15), COALESCE(NULLIF(sp.FechaCotizacion3, ''), NULLIF(sp.FechaCotizacion, ''), SUBSTR(s.Fecha, 1, 10)), COALESCE(sp.NotasCotizacion3, ''),
       COALESCE(sp.ProveedorGanadorIndice, 0), COALESCE(sp.OrdenCompraFolio, '')
FROM SolicitudPartidas sp
INNER JOIN Solicitudes s ON s.Id = sp.SolicitudId
WHERE (sp.Proveedor1Id > 0 AND sp.PrecioSinIva1 > 0)
   OR (sp.Proveedor2Id > 0 AND sp.PrecioSinIva2 > 0)
   OR (sp.Proveedor3Id > 0 AND sp.PrecioSinIva3 > 0)
ORDER BY sp.Id DESC";
            using var rs = cmdSp.ExecuteReader();
            while (rs.Read())
            {
                int spId = rs.GetInt32(0);
                int pid = rs.GetInt32(1);
                string descLib = rs.GetString(2);
                string solFolio = rs.GetString(3);
                int ganIdx = rs.GetInt32(23);
                string ocFolio = rs.GetString(24);

                if (productoId.HasValue && pid != productoId.Value)
                    continue;

                prodDict.TryGetValue(pid, out var prd);
                string codProd = prd != null ? prd.CodigoInterno : "LÍNEA ABIERTA";
                string nomProd = prd != null ? prd.Nombre : (string.IsNullOrWhiteSpace(descLib) ? "Concepto Abierto" : descLib);

                void IntentarAgregarOpc(int opcIdx, int pvId, decimal precio, int diasEnt, int diasVig, string fCot, string notas)
                {
                    if (pvId <= 0 || precio <= 0m) return;
                    if (proveedorId.HasValue && pvId != proveedorId.Value) return;
                    if (lista.Any(x => x.ProveedorId == pvId && x.ProductoId == pid && x.DescripcionConcepto.Equals(nomProd, StringComparison.OrdinalIgnoreCase) && x.FechaCotizacion == fCot))
                        return;

                    provDict.TryGetValue(pvId, out var prv);
                    string nomProv = prv != null ? prv.NombreMostrar : $"Proveedor #{pvId}";
                    bool esGan = ganIdx == opcIdx;
                    string folOrig = esGan && !string.IsNullOrWhiteSpace(ocFolio) ? ocFolio : solFolio;

                    lista.Add(new RegistroCotizacionVigencia
                    {
                        Id = 0,
                        ProductoId = pid,
                        ProductoCodigo = codProd,
                        DescripcionConcepto = nomProd,
                        Familia = prd?.Familia ?? ExtraerFamiliaSugerida(nomProd),
                        ProveedorId = pvId,
                        ProveedorNombre = nomProv,
                        PrecioSinIva = Math.Round(precio, 2),
                        DiasEntrega = diasEnt > 0 ? diasEnt : 2,
                        DiasVigencia = diasVig > 0 ? diasVig : 15,
                        FechaCotizacion = fCot,
                        OrigenFolio = folOrig,
                        NotasCotizacion = notas,
                        FueGanador = esGan,
                        SolicitudPartidaId = spId
                    });
                }

                IntentarAgregarOpc(1, rs.GetInt32(5), Convert.ToDecimal(rs.GetDouble(6)), rs.GetInt32(7), rs.GetInt32(8), rs.GetString(9), rs.GetString(10));
                IntentarAgregarOpc(2, rs.GetInt32(11), Convert.ToDecimal(rs.GetDouble(12)), rs.GetInt32(13), rs.GetInt32(14), rs.GetString(15), rs.GetString(16));
                IntentarAgregarOpc(3, rs.GetInt32(17), Convert.ToDecimal(rs.GetDouble(18)), rs.GetInt32(19), rs.GetInt32(20), rs.GetString(21), rs.GetString(22));
            }
        }

        return lista
            .OrderByDescending(x => x.FechaCotizacion)
            .ThenBy(x => x.PrecioSinIva)
            .ToList();
    }

    /// <summary>
    /// Vincula una partida de Línea Abierta (ProductoId == 0) y todo su historial de cotizaciones y OCs con el mismo concepto a un SKU del Catálogo.
    /// </summary>
    public static int VincularLineaAbiertaAProductoCatalogo(int productoIdDestino, int? solicitudPartidaId = null, int? bancoPrecioId = null, string? descripcionConcepto = null)
    {
        if (productoIdDestino <= 0) return 0;
        var prod = ObtenerProductos().FirstOrDefault(p => p.Id == productoIdDestino);
        if (prod == null) return 0;

        string conceptoNorm = (descripcionConcepto ?? string.Empty).Trim();
        int totalActualizados = 0;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        if (solicitudPartidaId.HasValue && solicitudPartidaId.Value > 0)
        {
            using var cmdSp = conn.CreateCommand();
            cmdSp.Transaction = tx;
            cmdSp.CommandText = "UPDATE SolicitudPartidas SET ProductoId = $pid WHERE Id = $spid";
            cmdSp.Parameters.AddWithValue("$pid", productoIdDestino);
            cmdSp.Parameters.AddWithValue("$spid", solicitudPartidaId.Value);
            totalActualizados += cmdSp.ExecuteNonQuery();
        }

        if (bancoPrecioId.HasValue && bancoPrecioId.Value > 0)
        {
            using var cmdBp = conn.CreateCommand();
            cmdBp.Transaction = tx;
            cmdBp.CommandText = "UPDATE BancoPreciosHistorico SET ProductoId = $pid, Familia = CASE WHEN TRIM(Familia) = '' THEN $fam ELSE Familia END WHERE Id = $bpid";
            cmdBp.Parameters.AddWithValue("$pid", productoIdDestino);
            cmdBp.Parameters.AddWithValue("$fam", prod.Familia ?? string.Empty);
            cmdBp.Parameters.AddWithValue("$bpid", bancoPrecioId.Value);
            totalActualizados += cmdBp.ExecuteNonQuery();
        }

        if (!string.IsNullOrWhiteSpace(conceptoNorm))
        {
            using (var cmdBpDesc = conn.CreateCommand())
            {
                cmdBpDesc.Transaction = tx;
                cmdBpDesc.CommandText = @"
UPDATE BancoPreciosHistorico
SET ProductoId = $pid,
    Familia = CASE WHEN TRIM(Familia) = '' THEN $fam ELSE Familia END
WHERE COALESCE(ProductoId, 0) <= 0
  AND UPPER(TRIM(DescripcionConcepto)) = UPPER(TRIM($desc))";
                cmdBpDesc.Parameters.AddWithValue("$pid", productoIdDestino);
                cmdBpDesc.Parameters.AddWithValue("$fam", prod.Familia ?? string.Empty);
                cmdBpDesc.Parameters.AddWithValue("$desc", conceptoNorm);
                totalActualizados += cmdBpDesc.ExecuteNonQuery();
            }

            using (var cmdSpDesc = conn.CreateCommand())
            {
                cmdSpDesc.Transaction = tx;
                cmdSpDesc.CommandText = @"
UPDATE SolicitudPartidas
SET ProductoId = $pid
WHERE COALESCE(ProductoId, 0) <= 0
  AND UPPER(TRIM(DescripcionLibre)) = UPPER(TRIM($desc))";
                cmdSpDesc.Parameters.AddWithValue("$pid", productoIdDestino);
                cmdSpDesc.Parameters.AddWithValue("$desc", conceptoNorm);
                totalActualizados += cmdSpDesc.ExecuteNonQuery();
            }

            using (var cmdOcpDesc = conn.CreateCommand())
            {
                cmdOcpDesc.Transaction = tx;
                cmdOcpDesc.CommandText = @"
UPDATE OrdenCompraPartidas
SET ProductoId = $pid
WHERE COALESCE(ProductoId, 0) <= 0
  AND UPPER(TRIM(DescripcionLibre)) = UPPER(TRIM($desc))";
                cmdOcpDesc.Parameters.AddWithValue("$pid", productoIdDestino);
                cmdOcpDesc.Parameters.AddWithValue("$desc", conceptoNorm);
                totalActualizados += cmdOcpDesc.ExecuteNonQuery();
            }
        }

        // Actualizar UltimoCostoCompra (con IVA) del producto si aún está en 0 y hay cotización ganadora o precio en BancoPreciosHistorico
        using (var cmdCosto = conn.CreateCommand())
        {
            cmdCosto.Transaction = tx;
            cmdCosto.CommandText = @"
UPDATE Productos
SET UltimoCostoCompra = COALESCE(
    (SELECT ROUND(PrecioSinIva * 1.16, 2) FROM BancoPreciosHistorico WHERE ProductoId = $pid AND PrecioSinIva > 0 ORDER BY FueGanador DESC, Id DESC LIMIT 1),
    UltimoCostoCompra
)
WHERE Id = $pid AND COALESCE(UltimoCostoCompra, 0) <= 0";
            cmdCosto.Parameters.AddWithValue("$pid", productoIdDestino);
            cmdCosto.ExecuteNonQuery();
        }

        tx.Commit();
        return totalActualizados;
    }

    public static List<OrdenCompra> GenerarOrdenesDesdeComparativa(
        Solicitud solicitud,
        string folioComparativa,
        List<CotizacionRenglonComparativo> renglones,
        bool notificarCorreo = true)
    {
        var articulosActuales = ObtenerSolicitudes().First(s => s.Id == solicitud.Id);
        var idsPermitidos = articulosActuales.Partidas
            .Where(p => !p.TieneOcGenerada)
            .Select(p => p.Id)
            .ToHashSet();
        var idsEmitidos = articulosActuales.Partidas.Where(p => p.TieneOcGenerada).Select(p => p.Id).ToHashSet();
        renglones = renglones.Where(r => idsPermitidos.Contains(r.SolicitudPartidaId) ||
            (r.TieneOcGenerada && idsEmitidos.Contains(r.SolicitudPartidaId))).ToList();
        if (renglones.Count == 0) return new List<OrdenCompra>();

        // Primero guardar el borrador de todas las cotizaciones capturadas (sin disparar correo de Etapa 2 para no duplicar con Etapa 3)
        GuardarBorradorComparativa(solicitud.Id, renglones, folioComparativa, forzarNotificacionCorreo: false);

        var creadas = new List<OrdenCompra>();
        var proveedoresDict = ObtenerProveedores().ToDictionary(p => p.Id);

        // Filtrar solo los productos que tienen proveedor ganador elegido (incluyendo 4+ ProveedoresExtra), precio > 0 y que aún NO tienen OC emitida
        var partidasParaEmitir = renglones
            .Where(r => !r.TieneOcGenerada &&
                        r.ProveedorGanadorIndice >= 1 &&
                        r.ProveedorGanadorId > 0 &&
                        r.PrecioUnitarioGanadorSinIva > 0)
            .GroupBy(r => r.ProveedorGanadorId)
            .ToList();

        if (partidasParaEmitir.Count == 0)
            return creadas;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        foreach (var grupo in partidasParaEmitir)
        {
            int proveedorId = grupo.Key;
            if (!proveedoresDict.TryGetValue(proveedorId, out var prov))
                continue;

            decimal subtotalPartidas = 0m;
            decimal fleteAcumuladoConIva = 0m;
            int diasEntregaMax = 1;
            var partidasOc = new List<(int solicitudPartidaId, int prodId, string descLibre, string unidadCompra, decimal cant, decimal precioSinIva, string notaCot)>();

            foreach (var reng in grupo)
            {
                decimal precioSinIva = reng.PrecioUnitarioGanadorSinIva;
                subtotalPartidas += Math.Round(reng.Cantidad * precioSinIva, 2);
                fleteAcumuladoConIva += reng.FleteGanadorConIva;
                if (reng.DiasEntregaGanador > diasEntregaMax)
                    diasEntregaMax = reng.DiasEntregaGanador;

                string notaOpc = reng.ProveedorGanadorIndice switch
                {
                    1 => reng.NotasCotizacion1,
                    2 => reng.NotasCotizacion2,
                    3 => reng.NotasCotizacion3,
                    _ => reng.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == reng.ProveedorGanadorIndice)?.NotasCotizacion ?? ""
                };
                string descLib = !string.IsNullOrWhiteSpace(reng.DescripcionLibre) ? reng.DescripcionLibre : reng.ProductoNombre;
                partidasOc.Add((reng.SolicitudPartidaId, reng.ProductoId, descLib, reng.UnidadCompra, reng.Cantidad, precioSinIva, notaOpc));
            }

            // Si en la Hoja por Proveedor se asignó un Flete General en la primera fila, pero a este proveedor se le adjudicó otra fila distinta, rescatar ese flete general
            if (fleteAcumuladoConIva <= 0m)
            {
                foreach (var rOtro in renglones.Where(x => !grupo.Contains(x)))
                {
                    decimal fleteRescatado = 0m;
                    if (rOtro.Proveedor1Id == proveedorId && rOtro.FleteConIva1 > 0m && rOtro.ProveedorGanadorId != proveedorId)
                    {
                        fleteRescatado = rOtro.FleteConIva1;
                        rOtro.FleteConIva1 = 0m;
                    }
                    else if (rOtro.Proveedor2Id == proveedorId && rOtro.FleteConIva2 > 0m && rOtro.ProveedorGanadorId != proveedorId)
                    {
                        fleteRescatado = rOtro.FleteConIva2;
                        rOtro.FleteConIva2 = 0m;
                    }
                    else if (rOtro.Proveedor3Id == proveedorId && rOtro.FleteConIva3 > 0m && rOtro.ProveedorGanadorId != proveedorId)
                    {
                        fleteRescatado = rOtro.FleteConIva3;
                        rOtro.FleteConIva3 = 0m;
                    }
                    else
                    {
                        var exOtro = rOtro.ProveedoresExtra.FirstOrDefault(x => x.ProveedorId == proveedorId && x.FleteConIva > 0m);
                        if (exOtro != null && rOtro.ProveedorGanadorId != proveedorId)
                        {
                            fleteRescatado = exOtro.FleteConIva;
                            exOtro.FleteConIva = 0m;
                            GuardarProveedorExtraPartida(exOtro);
                        }
                    }

                    if (fleteRescatado > 0m)
                    {
                        fleteAcumuladoConIva = fleteRescatado;
                        var primGan = grupo.First();
                        if (primGan.ProveedorGanadorIndice == 1) primGan.FleteConIva1 = fleteRescatado;
                        else if (primGan.ProveedorGanadorIndice == 2) primGan.FleteConIva2 = fleteRescatado;
                        else if (primGan.ProveedorGanadorIndice == 3) primGan.FleteConIva3 = fleteRescatado;
                        else
                        {
                            var exGan = primGan.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == primGan.ProveedorGanadorIndice);
                            if (exGan != null)
                            {
                                exGan.FleteConIva = fleteRescatado;
                                GuardarProveedorExtraPartida(exGan);
                            }
                        }
                        GuardarCotizacionPartida(rOtro, folioComparativa);
                        GuardarCotizacionPartida(primGan, folioComparativa);
                        break;
                    }
                }
            }

            decimal fleteSinIva = Math.Round(fleteAcumuladoConIva / 1.16m, 2);
            decimal subtotalTotal = Math.Round(subtotalPartidas + fleteSinIva, 2);
            decimal ivaTotal = Math.Round(subtotalTotal * 0.16m, 2);
            decimal totalConIva = Math.Round(subtotalTotal + ivaTotal, 2);

            string folioOc = GenerarSiguienteFolio("OC", "OrdenesCompra");
            string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string condPagoProv = string.IsNullOrWhiteSpace(prov.CondicionesPago) || prov.CondicionesPago.Equals("Contado", StringComparison.OrdinalIgnoreCase)
                ? "Contado (Transferencia previa)"
                : prov.CondicionesPago;

            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO OrdenesCompra (Folio, Fecha, SolicitudId, CotizacionFolio, ProveedorId, ProyectoId, DepartamentoNombre, EncargadoNombre, TiempoEntregaDias, CostoEnvioConIva, Subtotal, Iva, TotalConIva, Estatus, Notas, CondicionPago, FechaProgramadaPago)
VALUES ($fol, $fec, $sid, $cfol, $pvid, $prid, $dep, $enc, $dias, $flete, $sub, $iva, $tot, 'Pendiente Vo.Bo. Dirección', $not, $cpag, $fprog);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$fol", folioOc);
            cmd.Parameters.AddWithValue("$fec", fecha);
            cmd.Parameters.AddWithValue("$sid", solicitud.Id);
            cmd.Parameters.AddWithValue("$cfol", folioComparativa);
            cmd.Parameters.AddWithValue("$pvid", prov.Id);
            cmd.Parameters.AddWithValue("$prid", solicitud.ProyectoId);
            cmd.Parameters.AddWithValue("$dep", solicitud.DepartamentoNombre);
            cmd.Parameters.AddWithValue("$enc", solicitud.EncargadoNombre);
            cmd.Parameters.AddWithValue("$dias", diasEntregaMax);
            cmd.Parameters.AddWithValue("$flete", (double)fleteAcumuladoConIva);
            cmd.Parameters.AddWithValue("$sub", (double)subtotalTotal);
            cmd.Parameters.AddWithValue("$iva", (double)ivaTotal);
            cmd.Parameters.AddWithValue("$tot", (double)totalConIva);
            cmd.Parameters.AddWithValue("$not", $"Generada desde comparativa por producto {folioComparativa} ({solicitud.Folio})");
            cmd.Parameters.AddWithValue("$cpag", condPagoProv);
            cmd.Parameters.AddWithValue("$fprog", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"));
            int ocId = Convert.ToInt32(cmd.ExecuteScalar());

            foreach (var p in partidasOc)
            {
                using var pCmd = conn.CreateCommand();
                pCmd.Transaction = tx;
                pCmd.CommandText = @"
INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, DescripcionLibre, UnidadLibre, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva)
VALUES ($ocid, $pid, $dlib, $ulib, $cant, 0, $precio)";
                pCmd.Parameters.AddWithValue("$ocid", ocId);
                pCmd.Parameters.AddWithValue("$pid", p.prodId);
                pCmd.Parameters.AddWithValue("$dlib", p.descLibre ?? string.Empty);
                pCmd.Parameters.AddWithValue("$ulib", string.IsNullOrWhiteSpace(p.unidadCompra) ? "Pieza" : p.unidadCompra);
                pCmd.Parameters.AddWithValue("$cant", (double)p.cant);
                pCmd.Parameters.AddWithValue("$precio", (double)p.precioSinIva);
                pCmd.ExecuteNonQuery();

                if (p.solicitudPartidaId > 0)
                {
                    using var upSp = conn.CreateCommand();
                    upSp.Transaction = tx;
                    upSp.CommandText = @"
UPDATE SolicitudPartidas
SET OrdenCompraId = $ocid, OrdenCompraFolio = $ocfol
WHERE Id = $spid AND SolicitudId = $sid AND OrdenCompraId = 0";
                    upSp.Parameters.AddWithValue("$sid", solicitud.Id);
                    upSp.Parameters.AddWithValue("$ocid", ocId);
                    upSp.Parameters.AddWithValue("$ocfol", folioOc);
                    upSp.Parameters.AddWithValue("$spid", p.solicitudPartidaId);
                    if (upSp.ExecuteNonQuery() == 0)
                        throw new InvalidOperationException("Un artículo cambió antes de emitir la orden. Actualice la comparativa e intente nuevamente.");
                }

                if (p.prodId > 0 && p.precioSinIva > 0)
                {
                    decimal costoConIva = Math.Round(p.precioSinIva * 1.16m, 2);
                    using var upPr = conn.CreateCommand();
                    upPr.Transaction = tx;
                    upPr.CommandText = "UPDATE Productos SET UltimoCostoCompra = $costo WHERE Id = $pid AND UPPER(TRIM(COALESCE(UnidadCompra, 'Pieza'))) = UPPER(TRIM($uc))";
                    upPr.Parameters.AddWithValue("$costo", (double)costoConIva);
                    upPr.Parameters.AddWithValue("$pid", p.prodId);
                    upPr.Parameters.AddWithValue("$uc", string.IsNullOrWhiteSpace(p.unidadCompra) ? "Pieza" : p.unidadCompra);
                    upPr.ExecuteNonQuery();
                }
            }

            tx.Commit();

            foreach (var reng in grupo)
            {
                int vigGan = reng.ProveedorGanadorIndice switch
                {
                    1 => reng.DiasVigencia1 > 0 ? reng.DiasVigencia1 : 15,
                    2 => reng.DiasVigencia2 > 0 ? reng.DiasVigencia2 : 15,
                    3 => reng.DiasVigencia3 > 0 ? reng.DiasVigencia3 : 15,
                    _ => reng.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == reng.ProveedorGanadorIndice)?.DiasVigencia ?? 15
                };
                string fecGan = reng.ProveedorGanadorIndice switch
                {
                    1 => reng.FechaCotizacion1,
                    2 => reng.FechaCotizacion2,
                    3 => reng.FechaCotizacion3,
                    _ => reng.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == reng.ProveedorGanadorIndice)?.FechaCotizacion ?? ""
                };
                string notaGan = reng.ProveedorGanadorIndice switch
                {
                    1 => reng.NotasCotizacion1,
                    2 => reng.NotasCotizacion2,
                    3 => reng.NotasCotizacion3,
                    _ => reng.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == reng.ProveedorGanadorIndice)?.NotasCotizacion ?? ""
                };
                string descLib = !string.IsNullOrWhiteSpace(reng.DescripcionLibre) ? reng.DescripcionLibre : reng.ProductoNombre;
                RegistrarPrecioEnBancoHistorico(
                    reng.ProductoId,
                    descLib,
                    reng.Familia,
                    prov.Id,
                    reng.PrecioUnitarioGanadorSinIva,
                    reng.DiasEntregaGanador,
                    vigGan,
                    folioOc,
                    fecGan,
                    notaGan,
                    fueGanador: true,
                    solicitudPartidaId: reng.SolicitudPartidaId);
            }

            creadas.Add(ObtenerOrdenesCompra().First(o => o.Id == ocId));
        }

        // Actualizar estatus de la Solicitud según si todas sus partidas ya tienen OC o si aún quedan pendientes
        using (var chkCmd = conn.CreateCommand())
        {
            chkCmd.CommandText = "SELECT COUNT(*) FROM SolicitudPartidas WHERE SolicitudId = $sid AND COALESCE(OrdenCompraId, 0) = 0";
            chkCmd.Parameters.AddWithValue("$sid", solicitud.Id);
            int pendientesSinOc = Convert.ToInt32(chkCmd.ExecuteScalar());

            using var upSol = conn.CreateCommand();
            upSol.CommandText = "UPDATE Solicitudes SET Estatus = $est WHERE Id = $sid";
            upSol.Parameters.AddWithValue("$est", pendientesSinOc == 0 ? "OC Generada" : "Parcial en OC");
            upSol.Parameters.AddWithValue("$sid", solicitud.Id);
            upSol.ExecuteNonQuery();
        }

        if (notificarCorreo && creadas.Count > 0)
        {
            string foliosOc = string.Join(", ", creadas.Select(o => o.Folio));
            NotificarCambioEstadoCotizacionGerente(solicitud.Id, foliosOc, "3_OC_EMITIDA");
        }

        return creadas;
    }

    public static string CalcularTiempoRelativoEntregaOc(OrdenCompra? oc, string? fechaProgramadaPago = null)
    {
        int dias = (oc?.TiempoEntregaDias ?? 0) > 0 ? oc!.TiempoEntregaDias : 2;
        DateTime baseDate = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(fechaProgramadaPago) && DateTime.TryParse(fechaProgramadaPago, out var fProg) && fProg.Date > DateTime.Today)
            baseDate = fProg.Date;
        DateTime estDate = baseDate.AddDays(dias);
        return dias == 1
            ? $"1 día hábil (entrega est. {estDate:dd/MMM/yyyy})"
            : $"{dias} días hábiles (entrega est. {estDate:dd/MMM/yyyy})";
    }

    public static void ActualizarEstatusYCondicionOrdenCompra(
        int ocId,
        string nuevoEstatus,
        string condicionPago,
        string fechaProgramadaPago,
        string? tiempoAproxEntregaPago = null,
        bool notificarCorreo = true)
    {
        if (ocId <= 0) return;
        var ocPrevia = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ocId);
        string estLimpio = string.IsNullOrWhiteSpace(nuevoEstatus) ? "Emitida" : nuevoEstatus.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE OrdenesCompra
SET Estatus = $est,
    CondicionPago = $cp,
    FechaProgramadaPago = $fp
WHERE Id = $id";
        cmd.Parameters.AddWithValue("$est", estLimpio);
        cmd.Parameters.AddWithValue("$cp", string.IsNullOrWhiteSpace(condicionPago) ? "Contado (Transferencia previa)" : condicionPago.Trim());
        cmd.Parameters.AddWithValue("$fp", (fechaProgramadaPago ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$id", ocId);
        cmd.ExecuteNonQuery();

        if (notificarCorreo && ocPrevia != null)
        {
            if (estLimpio.Contains("Pagada", StringComparison.OrdinalIgnoreCase) &&
                !ocPrevia.Estatus.Contains("Pagada", StringComparison.OrdinalIgnoreCase))
            {
                string tiempoTexto = !string.IsNullOrWhiteSpace(tiempoAproxEntregaPago)
                    ? tiempoAproxEntregaPago.Trim()
                    : CalcularTiempoRelativoEntregaOc(ocPrevia, fechaProgramadaPago);
                NotificarCambioEstadoCotizacionGerente(ocPrevia.SolicitudId, ocPrevia.Folio, "4_PAGADA", tiempoTexto);
            }
            else if (estLimpio.Contains("Pendiente Pago", StringComparison.OrdinalIgnoreCase) &&
                     !ocPrevia.Estatus.Contains("Pendiente Pago", StringComparison.OrdinalIgnoreCase))
            {
                string folioSol = ObtenerSolicitudes().FirstOrDefault(s => s.Id == ocPrevia.SolicitudId)?.Folio ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folioSol) && !YaSeEnvioNotificacionEtapa(folioSol, "3_OC_EMITIDA"))
                {
                    NotificarCambioEstadoCotizacionGerente(ocPrevia.SolicitudId, ocPrevia.Folio, "3_OC_EMITIDA");
                }
            }
        }
    }

    public static List<string> GenerarActivosHerramientaDesdeRecepcion(int productoId, int cantidad, string marcaModelo, string ubicacion = "Gabinete H-1")
    {
        var generados = new List<string>();
        if (productoId <= 0 || cantidad <= 0) return generados;

        var existentes = ObtenerHerramientas();
        int maxNum = 0;
        foreach (var h in existentes)
        {
            if (h.CodigoIndividual.StartsWith("HER-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(h.CodigoIndividual[4..].Trim(), out int n) && n > maxNum)
            {
                maxNum = n;
            }
        }

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        for (int i = 0; i < cantidad; i++)
        {
            maxNum++;
            string codigo = $"HER-{maxNum:D4}";
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO HerramientasActivos (ProductoId, CodigoIndividual, NumeroSerie, MarcaModelo, Estado, Ubicacion, EncargadoActual, ProyectoActual, NotasEstado)
VALUES ($pid, $cod, 'S/N', $mm, 'Disponible', $ub, '', '', 'Alta desde recepción de compra')";
            cmd.Parameters.AddWithValue("$pid", productoId);
            cmd.Parameters.AddWithValue("$cod", codigo);
            cmd.Parameters.AddWithValue("$mm", string.IsNullOrWhiteSpace(marcaModelo) ? "Estándar" : marcaModelo.Trim());
            cmd.Parameters.AddWithValue("$ub", string.IsNullOrWhiteSpace(ubicacion) ? "Gabinete H-1" : ubicacion.Trim());
            cmd.ExecuteNonQuery();
            generados.Add(codigo);
        }

        using (var upP = conn.CreateCommand())
        {
            upP.Transaction = tx;
            upP.CommandText = "UPDATE Productos SET EsHerramienta = 1 WHERE Id = $pid";
            upP.Parameters.AddWithValue("$pid", productoId);
            upP.ExecuteNonQuery();
        }

        tx.Commit();
        return generados;
    }

    public static EntradaAlmacen RegistrarRecepcionOrdenCompra(
        OrdenCompra oc,
        List<(OrdenCompraPartida partida, decimal cantidadRecibirAhora)> recepcion,
        string notas,
        bool reservarParaProyecto = false,
        string? lugarEntregaPersonalizado = null,
        bool notificarCorreo = true)
    {
        // Si alguna partida por recibir era un Concepto Abierto (ProductoId <= 0), reutilizar SKU si ya existe por nombre o darla de alta en el Catálogo (PRD-xxxx)
        foreach (var (partida, cantRecibir) in recepcion.Where(x => x.cantidadRecibirAhora > 0))
        {
            if (partida.ProductoId <= 0)
            {
                string descLibreOrig = partida.DescripcionLibre ?? string.Empty;
                string nombreReal = !string.IsNullOrWhiteSpace(partida.DescripcionLibre) ? partida.DescripcionLibre.Trim() : partida.ProductoNombre.Trim();
                var prodExistente = ObtenerProductos().FirstOrDefault(p => string.Equals(p.Nombre.Trim(), nombreReal, StringComparison.OrdinalIgnoreCase));

                int nuevoProdId;
                string nuevoCodigo;
                string nuevoNombre;

                if (prodExistente != null)
                {
                    nuevoProdId = prodExistente.Id;
                    nuevoCodigo = prodExistente.CodigoInterno;
                    nuevoNombre = prodExistente.Nombre;
                }
                else
                {
                    var info = ClasificarYExtraerEspecificacionesProducto(nombreReal, notas);
                    string fam = ExtraerFamiliaSugerida(nombreReal);
                    var nuevoProd = new Producto
                    {
                        Nombre = nombreReal,
                        Descripcion = $"Creado al recibir Compra {oc.Folio} ({oc.ProveedorNombre})",
                        CategoriaId = info.CategoriaId,
                        Familia = fam,
                        Marca = info.Marca,
                        Modelo = info.Modelo,
                        Medidas = info.Medidas,
                        CapacidadElectrica = info.CapacidadElectrica,
                        Especificacion1 = info.Especificacion1,
                        Especificacion2 = info.Especificacion2,
                        UnidadCompra = string.IsNullOrWhiteSpace(partida.UnidadCompra) ? "Pieza" : partida.UnidadCompra,
                        UnidadAlmacen = string.IsNullOrWhiteSpace(partida.UnidadAlmacen) ? "Pieza" : partida.UnidadAlmacen,
                        FactorConversion = Math.Max(1m, partida.FactorConversion),
                        StockActual = 0m,
                        StockMinimo = 0m,
                        UltimoCostoCompra = partida.PrecioUnitarioConIva,
                        Ubicacion = "Almacén General"
                    };
                    nuevoProdId = GuardarProducto(nuevoProd);
                    nuevoCodigo = nuevoProd.CodigoInterno;
                    nuevoNombre = nuevoProd.Nombre;
                }

                partida.ProductoId = nuevoProdId;
                partida.ProductoCodigo = nuevoCodigo;
                partida.ProductoNombre = nuevoNombre;

                if (!string.IsNullOrWhiteSpace(oc.ProveedorRfc) && partida.PrecioUnitarioSinIva > 0m)
                {
                    GuardarMapaProductoProveedor(oc.ProveedorRfc, nuevoCodigo, nuevoNombre, nuevoProdId, partida.PrecioUnitarioSinIva);
                }

                using var connUp = new SqliteConnection(ConnectionString);
                connUp.Open();
                using var upOcp = connUp.CreateCommand();
                upOcp.CommandText = @"
UPDATE OrdenCompraPartidas SET ProductoId = $pid, DescripcionLibre = $nom WHERE Id = $id;
UPDATE SolicitudPartidas
SET ProductoId = $pid
WHERE OrdenCompraId = $ocid
  AND ProductoId <= 0
  AND (UPPER(TRIM(COALESCE(DescripcionLibre, ''))) = UPPER(TRIM($nom))
       OR UPPER(TRIM(COALESCE(DescripcionLibre, ''))) = UPPER(TRIM($dlibOrig)));";
                upOcp.Parameters.AddWithValue("$pid", nuevoProdId);
                upOcp.Parameters.AddWithValue("$nom", nombreReal);
                upOcp.Parameters.AddWithValue("$dlibOrig", descLibreOrig.Trim());
                upOcp.Parameters.AddWithValue("$id", partida.Id);
                upOcp.Parameters.AddWithValue("$ocid", oc.Id);
                upOcp.ExecuteNonQuery();
            }
        }

        string folioEnt = GenerarSiguienteFolio("ENT", "EntradasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas)
VALUES ($fol, $fec, $ocid, $ocfol, $prov, $proy, 'Recepción OC', '', $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folioEnt);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$ocid", oc.Id);
        cmd.Parameters.AddWithValue("$ocfol", oc.Folio);
        cmd.Parameters.AddWithValue("$prov", oc.ProveedorNombre);
        cmd.Parameters.AddWithValue("$proy", oc.ProyectoNombre);
        cmd.Parameters.AddWithValue("$not", notas.Trim());
        int entId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var (partida, cantRecibir) in recepcion.Where(x => x.cantidadRecibirAhora > 0))
        {
            decimal factor = Math.Max(1m, partida.FactorConversion);
            decimal cantAlmacen = Math.Round(cantRecibir * factor, 2);
            decimal cantReserva = (reservarParaProyecto && oc.ProyectoId > 0) ? cantAlmacen : 0m;

            using var epCmd = conn.CreateCommand();
            epCmd.Transaction = tx;
            epCmd.CommandText = @"
INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva)
VALUES ($eid, $pid, $cc, $uc, $fc, $ca, $ua, $costo)";
            epCmd.Parameters.AddWithValue("$eid", entId);
            epCmd.Parameters.AddWithValue("$pid", partida.ProductoId);
            epCmd.Parameters.AddWithValue("$cc", (double)cantRecibir);
            epCmd.Parameters.AddWithValue("$uc", partida.UnidadCompra);
            epCmd.Parameters.AddWithValue("$fc", (double)factor);
            epCmd.Parameters.AddWithValue("$ca", (double)cantAlmacen);
            epCmd.Parameters.AddWithValue("$ua", partida.UnidadAlmacen);
            epCmd.Parameters.AddWithValue("$costo", (double)partida.PrecioUnitarioConIva);
            epCmd.ExecuteNonQuery();

            using var upOcp = conn.CreateCommand();
            upOcp.Transaction = tx;
            upOcp.CommandText = @"
UPDATE OrdenCompraPartidas
SET CantidadRecibida = CantidadRecibida + $rec
WHERE Id = $id";
            upOcp.Parameters.AddWithValue("$rec", (double)cantRecibir);
            upOcp.Parameters.AddWithValue("$id", partida.Id);
            upOcp.ExecuteNonQuery();

            using var upProd = conn.CreateCommand();
            upProd.Transaction = tx;
            upProd.CommandText = @"
UPDATE Productos
SET StockActual = StockActual + $ca,
    StockReservadoProyecto = StockReservadoProyecto + $res,
    FactorConversion = CASE WHEN UPPER(TRIM(UnidadCompra)) = UPPER(TRIM($uc)) THEN $fc ELSE FactorConversion END,
    UltimoCostoCompra = CASE WHEN UPPER(TRIM(UnidadCompra)) = UPPER(TRIM($uc)) THEN $costo ELSE ROUND($costo / $fc, 2) END
WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$ca", (double)cantAlmacen);
            upProd.Parameters.AddWithValue("$res", (double)cantReserva);
            upProd.Parameters.AddWithValue("$fc", (double)factor);
            upProd.Parameters.AddWithValue("$uc", partida.UnidadCompra ?? "Pieza");
            upProd.Parameters.AddWithValue("$costo", (double)partida.PrecioUnitarioConIva);
            upProd.Parameters.AddWithValue("$pid", partida.ProductoId);
            upProd.ExecuteNonQuery();
        }

        // Verificar si la OC ya quedó 100% recibida o parcial
        using var chkCmd = conn.CreateCommand();
        chkCmd.Transaction = tx;
        chkCmd.CommandText = @"
SELECT COUNT(*) FROM OrdenCompraPartidas
WHERE OrdenCompraId = $ocid AND CantidadRecibida < CantidadPedida - 0.001";
        chkCmd.Parameters.AddWithValue("$ocid", oc.Id);
        int pendientes = Convert.ToInt32(chkCmd.ExecuteScalar());

        string nuevoEstatus = pendientes == 0 ? "Recibida Completa" : "Recibida Parcialmente";
        using var upOc = conn.CreateCommand();
        upOc.Transaction = tx;
        upOc.CommandText = "UPDATE OrdenesCompra SET Estatus = $est WHERE Id = $ocid";
        upOc.Parameters.AddWithValue("$est", nuevoEstatus);
        upOc.Parameters.AddWithValue("$ocid", oc.Id);
        upOc.ExecuteNonQuery();

        using var upEnt = conn.CreateCommand();
        upEnt.Transaction = tx;
        upEnt.CommandText = "UPDATE EntradasAlmacen SET TipoEntrada = $te WHERE Id = $eid";
        upEnt.Parameters.AddWithValue("$te", pendientes == 0 ? "Recepción Completa OC" : "Recepción Parcial OC");
        upEnt.Parameters.AddWithValue("$eid", entId);
        upEnt.ExecuteNonQuery();

        if (pendientes == 0 && oc.SolicitudId > 0)
        {
            using var chkSol = conn.CreateCommand();
            chkSol.Transaction = tx;
            chkSol.CommandText = @"
SELECT COUNT(*) FROM OrdenesCompra
WHERE SolicitudId = $sid AND Estatus NOT IN ('Recibida Completa', 'Cerrada Parcial Definitiva', 'Cerrada Parcial (Admin)', 'Cancelada')";
            chkSol.Parameters.AddWithValue("$sid", oc.SolicitudId);
            int ocsPendSol = Convert.ToInt32(chkSol.ExecuteScalar());
            if (ocsPendSol == 0)
            {
                using var upSol = conn.CreateCommand();
                upSol.Transaction = tx;
                upSol.CommandText = @"
UPDATE Solicitudes SET Estatus = 'Atendida'
WHERE Id = $sid AND (UsaPartidas = 0 OR NOT EXISTS (
    SELECT 1 FROM SolicitudPartidas WHERE SolicitudId = $sid AND OrdenCompraId = 0
));";
                upSol.Parameters.AddWithValue("$sid", oc.SolicitudId);
                upSol.ExecuteNonQuery();
            }
        }

        tx.Commit();
        SincronizarHerramientasCatalogoConDirectorio(conn);
        var entradaResult = ObtenerEntradasAlmacen().First(e => e.Id == entId);

        if (notificarCorreo)
        {
            string destinoBase = !string.IsNullOrWhiteSpace(lugarEntregaPersonalizado)
                ? lugarEntregaPersonalizado.Trim()
                : (reservarParaProyecto && !string.IsNullOrWhiteSpace(oc.ProyectoNombre)
                    ? $"Almacén General — apartado para {oc.ProyectoNombre}"
                    : "Almacén General");
            string destinoAlmacen = destinoBase.Contains("Vale", StringComparison.OrdinalIgnoreCase)
                ? destinoBase
                : $"{destinoBase} (Vale {folioEnt})";
            NotificarCambioEstadoCotizacionGerente(oc.SolicitudId, oc.Folio, "5_CONCLUIDA", null, destinoAlmacen);
        }

        return entradaResult;
    }

    public static List<EntradaAlmacen> ObtenerEntradasAlmacen()
    {
        var list = new List<EntradaAlmacen>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas
FROM EntradasAlmacen
ORDER BY Id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new EntradaAlmacen
            {
                Id = r.GetInt32(0),
                Folio = r.GetString(1),
                Fecha = r.GetString(2),
                OrdenCompraId = r.IsDBNull(3) ? null : r.GetInt32(3),
                OrdenCompraFolio = r.GetString(4),
                ProveedorNombre = r.GetString(5),
                ProyectoNombre = r.GetString(6),
                TipoEntrada = r.GetString(7),
                FacturaUuid = r.GetString(8),
                Notas = r.GetString(9)
            });
        }

        foreach (var ent in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT ep.Id, ep.EntradaAlmacenId, ep.ProductoId, COALESCE(p.CodigoInterno, 'PRD'), COALESCE(p.Nombre, 'Producto'),
       ep.CantidadCompraRecibida, ep.UnidadCompra, ep.FactorConversion,
       ep.CantidadIngresadaAlmacen, ep.UnidadAlmacen, ep.CostoUnitarioCompraConIva
FROM EntradaPartidas ep
LEFT JOIN Productos p ON p.Id = ep.ProductoId
WHERE ep.EntradaAlmacenId = $eid";
            pCmd.Parameters.AddWithValue("$eid", ent.Id);
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                ent.Partidas.Add(new EntradaPartida
                {
                    Id = pr.GetInt32(0),
                    EntradaAlmacenId = pr.GetInt32(1),
                    ProductoId = pr.GetInt32(2),
                    ProductoCodigo = pr.GetString(3),
                    ProductoNombre = pr.GetString(4),
                    CantidadCompraRecibida = Convert.ToDecimal(pr.GetDouble(5)),
                    UnidadCompra = pr.GetString(6),
                    FactorConversion = Convert.ToDecimal(pr.GetDouble(7)),
                    CantidadIngresadaAlmacen = Convert.ToDecimal(pr.GetDouble(8)),
                    UnidadAlmacen = pr.GetString(9),
                    CostoUnitarioCompraConIva = Convert.ToDecimal(pr.GetDouble(10))
                });
            }
        }
        return list;
    }

    // ==================== SALIDAS DE ALMACÉN (CONSUMO) ====================

    public static decimal ConvertirCantidadSolicitudAUnidadAlmacen(SolicitudPartida part, Producto prod)
    {
        decimal cant = part.CantidadSolicitada;
        decimal factor = prod.FactorConversion > 0m ? prod.FactorConversion : 1m;
        if (factor > 1m)
        {
            string uSol = (part.UnidadCompra ?? string.Empty).Trim();
            string uComp = (prod.UnidadCompra ?? string.Empty).Trim();
            string uAlm = (prod.UnidadAlmacen ?? string.Empty).Trim();
            if (!string.Equals(uComp, uAlm, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(uSol) || string.Equals(uSol, uComp, StringComparison.OrdinalIgnoreCase)))
            {
                return Math.Round(cant * factor, 2);
            }
        }
        return cant;
    }

    public static Dictionary<int, decimal> ObtenerCantidadesEntregadasPorFolioEnSalidas(string? folioReferencia)
    {
        var entregadas = new Dictionary<int, decimal>();
        if (string.IsNullOrWhiteSpace(folioReferencia))
            return entregadas;

        string fol = folioReferencia.Trim();
        var salidasVinculadas = ObtenerSalidasAlmacen()
            .Where(s => !string.IsNullOrWhiteSpace(s.Notas) && s.Notas.Contains(fol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var sal in salidasVinculadas)
        {
            foreach (var sp in sal.Partidas)
            {
                decimal neta = Math.Max(0m, sp.CantidadEntregada - sp.CantidadDevuelta);
                if (neta > 0m)
                {
                    entregadas[sp.ProductoId] = entregadas.TryGetValue(sp.ProductoId, out decimal prev) ? prev + neta : neta;
                }
            }
        }
        return entregadas;
    }

    public static SalidaAlmacen RegistrarSalidaAlmacen(
        int departamentoId,
        int encargadoId,
        int proyectoId,
        string notas,
        List<SalidaPartida> partidas,
        string personaRecibe = "")
    {
        string folio = GenerarSiguienteFolio("SAL", "SalidasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        decimal costoTotal = partidas.Sum(p => p.ImporteCosto);
        var enc = ObtenerEncargados().FirstOrDefault(e => e.Id == encargadoId);
        string nombreRecibeFinal = string.IsNullOrWhiteSpace(personaRecibe)
            ? (enc?.Nombre ?? "Personal autorizado")
            : personaRecibe.Trim();

        int proyStockGralId = ObtenerProyectoStockGeneralId();
        bool descontarReservaProyecto = proyectoId > 0 && proyectoId != proyStockGralId;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO SalidasAlmacen (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, CostoTotalEstimado, Notas, PersonaRecibe)
VALUES ($fol, $fec, $dep, $enc, $proy, $costo, $not, $per);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folio);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$dep", departamentoId);
        cmd.Parameters.AddWithValue("$enc", encargadoId);
        cmd.Parameters.AddWithValue("$proy", proyectoId);
        cmd.Parameters.AddWithValue("$costo", (double)costoTotal);
        cmd.Parameters.AddWithValue("$not", notas.Trim());
        cmd.Parameters.AddWithValue("$per", nombreRecibeFinal);
        int salId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var part in partidas)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO SalidaPartidas (SalidaAlmacenId, ProductoId, CantidadEntregada, UnidadAlmacen, CostoUnitarioAlmacen)
VALUES ($sid, $pid, $cant, $ua, $costo)";
            pCmd.Parameters.AddWithValue("$sid", salId);
            pCmd.Parameters.AddWithValue("$pid", part.ProductoId);
            pCmd.Parameters.AddWithValue("$cant", (double)part.CantidadEntregada);
            pCmd.Parameters.AddWithValue("$ua", part.UnidadAlmacen);
            pCmd.Parameters.AddWithValue("$costo", (double)part.CostoUnitarioAlmacen);
            pCmd.ExecuteNonQuery();

            using var upProd = conn.CreateCommand();
            upProd.Transaction = tx;
            upProd.CommandText = descontarReservaProyecto
                ? "UPDATE Productos SET StockActual = MAX(0, StockActual - $cant), StockReservadoProyecto = MAX(0, StockReservadoProyecto - $cant) WHERE Id = $pid"
                : "UPDATE Productos SET StockActual = MAX(0, StockActual - $cant) WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$cant", (double)part.CantidadEntregada);
            upProd.Parameters.AddWithValue("$pid", part.ProductoId);
            upProd.ExecuteNonQuery();
        }

        tx.Commit();
        return ObtenerSalidasAlmacen().First(s => s.Id == salId);
    }

    public static List<SalidaAlmacen> ObtenerSalidasAlmacen()
    {
        var list = new List<SalidaAlmacen>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT s.Id, s.Folio, s.Fecha, s.DepartamentoId, COALESCE(d.Nombre, 'Sin depto'), s.EncargadoId, COALESCE(e.Nombre, 'Sin encargado'),
       s.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Sin proyecto'), s.CostoTotalEstimado, s.Notas, COALESCE(s.PersonaRecibe, '')
FROM SalidasAlmacen s
LEFT JOIN Departamentos d ON d.Id = s.DepartamentoId
LEFT JOIN Encargados e ON e.Id = s.EncargadoId
LEFT JOIN Proyectos pr ON pr.Id = s.ProyectoId
ORDER BY s.Id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string encNom = r.GetString(6);
            string perRec = r.GetString(11);
            list.Add(new SalidaAlmacen
            {
                Id = r.GetInt32(0),
                Folio = r.GetString(1),
                Fecha = r.GetString(2),
                DepartamentoId = r.GetInt32(3),
                DepartamentoNombre = r.GetString(4),
                EncargadoId = r.GetInt32(5),
                EncargadoNombre = !string.IsNullOrWhiteSpace(perRec) ? perRec : encNom,
                PersonaRecibe = !string.IsNullOrWhiteSpace(perRec) ? perRec : encNom,
                ProyectoId = r.GetInt32(7),
                ProyectoNombre = r.GetString(8),
                CostoTotalEstimado = Convert.ToDecimal(r.GetDouble(9)),
                Notas = r.GetString(10)
            });
        }

        foreach (var sal in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT sp.Id, sp.SalidaAlmacenId, sp.ProductoId, COALESCE(p.CodigoInterno, 'PRD'), COALESCE(p.Nombre, 'Producto'),
       sp.CantidadEntregada, sp.UnidadAlmacen, sp.CostoUnitarioAlmacen, COALESCE(sp.CantidadDevuelta, 0)
FROM SalidaPartidas sp
LEFT JOIN Productos p ON p.Id = sp.ProductoId
WHERE sp.SalidaAlmacenId = $sid";
            pCmd.Parameters.AddWithValue("$sid", sal.Id);
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                sal.Partidas.Add(new SalidaPartida
                {
                    Id = pr.GetInt32(0),
                    SalidaAlmacenId = pr.GetInt32(1),
                    ProductoId = pr.GetInt32(2),
                    ProductoCodigo = pr.GetString(3),
                    ProductoNombre = pr.GetString(4),
                    CantidadEntregada = Convert.ToDecimal(pr.GetDouble(5)),
                    UnidadAlmacen = pr.GetString(6),
                    CostoUnitarioAlmacen = Convert.ToDecimal(pr.GetDouble(7)),
                    CantidadDevuelta = Convert.ToDecimal(pr.GetDouble(8)),
                    CantidadDevolverAhora = 0m
                });
            }
        }
        return list;
    }

    public static (SalidaAlmacen SalidaActualizada, EntradaAlmacen EntradaDevolucion) RegistrarDevolucionMaterialSalida(
        int salidaId,
        List<(int SalidaPartidaId, int ProductoId, decimal CantidadDevolver)> devoluciones,
        string observaciones = "")
    {
        var salPrevia = ObtenerSalidasAlmacen().FirstOrDefault(s => s.Id == salidaId)
            ?? throw new InvalidOperationException("No se encontró el vale de salida especificado.");

        var devValidas = devoluciones.Where(d => d.CantidadDevolver > 0).ToList();
        if (devValidas.Count == 0)
            throw new InvalidOperationException("Debes indicar al menos un artículo con cantidad a devolver mayor a cero.");

        string folioEnt = GenerarSiguienteFolio("ENT", "EntradasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        foreach (var dev in devValidas)
        {
            var partOrig = salPrevia.Partidas.FirstOrDefault(p => p.Id == dev.SalidaPartidaId);
            if (partOrig == null) continue;

            decimal maxDevolver = Math.Max(0m, partOrig.CantidadEntregada - partOrig.CantidadDevuelta);
            decimal cantReal = Math.Min(dev.CantidadDevolver, maxDevolver);
            if (cantReal <= 0) continue;

            using var upPart = conn.CreateCommand();
            upPart.Transaction = tx;
            upPart.CommandText = @"
UPDATE SalidaPartidas
SET CantidadDevuelta = COALESCE(CantidadDevuelta, 0) + $cdev
WHERE Id = $spid";
            upPart.Parameters.AddWithValue("$cdev", (double)cantReal);
            upPart.Parameters.AddWithValue("$spid", partOrig.Id);
            upPart.ExecuteNonQuery();

            using var upProd = conn.CreateCommand();
            upProd.Transaction = tx;
            upProd.CommandText = @"
UPDATE Productos
SET StockActual = StockActual + $cdev
WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$cdev", (double)cantReal);
            upProd.Parameters.AddWithValue("$pid", partOrig.ProductoId);
            upProd.ExecuteNonQuery();
        }

        // Recalcular si la devolución de la salida es parcial o completa y su costo neto consumido
        decimal totalEntregado = 0m;
        decimal totalDevuelto = 0m;
        decimal nuevoCostoNeto = 0m;

        using (var qCmd = conn.CreateCommand())
        {
            qCmd.Transaction = tx;
            qCmd.CommandText = "SELECT CantidadEntregada, COALESCE(CantidadDevuelta, 0), CostoUnitarioAlmacen FROM SalidaPartidas WHERE SalidaAlmacenId = $sid";
            qCmd.Parameters.AddWithValue("$sid", salidaId);
            using var qr = qCmd.ExecuteReader();
            while (qr.Read())
            {
                decimal ce = Convert.ToDecimal(qr.GetDouble(0));
                decimal cd = Convert.ToDecimal(qr.GetDouble(1));
                decimal cu = Convert.ToDecimal(qr.GetDouble(2));
                totalEntregado += ce;
                totalDevuelto += cd;
                nuevoCostoNeto += Math.Max(0m, ce - cd) * cu;
            }
        }

        bool esDevolucionCompleta = totalEntregado > 0 && totalDevuelto >= totalEntregado - 0.001m;
        string tipoEntrada = esDevolucionCompleta ? "Devolución Completa Material" : "Devolución Parcial Material";
        string notaDevolucion = string.IsNullOrWhiteSpace(observaciones)
            ? $"Reingreso por {tipoEntrada} del vale {salPrevia.Folio} ({salPrevia.PersonaRecibeEfectiva})"
            : $"Reingreso por {tipoEntrada} del vale {salPrevia.Folio}: {observaciones.Trim()}";

        using (var upSal = conn.CreateCommand())
        {
            upSal.Transaction = tx;
            upSal.CommandText = "UPDATE SalidasAlmacen SET CostoTotalEstimado = $cneto WHERE Id = $sid";
            upSal.Parameters.AddWithValue("$cneto", (double)nuevoCostoNeto);
            upSal.Parameters.AddWithValue("$sid", salidaId);
            upSal.ExecuteNonQuery();
        }

        // Crear registro trazable en EntradasAlmacen
        int entId;
        using (var insEnt = conn.CreateCommand())
        {
            insEnt.Transaction = tx;
            insEnt.CommandText = @"
INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas)
VALUES ($fol, $fec, NULL, $ocf, $prov, $proy, $tipo, '', $not);
SELECT last_insert_rowid();";
            insEnt.Parameters.AddWithValue("$fol", folioEnt);
            insEnt.Parameters.AddWithValue("$fec", fecha);
            insEnt.Parameters.AddWithValue("$ocf", salPrevia.Folio);
            insEnt.Parameters.AddWithValue("$prov", $"Devolución: {salPrevia.PersonaRecibeEfectiva} ({salPrevia.DepartamentoNombre})");
            insEnt.Parameters.AddWithValue("$proy", salPrevia.ProyectoNombre);
            insEnt.Parameters.AddWithValue("$tipo", tipoEntrada);
            insEnt.Parameters.AddWithValue("$not", notaDevolucion);
            entId = Convert.ToInt32(insEnt.ExecuteScalar());
        }

        foreach (var dev in devValidas)
        {
            var partOrig = salPrevia.Partidas.FirstOrDefault(p => p.Id == dev.SalidaPartidaId);
            if (partOrig == null) continue;
            decimal maxDevolver = Math.Max(0m, partOrig.CantidadEntregada - partOrig.CantidadDevuelta);
            decimal cantReal = Math.Min(dev.CantidadDevolver, maxDevolver);
            if (cantReal <= 0) continue;

            using var insEp = conn.CreateCommand();
            insEp.Transaction = tx;
            insEp.CommandText = @"
INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva)
VALUES ($eid, $pid, $cant, $ua, 1, $cant, $ua, $costo)";
            insEp.Parameters.AddWithValue("$eid", entId);
            insEp.Parameters.AddWithValue("$pid", partOrig.ProductoId);
            insEp.Parameters.AddWithValue("$cant", (double)cantReal);
            insEp.Parameters.AddWithValue("$ua", partOrig.UnidadAlmacen);
            insEp.Parameters.AddWithValue("$costo", (double)partOrig.CostoUnitarioAlmacen);
            insEp.ExecuteNonQuery();
        }

        tx.Commit();

        var salActualizada = ObtenerSalidasAlmacen().First(s => s.Id == salidaId);
        var entCreada = ObtenerEntradasAlmacen().First(e => e.Id == entId);
        return (salActualizada, entCreada);
    }

    // ==================== PRÉSTAMO Y DEVOLUCIÓN DE HERRAMIENTAS ====================

    public static PrestamoHerramienta RegistrarPrestamoHerramienta(
        int departamentoId,
        int encargadoId,
        int proyectoId,
        string notas,
        List<HerramientaActivo> herramientas,
        string personaRecibe = "",
        string fechaCompromiso = "")
    {
        string folio = GenerarSiguienteFolio("PRE", "PrestamosHerramienta");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var dep = ObtenerDepartamentos().FirstOrDefault(d => d.Id == departamentoId);
        var enc = ObtenerEncargados().FirstOrDefault(e => e.Id == encargadoId);
        var proy = ObtenerProyectos().FirstOrDefault(p => p.Id == proyectoId);

        string nombreRecibeFinal = string.IsNullOrWhiteSpace(personaRecibe)
            ? (enc?.Nombre ?? "Personal autorizado")
            : personaRecibe.Trim();

        string compromisoFinal = string.IsNullOrWhiteSpace(fechaCompromiso)
            ? DateTime.Now.AddDays(3).ToString("yyyy-MM-dd")
            : fechaCompromiso.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO PrestamosHerramienta (Folio, FechaSalida, FechaDevolucion, DepartamentoId, EncargadoId, ProyectoId, Estatus, Notas, PersonaRecibe, FechaCompromiso)
VALUES ($fol, $fec, '', $dep, $enc, $proy, 'Activo', $not, $per, $fcomp);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folio);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$dep", departamentoId);
        cmd.Parameters.AddWithValue("$enc", encargadoId);
        cmd.Parameters.AddWithValue("$proy", proyectoId);
        cmd.Parameters.AddWithValue("$not", notas.Trim());
        cmd.Parameters.AddWithValue("$per", nombreRecibeFinal);
        cmd.Parameters.AddWithValue("$fcomp", compromisoFinal);
        int preId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var h in herramientas)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO PrestamoPartidas (PrestamoHerramientaId, HerramientaActivoId, EstadoSalida, EstadoDevolucion, FechaDevuelto, Observaciones)
VALUES ($pid, $hid, 'Bueno', 'Pendiente', '', $obs)";
            pCmd.Parameters.AddWithValue("$pid", preId);
            pCmd.Parameters.AddWithValue("$hid", h.Id);
            pCmd.Parameters.AddWithValue("$obs", h.NotasEstado ?? string.Empty);
            pCmd.ExecuteNonQuery();

            using var upHer = conn.CreateCommand();
            upHer.Transaction = tx;
            upHer.CommandText = @"
UPDATE HerramientasActivos
SET Estado = 'En Préstamo',
    EncargadoActual = $encNom,
    ProyectoActual = $proyNom
WHERE Id = $hid";
            upHer.Parameters.AddWithValue("$encNom", dep != null ? $"{nombreRecibeFinal} ({dep.Codigo})" : nombreRecibeFinal);
            upHer.Parameters.AddWithValue("$proyNom", proy?.Codigo ?? "");
            upHer.Parameters.AddWithValue("$hid", h.Id);
            upHer.ExecuteNonQuery();
        }

        tx.Commit();
        return ObtenerPrestamosHerramienta().First(p => p.Id == preId);
    }

    public static void DevolverHerramientaPartida(int prestamoPartidaId, int herramientaId, int prestamoId, string estadoRetorno, string observaciones)
    {
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string nuevoEstadoHerramienta = estadoRetorno switch
        {
            "Bueno" or "Bueno (Completo)" => "Disponible",
            "Incompleto / Falta Pieza" => "Disponible (Incompleta)",
            "Dañado" => "Dañada",
            "Extraviado" => "Extraviada / Baja",
            _ => "Disponible"
        };

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE PrestamoPartidas
SET EstadoDevolucion = $est, FechaDevuelto = $fec, Observaciones = $obs
WHERE Id = $id";
        cmd.Parameters.AddWithValue("$est", estadoRetorno);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$obs", observaciones.Trim());
        cmd.Parameters.AddWithValue("$id", prestamoPartidaId);
        cmd.ExecuteNonQuery();

        using var upHer = conn.CreateCommand();
        upHer.Transaction = tx;
        upHer.CommandText = @"
UPDATE HerramientasActivos
SET Estado = $estH, EncargadoActual = '', ProyectoActual = '', NotasEstado = $obs
WHERE Id = $hid";
        upHer.Parameters.AddWithValue("$estH", nuevoEstadoHerramienta);
        upHer.Parameters.AddWithValue("$obs", observaciones.Trim());
        upHer.Parameters.AddWithValue("$hid", herramientaId);
        upHer.ExecuteNonQuery();

        // Revisar si todas las herramientas del préstamo ya fueron devueltas o si es devolución parcial
        using var chk = conn.CreateCommand();
        chk.Transaction = tx;
        chk.CommandText = "SELECT COUNT(*) FROM PrestamoPartidas WHERE PrestamoHerramientaId = $pid AND EstadoDevolucion = 'Pendiente'";
        chk.Parameters.AddWithValue("$pid", prestamoId);
        int pend = Convert.ToInt32(chk.ExecuteScalar());
        if (pend == 0)
        {
            using var chkInc = conn.CreateCommand();
            chkInc.Transaction = tx;
            chkInc.CommandText = "SELECT COUNT(*) FROM PrestamoPartidas WHERE PrestamoHerramientaId = $pid AND EstadoDevolucion IN ('Incompleto / Falta Pieza', 'Dañado', 'Extraviado')";
            chkInc.Parameters.AddWithValue("$pid", prestamoId);
            int incidencias = Convert.ToInt32(chkInc.ExecuteScalar());
            string estatusFinal = incidencias > 0 ? "Devuelto con Incidencia" : "Devuelto Completo";

            using var upPre = conn.CreateCommand();
            upPre.Transaction = tx;
            upPre.CommandText = "UPDATE PrestamosHerramienta SET Estatus = $estF, FechaDevolucion = $fec WHERE Id = $pid";
            upPre.Parameters.AddWithValue("$estF", estatusFinal);
            upPre.Parameters.AddWithValue("$fec", fecha);
            upPre.Parameters.AddWithValue("$pid", prestamoId);
            upPre.ExecuteNonQuery();
        }
        else
        {
            using var upPreParcial = conn.CreateCommand();
            upPreParcial.Transaction = tx;
            upPreParcial.CommandText = "UPDATE PrestamosHerramienta SET Estatus = 'Devuelto Parcial', FechaDevolucion = $fec WHERE Id = $pid";
            upPreParcial.Parameters.AddWithValue("$fec", fecha);
            upPreParcial.Parameters.AddWithValue("$pid", prestamoId);
            upPreParcial.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static List<PrestamoHerramienta> ObtenerPrestamosHerramienta()
    {
        var list = new List<PrestamoHerramienta>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT p.Id, p.Folio, p.FechaSalida, p.FechaDevolucion, p.DepartamentoId, COALESCE(d.Nombre, 'Sin depto'),
       p.EncargadoId, COALESCE(e.Nombre, 'Sin encargado'), p.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Sin proyecto'), p.Estatus, p.Notas,
       COALESCE(p.PersonaRecibe, ''), COALESCE(p.FechaCompromiso, '')
FROM PrestamosHerramienta p
LEFT JOIN Departamentos d ON d.Id = p.DepartamentoId
LEFT JOIN Encargados e ON e.Id = p.EncargadoId
LEFT JOIN Proyectos pr ON pr.Id = p.ProyectoId
ORDER BY p.Id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PrestamoHerramienta
            {
                Id = r.GetInt32(0),
                Folio = r.GetString(1),
                FechaSalida = r.GetString(2),
                FechaDevolucion = r.GetString(3),
                DepartamentoId = r.GetInt32(4),
                DepartamentoNombre = r.GetString(5),
                EncargadoId = r.GetInt32(6),
                EncargadoNombre = r.GetString(7),
                ProyectoId = r.GetInt32(8),
                ProyectoNombre = r.GetString(9),
                Estatus = r.GetString(10),
                Notas = r.GetString(11),
                PersonaRecibe = r.GetString(12),
                FechaCompromiso = r.GetString(13)
            });
        }

        foreach (var pre in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT pp.Id, pp.PrestamoHerramientaId, pp.HerramientaActivoId, COALESCE(h.CodigoIndividual, 'HER'),
       COALESCE(pr.Nombre, 'Herramienta'), COALESCE(h.MarcaModelo, ''), COALESCE(h.NumeroSerie, ''), pp.EstadoSalida, pp.EstadoDevolucion, pp.FechaDevuelto, pp.Observaciones
FROM PrestamoPartidas pp
LEFT JOIN HerramientasActivos h ON h.Id = pp.HerramientaActivoId
LEFT JOIN Productos pr ON pr.Id = h.ProductoId
WHERE pp.PrestamoHerramientaId = $pid";
            pCmd.Parameters.AddWithValue("$pid", pre.Id);
            using var r2 = pCmd.ExecuteReader();
            while (r2.Read())
            {
                pre.Partidas.Add(new PrestamoPartida
                {
                    Id = r2.GetInt32(0),
                    PrestamoHerramientaId = r2.GetInt32(1),
                    HerramientaActivoId = r2.GetInt32(2),
                    CodigoIndividual = r2.GetString(3),
                    ProductoNombre = r2.GetString(4),
                    MarcaModelo = r2.GetString(5),
                    NumeroSerie = r2.GetString(6),
                    EstadoSalida = r2.GetString(7),
                    EstadoDevolucion = r2.GetString(8),
                    FechaDevuelto = r2.GetString(9),
                    Observaciones = r2.GetString(10)
                });
            }
        }
        return list;
    }

    // ==================== FACTURAS XML (CFDI 4.0) ====================

    public static int ObtenerProyectoStockGeneralId()
    {
        var proyectos = ObtenerProyectos();
        var stock = proyectos.FirstOrDefault(p => p.EsStockGeneral) ?? proyectos.FirstOrDefault();
        if (stock != null)
            return stock.Id;

        GuardarProyecto("STOCK-ALM", "Reposición de Stock General de Almacén", "Almacén Interno", "Activo");
        return ObtenerProyectos().First().Id;
    }

    public static int AsegurarProveedorPorRfc(string emisorRfc, string emisorNombre)
    {
        string rfcNorm = (emisorRfc ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(rfcNorm))
            rfcNorm = "XAXX010101000";

        string razonLimpia = string.IsNullOrWhiteSpace(emisorNombre) ? rfcNorm : emisorNombre.Trim();
        string nomCorto = Proveedor.ExtraerNombreComercialCorto(razonLimpia);
        var todosProvs = ObtenerProveedores();

        if (!string.Equals(rfcNorm, "XAXX010101000", StringComparison.OrdinalIgnoreCase))
        {
            var provPorRfc = todosProvs.FirstOrDefault(p => p.Rfc.Trim().Equals(rfcNorm, StringComparison.OrdinalIgnoreCase));
            if (provPorRfc != null)
                return provPorRfc.Id;
        }

        // Buscar si ya fue dado de alta en Comparativa con RFC genérico o por nombre comercial / razón social equivalente
        var provPorNombre = todosProvs.FirstOrDefault(p =>
            string.Equals(p.RazonSocial.Trim(), razonLimpia, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.NombreComercial.Trim(), razonLimpia, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(nomCorto) && nomCorto.Length >= 4 &&
             (string.Equals(p.NombreComercial.Trim(), nomCorto, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(Proveedor.ExtraerNombreComercialCorto(p.RazonSocial), nomCorto, StringComparison.OrdinalIgnoreCase))));

        if (provPorNombre != null)
        {
            if (!string.Equals(rfcNorm, "XAXX010101000", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(provPorNombre.Rfc.Trim(), "XAXX010101000", StringComparison.OrdinalIgnoreCase))
            {
                provPorNombre.Rfc = rfcNorm;
                if (string.IsNullOrWhiteSpace(provPorNombre.RazonSocial) || string.Equals(provPorNombre.RazonSocial, provPorNombre.NombreComercial, StringComparison.OrdinalIgnoreCase))
                    provPorNombre.RazonSocial = razonLimpia;
                GuardarProveedor(provPorNombre);
            }
            return provPorNombre.Id;
        }

        int nuevoId = GuardarProveedor(new Proveedor
        {
            Rfc = rfcNorm,
            RazonSocial = razonLimpia,
            NombreComercial = nomCorto,
            Notas = "Registrado automáticamente desde Factura XML CFDI"
        });

        return nuevoId > 0
            ? nuevoId
            : ObtenerProveedores().Last(p => p.Rfc.Trim().Equals(rfcNorm, StringComparison.OrdinalIgnoreCase)).Id;
    }

    public static int? BuscarProductoVinculadoXml(string emisorRfc, string noIdentificacion, string descripcion)
    {
        string rfc = (emisorRfc ?? string.Empty).Trim().ToUpperInvariant();
        string clave = (noIdentificacion ?? string.Empty).Trim();
        string desc = (descripcion ?? string.Empty).Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.ProductoId
FROM ProductoProveedorMapa m
INNER JOIN Productos p ON p.Id = m.ProductoId
WHERE m.EmisorRfc = $rfc
  AND ((LENGTH(m.ClaveProveedor) >= 4 AND m.ClaveProveedor = $clave)
       OR (m.DescripcionProveedor <> '' AND m.DescripcionProveedor = $desc))
LIMIT 1";
        cmd.Parameters.AddWithValue("$rfc", rfc);
        cmd.Parameters.AddWithValue("$clave", clave);
        cmd.Parameters.AddWithValue("$desc", desc);
        var res = cmd.ExecuteScalar();
        return res == null || res == DBNull.Value ? null : Convert.ToInt32(res);
    }

    public static Producto? BuscarProductoExistenteParaConceptoXml(string emisorRfc, string noIdentificacion, string descripcion)
    {
        var todosProductos = ObtenerProductos();
        string noId = (noIdentificacion ?? string.Empty).Trim();
        string desc = (descripcion ?? string.Empty).Trim();

        int? prodIdMapa = BuscarProductoVinculadoXml(emisorRfc, noId, desc);
        if (prodIdMapa.HasValue)
        {
            var porMapa = todosProductos.FirstOrDefault(p => p.Id == prodIdMapa.Value);
            if (porMapa != null)
                return porMapa;
        }

        // Solo usar NoIdentificacion como código único si tiene al menos 4 caracteres (evitar números de partida como "1" o "2")
        if (noId.Length >= 4)
        {
            var porCodigo = todosProductos.FirstOrDefault(p =>
                p.CodigoInterno.Equals(noId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(p.CodigoBarras) && p.CodigoBarras.Length >= 4 && p.CodigoBarras.Equals(noId, StringComparison.OrdinalIgnoreCase)));
            if (porCodigo != null)
                return porCodigo;
        }

        if (!string.IsNullOrWhiteSpace(desc))
        {
            var porNombreExacto = todosProductos.FirstOrDefault(p =>
                p.Nombre.Equals(desc, StringComparison.OrdinalIgnoreCase));
            if (porNombreExacto != null)
                return porNombreExacto;

            // Solo permitir coincidencia parcial por subcadena cuando ambos nombres son suficientemente descriptivos (>= 14 caracteres)
            // y no tienen códigos de barras de proveedor distintos entre sí.
            if (desc.Length >= 14)
            {
                var porCoincidencia = todosProductos.FirstOrDefault(p =>
                    p.Nombre.Length >= 14 &&
                    (desc.Contains(p.Nombre, StringComparison.OrdinalIgnoreCase) ||
                     p.Nombre.Contains(desc, StringComparison.OrdinalIgnoreCase)) &&
                    (noId.Length < 4 ||
                     string.IsNullOrWhiteSpace(p.CodigoBarras) ||
                     p.CodigoBarras.Equals(p.CodigoInterno, StringComparison.OrdinalIgnoreCase) ||
                     p.CodigoBarras.Equals(noId, StringComparison.OrdinalIgnoreCase)));
                if (porCoincidencia != null)
                    return porCoincidencia;
            }
        }

        return null;
    }

    public static Producto CrearProductoDesdeConceptoXml(string emisorRfc, string serieFolio, FacturaConcepto c)
    {
        var categorias = ObtenerCategorias();
        int categoriaId;
        if (categorias.Count == 0)
        {
            GuardarCategoria("General");
            categorias = ObtenerCategorias();
        }
        categoriaId = categorias.First().Id;

        string nuevoCodigo = GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
        string noId = (c.NoIdentificacion ?? string.Empty).Trim();
        var existentes = ObtenerProductos();
        bool codigoBarrasDisponible = noId.Length >= 4 &&
            !existentes.Any(p => p.CodigoInterno.Equals(noId, StringComparison.OrdinalIgnoreCase) ||
                                 p.CodigoBarras.Equals(noId, StringComparison.OrdinalIgnoreCase));

        string codigoBarras = codigoBarrasDisponible ? noId : nuevoCodigo;
        string nombre = string.IsNullOrWhiteSpace(c.Descripcion)
            ? $"Producto Factura {nuevoCodigo}"
            : c.Descripcion.Trim();
        string unidad = NormalizarUnidadMedida(c.Unidad);
        decimal costoUnitarioConIva = c.Cantidad > 0m && (c.Importe + c.IvaConcepto) > 0m
            ? Math.Round((c.Importe + c.IvaConcepto) / c.Cantidad, 2)
            : Math.Round(c.ValorUnitario * 1.16m, 2);

        var info = ClasificarYExtraerEspecificacionesProducto(nombre, c.ClaveProdServ);
        if (categorias.Any(x => x.Id == info.CategoriaId))
            categoriaId = info.CategoriaId;

        var nuevoProd = new Producto
        {
            CodigoInterno = nuevoCodigo,
            CodigoBarras = codigoBarras,
            Nombre = nombre,
            Descripcion = $"Clave SAT: {c.ClaveProdServ} | ID Prov: {noId} | Creado desde Factura {serieFolio}",
            CategoriaId = categoriaId,
            Marca = info.Marca,
            Modelo = !string.IsNullOrWhiteSpace(info.Modelo) ? info.Modelo : (noId.Length >= 4 ? noId : string.Empty),
            Medidas = info.Medidas,
            CapacidadElectrica = info.CapacidadElectrica,
            Especificacion1 = info.Especificacion1,
            Especificacion2 = info.Especificacion2,
            UnidadCompra = unidad,
            UnidadAlmacen = unidad,
            FactorConversion = 1m,
            StockActual = 0m,
            StockMinimo = 0m,
            UltimoCostoCompra = costoUnitarioConIva,
            Ubicacion = "Almacén General",
            EsHerramienta = false
        };

        int nuevoId = GuardarProducto(nuevoProd);
        nuevoProd.Id = nuevoId;

        GuardarMapaProductoProveedor(emisorRfc, noId, nombre, nuevoId, c.ValorUnitario);

        c.ProductoIdVinculado = nuevoId;
        c.ProductoVinculadoNombre = $"✔ Creado: [{nuevoProd.CodigoInterno}] {nuevoProd.Nombre}";
        return nuevoProd;
    }

    public static void GuardarMapaProductoProveedor(string emisorRfc, string clave, string descripcion, int productoId, decimal ultimoPrecioSinIva)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM ProductoProveedorMapa WHERE EmisorRfc = $rfc AND (ClaveProveedor = $c AND DescripcionProveedor = $d)";
        del.Parameters.AddWithValue("$rfc", (emisorRfc ?? string.Empty).Trim().ToUpperInvariant());
        del.Parameters.AddWithValue("$c", (clave ?? string.Empty).Trim());
        del.Parameters.AddWithValue("$d", (descripcion ?? string.Empty).Trim());
        del.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ProductoProveedorMapa (EmisorRfc, ClaveProveedor, DescripcionProveedor, ProductoId, UltimoPrecioSinIva)
VALUES ($rfc, $c, $d, $pid, $p)";
        cmd.Parameters.AddWithValue("$rfc", (emisorRfc ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$c", (clave ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$d", (descripcion ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$pid", productoId);
        cmd.Parameters.AddWithValue("$p", (double)ultimoPrecioSinIva);
        cmd.ExecuteNonQuery();
    }

    private static decimal CalcularPrecioUnitarioSinIvaParaOc(FacturaConcepto c)
    {
        decimal cant = c.Cantidad > 0m ? c.Cantidad : 1m;
        decimal totalConceptoConIva = c.Importe + c.IvaConcepto;
        if (totalConceptoConIva > 0m)
        {
            //OrdenCompraPartida calcula ImporteConIva = Round(Round(Cantidad * PrecioSinIva, 2) * 1.16, 2)
            return Math.Round((totalConceptoConIva / 1.16m) / cant, 4);
        }
        return c.ValorUnitario;
    }

    public static void SincronizarOrdenCompraConSusFacturas(int ordenCompraId, int? proyectoIdPreferido = null)
    {
        if (ordenCompraId <= 0) return;

        var todasFacturasOc = ObtenerFacturasXml().Where(f => f.OrdenCompraId == ordenCompraId).OrderBy(f => f.Id).ToList();
        if (todasFacturasOc.Count == 0) return;

        // Si ya existe al menos un CFDI XML real en esta OC y también había una nota sintética NOTA-..., eliminar la nota sintética para que el XML fiscal la reemplace limpiamente
        var xmlsReales = todasFacturasOc.Where(f => !f.EsNotaSinFactura).ToList();
        var notasSinteticas = todasFacturasOc.Where(f => f.EsNotaSinFactura).ToList();
        if (xmlsReales.Count > 0 && notasSinteticas.Count > 0)
        {
            using var connClean = new SqliteConnection(ConnectionString);
            connClean.Open();
            foreach (var nota in notasSinteticas)
            {
                using var delC = connClean.CreateCommand();
                delC.CommandText = "DELETE FROM FacturaConceptos WHERE FacturaXmlId = $id; DELETE FROM FacturasXml WHERE Id = $id;";
                delC.Parameters.AddWithValue("$id", nota.Id);
                delC.ExecuteNonQuery();
            }
            todasFacturasOc = xmlsReales;
        }

        var facturasDeEstaOc = todasFacturasOc;
        var primeraFac = facturasDeEstaOc.First();
        int provId = AsegurarProveedorPorRfc(primeraFac.EmisorRfc, primeraFac.EmisorNombre);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // Leer las partidas actuales de la OC para NO borrar partidas pendientes cuando se sube una factura parcial
        var partidasActualesOc = new List<(int id, int prodId, string descLibre, string unidadLibre, decimal cantPedida, decimal cantRecibida, decimal precioSinIva)>();
        using (var qPart = conn.CreateCommand())
        {
            qPart.CommandText = @"
SELECT Id, ProductoId, COALESCE(DescripcionLibre, ''), COALESCE(UnidadLibre, 'Pieza'), CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva
FROM OrdenCompraPartidas
WHERE OrdenCompraId = $ocid
ORDER BY Id ASC";
            qPart.Parameters.AddWithValue("$ocid", ordenCompraId);
            using var r = qPart.ExecuteReader();
            while (r.Read())
            {
                partidasActualesOc.Add((
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.GetString(2),
                    r.GetString(3),
                    Convert.ToDecimal(r.GetDouble(4)),
                    Convert.ToDecimal(r.GetDouble(5)),
                    Convert.ToDecimal(r.GetDouble(6))));
            }
        }

        decimal costoEnvioOc = 0m;
        using (var qEnv = conn.CreateCommand())
        {
            qEnv.CommandText = "SELECT COALESCE(CostoEnvioConIva, 0) FROM OrdenesCompra WHERE Id = $ocid";
            qEnv.Parameters.AddWithValue("$ocid", ordenCompraId);
            var envObj = qEnv.ExecuteScalar();
            if (envObj != null && envObj != DBNull.Value)
                costoEnvioOc = Convert.ToDecimal(envObj);
        }

        using var tx = conn.BeginTransaction();

        // Agrupar conceptos facturados por ProductoId para actualizar o agregar sin destruir partidas no facturadas aún
        var conceptosPorProd = facturasDeEstaOc
            .SelectMany(f => f.Conceptos)
            .Where(x => x.ProductoIdVinculado.HasValue && x.ProductoIdVinculado.Value > 0)
            .GroupBy(x => x.ProductoIdVinculado!.Value)
            .ToList();

        var idsPartidasTocadas = new HashSet<int>();

        foreach (var grp in conceptosPorProd)
        {
            int prodId = grp.Key;
            decimal cantFacturadaTotal = grp.Sum(c => c.Cantidad > 0m ? c.Cantidad : 1m);
            var ultimoConcepto = grp.Last();
            decimal precioSinIva = CalcularPrecioUnitarioSinIvaParaOc(ultimoConcepto);
            decimal costoConIva = Math.Round(precioSinIva * 1.16m, 2);

            // Buscar si ya existe en la OC por ProductoId o como concepto abierto con la misma descripción
            var match = partidasActualesOc.FirstOrDefault(p => p.prodId == prodId && !idsPartidasTocadas.Contains(p.id));
            if (match.id == 0)
            {
                string descConc = (ultimoConcepto.Descripcion ?? string.Empty).Trim();
                match = partidasActualesOc.FirstOrDefault(p =>
                    p.prodId <= 0 &&
                    !idsPartidasTocadas.Contains(p.id) &&
                    !string.IsNullOrWhiteSpace(p.descLibre) &&
                    (string.Equals(p.descLibre.Trim(), descConc, StringComparison.OrdinalIgnoreCase) ||
                     descConc.Contains(p.descLibre.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     p.descLibre.Trim().Contains(descConc, StringComparison.OrdinalIgnoreCase)));
            }

            if (match.id > 0)
            {
                idsPartidasTocadas.Add(match.id);
                decimal nuevaCantPedida = Math.Max(match.cantPedida, cantFacturadaTotal);

                using var upPart = conn.CreateCommand();
                upPart.Transaction = tx;
                upPart.CommandText = @"
UPDATE OrdenCompraPartidas
SET ProductoId = $pid,
    CantidadPedida = $cant,
    PrecioUnitarioSinIva = $precio
WHERE Id = $id";
                upPart.Parameters.AddWithValue("$pid", prodId);
                upPart.Parameters.AddWithValue("$cant", (double)nuevaCantPedida);
                upPart.Parameters.AddWithValue("$precio", (double)precioSinIva);
                upPart.Parameters.AddWithValue("$id", match.id);
                upPart.ExecuteNonQuery();

                if (match.prodId <= 0 && !string.IsNullOrWhiteSpace(match.descLibre))
                {
                    using var upSolP = conn.CreateCommand();
                    upSolP.Transaction = tx;
                    upSolP.CommandText = @"
UPDATE SolicitudPartidas
SET ProductoId = $pid
WHERE OrdenCompraId = $ocid
  AND ProductoId <= 0
  AND UPPER(TRIM(COALESCE(DescripcionLibre, ''))) = UPPER(TRIM($dlib))";
                    upSolP.Parameters.AddWithValue("$pid", prodId);
                    upSolP.Parameters.AddWithValue("$ocid", ordenCompraId);
                    upSolP.Parameters.AddWithValue("$dlib", match.descLibre.Trim());
                    upSolP.ExecuteNonQuery();
                }
            }
            else
            {
                using var insPart = conn.CreateCommand();
                insPart.Transaction = tx;
                insPart.CommandText = @"
INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, DescripcionLibre, UnidadLibre, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva)
VALUES ($ocid, $pid, $dlib, $ulib, $cant, 0, $precio)";
                insPart.Parameters.AddWithValue("$ocid", ordenCompraId);
                insPart.Parameters.AddWithValue("$pid", prodId);
                insPart.Parameters.AddWithValue("$dlib", (ultimoConcepto.Descripcion ?? string.Empty).Trim());
                insPart.Parameters.AddWithValue("$ulib", NormalizarUnidadMedida(ultimoConcepto.Unidad));
                insPart.Parameters.AddWithValue("$cant", (double)cantFacturadaTotal);
                insPart.Parameters.AddWithValue("$precio", (double)precioSinIva);
                insPart.ExecuteNonQuery();
            }

            // Actualizar costo real en Entradas de Almacén previas de esta OC (cuando se recibió primero con remisión)
            using var upEp = conn.CreateCommand();
            upEp.Transaction = tx;
            upEp.CommandText = @"
UPDATE EntradaPartidas
SET CostoUnitarioCompraConIva = $costo
WHERE ProductoId = $pid AND EntradaAlmacenId IN (SELECT Id FROM EntradasAlmacen WHERE OrdenCompraId = $ocid)";
            upEp.Parameters.AddWithValue("$costo", (double)costoConIva);
            upEp.Parameters.AddWithValue("$pid", prodId);
            upEp.Parameters.AddWithValue("$ocid", ordenCompraId);
            upEp.ExecuteNonQuery();
        }

        // Recalcular totales de la OC tomando en cuenta todas sus partidas (facturadas + pendientes por facturar)
        decimal subtotalPartidasOc = 0m;
        decimal totalPedidasOc = 0m;
        decimal totalRecibidasOc = 0m;
        int partidasPendientesRecibir = 0;

        using (var qTot = conn.CreateCommand())
        {
            qTot.Transaction = tx;
            qTot.CommandText = "SELECT CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva FROM OrdenCompraPartidas WHERE OrdenCompraId = $ocid";
            qTot.Parameters.AddWithValue("$ocid", ordenCompraId);
            using var r = qTot.ExecuteReader();
            while (r.Read())
            {
                decimal cp = Convert.ToDecimal(r.GetDouble(0));
                decimal cr = Convert.ToDecimal(r.GetDouble(1));
                decimal pu = Convert.ToDecimal(r.GetDouble(2));
                subtotalPartidasOc += Math.Round(cp * pu, 2);
                totalPedidasOc += cp;
                totalRecibidasOc += cr;
                if (cr < cp - 0.001m)
                    partidasPendientesRecibir++;
            }
        }

        decimal fleteSinIva = Math.Round(costoEnvioOc / 1.16m, 2);
        decimal subtotalCalculado = Math.Round(subtotalPartidasOc + fleteSinIva, 2);
        decimal ivaCalculado = Math.Round(subtotalCalculado * 0.16m, 2);
        decimal totalCalculado = Math.Round(subtotalCalculado + ivaCalculado, 2);

        decimal subtotalFacturas = facturasDeEstaOc.Sum(f => Math.Max(0m, f.Subtotal - f.Descuento));
        decimal ivaFacturas = facturasDeEstaOc.Sum(f => f.Iva);
        decimal totalFacturas = facturasDeEstaOc.Sum(f => f.Total);

        // Si las facturas cubren el total o más, usar el importe fiscal exacto; si es factura parcial, conservar el total de toda la OC
        bool usarTotalesFactura = totalFacturas >= totalCalculado - 1.0m || partidasActualesOc.Count <= idsPartidasTocadas.Count;
        decimal subtotalFinal = usarTotalesFactura ? subtotalFacturas : subtotalCalculado;
        decimal ivaFinal = usarTotalesFactura ? ivaFacturas : ivaCalculado;
        decimal totalFinal = usarTotalesFactura ? totalFacturas : totalCalculado;

        string foliosFacturas = string.Join(", ", facturasDeEstaOc.Select(f => string.IsNullOrWhiteSpace(f.SerieFolio) ? f.Uuid[..Math.Min(8, f.Uuid.Length)] : f.SerieFolio));

        using (var upOc = conn.CreateCommand())
        {
            upOc.Transaction = tx;
            if (proyectoIdPreferido.HasValue && proyectoIdPreferido.Value > 0)
            {
                upOc.CommandText = @"
UPDATE OrdenesCompra
SET ProveedorId = $pvid,
    ProyectoId = $prid,
    Subtotal = $sub,
    Iva = $iva,
    TotalConIva = $tot,
    Notas = $not
WHERE Id = $ocid";
                upOc.Parameters.AddWithValue("$prid", proyectoIdPreferido.Value);
            }
            else
            {
                upOc.CommandText = @"
UPDATE OrdenesCompra
SET ProveedorId = $pvid,
    Subtotal = $sub,
    Iva = $iva,
    TotalConIva = $tot,
    Notas = $not
WHERE Id = $ocid";
            }
            upOc.Parameters.AddWithValue("$pvid", provId);
            upOc.Parameters.AddWithValue("$sub", (double)subtotalFinal);
            upOc.Parameters.AddWithValue("$iva", (double)ivaFinal);
            upOc.Parameters.AddWithValue("$tot", (double)totalFinal);
            upOc.Parameters.AddWithValue("$not", $"Asociada a Factura(s) XML: {foliosFacturas}");
            upOc.Parameters.AddWithValue("$ocid", ordenCompraId);
            upOc.ExecuteNonQuery();
        }

        // Actualizar UUID fiscal en Entradas de Almacén de esta OC y recalcular Estatus de la OC según recepción física
        using (var upEntUuid = conn.CreateCommand())
        {
            upEntUuid.Transaction = tx;
            upEntUuid.CommandText = "UPDATE EntradasAlmacen SET FacturaUuid = $uuid WHERE OrdenCompraId = $ocid AND TRIM(FacturaUuid) = ''";
            upEntUuid.Parameters.AddWithValue("$uuid", primeraFac.Uuid);
            upEntUuid.Parameters.AddWithValue("$ocid", ordenCompraId);
            upEntUuid.ExecuteNonQuery();
        }

        if (totalRecibidasOc > 0m)
        {
            string estRec = partidasPendientesRecibir == 0 ? "Recibida Completa" : "Recibida Parcialmente";
            using var upEst = conn.CreateCommand();
            upEst.Transaction = tx;
            upEst.CommandText = "UPDATE OrdenesCompra SET Estatus = $est WHERE Id = $ocid";
            upEst.Parameters.AddWithValue("$est", estRec);
            upEst.Parameters.AddWithValue("$ocid", ordenCompraId);
            upEst.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static (int ocId, string ocFolio) CrearOrdenCompraDesdeFactura(FacturaXml factura, int proveedorId, int proyectoId)
    {
        string folioOc = GenerarSiguienteFolio("OC", "OrdenesCompra");
        string fecha = string.IsNullOrWhiteSpace(factura.FechaTimbrado)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            : factura.FechaTimbrado;

        var prov = ObtenerProveedores().FirstOrDefault(p => p.Id == proveedorId);
        int dias = prov?.DiasEntregaPromedio ?? 3;
        decimal subtotal = Math.Max(0m, factura.Subtotal - factura.Descuento);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO OrdenesCompra (Folio, Fecha, SolicitudId, CotizacionFolio, ProveedorId, ProyectoId, DepartamentoNombre, EncargadoNombre, TiempoEntregaDias, CostoEnvioConIva, Subtotal, Iva, TotalConIva, Estatus, Notas)
VALUES ($fol, $fec, 0, '', $pvid, $prid, 'Compras / Almacén', 'Facturación XML', $dias, 0, $sub, $iva, $tot, 'Emitida', $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folioOc);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$pvid", proveedorId);
        cmd.Parameters.AddWithValue("$prid", proyectoId);
        cmd.Parameters.AddWithValue("$dias", dias);
        cmd.Parameters.AddWithValue("$sub", (double)subtotal);
        cmd.Parameters.AddWithValue("$iva", (double)factura.Iva);
        cmd.Parameters.AddWithValue("$tot", (double)factura.Total);
        cmd.Parameters.AddWithValue("$not", $"Generada desde Factura XML {factura.SerieFolio} ({factura.Uuid})");
        int ocId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var c in factura.Conceptos.Where(x => x.ProductoIdVinculado.HasValue && x.ProductoIdVinculado.Value > 0))
        {
            decimal cantPedida = c.Cantidad > 0m ? c.Cantidad : 1m;
            decimal precioSinIva = CalcularPrecioUnitarioSinIvaParaOc(c);

            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva)
VALUES ($ocid, $pid, $cant, 0, $precio)";
            pCmd.Parameters.AddWithValue("$ocid", ocId);
            pCmd.Parameters.AddWithValue("$pid", c.ProductoIdVinculado!.Value);
            pCmd.Parameters.AddWithValue("$cant", (double)cantPedida);
            pCmd.Parameters.AddWithValue("$precio", (double)precioSinIva);
            pCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return (ocId, folioOc);
    }

    public static void RegularizarEntradaSinOcConOrdenCompra(int entradaId, int ordenCompraId, string? facturaUuid = null)
    {
        if (entradaId <= 0 || ordenCompraId <= 0) return;

        var entrada = ObtenerEntradasAlmacen().FirstOrDefault(e => e.Id == entradaId);
        var oc = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ordenCompraId);
        if (entrada == null || oc == null) return;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // 1. Vincular la Entrada SIN-OC a la Orden de Compra sin volver a incrementar StockActual
        using (var upEnt = conn.CreateCommand())
        {
            upEnt.Transaction = tx;
            upEnt.CommandText = @"
UPDATE EntradasAlmacen
SET OrdenCompraId = $ocid,
    OrdenCompraFolio = $ocfol,
    TipoEntrada = 'Entrada Regularizada con OC',
    FacturaUuid = CASE WHEN $uuid <> '' THEN $uuid ELSE FacturaUuid END
WHERE Id = $eid";
            upEnt.Parameters.AddWithValue("$ocid", oc.Id);
            upEnt.Parameters.AddWithValue("$ocfol", oc.Folio);
            upEnt.Parameters.AddWithValue("$uuid", (facturaUuid ?? string.Empty).Trim());
            upEnt.Parameters.AddWithValue("$eid", entrada.Id);
            upEnt.ExecuteNonQuery();
        }

        // 2. Acreditar las cantidades recibidas de esa entrada en OrdenCompraPartidas y actualizar costos reales en EntradaPartidas
        foreach (var ep in entrada.Partidas)
        {
            var partOc = oc.Partidas.FirstOrDefault(p => p.ProductoId == ep.ProductoId);
            if (partOc != null)
            {
                decimal nuevaRecibida = Math.Min(partOc.CantidadPedida, partOc.CantidadRecibida + ep.CantidadCompraRecibida);
                using (var upOcp = conn.CreateCommand())
                {
                    upOcp.Transaction = tx;
                    upOcp.CommandText = "UPDATE OrdenCompraPartidas SET CantidadRecibida = $rec WHERE Id = $id";
                    upOcp.Parameters.AddWithValue("$rec", (double)nuevaRecibida);
                    upOcp.Parameters.AddWithValue("$id", partOc.Id);
                    upOcp.ExecuteNonQuery();
                }

                if (partOc.PrecioUnitarioConIva > 0m)
                {
                    using var upEp = conn.CreateCommand();
                    upEp.Transaction = tx;
                    upEp.CommandText = "UPDATE EntradaPartidas SET CostoUnitarioCompraConIva = $costo WHERE Id = $epid";
                    upEp.Parameters.AddWithValue("$costo", (double)partOc.PrecioUnitarioConIva);
                    upEp.Parameters.AddWithValue("$epid", ep.Id);
                    upEp.ExecuteNonQuery();
                }
            }
        }

        // 3. Recalcular el estatus de recepción de la OC
        using (var chkCmd = conn.CreateCommand())
        {
            chkCmd.Transaction = tx;
            chkCmd.CommandText = @"
SELECT COUNT(*) FROM OrdenCompraPartidas
WHERE OrdenCompraId = $ocid AND CantidadRecibida < CantidadPedida - 0.001";
            chkCmd.Parameters.AddWithValue("$ocid", oc.Id);
            int pendientes = Convert.ToInt32(chkCmd.ExecuteScalar());

            using var upOc = conn.CreateCommand();
            upOc.Transaction = tx;
            upOc.CommandText = "UPDATE OrdenesCompra SET Estatus = $est WHERE Id = $ocid";
            upOc.Parameters.AddWithValue("$est", pendientes == 0 ? "Recibida Completa" : "Recibida Parcialmente");
            upOc.Parameters.AddWithValue("$ocid", oc.Id);
            upOc.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static FacturaXml GuardarFacturaXml(
        FacturaXml factura,
        bool ingresarAlmacenAutomaticamente = false,
        int? entradaSinOcIdParaRegularizar = null,
        bool entregadoDirectoEnObra = false,
        string personaRecibioObra = "",
        int vehiculoFlotillaId = 0,
        string tipoGastoVehiculo = "",
        int kilometrajeVehiculo = 0)
    {
        // 1. Asegurar que el proveedor exista por RFC
        int proveedorId = AsegurarProveedorPorRfc(factura.EmisorRfc, factura.EmisorNombre);
        int proyectoId = factura.ProyectoId.HasValue && factura.ProyectoId.Value > 0
            ? factura.ProyectoId.Value
            : ObtenerProyectoStockGeneralId();

        // 2. Filtrar conceptos vacíos de Addenda y comprobar cada concepto de la factura
        factura.Conceptos = factura.Conceptos
            .Where(c => !string.IsNullOrWhiteSpace(c.Descripcion) || !string.IsNullOrWhiteSpace(c.ClaveProdServ) || c.Importe > 0m || c.ValorUnitario > 0m)
            .ToList();

        var catalogoActual = ObtenerProductos().ToDictionary(p => p.Id);
        foreach (var c in factura.Conceptos)
        {
            if (c.ProductoIdVinculado.HasValue && catalogoActual.ContainsKey(c.ProductoIdVinculado.Value))
            {
                GuardarMapaProductoProveedor(factura.EmisorRfc, c.NoIdentificacion, c.Descripcion, c.ProductoIdVinculado.Value, c.ValorUnitario);
            }
            else
            {
                var existente = BuscarProductoExistenteParaConceptoXml(factura.EmisorRfc, c.NoIdentificacion, c.Descripcion);
                if (existente != null)
                {
                    c.ProductoIdVinculado = existente.Id;
                    c.ProductoVinculadoNombre = $"✔ Registrado: [{existente.CodigoInterno}] {existente.Nombre}";
                    GuardarMapaProductoProveedor(factura.EmisorRfc, c.NoIdentificacion, c.Descripcion, existente.Id, c.ValorUnitario);
                }
                else
                {
                    var nuevo = CrearProductoDesdeConceptoXml(factura.EmisorRfc, factura.SerieFolio, c);
                    catalogoActual[nuevo.Id] = nuevo;
                }
            }
        }

        // 3. Verificar si tiene una Orden de Compra existente válida del mismo proveedor; de lo contrario, crear una nueva OC automáticamente
        var ocsActuales = ObtenerOrdenesCompra();
        OrdenCompra? ocExistenteValida = null;
        if (factura.OrdenCompraId.HasValue && factura.OrdenCompraId.Value > 0)
        {
            ocExistenteValida = ocsActuales.FirstOrDefault(o => o.Id == factura.OrdenCompraId.Value && o.ProveedorId == proveedorId);
        }

        bool seCreoNuevaOc = false;
        if (ocExistenteValida == null)
        {
            var (nuevaOcId, nuevoFolioOc) = CrearOrdenCompraDesdeFactura(factura, proveedorId, proyectoId);
            factura.OrdenCompraId = nuevaOcId;
            factura.OrdenCompraFolio = nuevoFolioOc;
            seCreoNuevaOc = true;
        }
        else
        {
            factura.OrdenCompraId = ocExistenteValida.Id;
            factura.OrdenCompraFolio = ocExistenteValida.Folio;
        }

        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO FacturasXml (Uuid, SerieFolio, FechaTimbrado, EmisorRfc, EmisorNombre, Moneda, TipoCambio, Subtotal, Descuento, Iva, Total, OrdenCompraId, OrdenCompraFolio, RutaArchivoXml)
VALUES ($uuid, $sf, $fec, $rfc, $nom, $mon, $tc, $sub, $desc, $iva, $tot, $ocid, $ocfol, $ruta);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$uuid", factura.Uuid.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("$sf", factura.SerieFolio);
            cmd.Parameters.AddWithValue("$fec", factura.FechaTimbrado);
            cmd.Parameters.AddWithValue("$rfc", factura.EmisorRfc.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("$nom", factura.EmisorNombre.Trim());
            cmd.Parameters.AddWithValue("$mon", factura.Moneda);
            cmd.Parameters.AddWithValue("$tc", (double)factura.TipoCambio);
            cmd.Parameters.AddWithValue("$sub", (double)factura.Subtotal);
            cmd.Parameters.AddWithValue("$desc", (double)factura.Descuento);
            cmd.Parameters.AddWithValue("$iva", (double)factura.Iva);
            cmd.Parameters.AddWithValue("$tot", (double)factura.Total);
            cmd.Parameters.AddWithValue("$ocid", factura.OrdenCompraId.HasValue ? factura.OrdenCompraId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$ocfol", factura.OrdenCompraFolio ?? string.Empty);
            cmd.Parameters.AddWithValue("$ruta", factura.RutaArchivoXml ?? string.Empty);
            int facId = Convert.ToInt32(cmd.ExecuteScalar());

            foreach (var c in factura.Conceptos)
            {
                using var cCmd = conn.CreateCommand();
                cCmd.Transaction = tx;
                cCmd.CommandText = @"
INSERT INTO FacturaConceptos (FacturaXmlId, ClaveProdServ, NoIdentificacion, Descripcion, Unidad, Cantidad, ValorUnitario, Importe, IvaConcepto, ProductoIdVinculado)
VALUES ($fid, $cps, $nid, $desc, $uni, $cant, $vu, $imp, $iva, $pid)";
                cCmd.Parameters.AddWithValue("$fid", facId);
                cCmd.Parameters.AddWithValue("$cps", c.ClaveProdServ);
                cCmd.Parameters.AddWithValue("$nid", c.NoIdentificacion);
                cCmd.Parameters.AddWithValue("$desc", c.Descripcion);
                cCmd.Parameters.AddWithValue("$uni", c.Unidad);
                cCmd.Parameters.AddWithValue("$cant", (double)c.Cantidad);
                cCmd.Parameters.AddWithValue("$vu", (double)c.ValorUnitario);
                cCmd.Parameters.AddWithValue("$imp", (double)c.Importe);
                cCmd.Parameters.AddWithValue("$iva", (double)c.IvaConcepto);
                cCmd.Parameters.AddWithValue("$pid", c.ProductoIdVinculado.HasValue ? c.ProductoIdVinculado.Value : DBNull.Value);
                cCmd.ExecuteNonQuery();

                if (c.ProductoIdVinculado.HasValue && c.ProductoIdVinculado.Value > 0)
                {
                    decimal costoConIva = c.Cantidad > 0m && (c.Importe + c.IvaConcepto) > 0m
                        ? Math.Round((c.Importe + c.IvaConcepto) / c.Cantidad, 2)
                        : Math.Round(c.ValorUnitario * 1.16m, 2);
                    using var upP = conn.CreateCommand();
                    upP.Transaction = tx;
                    upP.CommandText = "UPDATE Productos SET UltimoCostoCompra = $costo WHERE Id = $pid";
                    upP.Parameters.AddWithValue("$costo", (double)costoConIva);
                    upP.Parameters.AddWithValue("$pid", c.ProductoIdVinculado.Value);
                    upP.ExecuteNonQuery();
                }
            }

            tx.Commit();
            factura.Id = facId;
        }

        if (!seCreoNuevaOc && factura.OrdenCompraId.HasValue)
        {
            SincronizarOrdenCompraConSusFacturas(factura.OrdenCompraId.Value, proyectoId);
        }

        // 4. Si existe una Entrada sin OC ('SIN-OC') indicada o que coincida con el folio/UUID de esta factura, regularizarla sin duplicar StockActual
        if (factura.OrdenCompraId.HasValue && factura.OrdenCompraId.Value > 0)
        {
            int? idSinOc = entradaSinOcIdParaRegularizar;
            if (!idSinOc.HasValue || idSinOc.Value <= 0)
            {
                string sf = (factura.SerieFolio ?? string.Empty).Trim();
                string uuid = (factura.Uuid ?? string.Empty).Trim();
                var coincidenciaSinOc = ObtenerEntradasAlmacen().FirstOrDefault(e =>
                    string.Equals(e.OrdenCompraFolio, "SIN-OC", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(e.FacturaUuid) &&
                    ((!string.IsNullOrWhiteSpace(sf) && string.Equals(e.FacturaUuid.Trim(), sf, StringComparison.OrdinalIgnoreCase)) ||
                     (!string.IsNullOrWhiteSpace(uuid) && string.Equals(e.FacturaUuid.Trim(), uuid, StringComparison.OrdinalIgnoreCase))));
                if (coincidenciaSinOc != null)
                    idSinOc = coincidenciaSinOc.Id;
            }

            if (idSinOc.HasValue && idSinOc.Value > 0)
            {
                RegularizarEntradaSinOcConOrdenCompra(idSinOc.Value, factura.OrdenCompraId.Value, factura.Uuid);
            }
        }

        if (entregadoDirectoEnObra && factura.OrdenCompraId.HasValue && factura.OrdenCompraId.Value > 0)
        {
            var ocActual = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == factura.OrdenCompraId.Value);
            if (ocActual != null)
            {
                var pendientes = ocActual.Partidas
                    .Where(p => p.CantidadPendiente > 0m)
                    .Select(p => (p, p.CantidadPendiente))
                    .ToList();
                if (pendientes.Count > 0)
                {
                    RegistrarRecepcionDirectaAObra(
                        ocActual,
                        pendientes,
                        string.IsNullOrWhiteSpace(personaRecibioObra) ? "Personal de Obra (Compra Mostrador)" : personaRecibioObra,
                        factura.SerieFolio ?? string.Empty,
                        $"Compra urgente de mostrador consumida en obra — Factura XML {factura.SerieFolio} ({factura.Uuid})");
                }
            }
        }
        else if (ingresarAlmacenAutomaticamente && factura.OrdenCompraId.HasValue && factura.OrdenCompraId.Value > 0)
        {
            var ocActual = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == factura.OrdenCompraId.Value);
            if (ocActual != null)
            {
                var pendientes = ocActual.Partidas
                    .Where(p => p.CantidadPendiente > 0m)
                    .Select(p => (p, p.CantidadPendiente))
                    .ToList();
                if (pendientes.Count > 0)
                {
                    RegistrarRecepcionOrdenCompra(
                        ocActual,
                        pendientes,
                        $"Ingreso automático al registrar Factura XML {factura.SerieFolio} ({factura.Uuid})");
                }
            }
        }

        if (vehiculoFlotillaId > 0)
        {
            string proyNom = ObtenerProyectos().FirstOrDefault(p => p.Id == proyectoId)?.Nombre ?? "Operación Flotilla";
            string resumenConceptos = string.Join(", ", factura.Conceptos.Take(3).Select(c => c.Descripcion));
            RegistrarGastoVehiculo(new RegistroGastoVehiculo
            {
                VehiculoId = vehiculoFlotillaId,
                Fecha = string.IsNullOrWhiteSpace(factura.FechaTimbrado) ? DateTime.Now.ToString("yyyy-MM-dd") : (factura.FechaTimbrado.Length >= 10 ? factura.FechaTimbrado[..10] : factura.FechaTimbrado),
                TipoGasto = string.IsNullOrWhiteSpace(tipoGastoVehiculo) ? "Mantenimiento / Taller / Combustible" : tipoGastoVehiculo,
                KilometrajeRegistro = kilometrajeVehiculo,
                MontoConIva = factura.Total,
                ProveedorTaller = factura.EmisorNombre ?? string.Empty,
                FolioReferencia = !string.IsNullOrWhiteSpace(factura.SerieFolio) ? $"{factura.SerieFolio} ({factura.OrdenCompraFolio ?? string.Empty})" : (factura.OrdenCompraFolio ?? string.Empty),
                ProyectoNombre = proyNom,
                Descripcion = $"Factura XML {factura.SerieFolio}: {resumenConceptos}"
            });
        }

        RegistrarBitacoraUsuario(
            "Facturas XML",
            entregadoDirectoEnObra ? "Factura XML (Directo a Obra)" : "Registro Factura XML",
            factura.OrdenCompraFolio,
            $"Factura {factura.SerieFolio} ({factura.EmisorNombre}) Total: {factura.Total:C2}");

        return ObtenerFacturasXml().First(f => f.Id == factura.Id);
    }

    public static void SincronizarFacturasYOrdenesExistentes()
    {
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();

            // 1. Limpiar conceptos vacíos generados por nodos Addenda ($0.00 y sin descripción)
            using (var delEmpty = conn.CreateCommand())
            {
                delEmpty.CommandText = "DELETE FROM FacturaConceptos WHERE TRIM(Descripcion) = '' AND Importe <= 0 AND ValorUnitario <= 0";
                delEmpty.ExecuteNonQuery();
            }

            // 2. Limpiar productos temporales $0.00 sin nombre real que hayan quedado huérfanos
            using (var delOrphanProd = conn.CreateCommand())
            {
                delOrphanProd.CommandText = @"
DELETE FROM Productos
WHERE Nombre LIKE 'Producto Factura PRD-%'
  AND UltimoCostoCompra <= 0
  AND Id NOT IN (SELECT COALESCE(ProductoIdVinculado, 0) FROM FacturaConceptos)";
                delOrphanProd.ExecuteNonQuery();
            }

            // 3. Evitar que números de línea cortos ('1', '2') se usen como Código de Barras global
            using (var fixBarcodes = conn.CreateCommand())
            {
                fixBarcodes.CommandText = "UPDATE Productos SET CodigoBarras = CodigoInterno WHERE LENGTH(TRIM(CodigoBarras)) < 4";
                fixBarcodes.ExecuteNonQuery();
            }

            // 4. Limpiar Órdenes de Compra demo huérfanas cuyo proveedor fue eliminado del directorio
            using (var delOrphanOcs = conn.CreateCommand())
            {
                delOrphanOcs.CommandText = @"
DELETE FROM OrdenCompraPartidas WHERE OrdenCompraId IN (
    SELECT Id FROM OrdenesCompra WHERE ProveedorId NOT IN (SELECT Id FROM Proveedores)
);
UPDATE FacturasXml SET OrdenCompraId = NULL, OrdenCompraFolio = '' WHERE OrdenCompraId IN (
    SELECT Id FROM OrdenesCompra WHERE ProveedorId NOT IN (SELECT Id FROM Proveedores)
);
DELETE FROM OrdenesCompra WHERE ProveedorId NOT IN (SELECT Id FROM Proveedores);";
                delOrphanOcs.ExecuteNonQuery();
            }

            // 5. Si algún concepto apuntaba a un producto ya eliminado del catálogo, marcarlo como -1 para no recrearlo
            using (var markDeletedProd = conn.CreateCommand())
            {
                markDeletedProd.CommandText = @"
UPDATE FacturaConceptos
SET ProductoIdVinculado = -1
WHERE ProductoIdVinculado IS NOT NULL
  AND ProductoIdVinculado > 0
  AND ProductoIdVinculado NOT IN (SELECT Id FROM Productos)";
                markDeletedProd.ExecuteNonQuery();
            }
        }

        int defaultProyectoId = ObtenerProyectoStockGeneralId();
        var proyectosIds = ObtenerProyectos().Select(p => p.Id).ToHashSet();
        var facturas = ObtenerFacturasXml().OrderBy(f => f.Id).ToList();
        var catalogo = ObtenerProductos().ToDictionary(p => p.Id);
        var ocsDict = ObtenerOrdenesCompra().ToDictionary(o => o.Id);

        foreach (var fac in facturas)
        {
            int provId = AsegurarProveedorPorRfc(fac.EmisorRfc, fac.EmisorNombre);

            foreach (var c in fac.Conceptos)
            {
                // Si el usuario eliminó este producto del catálogo (-1), respetarlo y no volver a crearlo
                if (c.ProductoIdVinculado == -1)
                    continue;

                bool requiereRevinculacion = !c.ProductoIdVinculado.HasValue;

                // Si fue vinculado por un NoIdentificacion corto (ej. "1") a un producto con nombre totalmente distinto, corregir
                if (!requiereRevinculacion && c.NoIdentificacion.Trim().Length < 4 && catalogo.TryGetValue(c.ProductoIdVinculado!.Value, out var prodActual))
                {
                    string desc = c.Descripcion.Trim();
                    if (!string.IsNullOrWhiteSpace(desc) &&
                        !prodActual.Nombre.Equals(desc, StringComparison.OrdinalIgnoreCase) &&
                        !desc.Contains(prodActual.Nombre, StringComparison.OrdinalIgnoreCase) &&
                        !prodActual.Nombre.Contains(desc, StringComparison.OrdinalIgnoreCase))
                    {
                        requiereRevinculacion = true;
                        using var connDelMap = new SqliteConnection(ConnectionString);
                        connDelMap.Open();
                        using var delMap = connDelMap.CreateCommand();
                        delMap.CommandText = "DELETE FROM ProductoProveedorMapa WHERE EmisorRfc = $rfc AND DescripcionProveedor = $desc";
                        delMap.Parameters.AddWithValue("$rfc", fac.EmisorRfc.Trim().ToUpperInvariant());
                        delMap.Parameters.AddWithValue("$desc", desc);
                        delMap.ExecuteNonQuery();
                    }
                }

                if (requiereRevinculacion)
                {
                    var existente = BuscarProductoExistenteParaConceptoXml(fac.EmisorRfc, c.NoIdentificacion, c.Descripcion);
                    if (existente != null)
                    {
                        c.ProductoIdVinculado = existente.Id;
                        GuardarMapaProductoProveedor(fac.EmisorRfc, c.NoIdentificacion, c.Descripcion, existente.Id, c.ValorUnitario);
                    }
                    else
                    {
                        var nuevo = CrearProductoDesdeConceptoXml(fac.EmisorRfc, fac.SerieFolio, c);
                        catalogo[nuevo.Id] = nuevo;
                        c.ProductoIdVinculado = nuevo.Id;
                    }

                    using var connUp = new SqliteConnection(ConnectionString);
                    connUp.Open();
                    using var upC = connUp.CreateCommand();
                    upC.CommandText = "UPDATE FacturaConceptos SET ProductoIdVinculado = $pid WHERE Id = $cid";
                    upC.Parameters.AddWithValue("$pid", c.ProductoIdVinculado.Value);
                    upC.Parameters.AddWithValue("$cid", c.Id);
                    upC.ExecuteNonQuery();
                }
            }

            // Verificar si la factura tiene una Orden de Compra válida de su mismo proveedor
            OrdenCompra? ocActual = null;
            if (fac.OrdenCompraId.HasValue && ocsDict.TryGetValue(fac.OrdenCompraId.Value, out var ocCand) && ocCand.ProveedorId == provId)
            {
                ocActual = ocCand;
            }

            if (ocActual == null)
            {
                var (nuevaOcId, nuevoFolio) = CrearOrdenCompraDesdeFactura(fac, provId, defaultProyectoId);
                using var connOc = new SqliteConnection(ConnectionString);
                connOc.Open();
                using var upFac = connOc.CreateCommand();
                upFac.CommandText = "UPDATE FacturasXml SET OrdenCompraId = $ocid, OrdenCompraFolio = $fol WHERE Id = $fid";
                upFac.Parameters.AddWithValue("$ocid", nuevaOcId);
                upFac.Parameters.AddWithValue("$fol", nuevoFolio);
                upFac.Parameters.AddWithValue("$fid", fac.Id);
                upFac.ExecuteNonQuery();
            }
        }
    }

    public static void ActualizarConceptoFacturaRegistrada(int facturaId, int conceptoId, int nuevoProductoId)
    {
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE FacturaConceptos SET ProductoIdVinculado = $pid WHERE Id = $cid AND FacturaXmlId = $fid";
            cmd.Parameters.AddWithValue("$pid", nuevoProductoId);
            cmd.Parameters.AddWithValue("$cid", conceptoId);
            cmd.Parameters.AddWithValue("$fid", facturaId);
            cmd.ExecuteNonQuery();
        }

        var fac = ObtenerFacturasXml().FirstOrDefault(f => f.Id == facturaId);
        if (fac != null)
        {
            var concepto = fac.Conceptos.FirstOrDefault(c => c.Id == conceptoId);
            if (concepto != null)
            {
                GuardarMapaProductoProveedor(fac.EmisorRfc, concepto.NoIdentificacion, concepto.Descripcion, nuevoProductoId, concepto.ValorUnitario);
                decimal costoConIva = concepto.Cantidad > 0m && (concepto.Importe + concepto.IvaConcepto) > 0m
                    ? Math.Round((concepto.Importe + concepto.IvaConcepto) / concepto.Cantidad, 2)
                    : Math.Round(concepto.ValorUnitario * 1.16m, 2);

                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var upP = conn.CreateCommand();
                upP.CommandText = "UPDATE Productos SET UltimoCostoCompra = $costo WHERE Id = $pid";
                upP.Parameters.AddWithValue("$costo", (double)costoConIva);
                upP.Parameters.AddWithValue("$pid", nuevoProductoId);
                upP.ExecuteNonQuery();
            }

            if (fac.OrdenCompraId.HasValue && fac.OrdenCompraId.Value > 0)
            {
                SincronizarOrdenCompraConSusFacturas(fac.OrdenCompraId.Value);
            }
        }
    }

    public static void ActualizarProyectoYOrdenFacturaRegistrada(int facturaId, int proyectoId, int? ordenCompraId)
    {
        var fac = ObtenerFacturasXml().FirstOrDefault(f => f.Id == facturaId);
        if (fac == null) return;

        int provId = AsegurarProveedorPorRfc(fac.EmisorRfc, fac.EmisorNombre);
        int? ocAnteriorId = fac.OrdenCompraId;

        if (!ordenCompraId.HasValue || ordenCompraId.Value <= 0)
        {
            // Si ya tenía una OC propia para esta sola factura, solo actualizar su proyecto y partidas
            var facturasMismaOc = ocAnteriorId.HasValue
                ? ObtenerFacturasXml().Where(f => f.OrdenCompraId == ocAnteriorId.Value).ToList()
                : new List<FacturaXml>();

            if (ocAnteriorId.HasValue && facturasMismaOc.Count == 1)
            {
                SincronizarOrdenCompraConSusFacturas(ocAnteriorId.Value, proyectoId);
                return;
            }

            var (nuevaOcId, nuevoFolio) = CrearOrdenCompraDesdeFactura(fac, provId, proyectoId);
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var up = conn.CreateCommand();
            up.CommandText = "UPDATE FacturasXml SET OrdenCompraId = $ocid, OrdenCompraFolio = $fol WHERE Id = $fid";
            up.Parameters.AddWithValue("$ocid", nuevaOcId);
            up.Parameters.AddWithValue("$fol", nuevoFolio);
            up.Parameters.AddWithValue("$fid", facturaId);
            up.ExecuteNonQuery();

            if (ocAnteriorId.HasValue && ocAnteriorId.Value > 0)
                SincronizarOrdenCompraConSusFacturas(ocAnteriorId.Value);
        }
        else
        {
            var ocDestino = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ordenCompraId.Value);
            if (ocDestino != null)
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var up = conn.CreateCommand();
                up.CommandText = "UPDATE FacturasXml SET OrdenCompraId = $ocid, OrdenCompraFolio = $fol WHERE Id = $fid";
                up.Parameters.AddWithValue("$ocid", ocDestino.Id);
                up.Parameters.AddWithValue("$fol", ocDestino.Folio);
                up.Parameters.AddWithValue("$fid", facturaId);
                up.ExecuteNonQuery();

                SincronizarOrdenCompraConSusFacturas(ocDestino.Id, proyectoId);
                if (ocAnteriorId.HasValue && ocAnteriorId.Value > 0 && ocAnteriorId.Value != ocDestino.Id)
                    SincronizarOrdenCompraConSusFacturas(ocAnteriorId.Value);
            }
        }
    }

    public static List<FacturaXml> ObtenerFacturasXml()
    {
        var list = new List<FacturaXml>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT f.Id, f.Uuid, f.SerieFolio, f.FechaTimbrado, f.EmisorRfc, f.EmisorNombre, f.Moneda, f.TipoCambio,
       f.Subtotal, f.Descuento, f.Iva, f.Total, f.OrdenCompraId, f.OrdenCompraFolio, f.RutaArchivoXml,
       oc.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Stock General')
FROM FacturasXml f
LEFT JOIN OrdenesCompra oc ON oc.Id = f.OrdenCompraId
LEFT JOIN Proyectos pr ON pr.Id = oc.ProyectoId
ORDER BY f.Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new FacturaXml
                {
                    Id = r.GetInt32(0),
                    Uuid = r.GetString(1),
                    SerieFolio = r.GetString(2),
                    FechaTimbrado = r.GetString(3),
                    EmisorRfc = r.GetString(4),
                    EmisorNombre = r.GetString(5),
                    Moneda = r.GetString(6),
                    TipoCambio = Convert.ToDecimal(r.GetDouble(7)),
                    Subtotal = Convert.ToDecimal(r.GetDouble(8)),
                    Descuento = Convert.ToDecimal(r.GetDouble(9)),
                    Iva = Convert.ToDecimal(r.GetDouble(10)),
                    Total = Convert.ToDecimal(r.GetDouble(11)),
                    OrdenCompraId = r.IsDBNull(12) ? null : r.GetInt32(12),
                    OrdenCompraFolio = r.GetString(13),
                    RutaArchivoXml = r.GetString(14),
                    ProyectoId = r.IsDBNull(15) ? null : r.GetInt32(15),
                    ProyectoNombre = r.GetString(16)
                });
            }
        }

        if (list.Count == 0)
            return list;

        var facDict = list.ToDictionary(f => f.Id);
        var productosDict = ObtenerProductos().ToDictionary(p => p.Id, p => $"✔ [{p.CodigoInterno}] {p.Nombre}");

        using (var cCmd = conn.CreateCommand())
        {
            cCmd.CommandText = @"
SELECT Id, FacturaXmlId, ClaveProdServ, NoIdentificacion, Descripcion, Unidad, Cantidad, ValorUnitario, Importe, IvaConcepto, ProductoIdVinculado
FROM FacturaConceptos";
            using var cr = cCmd.ExecuteReader();
            while (cr.Read())
            {
                int facId = cr.GetInt32(1);
                if (!facDict.TryGetValue(facId, out var f))
                    continue;

                int? pid = cr.IsDBNull(10) ? null : cr.GetInt32(10);
                string estatusProd;
                if (pid.HasValue && pid.Value == -1)
                    estatusProd = "🗑 Eliminado del catálogo";
                else if (pid.HasValue && productosDict.TryGetValue(pid.Value, out var pNom))
                    estatusProd = pNom;
                else
                    estatusProd = "Sin vincular";

                f.Conceptos.Add(new FacturaConcepto
                {
                    Id = cr.GetInt32(0),
                    FacturaXmlId = facId,
                    ClaveProdServ = cr.GetString(2),
                    NoIdentificacion = cr.GetString(3),
                    Descripcion = cr.GetString(4),
                    Unidad = cr.GetString(5),
                    Cantidad = Convert.ToDecimal(cr.GetDouble(6)),
                    ValorUnitario = Convert.ToDecimal(cr.GetDouble(7)),
                    Importe = Convert.ToDecimal(cr.GetDouble(8)),
                    IvaConcepto = Convert.ToDecimal(cr.GetDouble(9)),
                    ProductoIdVinculado = pid > 0 && productosDict.ContainsKey(pid.Value) ? pid : (pid == -1 ? -1 : null),
                    ProductoVinculadoNombre = estatusProd
                });
            }
        }

        return list;
    }

    public static void EliminarFacturaXml(int facturaId)
    {
        var fac = ObtenerFacturasXml().FirstOrDefault(f => f.Id == facturaId);
        int? ocId = fac?.OrdenCompraId;

        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var delConceptos = conn.CreateCommand())
            {
                delConceptos.Transaction = tx;
                delConceptos.CommandText = "DELETE FROM FacturaConceptos WHERE FacturaXmlId = $id";
                delConceptos.Parameters.AddWithValue("$id", facturaId);
                delConceptos.ExecuteNonQuery();
            }

            using (var delFac = conn.CreateCommand())
            {
                delFac.Transaction = tx;
                delFac.CommandText = "DELETE FROM FacturasXml WHERE Id = $id";
                delFac.Parameters.AddWithValue("$id", facturaId);
                delFac.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Si la factura tenía una Orden de Compra asociada, actualizarla o eliminarla si quedó sin facturas ni recepciones
        if (ocId.HasValue && ocId.Value > 0)
        {
            var restantes = ObtenerFacturasXml().Where(f => f.OrdenCompraId == ocId.Value).ToList();
            if (restantes.Count > 0)
            {
                SincronizarOrdenCompraConSusFacturas(ocId.Value);
            }
            else
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var chkEnt = conn.CreateCommand();
                chkEnt.CommandText = "SELECT COUNT(*) FROM EntradasAlmacen WHERE OrdenCompraId = $ocid";
                chkEnt.Parameters.AddWithValue("$ocid", ocId.Value);
                int numEntradas = Convert.ToInt32(chkEnt.ExecuteScalar());

                if (numEntradas == 0)
                {
                    using var tx = conn.BeginTransaction();
                    using (var delPart = conn.CreateCommand())
                    {
                        delPart.Transaction = tx;
                        delPart.CommandText = "DELETE FROM OrdenCompraPartidas WHERE OrdenCompraId = $ocid";
                        delPart.Parameters.AddWithValue("$ocid", ocId.Value);
                        delPart.ExecuteNonQuery();
                    }
                    using (var delOc = conn.CreateCommand())
                    {
                        delOc.Transaction = tx;
                        delOc.CommandText = "DELETE FROM OrdenesCompra WHERE Id = $ocid";
                        delOc.Parameters.AddWithValue("$ocid", ocId.Value);
                        delOc.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
        }
    }

    public static string CrearRespaldoBaseDatos()
    {
        return CrearRespaldoIntegralZip();
    }

    public static string CrearRespaldoIntegralZip(string? rutaZipPersonalizada = null)
    {
        Directory.CreateDirectory(BackupsFolder);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string zipPath = string.IsNullOrWhiteSpace(rutaZipPersonalizada)
            ? Path.Combine(BackupsFolder, $"RespaldoIntegral_Lebaref_{timestamp}.zip")
            : rutaZipPersonalizada!;

        string tempDbCopy = Path.Combine(BackupsFolder, $"temp_db_snapshot_{timestamp}.db");
        try
        {
            using (var conn = new SqliteConnection(ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                try { cmd.ExecuteNonQuery(); } catch { }
            }
            File.Copy(DbPath, tempDbCopy, true);

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                if (File.Exists(tempDbCopy))
                {
                    using var entryStream = zip.CreateEntry("compras_almacen.db", System.IO.Compression.CompressionLevel.Optimal).Open();
                    using var fs = new FileStream(tempDbCopy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.CopyTo(entryStream);
                }

                if (Directory.Exists(ImagesFolder))
                {
                    foreach (var imgFile in Directory.GetFiles(ImagesFolder, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(BaseFolder, imgFile).Replace('\\', '/');
                        try
                        {
                            using var entryStream = zip.CreateEntry(rel, System.IO.Compression.CompressionLevel.Fastest).Open();
                            using var fs = new FileStream(imgFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            fs.CopyTo(entryStream);
                        }
                        catch { }
                    }
                }

                if (Directory.Exists(XmlFolder))
                {
                    foreach (var xmlFile in Directory.GetFiles(XmlFolder, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(BaseFolder, xmlFile).Replace('\\', '/');
                        try
                        {
                            using var entryStream = zip.CreateEntry(rel, System.IO.Compression.CompressionLevel.Optimal).Open();
                            using var fs = new FileStream(xmlFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            fs.CopyTo(entryStream);
                        }
                        catch { }
                    }
                }
            }

            RegistrarBitacoraUsuario(
                "Sistema / Respaldo",
                "Respaldo Integral ZIP (BD + Imágenes + XML)",
                Path.GetFileName(zipPath),
                $"Respaldo empaquetado exitosamente en: {zipPath}");
        }
        finally
        {
            try { if (File.Exists(tempDbCopy)) File.Delete(tempDbCopy); } catch { }
        }

        return zipPath;
    }

    public static void RestaurarRespaldoIntegralZip(string rutaZipOrigen)
    {
        if (string.IsNullOrWhiteSpace(rutaZipOrigen) || !File.Exists(rutaZipOrigen))
            throw new FileNotFoundException("No se encontró el archivo ZIP de respaldo seleccionado.");

        // Respaldo preventivo antes de restaurar
        try { CrearRespaldoIntegralZip(); } catch { }

        SqliteConnection.ClearAllPools();

        string tempExtractDir = Path.Combine(BackupsFolder, $"restore_tmp_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(tempExtractDir);
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(rutaZipOrigen, tempExtractDir, true);

            string dbRestaurada = Path.Combine(tempExtractDir, "compras_almacen.db");
            if (!File.Exists(dbRestaurada))
            {
                var algunDb = Directory.GetFiles(tempExtractDir, "*.db", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(algunDb))
                    dbRestaurada = algunDb;
            }

            if (File.Exists(dbRestaurada))
            {
                string walPath = DbPath + "-wal";
                string shmPath = DbPath + "-shm";
                try { if (File.Exists(walPath)) File.Delete(walPath); } catch { }
                try { if (File.Exists(shmPath)) File.Delete(shmPath); } catch { }
                File.Copy(dbRestaurada, DbPath, true);
            }

            string imgRestauradaDir = Path.Combine(tempExtractDir, "ImagenesCatalogo");
            if (Directory.Exists(imgRestauradaDir))
            {
                Directory.CreateDirectory(ImagesFolder);
                foreach (var f in Directory.GetFiles(imgRestauradaDir, "*", SearchOption.AllDirectories))
                {
                    string dest = Path.Combine(ImagesFolder, Path.GetFileName(f));
                    try { File.Copy(f, dest, true); } catch { }
                }
            }

            string xmlRestauradaDir = Path.Combine(tempExtractDir, "FacturasXml");
            if (Directory.Exists(xmlRestauradaDir))
            {
                Directory.CreateDirectory(XmlFolder);
                foreach (var f in Directory.GetFiles(xmlRestauradaDir, "*", SearchOption.AllDirectories))
                {
                    string dest = Path.Combine(XmlFolder, Path.GetFileName(f));
                    try { File.Copy(f, dest, true); } catch { }
                }
            }

            Initialize();
            RegistrarBitacoraUsuario(
                "Sistema / Restauración",
                "Restauración de Respaldo Integral ZIP",
                Path.GetFileName(rutaZipOrigen),
                $"Datos restaurados desde: {rutaZipOrigen}");
        }
        finally
        {
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
        }
    }

    // ==================== PERFIL ADMINISTRADOR Y BITÁCORA ====================

    private static string CalcularHashPinAdministrador(string pinPlano)
    {
        string limpio = (pinPlano ?? string.Empty).Trim();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(PinSalt + limpio);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return "SHA256:" + Convert.ToHexString(hash);
    }

    public static string ObtenerPinAdministrador()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'AdminPin' LIMIT 1";
        var res = cmd.ExecuteScalar()?.ToString();
        return string.IsNullOrWhiteSpace(res) ? CalcularHashPinAdministrador("1234") : res.Trim();
    }

    public static bool ValidarPinAdministrador(string pinIngresado)
    {
        if (string.IsNullOrWhiteSpace(pinIngresado))
            return false;

        string limpio = pinIngresado.Trim();
        string valorGuardado = ObtenerPinAdministrador();

        if (valorGuardado.StartsWith("SHA256:", StringComparison.Ordinal))
        {
            string hashIngresado = CalcularHashPinAdministrador(limpio);
            return string.Equals(hashIngresado, valorGuardado, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(limpio, valorGuardado, StringComparison.Ordinal);
    }

    public static void CambiarPinAdministrador(string nuevoPin)
    {
        string hashNuevo = CalcularHashPinAdministrador(nuevoPin);
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ('AdminPin', $pin)
ON CONFLICT(Clave) DO UPDATE SET Valor = $pin";
        cmd.Parameters.AddWithValue("$pin", hashNuevo);
        cmd.ExecuteNonQuery();
    }

    public static void RegistrarBitacoraAdmin(string accion, string referencia, string motivo)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO BitacoraAdministrador (Fecha, Accion, Referencia, Motivo)
VALUES ($fec, $acc, $ref, $mot)";
        cmd.Parameters.AddWithValue("$fec", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$acc", accion ?? string.Empty);
        cmd.Parameters.AddWithValue("$ref", referencia ?? string.Empty);
        cmd.Parameters.AddWithValue("$mot", motivo ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    // ==================== FORZAR ESTATUS / RECEPCIÓN DE OC (ADMIN) ====================

    public static void ForzarEstatusOrdenCompraAdmin(int ordenCompraId, string modoAccion, string motivoAdmin, string recibidoPor = "Administrador")
    {
        var oc = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ordenCompraId);
        if (oc == null) return;

        string notaBitacora = $"[ADMIN {DateTime.Now:yyyy-MM-dd HH:mm}] {modoAccion}: {motivoAdmin}";

        if (modoAccion.StartsWith("Recibida Completa (Ingresar pendientes", StringComparison.OrdinalIgnoreCase))
        {
            var pendientes = oc.Partidas
                .Where(p => p.CantidadPendiente > 0m)
                .Select(p => (p, p.CantidadPendiente))
                .ToList();

            if (pendientes.Count > 0)
            {
                RegistrarRecepcionOrdenCompra(
                    oc,
                    pendientes,
                    $"Recepción forzada por Administrador ({recibidoPor}). Motivo: {motivoAdmin}");
            }

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE OrdenesCompra
SET Estatus = 'Recibida Completa',
    Notas = CASE WHEN Notas = '' THEN $nb ELSE Notas || ' | ' || $nb END
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$nb", notaBitacora);
            cmd.Parameters.AddWithValue("$id", ordenCompraId);
            cmd.ExecuteNonQuery();
        }
        else if (modoAccion.StartsWith("Recibida Completa (Sin afectar Stock", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var upPart = conn.CreateCommand())
            {
                upPart.Transaction = tx;
                upPart.CommandText = "UPDATE OrdenCompraPartidas SET CantidadRecibida = CantidadPedida WHERE OrdenCompraId = $id";
                upPart.Parameters.AddWithValue("$id", ordenCompraId);
                upPart.ExecuteNonQuery();
            }

            using (var upOc = conn.CreateCommand())
            {
                upOc.Transaction = tx;
                upOc.CommandText = @"
UPDATE OrdenesCompra
SET Estatus = 'Recibida Completa',
    Notas = CASE WHEN Notas = '' THEN $nb ELSE Notas || ' | ' || $nb END
WHERE Id = $id";
                upOc.Parameters.AddWithValue("$nb", notaBitacora);
                upOc.Parameters.AddWithValue("$id", ordenCompraId);
                upOc.ExecuteNonQuery();
            }

            tx.Commit();
        }
        else if (modoAccion.StartsWith("Cerrada Parcial Definitiva", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE OrdenesCompra
SET Estatus = 'Cerrada Parcial (Admin)',
    Notas = CASE WHEN Notas = '' THEN $nb ELSE Notas || ' | ' || $nb END
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$nb", notaBitacora);
            cmd.Parameters.AddWithValue("$id", ordenCompraId);
            cmd.ExecuteNonQuery();
        }
        else if (modoAccion.StartsWith("Cancelar OC", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE OrdenesCompra
SET Estatus = 'Cancelada',
    Notas = CASE WHEN Notas = '' THEN $nb ELSE Notas || ' | ' || $nb END
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$nb", notaBitacora);
            cmd.Parameters.AddWithValue("$id", ordenCompraId);
            cmd.ExecuteNonQuery();
        }

        RegistrarBitacoraAdmin(modoAccion, oc.Folio, motivoAdmin);
    }

    // ==================== COMPRAS SIN FACTURA (NOTA DE REMISIÓN / TICKET) ====================

    public static FacturaXml LiquidarOrdenCompraSinFactura(int ordenCompraId, string folioNota, string fechaNota, string notasAdicionales)
    {
        var oc = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ordenCompraId)
            ?? throw new InvalidOperationException("No se encontró la Orden de Compra seleccionada.");

        var prov = ObtenerProveedores().FirstOrDefault(p => p.Id == oc.ProveedorId);
        string rfcProv = prov?.Rfc ?? "XAXX010101000";
        string nomProv = prov?.NombreComercial ?? oc.ProveedorNombre;
        string folioLimpio = string.IsNullOrWhiteSpace(folioNota) ? $"NOTA-{oc.Folio}" : folioNota.Trim();
        string uuidSintetico = $"NOTA-{DateTime.Now:yyyyMMddHHmmss}-{oc.Id}";

        var facturaNota = new FacturaXml
        {
            Uuid = uuidSintetico,
            SerieFolio = folioLimpio,
            FechaTimbrado = string.IsNullOrWhiteSpace(fechaNota) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : fechaNota.Trim(),
            EmisorRfc = rfcProv,
            EmisorNombre = nomProv,
            Moneda = "MXN",
            TipoCambio = 1m,
            Subtotal = oc.Subtotal,
            Descuento = 0m,
            Iva = oc.Iva,
            Total = oc.TotalConIva,
            OrdenCompraId = oc.Id,
            OrdenCompraFolio = oc.Folio,
            ProyectoId = oc.ProyectoId,
            RutaArchivoXml = $"SIN_FACTURA|{notasAdicionales}"
        };

        foreach (var p in oc.Partidas)
        {
            decimal imp = Math.Round(p.CantidadPedida * p.PrecioUnitarioSinIva, 2);
            decimal iva = Math.Round(imp * 0.16m, 2);
            facturaNota.Conceptos.Add(new FacturaConcepto
            {
                ClaveProdServ = "01010101",
                NoIdentificacion = p.ProductoCodigo,
                Descripcion = p.ProductoNombre,
                Unidad = p.UnidadCompra,
                Cantidad = p.CantidadPedida,
                ValorUnitario = p.PrecioUnitarioSinIva,
                Importe = imp,
                IvaConcepto = iva,
                ProductoIdVinculado = p.ProductoId
            });
        }

        RegistrarBitacoraAdmin("Liquidar OC sin Factura", oc.Folio, $"Folio Nota/Ticket: {folioLimpio} | {notasAdicionales}");
        return GuardarFacturaXml(facturaNota);
    }

    public static (FacturaXml Comprobante, string FolioOc, string FolioEntrada) RegistrarCompraDirectaSinFactura(
        int proveedorId,
        string proveedorAbiertoNombre,
        int proyectoId,
        string folioNota,
        string fechaCompra,
        bool aplicaIva,
        bool ingresarAlmacenAutomaticamente,
        string recibidoPor,
        string notas,
        List<PartidaCompraSinFactura> partidas)
    {
        if (partidas == null || partidas.Count == 0)
            throw new InvalidOperationException("Debes agregar al menos un artículo a la compra sin factura.");

        int provIdFinal = proveedorId;
        string rfcProv = "XAXX010101000";
        string nomProv = string.IsNullOrWhiteSpace(proveedorAbiertoNombre) ? "Proveedor General (Nota/Ticket)" : proveedorAbiertoNombre.Trim();

        if (provIdFinal > 0)
        {
            var provExistente = ObtenerProveedores().FirstOrDefault(p => p.Id == provIdFinal);
            if (provExistente != null)
            {
                rfcProv = string.IsNullOrWhiteSpace(provExistente.Rfc) ? "XAXX010101000" : provExistente.Rfc;
                nomProv = provExistente.NombreComercial;
            }
        }
        else
        {
            var provPorNombre = ObtenerProveedores().FirstOrDefault(p =>
                p.NombreComercial.Equals(nomProv, StringComparison.OrdinalIgnoreCase) ||
                p.RazonSocial.Equals(nomProv, StringComparison.OrdinalIgnoreCase));
            if (provPorNombre != null)
            {
                provIdFinal = provPorNombre.Id;
                rfcProv = provPorNombre.Rfc;
                nomProv = provPorNombre.NombreComercial;
            }
            else
            {
                GuardarProveedor(new Proveedor
                {
                    Rfc = "XAXX010101000",
                    RazonSocial = nomProv,
                    NombreComercial = nomProv,
                    Notas = "Proveedor registrado desde Compra sin Factura (Nota / Ticket)"
                });
                var recienCreado = ObtenerProveedores().First(p => p.NombreComercial.Equals(nomProv, StringComparison.OrdinalIgnoreCase));
                provIdFinal = recienCreado.Id;
            }
        }

        int proyFinal = proyectoId > 0 ? proyectoId : ObtenerProyectoStockGeneralId();
        string folioOc = GenerarSiguienteFolio("OC", "OrdenesCompra");
        string fechaStr = string.IsNullOrWhiteSpace(fechaCompra) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : fechaCompra.Trim();
        string folioNotaStr = string.IsNullOrWhiteSpace(folioNota) ? $"TICKET-{DateTime.Now:MMdd-HHmm}" : folioNota.Trim();

        decimal subtotal = partidas.Sum(x => Math.Round(x.Cantidad * x.PrecioUnitarioSinIva, 2));
        decimal iva = aplicaIva ? Math.Round(subtotal * 0.16m, 2) : 0m;
        decimal total = subtotal + iva;

        int ocId;
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO OrdenesCompra (Folio, Fecha, SolicitudId, CotizacionFolio, ProveedorId, ProyectoId, DepartamentoNombre, EncargadoNombre, TiempoEntregaDias, CostoEnvioConIva, Subtotal, Iva, TotalConIva, Estatus, Notas)
VALUES ($fol, $fec, 0, 'COMPRA-DIRECTA', $pvid, $prid, 'Compras / Almacén', $rec, 1, 0, $sub, $iva, $tot, 'Emitida', $not);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$fol", folioOc);
            cmd.Parameters.AddWithValue("$fec", fechaStr);
            cmd.Parameters.AddWithValue("$pvid", provIdFinal);
            cmd.Parameters.AddWithValue("$prid", proyFinal);
            cmd.Parameters.AddWithValue("$rec", string.IsNullOrWhiteSpace(recibidoPor) ? "Almacén" : recibidoPor.Trim());
            cmd.Parameters.AddWithValue("$sub", (double)subtotal);
            cmd.Parameters.AddWithValue("$iva", (double)iva);
            cmd.Parameters.AddWithValue("$tot", (double)total);
            cmd.Parameters.AddWithValue("$not", $"Compra sin Factura ({folioNotaStr}). {notas}".Trim());
            ocId = Convert.ToInt32(cmd.ExecuteScalar());

            foreach (var part in partidas)
            {
                using var pCmd = conn.CreateCommand();
                pCmd.Transaction = tx;
                pCmd.CommandText = @"
INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva)
VALUES ($ocid, $pid, $cant, 0, $precio)";
                pCmd.Parameters.AddWithValue("$ocid", ocId);
                pCmd.Parameters.AddWithValue("$pid", part.ProductoId);
                pCmd.Parameters.AddWithValue("$cant", (double)part.Cantidad);
                pCmd.Parameters.AddWithValue("$precio", (double)part.PrecioUnitarioSinIva);
                pCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        string uuidNota = $"NOTA-{DateTime.Now:yyyyMMddHHmmss}-{ocId}";
        var facturaNota = new FacturaXml
        {
            Uuid = uuidNota,
            SerieFolio = folioNotaStr,
            FechaTimbrado = fechaStr,
            EmisorRfc = rfcProv,
            EmisorNombre = nomProv,
            Moneda = "MXN",
            TipoCambio = 1m,
            Subtotal = subtotal,
            Descuento = 0m,
            Iva = iva,
            Total = total,
            OrdenCompraId = ocId,
            OrdenCompraFolio = folioOc,
            ProyectoId = proyFinal,
            RutaArchivoXml = $"SIN_FACTURA|{notas}"
        };

        foreach (var part in partidas)
        {
            decimal imp = Math.Round(part.Cantidad * part.PrecioUnitarioSinIva, 2);
            decimal ivaPart = aplicaIva ? Math.Round(imp * 0.16m, 2) : 0m;
            facturaNota.Conceptos.Add(new FacturaConcepto
            {
                ClaveProdServ = "01010101",
                NoIdentificacion = part.ProductoCodigo,
                Descripcion = part.ProductoNombre,
                Unidad = part.Unidad,
                Cantidad = part.Cantidad,
                ValorUnitario = part.PrecioUnitarioSinIva,
                Importe = imp,
                IvaConcepto = ivaPart,
                ProductoIdVinculado = part.ProductoId
            });
        }

        var comprobanteGuardado = GuardarFacturaXml(facturaNota);
        string folioEntrada = string.Empty;

        if (ingresarAlmacenAutomaticamente)
        {
            var ocActualizada = ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ocId);
            if (ocActualizada != null && ocActualizada.Partidas.Count > 0)
            {
                var recepciones = ocActualizada.Partidas
                    .Select(p => (p, p.CantidadPedida))
                    .ToList();
                var ent = RegistrarRecepcionOrdenCompra(
                    ocActualizada,
                    recepciones,
                    $"Entrada directa por Compra sin Factura ({folioNotaStr}) | Recibió: {recibidoPor}");
                folioEntrada = ent.Folio;
            }
        }

        RegistrarBitacoraAdmin("Compra Directa sin Factura", $"{folioOc} / {folioNotaStr}", $"Proveedor: {nomProv} | Total: {total:C2}");
        return (comprobanteGuardado, folioOc, folioEntrada);
    }

    // ==================== DASHBOARD: RECORDATORIOS Y CALENDARIO DE EVENTOS ====================

    private static void SeedEventosCalendarioSiVacio(SqliteConnection conn)
    {
        using (var chkSeed = conn.CreateCommand())
        {
            chkSeed.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'AgendaSeedInicializado' LIMIT 1";
            var val = chkSeed.ExecuteScalar()?.ToString();
            if (val == "1")
                return;
        }

        using (var markCmd = conn.CreateCommand())
        {
            markCmd.CommandText = "INSERT OR REPLACE INTO ConfiguracionSistema (Clave, Valor) VALUES ('AgendaSeedInicializado', '1');";
            markCmd.ExecuteNonQuery();
        }

        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT COUNT(*) FROM EventosCalendario";
        if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
            return;

        string hoy = DateTime.Now.ToString("yyyy-MM-dd");
        string manana = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
        string proxSemana = DateTime.Now.AddDays(4).ToString("yyyy-MM-dd");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO EventosCalendario (Fecha, Titulo, Descripcion, Categoria, Prioridad, Completado, ReferenciaFolio) VALUES
($f1, 'Revisar niveles de stock mínimo y consumibles críticos', 'Verificar artículos en alerta roja para armar carrito de solicitud semanal.', 'Inventario / Auditoría', 'Alta', 0, 'STOCK'),
($f2, 'Seguimiento de entregas pendientes con proveedores', 'Confirmar fechas de llegada de Órdenes de Compra abiertas.', 'Entrega OC', 'Media', 0, 'COMPRAS'),
($f3, 'Corte semanal de notas de remisión y facturas XML', 'Validar que todas las compras de la semana tengan comprobante registrado.', 'Pago a Proveedor', 'Media', 0, 'FINANZAS');";
        cmd.Parameters.AddWithValue("$f1", hoy);
        cmd.Parameters.AddWithValue("$f2", manana);
        cmd.Parameters.AddWithValue("$f3", proxSemana);
        cmd.ExecuteNonQuery();
    }

    public static void GuardarEventoCalendario(EventoCalendario ev)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO EventosCalendario (Fecha, Titulo, Descripcion, Categoria, Prioridad, Completado, ReferenciaFolio, Oculto, ClaveAutomatica)
VALUES ($fec, $tit, $desc, $cat, $prio, $comp, $ref, 0, '')";
        cmd.Parameters.AddWithValue("$fec", string.IsNullOrWhiteSpace(ev.Fecha) ? DateTime.Now.ToString("yyyy-MM-dd") : ev.Fecha.Trim());
        cmd.Parameters.AddWithValue("$tit", ev.Titulo.Trim());
        cmd.Parameters.AddWithValue("$desc", (ev.Descripcion ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(ev.Categoria) ? "Recordatorio Manual" : ev.Categoria.Trim());
        cmd.Parameters.AddWithValue("$prio", string.IsNullOrWhiteSpace(ev.Prioridad) ? "Media" : ev.Prioridad.Trim());
        cmd.Parameters.AddWithValue("$comp", ev.Completado ? 1 : 0);
        cmd.Parameters.AddWithValue("$ref", (ev.ReferenciaFolio ?? string.Empty).Trim());
        cmd.ExecuteNonQuery();
    }

    public static void AlternarCompletadoEventoCalendario(int eventoId, bool completado)
    {
        if (eventoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE EventosCalendario SET Completado = $c WHERE Id = $id";
        cmd.Parameters.AddWithValue("$c", completado ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", eventoId);
        cmd.ExecuteNonQuery();
    }

    public static void EliminarEventoCalendario(int eventoId)
    {
        if (eventoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EventosCalendario WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", eventoId);
        cmd.ExecuteNonQuery();
    }

    public static void AlternarCompletadoEventoUnificado(EventoCalendario ev)
    {
        if (ev == null) return;
        bool nuevoEstado = !ev.Completado;
        if (!ev.EsAutomatico && ev.Id > 0)
        {
            AlternarCompletadoEventoCalendario(ev.Id, nuevoEstado);
            return;
        }

        GuardarOverrideEventoAutomatico(ev, ev.Fecha, ev.Titulo, ev.Prioridad, completado: nuevoEstado, oculto: false);
    }

    public static void EliminarODescartarEventoUnificado(EventoCalendario ev)
    {
        if (ev == null) return;
        if (!ev.EsAutomatico && ev.Id > 0)
        {
            EliminarEventoCalendario(ev.Id);
            return;
        }

        GuardarOverrideEventoAutomatico(ev, ev.Fecha, ev.Titulo, ev.Prioridad, completado: ev.Completado, oculto: true);
    }

    public static void PosponerOEditarEventoUnificado(EventoCalendario ev, int diasExtra = 0, string? nuevaFechaFija = null, string? nuevoTitulo = null, string? nuevaPrioridad = null, string? nuevaDescripcion = null)
    {
        if (ev == null) return;
        DateTime dtBase = DateTime.TryParse(ev.Fecha, out var d) ? d.Date : DateTime.Today;
        string fechaFinal = !string.IsNullOrWhiteSpace(nuevaFechaFija)
            ? nuevaFechaFija.Trim()
            : dtBase.AddDays(diasExtra).ToString("yyyy-MM-dd");
        string tituloFinal = !string.IsNullOrWhiteSpace(nuevoTitulo) ? nuevoTitulo.Trim() : ev.Titulo;
        string prioFinal = !string.IsNullOrWhiteSpace(nuevaPrioridad) ? nuevaPrioridad.Trim() : ev.Prioridad;
        string descFinal = nuevaDescripcion != null ? nuevaDescripcion.Trim() : ev.Descripcion;

        if (!ev.EsAutomatico && ev.Id > 0)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE EventosCalendario
SET Fecha = $fec, Titulo = $tit, Prioridad = $prio, Descripcion = $desc
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$fec", fechaFinal);
            cmd.Parameters.AddWithValue("$tit", tituloFinal);
            cmd.Parameters.AddWithValue("$prio", prioFinal);
            cmd.Parameters.AddWithValue("$desc", descFinal);
            cmd.Parameters.AddWithValue("$id", ev.Id);
            cmd.ExecuteNonQuery();
            return;
        }

        // Si es evento automático de OC o Préstamo, también actualizar su fecha compromiso si aplica
        if (ev.ReferenciaFolio.StartsWith("OC-", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE OrdenesCompra SET FechaProgramadaPago = $fec WHERE Folio = $fol";
            cmd.Parameters.AddWithValue("$fec", fechaFinal);
            cmd.Parameters.AddWithValue("$fol", ev.ReferenciaFolio);
            cmd.ExecuteNonQuery();
        }
        else if (ev.ReferenciaFolio.StartsWith("PRE-", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE PrestamosHerramienta SET FechaCompromiso = $fec WHERE Folio = $fol";
            cmd.Parameters.AddWithValue("$fec", fechaFinal);
            cmd.Parameters.AddWithValue("$fol", ev.ReferenciaFolio);
            cmd.ExecuteNonQuery();
        }

        GuardarOverrideEventoAutomatico(ev, fechaFinal, tituloFinal, prioFinal, completado: ev.Completado, oculto: false);
    }

    private static void GuardarOverrideEventoAutomatico(EventoCalendario ev, string fecha, string titulo, string prioridad, bool completado, bool oculto)
    {
        string clave = ev.ClaveUnicaAutomatico;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM EventosCalendario WHERE ClaveAutomatica = $clv";
            del.Parameters.AddWithValue("$clv", clave);
            del.ExecuteNonQuery();
        }

        using var ins = conn.CreateCommand();
        ins.CommandText = @"
INSERT INTO EventosCalendario (Fecha, Titulo, Descripcion, Categoria, Prioridad, Completado, ReferenciaFolio, Oculto, ClaveAutomatica)
VALUES ($fec, $tit, $desc, $cat, $prio, $comp, $ref, $ocu, $clv)";
        ins.Parameters.AddWithValue("$fec", fecha);
        ins.Parameters.AddWithValue("$tit", titulo);
        ins.Parameters.AddWithValue("$desc", ev.Descripcion ?? string.Empty);
        ins.Parameters.AddWithValue("$cat", ev.Categoria ?? string.Empty);
        ins.Parameters.AddWithValue("$prio", prioridad ?? "Media");
        ins.Parameters.AddWithValue("$comp", completado ? 1 : 0);
        ins.Parameters.AddWithValue("$ref", ev.ReferenciaFolio ?? string.Empty);
        ins.Parameters.AddWithValue("$ocu", oculto ? 1 : 0);
        ins.Parameters.AddWithValue("$clv", clave);
        ins.ExecuteNonQuery();
    }

    public static int LimpiarEventosCompletadosCalendario()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        int afectados = 0;
        // 1. Eliminar los manuales completados
        using (var cmdMan = conn.CreateCommand())
        {
            cmdMan.CommandText = "DELETE FROM EventosCalendario WHERE Completado = 1 AND COALESCE(ClaveAutomatica, '') = ''";
            afectados += cmdMan.ExecuteNonQuery();
        }
        // 2. Ocultar los automáticos que estaban marcados como completados
        using (var cmdAuto = conn.CreateCommand())
        {
            cmdAuto.CommandText = "UPDATE EventosCalendario SET Oculto = 1 WHERE Completado = 1 AND COALESCE(ClaveAutomatica, '') <> ''";
            afectados += cmdAuto.ExecuteNonQuery();
        }
        return afectados;
    }

    public static void ActualizarStockMinimoProducto(int productoId, decimal nuevoStockMinimo)
    {
        if (productoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Productos SET StockMinimo = $min WHERE Id = $id";
        cmd.Parameters.AddWithValue("$min", (double)Math.Max(0m, nuevoStockMinimo));
        cmd.Parameters.AddWithValue("$id", productoId);
        cmd.ExecuteNonQuery();
    }

    public static List<PendienteOperativoCompra> ObtenerPendientesOperativosCompras()
    {
        var lista = new List<PendienteOperativoCompra>();

        // 1. Solicitudes pendientes de revisión o en comparativa sin OC
        var solicitudes = ObtenerSolicitudes()
            .Where(s => s.Estatus == "Pendiente" || s.Estatus == "En Comparativa" || s.Estatus == "Parcial en OC")
            .OrderByDescending(s => s.Id)
            .ToList();

        foreach (var s in solicitudes)
        {
            int sinOc = s.Partidas.Count(p => !p.TieneOcGenerada);
            lista.Add(new PendienteOperativoCompra
            {
                Tipo = "Solicitud Compra",
                Folio = s.Folio,
                Fecha = s.Fecha.Length >= 10 ? s.Fecha[..10] : s.Fecha,
                Titulo = $"{s.ProyectoMostrar} ({s.DepartamentoNombre})",
                Detalle = $"{sinOc} partida(s) por cotizar/adjudicar de {s.Partidas.Count} · Solicita: {s.EncargadoNombre}",
                Estatus = s.Estatus == "Pendiente" ? "Por Aprobar / Cotizar" : s.Estatus,
                TextoAccionPrincipal = "Cotizar →",
                EntidadId = s.Id
            });
        }

        // 2. Entradas de almacén pendientes de subir Factura XML
        var entradasSinXml = ObtenerEntradasAlmacen()
            .Where(e => string.IsNullOrWhiteSpace(e.FacturaUuid) || e.Notas.Contains("[PENDIENTE_XML]", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Id)
            .ToList();

        foreach (var en in entradasSinXml)
        {
            lista.Add(new PendienteOperativoCompra
            {
                Tipo = "Falta Factura XML",
                Folio = en.Folio,
                Fecha = en.Fecha.Length >= 10 ? en.Fecha[..10] : en.Fecha,
                Titulo = $"Entrada de {en.ProveedorNombre} {(string.IsNullOrWhiteSpace(en.OrdenCompraFolio) ? "(Sin OC previa)" : $"({en.OrdenCompraFolio})")}",
                Detalle = $"Proyecto: {en.ProyectoNombre} · {en.Partidas.Count} artículo(s) ingresados",
                Estatus = "Pendiente XML",
                TextoAccionPrincipal = "Subir XML →",
                EntidadId = en.Id
            });
        }

        // 3. Cotizaciones guardadas próximas a vencer (<= 3 días restantes)
        var cotizaciones = ObtenerCotizacionesBancoHistorico()
            .GroupBy(c => new { c.ProductoId, Concepto = c.DescripcionConcepto.ToUpperInvariant(), c.ProveedorId })
            .Select(g => g.First())
            .ToList();

        foreach (var cot in cotizaciones)
        {
            if (!DateTime.TryParse(cot.FechaCotizacion, out var dtCot))
                continue;
            int diasRest = (dtCot.Date.AddDays(cot.DiasVigencia > 0 ? cot.DiasVigencia : 15) - DateTime.Today).Days;
            if (diasRest >= -2 && diasRest <= 3)
            {
                lista.Add(new PendienteOperativoCompra
                {
                    Tipo = "Vigencia Cotización",
                    Folio = string.IsNullOrWhiteSpace(cot.OrigenFolio) ? "COT" : cot.OrigenFolio,
                    Fecha = dtCot.Date.AddDays(cot.DiasVigencia > 0 ? cot.DiasVigencia : 15).ToString("yyyy-MM-dd"),
                    Titulo = $"{cot.ProveedorNombre} — {cot.DescripcionConcepto}",
                    Detalle = $"Precio: {cot.PrecioSinIva:C2} s/IVA ({cot.PrecioConIva:C2} c/IVA) · {cot.TipoPartidaTexto}",
                    Estatus = diasRest < 0 ? $"Vencida ({Math.Abs(diasRest)}d)" : (diasRest == 0 ? "Vence Hoy" : $"Vence en {diasRest}d"),
                    TextoAccionPrincipal = "Ver Comparativa →",
                    EntidadId = cot.Id
                });
            }
        }

        return lista;
    }

    public static void DescartarAlertaVigenciaCotizacion(int bancoPrecioId)
    {
        if (bancoPrecioId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE BancoPreciosHistorico SET DiasVigencia = DiasVigencia + 15 WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", bancoPrecioId);
        cmd.ExecuteNonQuery();
    }

    public static void MarcarEntradaXmlResueltaManual(int entradaId, string referenciaXmlONota)
    {
        if (entradaId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE EntradasAlmacen
SET FacturaUuid = CASE WHEN TRIM(COALESCE(FacturaUuid, '')) = '' THEN $ref ELSE FacturaUuid END,
    Notas = REPLACE(COALESCE(Notas, ''), '[PENDIENTE_XML]', '[XML_RESUELTO]')
WHERE Id = $id";
        cmd.Parameters.AddWithValue("$ref", string.IsNullOrWhiteSpace(referenciaXmlONota) ? "RESUELTO-MANUAL" : referenciaXmlONota.Trim());
        cmd.Parameters.AddWithValue("$id", entradaId);
        cmd.ExecuteNonQuery();
    }

    public static List<EventoCalendario> ObtenerEventosCalendarioUnificados(string? filtroFecha = null)
    {
        var lista = new List<EventoCalendario>();
        var overridesAuto = new Dictionary<string, (string fecha, string titulo, string prioridad, bool completado, bool oculto)>(StringComparer.OrdinalIgnoreCase);

        // 1. Eventos manuales y overrides de automáticos guardados en la tabla EventosCalendario
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Fecha, Titulo, Descripcion, Categoria, Prioridad, Completado, ReferenciaFolio, COALESCE(Oculto, 0), COALESCE(ClaveAutomatica, '')
FROM EventosCalendario
ORDER BY Fecha ASC, Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                bool oculto = r.GetInt32(8) != 0;
                string claveAuto = r.GetString(9);
                if (!string.IsNullOrWhiteSpace(claveAuto))
                {
                    overridesAuto[claveAuto] = (r.GetString(1), r.GetString(2), r.GetString(5), r.GetInt32(6) == 1, oculto);
                    continue;
                }

                if (oculto) continue;

                lista.Add(new EventoCalendario
                {
                    Id = r.GetInt32(0),
                    Fecha = r.GetString(1),
                    Titulo = r.GetString(2),
                    Descripcion = r.GetString(3),
                    Categoria = r.GetString(4),
                    Prioridad = r.GetString(5),
                    Completado = r.GetInt32(6) == 1,
                    ReferenciaFolio = r.GetString(7),
                    EsAutomatico = false
                });
            }
        }

        void AgregarEventoAutomaticoConOverride(EventoCalendario evAuto)
        {
            if (overridesAuto.TryGetValue(evAuto.ClaveUnicaAutomatico, out var ov))
            {
                if (ov.oculto) return;
                if (!string.IsNullOrWhiteSpace(ov.fecha)) evAuto.Fecha = ov.fecha;
                if (!string.IsNullOrWhiteSpace(ov.titulo)) evAuto.Titulo = ov.titulo;
                if (!string.IsNullOrWhiteSpace(ov.prioridad)) evAuto.Prioridad = ov.prioridad;
                evAuto.Completado = ov.completado;
            }
            lista.Add(evAuto);
        }

        // 2. Eventos automáticos: Órdenes de Compra (Vo.Bo. Dirección, Pago Finanzas y Recepción Almacén)
        var todasOcs = ObtenerOrdenesCompra().Where(o => o.Estatus != "Cancelada").ToList();

        foreach (var oc in todasOcs)
        {
            DateTime fechaBase = DateTime.Now;
            if (DateTime.TryParse(oc.Fecha, out var dtOc))
                fechaBase = dtOc;

            if (oc.Estatus == "Pendiente Vo.Bo. Dirección")
            {
                string fSeg = !string.IsNullOrWhiteSpace(oc.FechaProgramadaPago)
                    ? oc.FechaProgramadaPago
                    : fechaBase.AddDays(1).ToString("yyyy-MM-dd");
                bool vencida = DateTime.TryParse(fSeg, out var dtS) && dtS.Date < DateTime.Now.Date;
                AgregarEventoAutomaticoConOverride(new EventoCalendario
                {
                    Id = -(200000 + oc.Id),
                    Fecha = fSeg.Length >= 10 ? fSeg[..10] : fSeg,
                    Titulo = $"Seguimiento Vo.Bo. Dirección: {oc.Folio} - {oc.ProveedorNombre}",
                    Descripcion = $"Solicita: {oc.DepartamentoNombre} ({oc.ProyectoNombre}) | Total: {oc.TotalConIva:C2}",
                    Categoria = "Autorización Dirección",
                    Prioridad = vencida ? "Alta" : "Media",
                    Completado = false,
                    ReferenciaFolio = oc.Folio,
                    EsAutomatico = true
                });
            }
            else if (oc.Estatus == "Autorizada / Pendiente Pago Finanzas")
            {
                string fPago = !string.IsNullOrWhiteSpace(oc.FechaProgramadaPago)
                    ? oc.FechaProgramadaPago
                    : fechaBase.AddDays(1).ToString("yyyy-MM-dd");
                bool vencida = DateTime.TryParse(fPago, out var dtP) && dtP.Date < DateTime.Now.Date;
                AgregarEventoAutomaticoConOverride(new EventoCalendario
                {
                    Id = -(300000 + oc.Id),
                    Fecha = fPago.Length >= 10 ? fPago[..10] : fPago,
                    Titulo = $"Pago programado Finanzas: {oc.Folio} - {oc.ProveedorNombre}",
                    Descripcion = $"Condición: {oc.CondicionPago} | Total a transferir: {oc.TotalConIva:C2}",
                    Categoria = "Pago a Proveedor",
                    Prioridad = vencida ? "Alta" : "Media",
                    Completado = false,
                    ReferenciaFolio = oc.Folio,
                    EsAutomatico = true
                });
            }
            else if (oc.Estatus == "Emitida" || oc.Estatus == "Pagada / Lista para Almacén" || oc.Estatus == "Recibida Parcial" || oc.Estatus == "Recibida Parcialmente")
            {
                DateTime fechaEstimada = fechaBase.AddDays(Math.Max(1, oc.TiempoEntregaDias));
                bool vencida = fechaEstimada.Date < DateTime.Now.Date;

                AgregarEventoAutomaticoConOverride(new EventoCalendario
                {
                    Id = -oc.Id,
                    Fecha = fechaEstimada.ToString("yyyy-MM-dd"),
                    Titulo = $"Recepción estimada Almacén: {oc.Folio} - {oc.ProveedorNombre}",
                    Descripcion = $"Destino: {oc.ProyectoNombre} | Avance: {oc.AvanceRecepcionTexto} | Total: {oc.TotalConIva:C2}",
                    Categoria = "Entrega OC",
                    Prioridad = vencida ? "Alta" : "Media",
                    Completado = false,
                    ReferenciaFolio = oc.Folio,
                    EsAutomatico = true
                });
            }

            if ((oc.Estatus == "Recibida Completa" || oc.Estatus == "Recibida Parcialmente") &&
                oc.CondicionPago.StartsWith("Crédito", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(oc.FechaProgramadaPago))
            {
                bool vencida = DateTime.TryParse(oc.FechaProgramadaPago, out var dtCred) && dtCred.Date < DateTime.Now.Date;
                AgregarEventoAutomaticoConOverride(new EventoCalendario
                {
                    Id = -(400000 + oc.Id),
                    Fecha = oc.FechaProgramadaPago.Length >= 10 ? oc.FechaProgramadaPago[..10] : oc.FechaProgramadaPago,
                    Titulo = $"Vencimiento de crédito: {oc.Folio} - {oc.ProveedorNombre}",
                    Descripcion = $"{oc.CondicionPago} | Monto por liquidar: {oc.TotalConIva:C2}",
                    Categoria = "Pago a Proveedor",
                    Prioridad = vencida ? "Alta" : "Media",
                    Completado = false,
                    ReferenciaFolio = oc.Folio,
                    EsAutomatico = true
                });
            }
        }

        // 3. Eventos automáticos: Devolución compromiso de Préstamos de Herramienta activos o parciales
        var prestamosActivos = ObtenerPrestamosHerramienta()
            .Where(p => p.Estatus == "Activo" || p.Estatus == "En Préstamo" || p.Estatus == "Devuelto Parcial")
            .ToList();

        foreach (var pre in prestamosActivos)
        {
            string fechaComp = pre.FechaCompromiso;
            if (string.IsNullOrWhiteSpace(fechaComp))
            {
                if (DateTime.TryParse(pre.FechaSalida, out var dtSal))
                    fechaComp = dtSal.AddDays(7).ToString("yyyy-MM-dd");
                else
                    fechaComp = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd");
            }

            bool vencido = DateTime.TryParse(fechaComp, out var dtC) && dtC.Date < DateTime.Now.Date;
            AgregarEventoAutomaticoConOverride(new EventoCalendario
            {
                Id = -(100000 + pre.Id),
                Fecha = fechaComp.Length >= 10 ? fechaComp[..10] : fechaComp,
                Titulo = $"Devolución herramienta {pre.Folio} ({pre.PersonaRecibeEfectiva})",
                Descripcion = $"Autorizó: {pre.DepartamentoNombre} | Herramientas: {pre.ResumenHerramientas}",
                Categoria = "Préstamo Herramienta",
                Prioridad = vencido ? "Alta" : "Media",
                Completado = false,
                ReferenciaFolio = pre.Folio,
                EsAutomatico = true
            });
        }

        // 4. Eventos automáticos: Cotizaciones próximas a vencer (<= 3 días restantes o recién vencidas hace <= 2 días)
        var cotizacionesRecientes = ObtenerCotizacionesBancoHistorico()
            .GroupBy(c => new { c.ProductoId, Concepto = c.DescripcionConcepto.ToUpperInvariant(), c.ProveedorId })
            .Select(g => g.First())
            .ToList();

        int idxCot = 1;
        foreach (var cot in cotizacionesRecientes)
        {
            if (!DateTime.TryParse(cot.FechaCotizacion, out var dtCot))
                continue;
            DateTime dtVence = dtCot.Date.AddDays(cot.DiasVigencia > 0 ? cot.DiasVigencia : 15);
            int diasRest = (dtVence - DateTime.Today).Days;
            if (diasRest >= -2 && diasRest <= 3)
            {
                string refFol = !string.IsNullOrWhiteSpace(cot.OrigenFolio) && cot.OrigenFolio.StartsWith("SOL-", StringComparison.OrdinalIgnoreCase)
                    ? cot.OrigenFolio
                    : (cot.ProductoId > 0 ? cot.ProductoCodigo : cot.OrigenFolio);

                AgregarEventoAutomaticoConOverride(new EventoCalendario
                {
                    Id = -(500000 + (cot.Id > 0 ? cot.Id : idxCot)),
                    Fecha = dtVence.ToString("yyyy-MM-dd"),
                    Titulo = $"Vigencia de cotización ({cot.ProveedorNombre}): {cot.DescripcionConcepto}",
                    Descripcion = $"Precio cotizado: {cot.PrecioSinIva:C2} s/IVA ({cot.PrecioConIva:C2} c/IVA) · Origen: {cot.OrigenFolio}",
                    Categoria = "Cotización por Vencer",
                    Prioridad = diasRest <= 0 ? "Alta" : "Media",
                    Completado = false,
                    ReferenciaFolio = refFol,
                    EsAutomatico = true
                });
                idxCot++;
            }
        }

        foreach (var ev in lista)
        {
            if (DateTime.TryParse(ev.Fecha, out var dtEv))
            {
                int diff = (dtEv.Date - DateTime.Now.Date).Days;
                ev.EstadoTiempoTexto = diff switch
                {
                    < 0 => $"Vencido ({Math.Abs(diff)}d)",
                    0 => "Hoy",
                    1 => "Mañana",
                    _ => $"En {diff} días"
                };
            }
            else
            {
                ev.EstadoTiempoTexto = "Pendiente";
            }
        }

        if (!string.IsNullOrWhiteSpace(filtroFecha))
        {
            lista = lista.Where(e => e.Fecha.StartsWith(filtroFecha.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return lista
            .OrderBy(e => e.Completado ? 1 : 0)
            .ThenBy(e => e.Fecha)
            .ToList();
    }

    // ==================== HISTORIAL DE COMPRAS POR PRODUCTO PARA GRÁFICAS (DISPERSIÓN Y BARRAS) ====================

    public static List<PuntoHistorialCompraProducto> ObtenerHistorialComprasProductoParaAnalisis(int productoId)
    {
        var puntos = new List<PuntoHistorialCompraProducto>();
        if (productoId <= 0) return puntos;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // 1. Puntos desde Órdenes de Compra reales
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT oc.Fecha, oc.Folio, COALESCE(pv.NombreComercial, 'Proveedor'), ocp.CantidadPedida, ocp.PrecioUnitarioSinIva
FROM OrdenCompraPartidas ocp
INNER JOIN OrdenesCompra oc ON oc.Id = ocp.OrdenCompraId
LEFT JOIN Proveedores pv ON pv.Id = oc.ProveedorId
WHERE ocp.ProductoId = $pid AND oc.Estatus <> 'Cancelada'
ORDER BY oc.Fecha ASC, oc.Id ASC";
            cmd.Parameters.AddWithValue("$pid", productoId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string fStr = r.GetString(0);
                if (!DateTime.TryParse(fStr, out var dt))
                    dt = DateTime.Now;

                puntos.Add(new PuntoHistorialCompraProducto
                {
                    ProductoId = productoId,
                    Fecha = dt,
                    FechaTexto = dt.ToString("yyyy-MM-dd"),
                    FolioDocumento = r.GetString(1),
                    ProveedorNombre = r.GetString(2),
                    Cantidad = Convert.ToDecimal(r.GetDouble(3)),
                    PrecioUnitarioSinIva = Convert.ToDecimal(r.GetDouble(4))
                });
            }
        }

        // 2. Si el producto tiene conceptos en FacturasXml cuya OC no estaba o para complementar, evitar duplicar la misma OC
        var foliosOcYaIncluidos = puntos.Select(p => p.FolioDocumento).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using (var cmdF = conn.CreateCommand())
        {
            cmdF.CommandText = @"
SELECT f.FechaTimbrado, f.SerieFolio, f.EmisorNombre, fc.Cantidad, fc.ValorUnitario, f.OrdenCompraFolio
FROM FacturaConceptos fc
INNER JOIN FacturasXml f ON f.Id = fc.FacturaXmlId
WHERE fc.ProductoIdVinculado = $pid
ORDER BY f.FechaTimbrado ASC";
            cmdF.Parameters.AddWithValue("$pid", productoId);
            using var r = cmdF.ExecuteReader();
            while (r.Read())
            {
                string ocFol = r.IsDBNull(5) ? string.Empty : r.GetString(5);
                if (!string.IsNullOrWhiteSpace(ocFol) && foliosOcYaIncluidos.Contains(ocFol))
                    continue;

                string fStr = r.GetString(0);
                if (!DateTime.TryParse(fStr, out var dt))
                    dt = DateTime.Now;

                puntos.Add(new PuntoHistorialCompraProducto
                {
                    ProductoId = productoId,
                    Fecha = dt,
                    FechaTexto = dt.ToString("yyyy-MM-dd"),
                    FolioDocumento = string.IsNullOrWhiteSpace(r.GetString(1)) ? "XML" : r.GetString(1),
                    ProveedorNombre = r.GetString(2),
                    Cantidad = Convert.ToDecimal(r.GetDouble(3)),
                    PrecioUnitarioSinIva = Convert.ToDecimal(r.GetDouble(4))
                });
            }
        }

        return puntos.OrderBy(p => p.Fecha).ToList();
    }

    public static List<MaterialRankingAnalisis> ObtenerRankingMaterialesConcurrentes()
    {
        var productos = ObtenerProductos();
        var prodDict = productos.ToDictionary(p => p.Id);
        var provDict = ObtenerProveedores().ToDictionary(p => p.Id);

        var freqSol = new Dictionary<int, (int numVeces, decimal cantTotal)>();
        var freqSal = new Dictionary<int, (int numVeces, decimal cantNeta)>();
        var ultCot = new Dictionary<int, (int provId, decimal precioSinIva, string fecha, int diasVig)>();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var cmdSol = conn.CreateCommand())
        {
            cmdSol.CommandText = @"
SELECT ProductoId, COUNT(*), SUM(CantidadSolicitada)
FROM SolicitudPartidas
WHERE ProductoId > 0
GROUP BY ProductoId";
            using var r = cmdSol.ExecuteReader();
            while (r.Read())
            {
                freqSol[r.GetInt32(0)] = (r.GetInt32(1), Convert.ToDecimal(r.GetDouble(2)));
            }
        }

        using (var cmdSal = conn.CreateCommand())
        {
            cmdSal.CommandText = @"
SELECT ProductoId, COUNT(*), SUM(MAX(0, CantidadEntregada - COALESCE(CantidadDevuelta, 0)))
FROM SalidaPartidas
WHERE ProductoId > 0
GROUP BY ProductoId";
            using var r = cmdSal.ExecuteReader();
            while (r.Read())
            {
                freqSal[r.GetInt32(0)] = (r.GetInt32(1), Convert.ToDecimal(r.GetDouble(2)));
            }
        }

        using (var cmdOc = conn.CreateCommand())
        {
            cmdOc.CommandText = @"
SELECT ocp.ProductoId, oc.ProveedorId, ocp.PrecioUnitarioSinIva, oc.Fecha
FROM OrdenCompraPartidas ocp
INNER JOIN OrdenesCompra oc ON oc.Id = ocp.OrdenCompraId
WHERE ocp.ProductoId > 0 AND ocp.PrecioUnitarioSinIva > 0 AND oc.Estatus <> 'Cancelada'
ORDER BY oc.Id ASC";
            using var r = cmdOc.ExecuteReader();
            while (r.Read())
            {
                ultCot[r.GetInt32(0)] = (r.GetInt32(1), Convert.ToDecimal(r.GetDouble(2)), r.GetString(3), 15);
            }
        }

        using (var cmdBp = conn.CreateCommand())
        {
            cmdBp.CommandText = @"
SELECT ProductoId, ProveedorId, PrecioSinIva, FechaCotizacion, DiasVigencia
FROM BancoPreciosHistorico
WHERE ProductoId > 0 AND PrecioSinIva > 0
ORDER BY Id ASC";
            using var r = cmdBp.ExecuteReader();
            while (r.Read())
            {
                ultCot[r.GetInt32(0)] = (r.GetInt32(1), Convert.ToDecimal(r.GetDouble(2)), r.GetString(3), r.GetInt32(4));
            }
        }

        var resultado = new List<MaterialRankingAnalisis>();
        foreach (var p in productos)
        {
            freqSol.TryGetValue(p.Id, out var fs);
            freqSal.TryGetValue(p.Id, out var fsal);
            ultCot.TryGetValue(p.Id, out var uc);

            if (fs.numVeces == 0 && fsal.numVeces == 0 && p.StockMinimo <= p.StockActual && uc.precioSinIva <= 0m)
                continue;

            string provNom = uc.provId > 0 && provDict.TryGetValue(uc.provId, out var prv) ? prv.NombreMostrar : "Sin proveedor reciente";
            decimal ultimoPrecioConIva = uc.precioSinIva > 0m ? Math.Round(uc.precioSinIva * 1.16m, 2) : p.UltimoCostoCompra;
            string vigTexto = "Sin cotización reciente";
            if (!string.IsNullOrWhiteSpace(uc.fecha) && DateTime.TryParse(uc.fecha, out var dtC))
            {
                int dv = uc.diasVig > 0 ? uc.diasVig : 15;
                int rest = (dtC.Date.AddDays(dv) - DateTime.Today).Days;
                vigTexto = rest >= 0 ? $"Vigente ({rest}d rest.)" : $"Vencido (hace {Math.Abs(rest)}d)";
            }

            resultado.Add(new MaterialRankingAnalisis
            {
                ProductoId = p.Id,
                ProductoCodigo = p.CodigoInterno,
                ProductoNombre = p.Nombre,
                CategoriaNombre = p.CategoriaNombre,
                UnidadAlmacen = p.UnidadAlmacen,
                FrecuenciaSolicitudes = fs.numVeces,
                CantidadTotalSolicitada = fs.cantTotal,
                FrecuenciaSalidas = fsal.numVeces,
                CantidadTotalConsumida = fsal.cantNeta,
                StockActual = p.StockActual,
                StockMinimo = p.StockMinimo,
                UltimoPrecioConIva = ultimoPrecioConIva,
                UltimoProveedorNombre = provNom,
                FechaUltimaCotizacion = uc.fecha?.Length >= 10 ? uc.fecha[..10] : (uc.fecha ?? ""),
                VigenciaPrecioTexto = vigTexto
            });
        }

        return resultado
            .OrderByDescending(x => x.StockMinimo > x.StockActual ? 1 : 0)
            .ThenByDescending(x => x.StockMinimo > x.StockActual ? (x.StockMinimo - x.StockActual) : 0m)
            .ThenByDescending(x => x.FrecuenciaSolicitudes + x.FrecuenciaSalidas)
            .ThenByDescending(x => x.CantidadTotalConsumida + x.CantidadTotalSolicitada)
            .ThenBy(x => x.StockActual)
            .ToList();
    }

    public static void ActualizarUbicacionProducto(int productoId, string ubicacion, bool sincronizarHerramientas = true)
    {
        if (productoId <= 0) return;
        string ubi = (ubicacion ?? "").Trim();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Productos SET Ubicacion=$u WHERE Id=$id";
            cmd.Parameters.AddWithValue("$u", ubi);
            cmd.Parameters.AddWithValue("$id", productoId);
            cmd.ExecuteNonQuery();
        }
        if (sincronizarHerramientas && !string.IsNullOrWhiteSpace(ubi))
        {
            using var cmdH = conn.CreateCommand();
            cmdH.CommandText = "UPDATE HerramientasActivos SET Ubicacion=$u WHERE ProductoId=$id AND (Ubicacion IS NULL OR TRIM(Ubicacion)='' OR TRIM(Ubicacion)='Almacén General')";
            cmdH.Parameters.AddWithValue("$u", ubi);
            cmdH.Parameters.AddWithValue("$id", productoId);
            cmdH.ExecuteNonQuery();
        }
    }

    public static void ActualizarUbicacionHerramientaIndividual(int herramientaId, string ubicacion)
    {
        if (herramientaId <= 0) return;
        string ubi = (ubicacion ?? "").Trim();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE HerramientasActivos SET Ubicacion=$u WHERE Id=$id";
        cmd.Parameters.AddWithValue("$u", ubi);
        cmd.Parameters.AddWithValue("$id", herramientaId);
        cmd.ExecuteNonQuery();
    }

    public static string ObtenerRolOperativoActivo()
    {
        try
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave='RolOperativoActivo' LIMIT 1";
            var val = cmd.ExecuteScalar()?.ToString();
            if (string.Equals(val, "Almacén", StringComparison.OrdinalIgnoreCase))
                return "Almacén";
        }
        catch
        {
            // Ignorar si aún no existe
        }
        return "Compras";
    }

    public static void GuardarRolOperativoActivo(string rol)
    {
        string r = string.Equals(rol, "Almacén", StringComparison.OrdinalIgnoreCase) ? "Almacén" : "Compras";
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ('RolOperativoActivo', $v)
ON CONFLICT(Clave) DO UPDATE SET Valor=$v;";
        cmd.Parameters.AddWithValue("$v", r);
        cmd.ExecuteNonQuery();
    }

    public static void RegistrarCheckpoint(
        string etapa,
        string checkpointCodigo,
        string folioReferencia,
        string estatusAnterior,
        string estatusNuevo,
        string rolOrigen,
        string rolesDestino,
        string mensaje,
        string usuarioDestino = "")
    {
        try
        {
            var usr = ObtenerUsuarioActivo();
            string nomUsr = usr?.NombreCompleto ?? "Compras";
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO NotificacionesCheckpoint
(FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, UsuarioOrigen, UsuarioDestino, Mensaje, Leida)
VALUES ($fh, $et, $cp, $fol, $ea, $en, $ro, $rd, $uo, $ud, $msg, 0);";
            cmd.Parameters.AddWithValue("$fh", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("$et", etapa ?? string.Empty);
            cmd.Parameters.AddWithValue("$cp", checkpointCodigo ?? string.Empty);
            cmd.Parameters.AddWithValue("$fol", folioReferencia ?? string.Empty);
            cmd.Parameters.AddWithValue("$ea", estatusAnterior ?? string.Empty);
            cmd.Parameters.AddWithValue("$en", estatusNuevo ?? string.Empty);
            cmd.Parameters.AddWithValue("$ro", string.IsNullOrWhiteSpace(rolOrigen) ? "Compras" : rolOrigen);
            cmd.Parameters.AddWithValue("$rd", string.IsNullOrWhiteSpace(rolesDestino) ? "Almacén" : rolesDestino);
            cmd.Parameters.AddWithValue("$uo", nomUsr);
            cmd.Parameters.AddWithValue("$ud", usuarioDestino ?? string.Empty);
            cmd.Parameters.AddWithValue("$msg", mensaje ?? string.Empty);
            cmd.ExecuteNonQuery();

            RegistrarBitacoraUsuario(etapa ?? "Sistema", $"Checkpoint {checkpointCodigo}", folioReferencia ?? string.Empty, mensaje ?? string.Empty);
        }
        catch
        {
            // No interrumpir flujo operativo por registro de notificación
        }
    }

    public static void EnviarNotificacionEntreUsuarios(
        string usuarioDestino,
        string rolDestino,
        string folioReferencia,
        string asunto,
        string mensaje)
    {
        var usrOrigen = ObtenerUsuarioActivo();
        string rolOrig = ObtenerRolOperativoActivo();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO NotificacionesCheckpoint
(FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, UsuarioOrigen, UsuarioDestino, Mensaje, Leida)
VALUES ($fh, $et, 'MSG-USR', $fol, 'Mensaje Interno', $asunto, $ro, $rd, $uo, $ud, $msg, 0);";
        cmd.Parameters.AddWithValue("$fh", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        cmd.Parameters.AddWithValue("$et", string.IsNullOrWhiteSpace(asunto) ? "Aviso entre Usuarios" : asunto.Trim());
        cmd.Parameters.AddWithValue("$fol", string.IsNullOrWhiteSpace(folioReferencia) ? "GENERAL" : folioReferencia.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$asunto", string.IsNullOrWhiteSpace(usuarioDestino) ? $"Para {rolDestino}" : $"Para @{usuarioDestino}");
        cmd.Parameters.AddWithValue("$ro", rolOrig);
        cmd.Parameters.AddWithValue("$rd", string.IsNullOrWhiteSpace(rolDestino) ? "Todos" : rolDestino.Trim());
        cmd.Parameters.AddWithValue("$uo", usrOrigen?.NombreCompleto ?? "Usuario");
        cmd.Parameters.AddWithValue("$ud", usuarioDestino?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$msg", mensaje?.Trim() ?? string.Empty);
        cmd.ExecuteNonQuery();

        RegistrarBitacoraUsuario("Notificaciones", "Envío de Aviso a Usuario", folioReferencia, $"Para @{usuarioDestino} ({rolDestino}): {mensaje}");
    }

    public static List<NotificacionCheckpoint> ObtenerNotificacionesCheckpoint(string? rolFiltro = null, int limite = 80, string? usuarioFiltro = null)
    {
        var lista = new List<NotificacionCheckpoint>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(usuarioFiltro))
        {
            cmd.CommandText = @"
SELECT Id, FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, Mensaje, Leida,
       COALESCE(UsuarioOrigen, ''), COALESCE(UsuarioDestino, '')
FROM NotificacionesCheckpoint
WHERE UsuarioDestino LIKE $usr OR UsuarioOrigen LIKE $usr OR RolesDestino LIKE $rol
ORDER BY Id DESC
LIMIT $lim;";
            cmd.Parameters.AddWithValue("$usr", $"%{usuarioFiltro.Trim()}%");
            cmd.Parameters.AddWithValue("$rol", $"%{(rolFiltro ?? "Todos").Trim()}%");
        }
        else if (string.IsNullOrWhiteSpace(rolFiltro) || string.Equals(rolFiltro, "Todos", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = @"
SELECT Id, FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, Mensaje, Leida,
       COALESCE(UsuarioOrigen, ''), COALESCE(UsuarioDestino, '')
FROM NotificacionesCheckpoint
ORDER BY Id DESC
LIMIT $lim;";
        }
        else
        {
            var usrActivo = ObtenerUsuarioActivo();
            cmd.CommandText = @"
SELECT Id, FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, Mensaje, Leida,
       COALESCE(UsuarioOrigen, ''), COALESCE(UsuarioDestino, '')
FROM NotificacionesCheckpoint
WHERE RolesDestino LIKE $rol OR RolOrigen LIKE $rol OR RolesDestino LIKE '%Todos%'
   OR ($unom <> '' AND (UsuarioDestino LIKE $unom OR UsuarioOrigen LIKE $unom))
ORDER BY Id DESC
LIMIT $lim;";
            cmd.Parameters.AddWithValue("$rol", $"%{rolFiltro.Trim()}%");
            cmd.Parameters.AddWithValue("$unom", usrActivo != null ? $"%{usrActivo.NombreCompleto}%" : "");
        }
        cmd.Parameters.AddWithValue("$lim", limite);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lista.Add(new NotificacionCheckpoint
            {
                Id = r.GetInt32(0),
                FechaHora = r.GetString(1),
                Etapa = r.GetString(2),
                CheckpointCodigo = r.GetString(3),
                FolioReferencia = r.GetString(4),
                EstatusAnterior = r.GetString(5),
                EstatusNuevo = r.GetString(6),
                RolOrigen = r.GetString(7),
                RolesDestino = r.GetString(8),
                Mensaje = r.GetString(9),
                Leida = r.GetInt32(10) != 0,
                UsuarioOrigen = r.GetString(11),
                UsuarioDestino = r.GetString(12)
            });
        }
        return lista;
    }

    public static int ContarNotificacionesNoLeidas(string rolDestino)
    {
        var usrActivo = ObtenerUsuarioActivo();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(1) FROM NotificacionesCheckpoint
WHERE Leida=0 AND (RolesDestino LIKE $rol OR RolesDestino LIKE '%Todos%' OR ($unom <> '' AND UsuarioDestino LIKE $unom))";
        cmd.Parameters.AddWithValue("$rol", $"%{rolDestino.Trim()}%");
        cmd.Parameters.AddWithValue("$unom", usrActivo != null ? $"%{usrActivo.NombreCompleto}%" : "");
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public static void MarcarNotificacionesLeidas(string? rolDestino = null)
    {
        var usrActivo = ObtenerUsuarioActivo();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(rolDestino))
        {
            cmd.CommandText = "UPDATE NotificacionesCheckpoint SET Leida=1 WHERE Leida=0";
        }
        else
        {
            cmd.CommandText = @"
UPDATE NotificacionesCheckpoint
SET Leida=1
WHERE Leida=0 AND (RolesDestino LIKE $rol OR RolesDestino LIKE '%Todos%' OR ($unom <> '' AND UsuarioDestino LIKE $unom))";
            cmd.Parameters.AddWithValue("$rol", $"%{rolDestino.Trim()}%");
            cmd.Parameters.AddWithValue("$unom", usrActivo != null ? $"%{usrActivo.NombreCompleto}%" : "");
        }
        cmd.ExecuteNonQuery();
    }

    // ==================== FASE 1.1: USUARIOS DEL SISTEMA Y BITÁCORA NOMINAL ====================

    private static void SeedUsuariosSistemaSiVacio(SqliteConnection conn)
    {
        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT COUNT(1) FROM UsuariosSistema;";
        int count = Convert.ToInt32(chk.ExecuteScalar() ?? 0);
        if (count > 0) return;

        string fechaHoy = DateTime.Now.ToString("yyyy-MM-dd");
        string pinDefault = CalcularHashPinAdministrador("1234");
        var iniciales = new (string user, string nombre, string puesto, string rol, string correoErp)[]
        {
            ("oscar", "Oscar (Jefe de Compras)", "Responsable de Compras y Abastecimiento", "Responsable de Compras", "autoriza@lebaref.test"),
            ("auxiliar", "Auxiliar de Compras", "Cotizaciones y Seguimiento de Compras", "Auxiliar de Compras", "compras@lebaref.test"),
            ("almacen", "Auxiliar de Almacén", "Recepción y Despacho de Bodega", "Auxiliar de Almacén", "almacen@lebaref.test"),
            ("operativo", "Solicitante / Residente de Obra", "Técnicos e Instaladores de Proyecto", "Solicitante", ""),
            ("direccion", "Dirección General", "Autorización Vo.Bo. de Proyectos", "Dirección", ""),
            ("finanzas", "Finanzas y Tesorería", "Programación de Pagos y Facturación", "Finanzas", "")
        };

        foreach (var u in iniciales)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = @"
INSERT INTO UsuariosSistema (Username, NombreCompleto, Puesto, Rol, PinHash, Activo, FechaAlta, CorreoErp)
VALUES ($usr, $nom, $pue, $rol, $pin, 1, $fec, $erp);";
            ins.Parameters.AddWithValue("$usr", u.user);
            ins.Parameters.AddWithValue("$nom", u.nombre);
            ins.Parameters.AddWithValue("$pue", u.puesto);
            ins.Parameters.AddWithValue("$rol", u.rol);
            ins.Parameters.AddWithValue("$pin", pinDefault);
            ins.Parameters.AddWithValue("$fec", fechaHoy);
            ins.Parameters.AddWithValue("$erp", u.correoErp);
            ins.ExecuteNonQuery();
        }
    }

    private static void MigrarUsuariosYRolesBase(SqliteConnection conn)
    {
        string pinDefault = CalcularHashPinAdministrador("1234");
        string fechaHoy = DateTime.Now.ToString("yyyy-MM-dd");
        var actuales = new List<(int id, string usr, string puesto, string rol, string pin, string erp)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Username, Puesto, Rol, COALESCE(PinHash, ''), COALESCE(CorreoErp, '') FROM UsuariosSistema;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                actuales.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
            }
        }

        bool existeAuxiliarCompras = false;
        foreach (var u in actuales)
        {
            string rolNorm = UsuarioSistema.NormalizarTipoUsuario(u.rol, u.usr, u.puesto);
            if (string.Equals(rolNorm, "Auxiliar de Compras", StringComparison.OrdinalIgnoreCase))
                existeAuxiliarCompras = true;

            string erpSug = u.erp;
            if (string.IsNullOrWhiteSpace(erpSug))
            {
                if (u.usr.Equals("oscar", StringComparison.OrdinalIgnoreCase)) erpSug = "autoriza@lebaref.test";
                else if (u.usr.Equals("almacen", StringComparison.OrdinalIgnoreCase)) erpSug = "almacen@lebaref.test";
                else if (u.usr.Equals("auxiliar", StringComparison.OrdinalIgnoreCase)) erpSug = "compras@lebaref.test";
            }

            string pinFinal = string.IsNullOrWhiteSpace(u.pin) ? pinDefault : u.pin;
            if (!string.Equals(u.rol, rolNorm, StringComparison.Ordinal) ||
                !string.Equals(u.pin, pinFinal, StringComparison.Ordinal) ||
                !string.Equals(u.erp, erpSug, StringComparison.Ordinal))
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE UsuariosSistema SET Rol=$rol, PinHash=$pin, CorreoErp=$erp WHERE Id=$id;";
                upd.Parameters.AddWithValue("$rol", rolNorm);
                upd.Parameters.AddWithValue("$pin", pinFinal);
                upd.Parameters.AddWithValue("$erp", erpSug);
                upd.Parameters.AddWithValue("$id", u.id);
                upd.ExecuteNonQuery();
            }
        }

        if (!existeAuxiliarCompras && !actuales.Any(x => x.usr.Equals("auxiliar", StringComparison.OrdinalIgnoreCase)))
        {
            using var insAux = conn.CreateCommand();
            insAux.CommandText = @"
INSERT INTO UsuariosSistema (Username, NombreCompleto, Puesto, Rol, PinHash, Activo, FechaAlta, CorreoErp)
VALUES ('auxiliar', 'Auxiliar de Compras', 'Cotizaciones y Seguimiento de Compras', 'Auxiliar de Compras', $pin, 1, $fec, 'compras@lebaref.test');";
            insAux.Parameters.AddWithValue("$pin", pinDefault);
            insAux.Parameters.AddWithValue("$fec", fechaHoy);
            insAux.ExecuteNonQuery();
        }
    }

    public static List<UsuarioSistema> ObtenerUsuariosSistema(bool soloActivos = false)
    {
        var list = new List<UsuarioSistema>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = soloActivos
            ? "SELECT Id, Username, NombreCompleto, Puesto, Rol, Activo, FechaAlta, COALESCE(CorreoErp, '') FROM UsuariosSistema WHERE Activo=1 ORDER BY Id ASC"
            : "SELECT Id, Username, NombreCompleto, Puesto, Rol, Activo, FechaAlta, COALESCE(CorreoErp, '') FROM UsuariosSistema ORDER BY Id ASC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string usr = r.GetString(1);
            string puesto = r.GetString(3);
            string rolRaw = r.GetString(4);
            list.Add(new UsuarioSistema
            {
                Id = r.GetInt32(0),
                Username = usr,
                NombreCompleto = r.GetString(2),
                Puesto = puesto,
                Rol = UsuarioSistema.NormalizarTipoUsuario(rolRaw, usr, puesto),
                Activo = r.GetInt32(5) != 0,
                FechaAlta = r.GetString(6),
                CorreoErp = r.GetString(7)
            });
        }
        return list;
    }

    public static UsuarioSistema ObtenerUsuarioActivo()
    {
        var usuarios = ObtenerUsuariosSistema(soloActivos: false);
        int idGuardado = 0;
        try
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave='UsuarioActivoId' LIMIT 1";
            int.TryParse(cmd.ExecuteScalar()?.ToString(), out idGuardado);
        }
        catch
        {
            // Ignorar
        }

        var encontrado = usuarios.FirstOrDefault(u => u.Id == idGuardado && u.Activo)
                         ?? usuarios.FirstOrDefault(u => u.Activo)
                         ?? new UsuarioSistema { Id = 1, Username = "oscar", NombreCompleto = "Oscar (Jefe de Compras)", Rol = "Responsable de Compras", Activo = true };
        return encontrado;
    }

    public static void GuardarUsuarioActivo(int usuarioId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ('UsuarioActivoId', $v)
ON CONFLICT(Clave) DO UPDATE SET Valor=$v;";
        cmd.Parameters.AddWithValue("$v", usuarioId.ToString());
        cmd.ExecuteNonQuery();
    }

    public static int GuardarUsuarioSistema(UsuarioSistema u, string? nuevoPinPlano = null)
    {
        string usrClean = (u.Username ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(usrClean))
            usrClean = $"usr_{DateTime.Now:HHmmss}";

        string rolLimpio = UsuarioSistema.NormalizarTipoUsuario(u.Rol, usrClean, u.Puesto);
        string correoErp = (u.CorreoErp ?? string.Empty).Trim().ToLowerInvariant();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        if (u.Id <= 0)
        {
            string pinHash = CalcularHashPinAdministrador(string.IsNullOrWhiteSpace(nuevoPinPlano) ? "1234" : nuevoPinPlano.Trim());
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO UsuariosSistema (Username, NombreCompleto, Puesto, Rol, PinHash, Activo, FechaAlta, CorreoErp)
VALUES ($usr, $nom, $pue, $rol, $pin, $act, $fec, $erp);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$usr", usrClean);
            cmd.Parameters.AddWithValue("$nom", u.NombreCompleto.Trim());
            cmd.Parameters.AddWithValue("$pue", u.Puesto?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$rol", rolLimpio);
            cmd.Parameters.AddWithValue("$pin", pinHash);
            cmd.Parameters.AddWithValue("$act", u.Activo ? 1 : 0);
            cmd.Parameters.AddWithValue("$fec", DateTime.Now.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$erp", correoErp);
            int nuevoId = Convert.ToInt32(cmd.ExecuteScalar());
            RegistrarBitacoraUsuario("Usuarios", "Alta de Usuario", $"@{usrClean}", $"Se creó el usuario '{u.NombreCompleto}' con tipo '{rolLimpio}'");
            return nuevoId;
        }
        else
        {
            using var cmd = conn.CreateCommand();
            if (!string.IsNullOrWhiteSpace(nuevoPinPlano))
            {
                cmd.CommandText = @"
UPDATE UsuariosSistema
SET Username=$usr, NombreCompleto=$nom, Puesto=$pue, Rol=$rol, Activo=$act, PinHash=$pin, CorreoErp=$erp
WHERE Id=$id;";
                cmd.Parameters.AddWithValue("$pin", CalcularHashPinAdministrador(nuevoPinPlano.Trim()));
            }
            else
            {
                cmd.CommandText = @"
UPDATE UsuariosSistema
SET Username=$usr, NombreCompleto=$nom, Puesto=$pue, Rol=$rol, Activo=$act, CorreoErp=$erp
WHERE Id=$id;";
            }
            cmd.Parameters.AddWithValue("$usr", usrClean);
            cmd.Parameters.AddWithValue("$nom", u.NombreCompleto.Trim());
            cmd.Parameters.AddWithValue("$pue", u.Puesto?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$rol", rolLimpio);
            cmd.Parameters.AddWithValue("$act", u.Activo ? 1 : 0);
            cmd.Parameters.AddWithValue("$erp", correoErp);
            cmd.Parameters.AddWithValue("$id", u.Id);
            cmd.ExecuteNonQuery();
            RegistrarBitacoraUsuario("Usuarios", "Edición de Usuario", $"@{usrClean}", $"Se actualizó el usuario '{u.NombreCompleto}' (Tipo: {rolLimpio}, Activo: {u.Activo})");
            return u.Id;
        }
    }

    public static (bool Exito, UsuarioSistema? Usuario, string MensajeError) AutenticarUsuarioLocal(string usuarioOCorreo, string passwordPlano)
    {
        string identificador = (usuarioOCorreo ?? string.Empty).Trim().ToLowerInvariant();
        if (identificador.StartsWith("@", StringComparison.Ordinal))
            identificador = identificador[1..];

        if (string.IsNullOrWhiteSpace(identificador))
            return (false, null, "Escriba su nombre de usuario o correo.");

        if (string.IsNullOrWhiteSpace(passwordPlano))
            return (false, null, "Escriba su contraseña.");

        string hashEsperado = CalcularHashPinAdministrador(passwordPlano.Trim());

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Username, NombreCompleto, Puesto, Rol, COALESCE(PinHash, ''), Activo, FechaAlta, COALESCE(CorreoErp, '')
FROM UsuariosSistema
WHERE LOWER(Username) = $id OR (CorreoErp <> '' AND LOWER(CorreoErp) = $id)
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", identificador);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return (false, null, "No se encontró ninguna cuenta registrada con ese usuario o correo.");

        int uid = r.GetInt32(0);
        string usr = r.GetString(1);
        string nom = r.GetString(2);
        string puesto = r.GetString(3);
        string rolRaw = r.GetString(4);
        string pinHashDb = r.GetString(5);
        bool activo = r.GetInt32(6) != 0;
        string fechaAlta = r.GetString(7);
        string correoErp = r.GetString(8);

        if (!activo)
            return (false, null, "Esta cuenta de usuario se encuentra inactiva. Solicite apoyo al Responsable de Compras.");

        bool coincide = string.Equals(pinHashDb, hashEsperado, StringComparison.Ordinal) ||
                        (!pinHashDb.StartsWith("SHA256:", StringComparison.Ordinal) && string.Equals(pinHashDb, passwordPlano.Trim(), StringComparison.Ordinal));

        if (!coincide)
            return (false, null, "Contraseña incorrecta. Verifique sus datos e inténtelo de nuevo.");

        var usuario = new UsuarioSistema
        {
            Id = uid,
            Username = usr,
            NombreCompleto = nom,
            Puesto = puesto,
            Rol = UsuarioSistema.NormalizarTipoUsuario(rolRaw, usr, puesto),
            Activo = true,
            FechaAlta = fechaAlta,
            CorreoErp = correoErp
        };
        return (true, usuario, string.Empty);
    }

    public static void CambiarPasswordUsuario(int usuarioId, string nuevoPasswordPlano)
    {
        if (usuarioId <= 0 || string.IsNullOrWhiteSpace(nuevoPasswordPlano))
            return;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE UsuariosSistema SET PinHash=$pin WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$pin", CalcularHashPinAdministrador(nuevoPasswordPlano.Trim()));
        cmd.Parameters.AddWithValue("$id", usuarioId);
        cmd.ExecuteNonQuery();
    }

    public static UsuarioSistema SincronizarUsuarioDesdeErpNext(
        string identidadErp,
        string nombreCompleto,
        string tipoUsuarioLebaref,
        string puesto,
        string passwordPlano)
    {
        string correoOLogin = (identidadErp ?? string.Empty).Trim().ToLowerInvariant();
        string baseUsername = correoOLogin.Contains('@') ? correoOLogin.Split('@')[0] : correoOLogin;
        baseUsername = System.Text.RegularExpressions.Regex.Replace(baseUsername, @"[^a-z0-9._-]", "_");
        if (string.IsNullOrWhiteSpace(baseUsername)) baseUsername = "erp_user";

        var todos = ObtenerUsuariosSistema(soloActivos: false);
        var existente = todos.FirstOrDefault(u =>
            (!string.IsNullOrWhiteSpace(u.CorreoErp) && string.Equals(u.CorreoErp, correoOLogin, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(u.Username, correoOLogin, StringComparison.OrdinalIgnoreCase) ||
            (correoOLogin == "administrator" && u.Username.Equals("oscar", StringComparison.OrdinalIgnoreCase)));

        if (existente != null)
        {
            existente.CorreoErp = correoOLogin;
            if (!string.IsNullOrWhiteSpace(nombreCompleto) && !existente.Username.Equals("oscar", StringComparison.OrdinalIgnoreCase))
                existente.NombreCompleto = nombreCompleto.Trim();
            existente.Rol = tipoUsuarioLebaref;
            existente.Activo = true;
            GuardarUsuarioSistema(existente, nuevoPinPlano: passwordPlano);
            return ObtenerUsuariosSistema(soloActivos: false).First(u => u.Id == existente.Id);
        }

        string usrUnico = baseUsername;
        int suf = 2;
        while (todos.Any(u => string.Equals(u.Username, usrUnico, StringComparison.OrdinalIgnoreCase)))
        {
            usrUnico = $"{baseUsername}{suf++}";
        }

        var nuevo = new UsuarioSistema
        {
            Username = usrUnico,
            NombreCompleto = string.IsNullOrWhiteSpace(nombreCompleto) ? correoOLogin : nombreCompleto.Trim(),
            Puesto = string.IsNullOrWhiteSpace(puesto) ? tipoUsuarioLebaref : puesto.Trim(),
            Rol = tipoUsuarioLebaref,
            CorreoErp = correoOLogin,
            Activo = true
        };
        int idNuevo = GuardarUsuarioSistema(nuevo, nuevoPinPlano: passwordPlano);
        return ObtenerUsuariosSistema(soloActivos: false).First(u => u.Id == idNuevo);
    }

    public static string ObtenerConfiguracionTexto(string clave, string valorPorDefecto = "")
    {
        try
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave=$k LIMIT 1;";
            cmd.Parameters.AddWithValue("$k", clave);
            var val = cmd.ExecuteScalar()?.ToString();
            return val ?? valorPorDefecto;
        }
        catch
        {
            return valorPorDefecto;
        }
    }

    public static void GuardarConfiguracionTexto(string clave, string valor)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ($k, $v)
ON CONFLICT(Clave) DO UPDATE SET Valor=$v;";
        cmd.Parameters.AddWithValue("$k", clave);
        cmd.Parameters.AddWithValue("$v", valor ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    public static void RegistrarBitacoraUsuario(string modulo, string accion, string? folioReferencia, string? detalle)
    {
        try
        {
            var usr = ObtenerUsuarioActivo();
            string rolAct = ObtenerRolOperativoActivo();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO BitacoraUsuario (FechaHora, UsuarioId, UsuarioNombre, Rol, Modulo, Accion, FolioReferencia, Detalle)
VALUES ($fh, $uid, $unom, $rol, $mod, $acc, $fol, $det);";
            cmd.Parameters.AddWithValue("$fh", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$uid", usr.Id);
            cmd.Parameters.AddWithValue("$unom", usr.NombreCompleto);
            cmd.Parameters.AddWithValue("$rol", rolAct);
            cmd.Parameters.AddWithValue("$mod", modulo ?? "General");
            cmd.Parameters.AddWithValue("$acc", accion ?? string.Empty);
            cmd.Parameters.AddWithValue("$fol", folioReferencia ?? string.Empty);
            cmd.Parameters.AddWithValue("$det", detalle ?? string.Empty);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // No bloquear por bitácora
        }
    }

    public static List<RegistroBitacoraUsuario> ObtenerBitacoraUsuarios(int? usuarioIdFiltro = null, string? textoFiltro = null, int limite = 250)
    {
        var lista = new List<RegistroBitacoraUsuario>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, FechaHora, UsuarioId, UsuarioNombre, Rol, Modulo, Accion, FolioReferencia, Detalle
FROM BitacoraUsuario
WHERE ($uid <= 0 OR UsuarioId = $uid)
  AND ($q = '' OR FolioReferencia LIKE $ql OR Accion LIKE $ql OR Detalle LIKE $ql OR Modulo LIKE $ql OR UsuarioNombre LIKE $ql)
ORDER BY Id DESC
LIMIT $lim;";
        int uid = usuarioIdFiltro ?? 0;
        string q = (textoFiltro ?? string.Empty).Trim();
        cmd.Parameters.AddWithValue("$uid", uid);
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$ql", $"%{q}%");
        cmd.Parameters.AddWithValue("$lim", limite);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lista.Add(new RegistroBitacoraUsuario
            {
                Id = r.GetInt32(0),
                FechaHora = r.GetString(1),
                UsuarioId = r.GetInt32(2),
                UsuarioNombre = r.GetString(3),
                Rol = r.GetString(4),
                Modulo = r.GetString(5),
                Accion = r.GetString(6),
                FolioReferencia = r.GetString(7),
                Detalle = r.GetString(8)
            });
        }
        return lista;
    }

    // ==================== FASE 1.2: SOLICITUDES POR FAMILIA GENÉRICA Y SUSTITUCIÓN DE MARCA ====================

    public static List<ResumenFamiliaCatalogo> ObtenerResumenFamiliasCatalogo()
    {
        var productos = ObtenerProductos();
        var grupos = productos
            .GroupBy(p => !string.IsNullOrWhiteSpace(p.Familia) ? p.Familia.Trim() : ExtraerFamiliaSugerida(p.Nombre), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key)
            .ToList();

        var resultado = new List<ResumenFamiliaCatalogo>();
        foreach (var g in grupos)
        {
            var miembros = g
                .OrderByDescending(p => p.StockActual)
                .ThenBy(p => p.UltimoCostoCompra > 0 ? p.UltimoCostoCompra : decimal.MaxValue)
                .ToList();

            var conPrecio = miembros.Where(p => p.UltimoCostoCompra > 0m).ToList();
            decimal mejorCosto = conPrecio.Count > 0 ? conPrecio.Min(p => p.UltimoCostoCompra) : 0m;
            var marcas = miembros
                .Select(p => !string.IsNullOrWhiteSpace(p.Marca) ? p.Marca.Trim() : p.Nombre)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            var sugerido = conPrecio.OrderBy(p => p.UltimoCostoCompra).FirstOrDefault() ?? miembros.First();

            resultado.Add(new ResumenFamiliaCatalogo
            {
                Familia = g.Key,
                CategoriaNombre = sugerido.CategoriaNombre,
                CantidadMarcasOpciones = miembros.Count,
                StockTotalFamilia = miembros.Sum(p => p.StockActual),
                UnidadComun = sugerido.UnidadCompra,
                MejorCostoConIva = mejorCosto,
                MarcasDisponiblesResumen = string.Join(", ", marcas),
                ProductoSugerido = sugerido,
                ProductosMiembros = miembros
            });
        }
        return resultado;
    }

    public static void CambiarProductoEnPartidaOrdenCompra(int ordenCompraPartidaId, Producto nuevoProducto, string motivo = "Cambio de marca/sustituto en recepción")
    {
        if (ordenCompraPartidaId <= 0 || nuevoProducto == null || nuevoProducto.Id <= 0) return;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE OrdenCompraPartidas
SET ProductoId = $pid,
    DescripcionLibre = $nom,
    UnidadLibre = $uni
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$pid", nuevoProducto.Id);
        cmd.Parameters.AddWithValue("$nom", nuevoProducto.Nombre);
        cmd.Parameters.AddWithValue("$uni", nuevoProducto.UnidadCompra);
        cmd.Parameters.AddWithValue("$id", ordenCompraPartidaId);
        cmd.ExecuteNonQuery();

        RegistrarBitacoraUsuario("Órdenes de Compra", "Sustitución de Marca / Producto", nuevoProducto.CodigoInterno, $"{motivo} -> [{nuevoProducto.CodigoInterno}] {nuevoProducto.Nombre}");
    }

    // ==================== FASE 1.3: RECOLECCIÓN CON PROVEEDOR Y ENTREGA DIRECTA A OBRA (ENT + SAL ESPEJO) ====================

    public static (EntradaAlmacen entrada, SalidaAlmacen? salida) RegistrarRecepcionDirectaAObra(
        OrdenCompra oc,
        List<(OrdenCompraPartida partida, decimal cantidadRecibirAhora)> recepcion,
        string personaRecogio,
        string folioRemision,
        string notas,
        bool esServicioNoInventariable = false,
        string? lugarEntregaPersonalizado = null,
        bool notificarCorreo = true)
    {
        // Asegurar SKUs si había conceptos abiertos (excepto cuando es un servicio puro no inventariable)
        foreach (var (partida, _) in recepcion.Where(x => x.cantidadRecibirAhora > 0))
        {
            if (partida.ProductoId <= 0)
            {
                string nombreReal = !string.IsNullOrWhiteSpace(partida.DescripcionLibre) ? partida.DescripcionLibre.Trim() : partida.ProductoNombre.Trim();
                var prodExistente = ObtenerProductos().FirstOrDefault(p => string.Equals(p.Nombre.Trim(), nombreReal, StringComparison.OrdinalIgnoreCase));
                if (prodExistente != null)
                {
                    partida.ProductoId = prodExistente.Id;
                    partida.ProductoCodigo = prodExistente.CodigoInterno;
                    partida.ProductoNombre = prodExistente.Nombre;
                }
                else
                {
                    var info = ClasificarYExtraerEspecificacionesProducto(nombreReal, notas);
                    var nuevoProd = new Producto
                    {
                        Nombre = nombreReal,
                        Descripcion = esServicioNoInventariable
                            ? $"Servicio / Flete registrado en {oc.Folio}"
                            : $"Material recolectado directo a obra en {oc.Folio}",
                        CategoriaId = info.CategoriaId,
                        Familia = esServicioNoInventariable ? "Servicios y Fletes" : ExtraerFamiliaSugerida(nombreReal),
                        Marca = info.Marca,
                        Modelo = info.Modelo,
                        Medidas = info.Medidas,
                        UnidadCompra = string.IsNullOrWhiteSpace(partida.UnidadCompra) ? "Pieza" : partida.UnidadCompra,
                        UnidadAlmacen = string.IsNullOrWhiteSpace(partida.UnidadAlmacen) ? "Pieza" : partida.UnidadAlmacen,
                        FactorConversion = Math.Max(1m, partida.FactorConversion),
                        StockActual = 0m,
                        StockMinimo = 0m,
                        UltimoCostoCompra = partida.PrecioUnitarioConIva,
                        Ubicacion = esServicioNoInventariable ? "SERVICIO" : "DIRECTO-OBRA"
                    };
                    int nuevoId = GuardarProducto(nuevoProd);
                    partida.ProductoId = nuevoId;
                    partida.ProductoCodigo = nuevoProd.CodigoInterno;
                    partida.ProductoNombre = nuevoProd.Nombre;
                }

                using var connUp = new SqliteConnection(ConnectionString);
                connUp.Open();
                using var upOcp = connUp.CreateCommand();
                upOcp.CommandText = "UPDATE OrdenCompraPartidas SET ProductoId = $pid WHERE Id = $id;";
                upOcp.Parameters.AddWithValue("$pid", partida.ProductoId);
                upOcp.Parameters.AddWithValue("$id", partida.Id);
                upOcp.ExecuteNonQuery();
            }
        }

        string folioEnt = GenerarSiguienteFolio("ENT", "EntradasAlmacen");
        string folioSal = GenerarSiguienteFolio("SAL", "SalidasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string responsableFinal = string.IsNullOrWhiteSpace(personaRecogio)
            ? (!string.IsNullOrWhiteSpace(oc.EncargadoNombre) ? oc.EncargadoNombre : "Personal de Obra")
            : personaRecogio.Trim();

        string tipoEnt = esServicioNoInventariable ? "Servicio / No Inventariable" : "Recolección Directa a Obra";
        string notasFinal = $"[{tipoEnt}] Recogió/Recibió: {responsableFinal}" +
                            (!string.IsNullOrWhiteSpace(folioRemision) ? $" | Remisión/Factura: {folioRemision.Trim()}" : "") +
                            (!string.IsNullOrWhiteSpace(notas) ? $" | {notas.Trim()}" : "");

        var departamentos = ObtenerDepartamentos();
        var encargados = ObtenerEncargados();
        int depId = departamentos.FirstOrDefault(d => string.Equals(d.Nombre, oc.DepartamentoNombre, StringComparison.OrdinalIgnoreCase))?.Id
                    ?? departamentos.FirstOrDefault()?.Id ?? 1;
        int encId = encargados.FirstOrDefault(e => string.Equals(e.Nombre, oc.EncargadoNombre, StringComparison.OrdinalIgnoreCase))?.Id
                    ?? encargados.FirstOrDefault()?.Id ?? 1;
        int proyId = oc.ProyectoId > 0 ? oc.ProyectoId : ObtenerProyectoStockGeneralId();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmdEnt = conn.CreateCommand();
        cmdEnt.Transaction = tx;
        cmdEnt.CommandText = @"
INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas)
VALUES ($fol, $fec, $ocid, $ocfol, $prov, $proy, $tipo, $rem, $not);
SELECT last_insert_rowid();";
        cmdEnt.Parameters.AddWithValue("$fol", folioEnt);
        cmdEnt.Parameters.AddWithValue("$fec", fecha);
        cmdEnt.Parameters.AddWithValue("$ocid", oc.Id);
        cmdEnt.Parameters.AddWithValue("$ocfol", oc.Folio);
        cmdEnt.Parameters.AddWithValue("$prov", oc.ProveedorNombre);
        cmdEnt.Parameters.AddWithValue("$proy", oc.ProyectoNombre);
        cmdEnt.Parameters.AddWithValue("$tipo", tipoEnt);
        cmdEnt.Parameters.AddWithValue("$rem", folioRemision?.Trim() ?? string.Empty);
        cmdEnt.Parameters.AddWithValue("$not", notasFinal);
        int entId = Convert.ToInt32(cmdEnt.ExecuteScalar());

        decimal costoTotalSalida = 0m;
        var partidasSalidaInsertar = new List<(int prodId, decimal cantAlmacen, string unidadAlmacen, decimal costoUnitAlmacen)>();

        foreach (var (partida, cantRecibir) in recepcion.Where(x => x.cantidadRecibirAhora > 0))
        {
            decimal factor = Math.Max(1m, partida.FactorConversion);
            decimal cantAlmacen = Math.Round(cantRecibir * factor, 2);
            decimal costoUnitAlmacen = factor > 0 ? Math.Round(partida.PrecioUnitarioConIva / factor, 2) : partida.PrecioUnitarioConIva;
            costoTotalSalida += Math.Round(cantAlmacen * costoUnitAlmacen, 2);
            partidasSalidaInsertar.Add((partida.ProductoId, cantAlmacen, partida.UnidadAlmacen, costoUnitAlmacen));

            using var epCmd = conn.CreateCommand();
            epCmd.Transaction = tx;
            epCmd.CommandText = @"
INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva)
VALUES ($eid, $pid, $cc, $uc, $fc, $ca, $ua, $costo)";
            epCmd.Parameters.AddWithValue("$eid", entId);
            epCmd.Parameters.AddWithValue("$pid", partida.ProductoId);
            epCmd.Parameters.AddWithValue("$cc", (double)cantRecibir);
            epCmd.Parameters.AddWithValue("$uc", partida.UnidadCompra);
            epCmd.Parameters.AddWithValue("$fc", (double)factor);
            epCmd.Parameters.AddWithValue("$ca", (double)cantAlmacen);
            epCmd.Parameters.AddWithValue("$ua", partida.UnidadAlmacen);
            epCmd.Parameters.AddWithValue("$costo", (double)partida.PrecioUnitarioConIva);
            epCmd.ExecuteNonQuery();

            using var upOcp = conn.CreateCommand();
            upOcp.Transaction = tx;
            upOcp.CommandText = "UPDATE OrdenCompraPartidas SET CantidadRecibida = CantidadRecibida + $rec WHERE Id = $id";
            upOcp.Parameters.AddWithValue("$rec", (double)cantRecibir);
            upOcp.Parameters.AddWithValue("$id", partida.Id);
            upOcp.ExecuteNonQuery();

            // Actualizar último costo de compra pero SIN sumar al StockActual de los anaqueles (porque fue directo a obra)
            using var upProd = conn.CreateCommand();
            upProd.Transaction = tx;
            upProd.CommandText = @"
UPDATE Productos
SET UltimoCostoCompra = CASE WHEN $costo > 0 THEN $costo ELSE UltimoCostoCompra END
WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$costo", (double)partida.PrecioUnitarioConIva);
            upProd.Parameters.AddWithValue("$pid", partida.ProductoId);
            upProd.ExecuteNonQuery();
        }

        int salId = 0;
        if (!esServicioNoInventariable && partidasSalidaInsertar.Count > 0)
        {
            string refSol = !string.IsNullOrWhiteSpace(oc.SolicitudFolio) ? $" | Solicitud: {oc.SolicitudFolio}" : "";
            string notasSalida = $"[DIRECTO A OBRA] Material recogido con proveedor ({oc.ProveedorNombre}) de {oc.Folio} ({folioEnt}){refSol}. No ingresó a anaquel de Almacén.";

            using var cmdSal = conn.CreateCommand();
            cmdSal.Transaction = tx;
            cmdSal.CommandText = @"
INSERT INTO SalidasAlmacen (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, CostoTotalEstimado, Notas, PersonaRecibe)
VALUES ($fol, $fec, $dep, $enc, $proy, $costo, $not, $per);
SELECT last_insert_rowid();";
            cmdSal.Parameters.AddWithValue("$fol", folioSal);
            cmdSal.Parameters.AddWithValue("$fec", fecha);
            cmdSal.Parameters.AddWithValue("$dep", depId);
            cmdSal.Parameters.AddWithValue("$enc", encId);
            cmdSal.Parameters.AddWithValue("$proy", proyId);
            cmdSal.Parameters.AddWithValue("$costo", (double)costoTotalSalida);
            cmdSal.Parameters.AddWithValue("$not", notasSalida);
            cmdSal.Parameters.AddWithValue("$per", responsableFinal);
            salId = Convert.ToInt32(cmdSal.ExecuteScalar());

            foreach (var sp in partidasSalidaInsertar)
            {
                using var pCmd = conn.CreateCommand();
                pCmd.Transaction = tx;
                pCmd.CommandText = @"
INSERT INTO SalidaPartidas (SalidaAlmacenId, ProductoId, CantidadEntregada, UnidadAlmacen, CostoUnitarioAlmacen)
VALUES ($sid, $pid, $cant, $ua, $costo)";
                pCmd.Parameters.AddWithValue("$sid", salId);
                pCmd.Parameters.AddWithValue("$pid", sp.prodId);
                pCmd.Parameters.AddWithValue("$cant", (double)sp.cantAlmacen);
                pCmd.Parameters.AddWithValue("$ua", sp.unidadAlmacen);
                pCmd.Parameters.AddWithValue("$costo", (double)sp.costoUnitAlmacen);
                pCmd.ExecuteNonQuery();
            }
        }

        // Actualizar estatus de la OC (Recibida Completa / Parcial) y de la Solicitud si ya quedó cubierta
        using var chkCmd = conn.CreateCommand();
        chkCmd.Transaction = tx;
        chkCmd.CommandText = @"
SELECT COUNT(*) FROM OrdenCompraPartidas
WHERE OrdenCompraId = $ocid AND CantidadRecibida < CantidadPedida - 0.001";
        chkCmd.Parameters.AddWithValue("$ocid", oc.Id);
        int pendientes = Convert.ToInt32(chkCmd.ExecuteScalar());

        string nuevoEstatus = pendientes == 0 ? "Recibida Completa" : "Recibida Parcialmente";
        using var upOc = conn.CreateCommand();
        upOc.Transaction = tx;
        upOc.CommandText = "UPDATE OrdenesCompra SET Estatus = $est WHERE Id = $ocid";
        upOc.Parameters.AddWithValue("$est", nuevoEstatus);
        upOc.Parameters.AddWithValue("$ocid", oc.Id);
        upOc.ExecuteNonQuery();

        if (pendientes == 0 && oc.SolicitudId > 0)
        {
            using var chkSol = conn.CreateCommand();
            chkSol.Transaction = tx;
            chkSol.CommandText = @"
SELECT COUNT(*) FROM OrdenesCompra
WHERE SolicitudId = $sid AND Estatus NOT IN ('Recibida Completa', 'Cerrada Parcial Definitiva', 'Cerrada Parcial (Admin)', 'Cancelada')";
            chkSol.Parameters.AddWithValue("$sid", oc.SolicitudId);
            int ocsPendSol = Convert.ToInt32(chkSol.ExecuteScalar());
            if (ocsPendSol == 0)
            {
                using var upSol = conn.CreateCommand();
                upSol.Transaction = tx;
                upSol.CommandText = "UPDATE Solicitudes SET Estatus = 'Atendida' WHERE Id = $sid";
                upSol.Parameters.AddWithValue("$sid", oc.SolicitudId);
                upSol.ExecuteNonQuery();
            }
        }

        tx.Commit();

        RegistrarBitacoraUsuario(
            "Órdenes de Compra / Obra",
            tipoEnt,
            $"{oc.Folio} -> {folioEnt}" + (salId > 0 ? $" / {folioSal}" : ""),
            $"Entregado directo a proyecto '{oc.ProyectoNombre}' con {responsableFinal}. Stock de anaquel sin alterar.");

        var entObj = ObtenerEntradasAlmacen().First(e => e.Id == entId);
        var salObj = salId > 0 ? ObtenerSalidasAlmacen().FirstOrDefault(s => s.Id == salId) : null;

        if (notificarCorreo)
        {
            string destinoObra = !string.IsNullOrWhiteSpace(lugarEntregaPersonalizado)
                ? lugarEntregaPersonalizado.Trim()
                : (esServicioNoInventariable
                    ? $"Servicio / Proyecto {oc.ProyectoNombre} (Recibió: {responsableFinal})"
                    : $"Obra / Proyecto {oc.ProyectoNombre} (Recibió: {responsableFinal}, Vale {folioSal})");
            NotificarCambioEstadoCotizacionGerente(oc.SolicitudId, oc.Folio, "5_CONCLUIDA", null, destinoObra);
        }

        return (entObj, salObj);
    }

    // ==================== FASE 1.4: REINGRESO DE SOBRANTE DE OBRA Y AJUSTES DE INVENTARIO / MERMA (AJU-xxxx) ====================

    public static EntradaAlmacen RegistrarReingresoSobranteObra(
        string proyectoOrigenNombre,
        string personaDevuelve,
        string notas,
        List<EntradaPartida> partidas)
    {
        string folioEnt = GenerarSiguienteFolio("ENT", "EntradasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string proyFinal = string.IsNullOrWhiteSpace(proyectoOrigenNombre) ? "Obra General" : proyectoOrigenNombre.Trim();
        string perFinal = string.IsNullOrWhiteSpace(personaDevuelve) ? "Personal Operativo" : personaDevuelve.Trim();
        string notasFinal = $"[SOBRANTE DE OBRA] Devuelve: {perFinal} de '{proyFinal}'" +
                            (!string.IsNullOrWhiteSpace(notas) ? $" | {notas.Trim()}" : "");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas)
VALUES ($fol, $fec, NULL, 'SOBRANTE-OBRA', $per, $proy, 'Retorno Sobrante de Obra', '', $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folioEnt);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$per", $"Devolvió: {perFinal}");
        cmd.Parameters.AddWithValue("$proy", proyFinal);
        cmd.Parameters.AddWithValue("$not", notasFinal);
        int entId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var p in partidas.Where(x => x.CantidadIngresadaAlmacen > 0))
        {
            using var epCmd = conn.CreateCommand();
            epCmd.Transaction = tx;
            epCmd.CommandText = @"
INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva)
VALUES ($eid, $pid, $cc, $uc, 1, $ca, $ua, $costo)";
            epCmd.Parameters.AddWithValue("$eid", entId);
            epCmd.Parameters.AddWithValue("$pid", p.ProductoId);
            epCmd.Parameters.AddWithValue("$cc", (double)p.CantidadIngresadaAlmacen);
            epCmd.Parameters.AddWithValue("$uc", p.UnidadAlmacen);
            epCmd.Parameters.AddWithValue("$ca", (double)p.CantidadIngresadaAlmacen);
            epCmd.Parameters.AddWithValue("$ua", p.UnidadAlmacen);
            epCmd.Parameters.AddWithValue("$costo", (double)p.CostoUnitarioCompraConIva);
            epCmd.ExecuteNonQuery();

            using var upProd = conn.CreateCommand();
            upProd.Transaction = tx;
            upProd.CommandText = @"
UPDATE Productos
SET StockActual = StockActual + $ca,
    Ubicacion = CASE WHEN $ubi <> '' THEN $ubi ELSE Ubicacion END
WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$ca", (double)p.CantidadIngresadaAlmacen);
            upProd.Parameters.AddWithValue("$ubi", p.UbicacionAlmacen ?? string.Empty);
            upProd.Parameters.AddWithValue("$pid", p.ProductoId);
            upProd.ExecuteNonQuery();
        }

        tx.Commit();

        RegistrarBitacoraUsuario(
            "Almacén",
            "Reingreso de Sobrante de Obra",
            folioEnt,
            $"Sobrante de '{proyFinal}' entregado por {perFinal} ({partidas.Count} partidas sumadas al stock de almacén).");

        return ObtenerEntradasAlmacen().First(e => e.Id == entId);
    }

    public static EntradaAlmacen RegistrarAjusteInventarioAlmacen(
        Producto producto,
        decimal nuevoStockFisicoReal,
        string motivoAjuste,
        string observaciones)
    {
        var prodActual = ObtenerProductos().First(p => p.Id == producto.Id);
        decimal stockAnterior = prodActual.StockActual;
        decimal nuevoStock = Math.Max(0m, Math.Round(nuevoStockFisicoReal, 2));
        decimal diferencia = nuevoStock - stockAnterior;

        string folioAju = GenerarSiguienteFolio("AJU", "EntradasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var usr = ObtenerUsuarioActivo();
        string tipoAju = diferencia >= 0 ? $"Ajuste Inventario (+{diferencia:0.##})" : $"Ajuste / Merma ({diferencia:0.##})";
        string motivoLimpio = string.IsNullOrWhiteSpace(motivoAjuste) ? "Conteo físico cíclico" : motivoAjuste.Trim();
        string detalleNotas = $"[{tipoAju} · {motivoLimpio}] Stock anterior: {stockAnterior:0.##} -> Stock físico real: {nuevoStock:0.##} {prodActual.UnidadAlmacen}" +
                              (!string.IsNullOrWhiteSpace(observaciones) ? $" | {observaciones.Trim()}" : "");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO EntradasAlmacen (Folio, Fecha, OrdenCompraId, OrdenCompraFolio, ProveedorNombre, ProyectoNombre, TipoEntrada, FacturaUuid, Notas)
VALUES ($fol, $fec, NULL, $mot, $resp, 'Ajuste Inventario Almacén', $tipo, '', $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folioAju);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$mot", motivoLimpio);
        cmd.Parameters.AddWithValue("$resp", usr.NombreCompleto);
        cmd.Parameters.AddWithValue("$tipo", tipoAju);
        cmd.Parameters.AddWithValue("$not", detalleNotas);
        int entId = Convert.ToInt32(cmd.ExecuteScalar());

        using (var epCmd = conn.CreateCommand())
        {
            epCmd.Transaction = tx;
            epCmd.CommandText = @"
INSERT INTO EntradaPartidas (EntradaAlmacenId, ProductoId, CantidadCompraRecibida, UnidadCompra, FactorConversion, CantidadIngresadaAlmacen, UnidadAlmacen, CostoUnitarioCompraConIva)
VALUES ($eid, $pid, $dif, $ua, 1, $dif, $ua, $costo)";
            epCmd.Parameters.AddWithValue("$eid", entId);
            epCmd.Parameters.AddWithValue("$pid", prodActual.Id);
            epCmd.Parameters.AddWithValue("$dif", (double)diferencia);
            epCmd.Parameters.AddWithValue("$ua", prodActual.UnidadAlmacen);
            epCmd.Parameters.AddWithValue("$costo", (double)prodActual.CostoUnitarioAlmacen);
            epCmd.ExecuteNonQuery();
        }

        using (var upProd = conn.CreateCommand())
        {
            upProd.Transaction = tx;
            upProd.CommandText = "UPDATE Productos SET StockActual = $nuevo WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$nuevo", (double)nuevoStock);
            upProd.Parameters.AddWithValue("$pid", prodActual.Id);
            upProd.ExecuteNonQuery();
        }

        tx.Commit();

        RegistrarBitacoraAdmin("Ajuste de Inventario / Merma", folioAju, $"{motivoLimpio} | {prodActual.CodigoInterno} ({prodActual.Nombre}): {stockAnterior:0.##} -> {nuevoStock:0.##} ({diferencia:+0.##;-0.##;0})");
        RegistrarBitacoraUsuario("Almacén", tipoAju, folioAju, $"{prodActual.CodigoInterno} {prodActual.Nombre}: {detalleNotas}");

        return ObtenerEntradasAlmacen().First(e => e.Id == entId);
    }

    // ============================================================================================================
    // FASE 2: MALETINES / KITS DE HERRAMIENTAS OPERATIVOS (KIT-xxxx) — SALIDA Y CHECK-LIST DE REGRESO EN 1 CLIC
    // ============================================================================================================

    private static void SeedKitsHerramientasSiVacio(SqliteConnection conn)
    {
        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT COUNT(*) FROM KitsHerramientas;";
        long count = Convert.ToInt64(chk.ExecuteScalar());
        if (count > 0) return;

        var idsHerramientas = new List<int>();
        using (var qHer = conn.CreateCommand())
        {
            qHer.CommandText = "SELECT Id FROM HerramientasActivos ORDER BY Id ASC LIMIT 8;";
            using var r = qHer.ExecuteReader();
            while (r.Read())
                idsHerramientas.Add(r.GetInt32(0));
        }

        using var ins1 = conn.CreateCommand();
        ins1.CommandText = @"
INSERT INTO KitsHerramientas (CodigoKit, Nombre, Especialidad, Ubicacion, ResponsableActual, ProyectoActual, UltimoValePrestamoFolio, Estatus, Notas)
VALUES ('KIT-0001', 'Maletín Eléctrico y Control — Cuadrilla 1', 'Eléctrica y Control', 'GABINETE-1', '', '', '', 'Disponible', 'Kit base para armado de tableros y puesta en marcha en obra.');
SELECT last_insert_rowid();";
        int kit1Id = Convert.ToInt32(ins1.ExecuteScalar());

        using var ins2 = conn.CreateCommand();
        ins2.CommandText = @"
INSERT INTO KitsHerramientas (CodigoKit, Nombre, Especialidad, Ubicacion, ResponsableActual, ProyectoActual, UltimoValePrestamoFolio, Estatus, Notas)
VALUES ('KIT-0002', 'Maletín Mecánico e Instalación — Cuadrilla 2', 'Mecánica / Obra', 'GABINETE-2', '', '', '', 'Disponible', 'Kit de montaje mecánico y perforación en planta.');
SELECT last_insert_rowid();";
        int kit2Id = Convert.ToInt32(ins2.ExecuteScalar());

        for (int i = 0; i < idsHerramientas.Count; i++)
        {
            int targetKit = (i % 2 == 0) ? kit1Id : kit2Id;
            using var insP = conn.CreateCommand();
            insP.CommandText = "INSERT OR IGNORE INTO KitHerramientasPartidas (KitId, HerramientaActivoId) VALUES ($k, $h);";
            insP.Parameters.AddWithValue("$k", targetKit);
            insP.Parameters.AddWithValue("$h", idsHerramientas[i]);
            insP.ExecuteNonQuery();
        }
    }

    public static List<KitHerramienta> ObtenerKitsHerramientas()
    {
        var lista = new List<KitHerramienta>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT Id, CodigoKit, Nombre, Especialidad, Ubicacion, ResponsableActual, ProyectoActual, UltimoValePrestamoFolio, Estatus, Notas
FROM KitsHerramientas
ORDER BY CodigoKit ASC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                lista.Add(new KitHerramienta
                {
                    Id = r.GetInt32(0),
                    CodigoKit = r.GetString(1),
                    Nombre = r.GetString(2),
                    Especialidad = r.GetString(3),
                    Ubicacion = r.GetString(4),
                    ResponsableActual = r.GetString(5),
                    ProyectoActual = r.GetString(6),
                    UltimoValePrestamoFolio = r.GetString(7),
                    Estatus = r.GetString(8),
                    Notas = r.GetString(9)
                });
            }
        }

        foreach (var kit in lista)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT kp.Id, kp.KitId, kp.HerramientaActivoId,
       COALESCE(h.CodigoIndividual, 'HER'),
       COALESCE(pr.Nombre, 'Herramienta'),
       COALESCE(h.MarcaModelo, ''),
       COALESCE(h.NumeroSerie, ''),
       COALESCE(h.Ubicacion, ''),
       COALESCE(h.Estado, 'Disponible'),
       COALESCE(h.EncargadoActual, '')
FROM KitHerramientasPartidas kp
INNER JOIN HerramientasActivos h ON h.Id = kp.HerramientaActivoId
LEFT JOIN Productos pr ON pr.Id = h.ProductoId
WHERE kp.KitId = $kid
ORDER BY h.CodigoIndividual ASC;";
            pCmd.Parameters.AddWithValue("$kid", kit.Id);
            using var r2 = pCmd.ExecuteReader();
            while (r2.Read())
            {
                string estAct = r2.GetString(8);
                kit.Partidas.Add(new KitHerramientaPartida
                {
                    Id = r2.GetInt32(0),
                    KitId = r2.GetInt32(1),
                    HerramientaActivoId = r2.GetInt32(2),
                    CodigoIndividual = r2.GetString(3),
                    HerramientaNombre = r2.GetString(4),
                    MarcaModelo = r2.GetString(5),
                    NumeroSerie = r2.GetString(6),
                    Ubicacion = r2.GetString(7),
                    EstadoActual = estAct,
                    EncargadoActual = r2.GetString(9),
                    RegresoOk = true,
                    EstadoRetornoElegido = "Bueno (Completo)"
                });
            }

            // Sincronizar estatus dinámico según el estado real de sus herramientas
            if (kit.Partidas.Count > 0)
            {
                if (kit.Partidas.Any(p => p.EstadoActual.Contains("Dañada", StringComparison.OrdinalIgnoreCase) || p.EstadoActual.Contains("Extraviada", StringComparison.OrdinalIgnoreCase)))
                    kit.Estatus = "Con Incidencia / Incompleto";
                else if (kit.Partidas.Any(p => string.Equals(p.EstadoActual, "En Préstamo", StringComparison.OrdinalIgnoreCase)))
                {
                    kit.Estatus = kit.Partidas.All(p => string.Equals(p.EstadoActual, "En Préstamo", StringComparison.OrdinalIgnoreCase))
                        ? "En Obra / Prestado"
                        : "Parcial en Obra";
                    if (string.IsNullOrWhiteSpace(kit.ResponsableActual))
                        kit.ResponsableActual = kit.Partidas.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.EncargadoActual))?.EncargadoActual ?? string.Empty;
                }
                else
                {
                    kit.Estatus = "Disponible";
                }
            }
        }

        return lista;
    }

    public static string GenerarSiguienteCodigoKit()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CodigoKit FROM KitsHerramientas ORDER BY Id DESC LIMIT 50;";
        int max = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string c = r.GetString(0);
            var parts = c.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out int n) && n > max)
                max = n;
        }
        return $"KIT-{(max + 1):0000}";
    }

    public static int GuardarKitHerramienta(KitHerramienta kit)
    {
        string codigo = string.IsNullOrWhiteSpace(kit.CodigoKit) ? GenerarSiguienteCodigoKit() : kit.CodigoKit.Trim().ToUpperInvariant();
        string nombre = string.IsNullOrWhiteSpace(kit.Nombre) ? $"Maletín Operativo {codigo}" : kit.Nombre.Trim();
        string esp = string.IsNullOrWhiteSpace(kit.Especialidad) ? "General" : kit.Especialidad.Trim();
        string ubi = string.IsNullOrWhiteSpace(kit.Ubicacion) ? "GABINETE-1" : kit.Ubicacion.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        if (kit.Id <= 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO KitsHerramientas (CodigoKit, Nombre, Especialidad, Ubicacion, ResponsableActual, ProyectoActual, UltimoValePrestamoFolio, Estatus, Notas)
VALUES ($cod, $nom, $esp, $ubi, '', '', '', 'Disponible', $not);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$cod", codigo);
            cmd.Parameters.AddWithValue("$nom", nombre);
            cmd.Parameters.AddWithValue("$esp", esp);
            cmd.Parameters.AddWithValue("$ubi", ubi);
            cmd.Parameters.AddWithValue("$not", kit.Notas?.Trim() ?? string.Empty);
            int nuevoId = Convert.ToInt32(cmd.ExecuteScalar());
            RegistrarBitacoraUsuario("Herramientas / Kits", "Alta de Maletín / Kit", codigo, $"Se creó el kit '{nombre}' en {ubi}.");
            return nuevoId;
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE KitsHerramientas
SET CodigoKit = $cod, Nombre = $nom, Especialidad = $esp, Ubicacion = $ubi, Notas = $not
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$cod", codigo);
            cmd.Parameters.AddWithValue("$nom", nombre);
            cmd.Parameters.AddWithValue("$esp", esp);
            cmd.Parameters.AddWithValue("$ubi", ubi);
            cmd.Parameters.AddWithValue("$not", kit.Notas?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$id", kit.Id);
            cmd.ExecuteNonQuery();
            RegistrarBitacoraUsuario("Herramientas / Kits", "Edición de Maletín / Kit", codigo, $"Se actualizó el kit '{nombre}'.");
            return kit.Id;
        }
    }

    public static void AgregarHerramientaAKit(int kitId, int herramientaActivoId)
    {
        if (kitId <= 0 || herramientaActivoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM KitHerramientasPartidas WHERE HerramientaActivoId = $hid;
INSERT OR IGNORE INTO KitHerramientasPartidas (KitId, HerramientaActivoId) VALUES ($kid, $hid);";
        cmd.Parameters.AddWithValue("$kid", kitId);
        cmd.Parameters.AddWithValue("$hid", herramientaActivoId);
        cmd.ExecuteNonQuery();
    }

    public static void QuitarHerramientaDeKit(int kitId, int herramientaActivoId)
    {
        if (kitId <= 0 || herramientaActivoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM KitHerramientasPartidas WHERE KitId = $kid AND HerramientaActivoId = $hid;";
        cmd.Parameters.AddWithValue("$kid", kitId);
        cmd.Parameters.AddWithValue("$hid", herramientaActivoId);
        cmd.ExecuteNonQuery();
    }

    public static void EliminarKitHerramienta(int kitId)
    {
        if (kitId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM KitHerramientasPartidas WHERE KitId = $kid;
DELETE FROM KitsHerramientas WHERE Id = $kid;";
        cmd.Parameters.AddWithValue("$kid", kitId);
        cmd.ExecuteNonQuery();
    }

    public static PrestamoHerramienta? DespacharKitHerramientaEnUnClic(
        int kitId,
        int departamentoId,
        int encargadoId,
        int proyectoId,
        string personaRecibe,
        string fechaCompromiso,
        string notas = "")
    {
        var kit = ObtenerKitsHerramientas().FirstOrDefault(k => k.Id == kitId);
        if (kit == null) return null;

        var todasHer = ObtenerHerramientasIndividuales().ToDictionary(h => h.Id);
        var herramientasDisponiblesKit = new List<HerramientaActivo>();
        foreach (var part in kit.Partidas)
        {
            if (todasHer.TryGetValue(part.HerramientaActivoId, out var hObj) &&
                string.Equals(hObj.Estado, "Disponible", StringComparison.OrdinalIgnoreCase))
            {
                herramientasDisponiblesKit.Add(hObj);
            }
        }

        if (herramientasDisponiblesKit.Count == 0)
            return null;

        string notaKit = $"[MALETÍN {kit.CodigoKit}: {kit.Nombre}]" + (string.IsNullOrWhiteSpace(notas) ? "" : $" | {notas.Trim()}");
        var prestamo = RegistrarPrestamoHerramienta(
            departamentoId,
            encargadoId,
            proyectoId,
            notaKit,
            herramientasDisponiblesKit,
            personaRecibe,
            fechaCompromiso);

        string proyNombre = ObtenerProyectos().FirstOrDefault(p => p.Id == proyectoId)?.Nombre ?? "Obra";
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var up = conn.CreateCommand();
            up.CommandText = @"
UPDATE KitsHerramientas
SET Estatus = 'En Obra / Prestado',
    ResponsableActual = $resp,
    ProyectoActual = $proy,
    UltimoValePrestamoFolio = $fol
WHERE Id = $kid;";
            up.Parameters.AddWithValue("$resp", personaRecibe.Trim());
            up.Parameters.AddWithValue("$proy", proyNombre);
            up.Parameters.AddWithValue("$fol", prestamo.Folio);
            up.Parameters.AddWithValue("$kid", kit.Id);
            up.ExecuteNonQuery();
        }

        RegistrarBitacoraUsuario(
            "Herramientas / Kits",
            "Despacho de Maletín en 1 Clic",
            $"{kit.CodigoKit} -> {prestamo.Folio}",
            $"Se despachó {kit.CodigoKit} ({herramientasDisponiblesKit.Count} herramientas) a '{personaRecibe}' para '{proyNombre}'.");

        return prestamo;
    }

    public static (int devueltasOk, int siguenEnObra, int conIncidencia) RegistrarChecklistRegresoKit(
        int kitId,
        List<KitHerramientaPartida> checklistItems,
        string observacionesGenerales = "")
    {
        var kit = ObtenerKitsHerramientas().FirstOrDefault(k => k.Id == kitId);
        if (kit == null) return (0, 0, 0);

        var prestamosActivos = ObtenerPrestamosHerramienta()
            .Where(p => p.Estatus != "Devuelto Completo" && p.Estatus != "Devuelto con Incidencia")
            .ToList();

        int okCount = 0;
        int siguenCount = 0;
        int incCount = 0;

        foreach (var item in checklistItems)
        {
            string estadoElegido = item.EstadoRetornoElegido ?? "Bueno (Completo)";
            if (estadoElegido.StartsWith("Se queda en obra", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(item.EstadoActual, "En Préstamo", StringComparison.OrdinalIgnoreCase))
                    siguenCount++;
                continue;
            }

            string estadoDb = estadoElegido switch
            {
                var s when s.Contains("Dañado", StringComparison.OrdinalIgnoreCase) => "Dañado",
                var s when s.Contains("Extraviado", StringComparison.OrdinalIgnoreCase) => "Extraviado",
                var s when s.Contains("Incompleto", StringComparison.OrdinalIgnoreCase) => "Incompleto / Falta Pieza",
                _ => "Bueno (Completo)"
            };

            string obsPartida = $"[Check-list {kit.CodigoKit}]" +
                                (!string.IsNullOrWhiteSpace(item.ObservacionesRetorno) ? $" {item.ObservacionesRetorno.Trim()}" : "") +
                                (!string.IsNullOrWhiteSpace(observacionesGenerales) ? $" | {observacionesGenerales.Trim()}" : "");

            // Buscar si tiene una partida de préstamo activa pendiente
            var prestamoMatch = prestamosActivos.FirstOrDefault(pr =>
                pr.Partidas.Any(pp => pp.HerramientaActivoId == item.HerramientaActivoId && pp.EstadoDevolucion == "Pendiente"));

            if (prestamoMatch != null)
            {
                var ppMatch = prestamoMatch.Partidas.First(pp => pp.HerramientaActivoId == item.HerramientaActivoId && pp.EstadoDevolucion == "Pendiente");
                DevolverHerramientaPartida(ppMatch.Id, item.HerramientaActivoId, prestamoMatch.Id, estadoDb, obsPartida);
            }
            else
            {
                // Actualizar directamente el estado de la herramienta si no tenía vale abierto
                string nuevoEstHer = estadoDb switch
                {
                    "Dañado" => "Dañada",
                    "Extraviado" => "Extraviada / Baja",
                    _ => "Disponible"
                };
                using var connH = new SqliteConnection(ConnectionString);
                connH.Open();
                using var upH = connH.CreateCommand();
                upH.CommandText = @"
UPDATE HerramientasActivos
SET Estado = $est, EncargadoActual = '', ProyectoActual = '', NotasEstado = $obs
WHERE Id = $hid;";
                upH.Parameters.AddWithValue("$est", nuevoEstHer);
                upH.Parameters.AddWithValue("$obs", obsPartida);
                upH.Parameters.AddWithValue("$hid", item.HerramientaActivoId);
                upH.ExecuteNonQuery();
            }

            if (estadoDb == "Bueno (Completo)")
                okCount++;
            else
                incCount++;
        }

        string nuevoEstatusKit = incCount > 0
            ? "Con Incidencia / Incompleto"
            : (siguenCount > 0 ? "Parcial en Obra" : "Disponible");

        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var upK = conn.CreateCommand();
            upK.CommandText = @"
UPDATE KitsHerramientas
SET Estatus = $est,
    ResponsableActual = CASE WHEN $siguen > 0 THEN ResponsableActual ELSE '' END,
    ProyectoActual = CASE WHEN $siguen > 0 THEN ProyectoActual ELSE '' END
WHERE Id = $kid;";
            upK.Parameters.AddWithValue("$est", nuevoEstatusKit);
            upK.Parameters.AddWithValue("$siguen", siguenCount);
            upK.Parameters.AddWithValue("$kid", kit.Id);
            upK.ExecuteNonQuery();
        }

        if (incCount > 0)
        {
            RegistrarCheckpoint(
                "Herramientas: Check-list de Maletín",
                "CP-KIT-INC",
                kit.CodigoKit,
                "En Obra / Prestado",
                nuevoEstatusKit,
                ObtenerRolOperativoActivo(),
                "Compras, Almacén",
                $"En el regreso del maletín {kit.CodigoKit} ({kit.Nombre}) se reportaron {incCount} herramienta(s) dañadas o extraviadas.");
        }

        RegistrarBitacoraUsuario(
            "Herramientas / Kits",
            "Check-list de Regreso de Maletín",
            kit.CodigoKit,
            $"Regresaron OK: {okCount}, Siguen en obra: {siguenCount}, Con incidencia: {incCount}. {observacionesGenerales}");

        return (okCount, siguenCount, incCount);
    }

    // ============================================================================================================
    // FASE 3: FLOTILLA DE CAMIONETAS (VEH-xxxx), COSTO POR KILÓMETRO (CPK) Y GARANTÍAS / TCO DE EQUIPOS
    // ============================================================================================================

    private static void SeedVehiculosFlotillaYGarantiasSiVacio(SqliteConnection conn)
    {
        // Limpiar registros semilla de ejemplo ('FAC-GAS-101', 'FAC-TAL-204', 'FAC-GAS-108', 'JKL-4821', 'MNP-9104') si estaban en la base
        using (var cleanSeed = conn.CreateCommand())
        {
            cleanSeed.CommandText = @"
DELETE FROM BitacoraGastosVehiculo WHERE FolioReferencia IN ('FAC-GAS-101', 'FAC-TAL-204', 'FAC-GAS-108');
DELETE FROM RevisionesSemanalesVehiculo WHERE Observaciones LIKE '%Revisión semanal de lunes sin novedades%' OR Observaciones LIKE '%Unidad limpia y con kit de emergencia completo%';
DELETE FROM VehiculosFlotilla WHERE Placas IN ('JKL-4821', 'MNP-9104');";
            cleanSeed.ExecuteNonQuery();
        }

        // Inicializar vigencia de garantía por defecto en herramientas activas si están vacías
        using var upHerGar = conn.CreateCommand();
        upHerGar.CommandText = @"
UPDATE HerramientasActivos
SET FechaCompra = CASE WHEN FechaCompra = '' THEN $fc ELSE FechaCompra END,
    FechaFinGarantia = CASE WHEN FechaFinGarantia = '' THEN $fg ELSE FechaFinGarantia END,
    CostoAdquisicion = CASE WHEN CostoAdquisicion <= 0 THEN COALESCE((SELECT UltimoCostoCompra FROM Productos p WHERE p.Id = HerramientasActivos.ProductoId), 2500) ELSE CostoAdquisicion END
WHERE Id IN (SELECT Id FROM HerramientasActivos ORDER BY Id ASC LIMIT 12);";
        upHerGar.Parameters.AddWithValue("$fc", DateTime.Today.AddMonths(-4).ToString("yyyy-MM-dd"));
        upHerGar.Parameters.AddWithValue("$fg", DateTime.Today.AddMonths(8).ToString("yyyy-MM-dd"));
        upHerGar.ExecuteNonQuery();
    }

    public static List<VehiculoFlotilla> ObtenerVehiculosFlotilla()
    {
        var lista = new List<VehiculoFlotilla>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT Id, CodigoVehiculo, Placas, MarcaModelo, Anio, ResponsableHabitual, KilometrajeInicial, KilometrajeActual, ProximoServicioKm, VigenciaSeguro, Estatus, Notas,
       COALESCE(IntervaloServicioKm, 10000), COALESCE(UltimoServicioKm, 0), COALESCE(CapacidadTanqueLitros, 60), COALESCE(TipoCombustible, 'Gasolina Regular')
FROM VehiculosFlotilla
ORDER BY CodigoVehiculo ASC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                lista.Add(new VehiculoFlotilla
                {
                    Id = r.GetInt32(0),
                    CodigoVehiculo = r.GetString(1),
                    Placas = r.GetString(2),
                    MarcaModelo = r.GetString(3),
                    Anio = r.GetInt32(4),
                    ResponsableHabitual = r.GetString(5),
                    KilometrajeInicial = r.GetInt32(6),
                    KilometrajeActual = r.GetInt32(7),
                    ProximoServicioKm = r.GetInt32(8),
                    VigenciaSeguro = r.GetString(9),
                    Estatus = r.GetString(10),
                    Notas = r.GetString(11),
                    IntervaloServicioKm = r.GetInt32(12) > 0 ? r.GetInt32(12) : 10000,
                    UltimoServicioKm = r.GetInt32(13),
                    CapacidadTanqueLitros = Convert.ToDecimal(r.GetDouble(14)),
                    TipoCombustible = r.GetString(15)
                });
            }
        }

        foreach (var v in lista)
        {
            using (var gCmd = conn.CreateCommand())
            {
                gCmd.CommandText = @"
SELECT Id, VehiculoId, Fecha, TipoGasto, KilometrajeRegistro, MontoConIva, ProveedorTaller, FolioReferencia, ProyectoNombre, Descripcion,
       COALESCE(LitrosCargados, 0), COALESCE(PrecioPorLitro, 0), COALESCE(TanqueLleno, 1)
FROM BitacoraGastosVehiculo
WHERE VehiculoId = $vid
ORDER BY Fecha DESC, Id DESC;";
                gCmd.Parameters.AddWithValue("$vid", v.Id);
                using var rg = gCmd.ExecuteReader();
                while (rg.Read())
                {
                    var reg = new RegistroGastoVehiculo
                    {
                        Id = rg.GetInt32(0),
                        VehiculoId = rg.GetInt32(1),
                        CodigoVehiculo = v.CodigoVehiculo,
                        VehiculoDescripcion = v.MarcaModelo,
                        Fecha = rg.GetString(2),
                        TipoGasto = rg.GetString(3),
                        KilometrajeRegistro = rg.GetInt32(4),
                        MontoConIva = Convert.ToDecimal(rg.GetDouble(5)),
                        ProveedorTaller = rg.GetString(6),
                        FolioReferencia = rg.GetString(7),
                        ProyectoNombre = rg.GetString(8),
                        Descripcion = rg.GetString(9),
                        LitrosCargados = Convert.ToDecimal(rg.GetDouble(10)),
                        PrecioPorLitro = Convert.ToDecimal(rg.GetDouble(11)),
                        TanqueLleno = rg.GetInt32(12) == 1
                    };
                    v.HistorialGastos.Add(reg);

                    if (reg.TipoGasto.Contains("Gasolina", StringComparison.OrdinalIgnoreCase) || reg.TipoGasto.Contains("Combustible", StringComparison.OrdinalIgnoreCase) || reg.TipoGasto.Contains("Diésel", StringComparison.OrdinalIgnoreCase))
                    {
                        v.GastoCombustibleAcumulado += reg.MontoConIva;
                        v.LitrosCombustibleAcumulados += reg.LitrosCargados;
                    }
                    else if (reg.TipoGasto.Contains("Mantenimiento", StringComparison.OrdinalIgnoreCase) || reg.TipoGasto.Contains("Taller", StringComparison.OrdinalIgnoreCase) || reg.TipoGasto.Contains("Llantas", StringComparison.OrdinalIgnoreCase) || reg.TipoGasto.Contains("Reparación", StringComparison.OrdinalIgnoreCase) || reg.TipoGasto.Contains("Refacciones", StringComparison.OrdinalIgnoreCase))
                    {
                        v.GastoMantenimientoAcumulado += reg.MontoConIva;
                    }
                    else
                    {
                        v.GastoOtrosAcumulado += reg.MontoConIva;
                    }
                }
            }

            using (var revCmd = conn.CreateCommand())
            {
                revCmd.CommandText = @"
SELECT Id, VehiculoId, FolioRevision, Fecha, ResponsableInspector, KilometrajeRegistrado, NivelCombustible, EstadoLlantas, EstadoNivelesFluidos, EstadoFrenosLuces, EstadoCarroceriaLimpieza, Observaciones
FROM RevisionesSemanalesVehiculo
WHERE VehiculoId = $vid
ORDER BY Fecha DESC, Id DESC;";
                revCmd.Parameters.AddWithValue("$vid", v.Id);
                using var rr = revCmd.ExecuteReader();
                while (rr.Read())
                {
                    v.HistorialRevisiones.Add(new RevisionSemanalVehiculo
                    {
                        Id = rr.GetInt32(0),
                        VehiculoId = rr.GetInt32(1),
                        CodigoVehiculo = v.CodigoVehiculo,
                        VehiculoDescripcion = v.MarcaModelo,
                        Placas = v.Placas,
                        FolioRevision = rr.GetString(2),
                        Fecha = rr.GetString(3),
                        ResponsableInspector = rr.GetString(4),
                        KilometrajeRegistrado = rr.GetInt32(5),
                        NivelCombustible = rr.GetString(6),
                        EstadoLlantas = rr.GetString(7),
                        EstadoNivelesFluidos = rr.GetString(8),
                        EstadoFrenosLuces = rr.GetString(9),
                        EstadoCarroceriaLimpieza = rr.GetString(10),
                        Observaciones = rr.GetString(11)
                    });
                }
            }
        }

        return lista;
    }

    public static string GenerarSiguienteCodigoVehiculo()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CodigoVehiculo FROM VehiculosFlotilla ORDER BY Id DESC LIMIT 50;";
        int max = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string c = r.GetString(0);
            var parts = c.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out int n) && n > max)
                max = n;
        }
        return $"VEH-{(max + 1):0000}";
    }

    public static string GenerarSiguienteFolioRevisionVehiculo()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FolioRevision FROM RevisionesSemanalesVehiculo ORDER BY Id DESC LIMIT 50;";
        int max = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string c = r.GetString(0);
            var parts = c.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out int n) && n > max)
                max = n;
        }
        return $"REV-{(max + 1):0000}";
    }

    public static int GuardarVehiculoFlotilla(VehiculoFlotilla v)
    {
        string cod = string.IsNullOrWhiteSpace(v.CodigoVehiculo) ? GenerarSiguienteCodigoVehiculo() : v.CodigoVehiculo.Trim().ToUpperInvariant();
        string mod = string.IsNullOrWhiteSpace(v.MarcaModelo) ? $"Unidad {cod}" : v.MarcaModelo.Trim();
        int kmIni = Math.Max(0, v.KilometrajeInicial);
        int kmAct = Math.Max(kmIni, v.KilometrajeActual);
        int intervalo = v.IntervaloServicioKm > 0 ? v.IntervaloServicioKm : 10000;
        int ultServ = v.UltimoServicioKm > 0 ? v.UltimoServicioKm : kmIni;
        int kmProx = v.ProximoServicioKm > 0 ? v.ProximoServicioKm : (ultServ + intervalo);
        decimal capTanque = v.CapacidadTanqueLitros > 0 ? v.CapacidadTanqueLitros : 60m;
        string tipoComb = string.IsNullOrWhiteSpace(v.TipoCombustible) ? "Gasolina Regular" : v.TipoCombustible.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        if (v.Id <= 0)
        {
            using (var chkDup = conn.CreateCommand())
            {
                chkDup.CommandText = "SELECT COUNT(*) FROM VehiculosFlotilla WHERE UPPER(CodigoVehiculo) = $cod;";
                chkDup.Parameters.AddWithValue("$cod", cod);
                if (Convert.ToInt64(chkDup.ExecuteScalar()) > 0)
                    cod = GenerarSiguienteCodigoVehiculo();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO VehiculosFlotilla (CodigoVehiculo, Placas, MarcaModelo, Anio, ResponsableHabitual, KilometrajeInicial, KilometrajeActual, ProximoServicioKm, IntervaloServicioKm, UltimoServicioKm, CapacidadTanqueLitros, TipoCombustible, VigenciaSeguro, Estatus, Notas)
VALUES ($cod, $pla, $mod, $anio, $resp, $kmi, $kma, $kmp, $intv, $ults, $cap, $tcomb, $seg, $est, $not);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$cod", cod);
            cmd.Parameters.AddWithValue("$pla", v.Placas?.Trim().ToUpperInvariant() ?? string.Empty);
            cmd.Parameters.AddWithValue("$mod", mod);
            cmd.Parameters.AddWithValue("$anio", v.Anio > 1990 ? v.Anio : DateTime.Today.Year);
            cmd.Parameters.AddWithValue("$resp", v.ResponsableHabitual?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$kmi", kmIni);
            cmd.Parameters.AddWithValue("$kma", kmAct);
            cmd.Parameters.AddWithValue("$kmp", kmProx);
            cmd.Parameters.AddWithValue("$intv", intervalo);
            cmd.Parameters.AddWithValue("$ults", ultServ);
            cmd.Parameters.AddWithValue("$cap", (double)capTanque);
            cmd.Parameters.AddWithValue("$tcomb", tipoComb);
            cmd.Parameters.AddWithValue("$seg", v.VigenciaSeguro?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$est", string.IsNullOrWhiteSpace(v.Estatus) ? "Activo en Operación" : v.Estatus.Trim());
            cmd.Parameters.AddWithValue("$not", v.Notas?.Trim() ?? string.Empty);
            int idNuevo = Convert.ToInt32(cmd.ExecuteScalar());
            RegistrarBitacoraUsuario("Flotilla / Vehículos", "Alta de Vehículo", cod, $"{mod} ({v.Placas}) Km actual: {kmAct:N0} | Servicio cada {intervalo:N0} km");
            return idNuevo;
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE VehiculosFlotilla
SET CodigoVehiculo=$cod, Placas=$pla, MarcaModelo=$mod, Anio=$anio, ResponsableHabitual=$resp,
    KilometrajeInicial=$kmi, KilometrajeActual=$kma, ProximoServicioKm=$kmp,
    IntervaloServicioKm=$intv, UltimoServicioKm=$ults, CapacidadTanqueLitros=$cap, TipoCombustible=$tcomb,
    VigenciaSeguro=$seg, Estatus=$est, Notas=$not
WHERE Id=$id;";
            cmd.Parameters.AddWithValue("$cod", cod);
            cmd.Parameters.AddWithValue("$pla", v.Placas?.Trim().ToUpperInvariant() ?? string.Empty);
            cmd.Parameters.AddWithValue("$mod", mod);
            cmd.Parameters.AddWithValue("$anio", v.Anio > 1990 ? v.Anio : DateTime.Today.Year);
            cmd.Parameters.AddWithValue("$resp", v.ResponsableHabitual?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$kmi", kmIni);
            cmd.Parameters.AddWithValue("$kma", kmAct);
            cmd.Parameters.AddWithValue("$kmp", kmProx);
            cmd.Parameters.AddWithValue("$intv", intervalo);
            cmd.Parameters.AddWithValue("$ults", ultServ);
            cmd.Parameters.AddWithValue("$cap", (double)capTanque);
            cmd.Parameters.AddWithValue("$tcomb", tipoComb);
            cmd.Parameters.AddWithValue("$seg", v.VigenciaSeguro?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$est", string.IsNullOrWhiteSpace(v.Estatus) ? "Activo en Operación" : v.Estatus.Trim());
            cmd.Parameters.AddWithValue("$not", v.Notas?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$id", v.Id);
            cmd.ExecuteNonQuery();
            RegistrarBitacoraUsuario("Flotilla / Vehículos", "Actualización de Vehículo", cod, $"{mod} ({v.Placas}) Km actual: {kmAct:N0} | Servicio cada {intervalo:N0} km");
            return v.Id;
        }
    }

    public static void EliminarVehiculoFlotilla(int vehiculoId)
    {
        if (vehiculoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM RevisionesSemanalesVehiculo WHERE VehiculoId = $id;
DELETE FROM BitacoraGastosVehiculo WHERE VehiculoId = $id;
DELETE FROM VehiculosFlotilla WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", vehiculoId);
        cmd.ExecuteNonQuery();
    }

    public static void EliminarGastoVehiculo(int gastoId)
    {
        if (gastoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM BitacoraGastosVehiculo WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", gastoId);
        cmd.ExecuteNonQuery();
    }

    public static void EliminarRevisionSemanalVehiculo(int revisionId)
    {
        if (revisionId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RevisionesSemanalesVehiculo WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", revisionId);
        cmd.ExecuteNonQuery();
    }

    public static void RegistrarGastoVehiculo(RegistroGastoVehiculo g)
    {
        if (g.VehiculoId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        decimal litros = Math.Max(0m, g.LitrosCargados);
        decimal precioLitro = Math.Max(0m, g.PrecioPorLitro);
        decimal monto = Math.Max(0m, g.MontoConIva);
        if (litros > 0 && precioLitro <= 0 && monto > 0)
            precioLitro = Math.Round(monto / litros, 2);
        else if (litros <= 0 && precioLitro > 0 && monto > 0)
            litros = Math.Round(monto / precioLitro, 2);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO BitacoraGastosVehiculo (VehiculoId, Fecha, TipoGasto, KilometrajeRegistro, LitrosCargados, PrecioPorLitro, TanqueLleno, MontoConIva, ProveedorTaller, FolioReferencia, ProyectoNombre, Descripcion)
VALUES ($vid, $fec, $tipo, $km, $lit, $pl, $lleno, $monto, $prov, $fol, $proy, $desc);";
            cmd.Parameters.AddWithValue("$vid", g.VehiculoId);
            cmd.Parameters.AddWithValue("$fec", string.IsNullOrWhiteSpace(g.Fecha) ? DateTime.Today.ToString("yyyy-MM-dd") : g.Fecha.Trim());
            cmd.Parameters.AddWithValue("$tipo", string.IsNullOrWhiteSpace(g.TipoGasto) ? "Gasolina / Combustible" : g.TipoGasto.Trim());
            cmd.Parameters.AddWithValue("$km", Math.Max(0, g.KilometrajeRegistro));
            cmd.Parameters.AddWithValue("$lit", (double)litros);
            cmd.Parameters.AddWithValue("$pl", (double)precioLitro);
            cmd.Parameters.AddWithValue("$lleno", g.TanqueLleno ? 1 : 0);
            cmd.Parameters.AddWithValue("$monto", (double)monto);
            cmd.Parameters.AddWithValue("$prov", g.ProveedorTaller?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$fol", g.FolioReferencia?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$proy", g.ProyectoNombre?.Trim() ?? "Operación General");
            cmd.Parameters.AddWithValue("$desc", g.Descripcion?.Trim() ?? string.Empty);
            cmd.ExecuteNonQuery();
        }

        if (g.KilometrajeRegistro > 0)
        {
            bool esMantPrev = g.TipoGasto != null && g.TipoGasto.Contains("Mantenimiento Preventivo", StringComparison.OrdinalIgnoreCase);
            using var upKm = conn.CreateCommand();
            upKm.Transaction = tx;
            upKm.CommandText = @"
UPDATE VehiculosFlotilla
SET KilometrajeActual = CASE WHEN $km > KilometrajeActual THEN $km ELSE KilometrajeActual END,
    UltimoServicioKm = CASE WHEN $esMant = 1 THEN $km ELSE UltimoServicioKm END,
    ProximoServicioKm = CASE WHEN $esMant = 1 THEN $km + COALESCE(NULLIF(IntervaloServicioKm, 0), 10000) ELSE ProximoServicioKm END
WHERE Id = $vid;";
            upKm.Parameters.AddWithValue("$km", g.KilometrajeRegistro);
            upKm.Parameters.AddWithValue("$esMant", esMantPrev ? 1 : 0);
            upKm.Parameters.AddWithValue("$vid", g.VehiculoId);
            upKm.ExecuteNonQuery();
        }

        tx.Commit();

        var veh = ObtenerVehiculosFlotilla().FirstOrDefault(v => v.Id == g.VehiculoId);
        RegistrarBitacoraUsuario(
            "Flotilla / Vehículos",
            g.TipoGasto ?? "Gasto Vehicular",
            veh?.CodigoVehiculo ?? $"VEH-{g.VehiculoId}",
            $"Monto: {monto:C2} | Km: {g.KilometrajeRegistro:N0} {(litros > 0 ? $"| {litros:0.##} L " : "")}| Ref: {g.FolioReferencia} | {g.Descripcion}");
    }

    public static RevisionSemanalVehiculo RegistrarRevisionSemanalVehiculo(RevisionSemanalVehiculo rev)
    {
        if (rev.VehiculoId <= 0) return rev;
        string folio = string.IsNullOrWhiteSpace(rev.FolioRevision) ? GenerarSiguienteFolioRevisionVehiculo() : rev.FolioRevision.Trim().ToUpperInvariant();
        string fecha = string.IsNullOrWhiteSpace(rev.Fecha) ? DateTime.Today.ToString("yyyy-MM-dd") : rev.Fecha.Trim();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO RevisionesSemanalesVehiculo (VehiculoId, FolioRevision, Fecha, ResponsableInspector, KilometrajeRegistrado, NivelCombustible, EstadoLlantas, EstadoNivelesFluidos, EstadoFrenosLuces, EstadoCarroceriaLimpieza, Observaciones)
VALUES ($vid, $fol, $fec, $resp, $km, $niv, $llan, $flu, $fre, $car, $obs);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$vid", rev.VehiculoId);
            cmd.Parameters.AddWithValue("$fol", folio);
            cmd.Parameters.AddWithValue("$fec", fecha);
            cmd.Parameters.AddWithValue("$resp", rev.ResponsableInspector?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$km", Math.Max(0, rev.KilometrajeRegistrado));
            cmd.Parameters.AddWithValue("$niv", string.IsNullOrWhiteSpace(rev.NivelCombustible) ? "3/4 Tanque" : rev.NivelCombustible.Trim());
            cmd.Parameters.AddWithValue("$llan", string.IsNullOrWhiteSpace(rev.EstadoLlantas) ? "OK" : rev.EstadoLlantas.Trim());
            cmd.Parameters.AddWithValue("$flu", string.IsNullOrWhiteSpace(rev.EstadoNivelesFluidos) ? "OK" : rev.EstadoNivelesFluidos.Trim());
            cmd.Parameters.AddWithValue("$fre", string.IsNullOrWhiteSpace(rev.EstadoFrenosLuces) ? "OK" : rev.EstadoFrenosLuces.Trim());
            cmd.Parameters.AddWithValue("$car", string.IsNullOrWhiteSpace(rev.EstadoCarroceriaLimpieza) ? "OK" : rev.EstadoCarroceriaLimpieza.Trim());
            cmd.Parameters.AddWithValue("$obs", rev.Observaciones?.Trim() ?? string.Empty);
            rev.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        if (rev.KilometrajeRegistrado > 0)
        {
            using var upKm = conn.CreateCommand();
            upKm.Transaction = tx;
            upKm.CommandText = @"
UPDATE VehiculosFlotilla
SET KilometrajeActual = CASE WHEN $km > KilometrajeActual THEN $km ELSE KilometrajeActual END
WHERE Id = $vid;";
            upKm.Parameters.AddWithValue("$km", rev.KilometrajeRegistrado);
            upKm.Parameters.AddWithValue("$vid", rev.VehiculoId);
            upKm.ExecuteNonQuery();
        }

        tx.Commit();

        rev.FolioRevision = folio;
        rev.Fecha = fecha;
        var veh = ObtenerVehiculosFlotilla().FirstOrDefault(v => v.Id == rev.VehiculoId);
        if (veh != null)
        {
            rev.CodigoVehiculo = veh.CodigoVehiculo;
            rev.VehiculoDescripcion = veh.MarcaModelo;
            rev.Placas = veh.Placas;
        }

        RegistrarBitacoraUsuario(
            "Flotilla / Revisión Semanal",
            "Check-list Semanal de Unidad",
            folio,
            $"{rev.CodigoVehiculo} ({rev.Placas}): Km {rev.KilometrajeRegistrado:N0} | Combustible: {rev.NivelCombustible} | {rev.ResumenEstadoFisico}");

        return rev;
    }

    public static void RegistrarMantenimientoGarantiaHerramienta(
        int herramientaId,
        string fechaCompra,
        string fechaFinGarantia,
        decimal costoAdquisicion,
        decimal gastoMantenimientoSumar,
        string motivoMantenimiento)
    {
        if (herramientaId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE HerramientasActivos
SET FechaCompra = $fc,
    FechaFinGarantia = $fg,
    CostoAdquisicion = $ca,
    GastoMantenimientoAcumulado = GastoMantenimientoAcumulado + $gm,
    NotasEstado = CASE WHEN $mot <> '' THEN $mot ELSE NotasEstado END
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$fc", fechaCompra?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$fg", fechaFinGarantia?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$ca", (double)Math.Max(0m, costoAdquisicion));
        cmd.Parameters.AddWithValue("$gm", (double)Math.Max(0m, gastoMantenimientoSumar));
        cmd.Parameters.AddWithValue("$mot", motivoMantenimiento?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$id", herramientaId);
        cmd.ExecuteNonQuery();

        var her = ObtenerHerramientas().FirstOrDefault(h => h.Id == herramientaId);
        if (her != null)
        {
            RegistrarBitacoraUsuario(
                "Herramientas / TCO y Garantías",
                gastoMantenimientoSumar > 0 ? "Cargo de Reparación / Mantenimiento" : "Actualización de Garantía / Costo",
                her.CodigoIndividual,
                $"{her.ProductoNombre}: Costo Adq. {her.CostoAdquisicion:C2}, Mant. Acum. {her.GastoMantenimientoAcumulado:C2} ({her.PorcentajeMantenimientoSobreCosto:0.#}%), Garantía: {her.EstadoGarantiaTexto}. {motivoMantenimiento}");

            if (her.AlertaReemplazoTco && gastoMantenimientoSumar > 0)
            {
                RegistrarCheckpoint(
                    "TCO Herramientas: Alerta de Reemplazo (>=50%)",
                    "CP-TCO",
                    her.CodigoIndividual,
                    "Mantenimiento",
                    "Evaluar Reemplazo por Costo Total",
                    ObtenerRolOperativoActivo(),
                    "Compras, Dirección",
                    $"La herramienta {her.CodigoIndividual} ({her.ProductoNombre}) acumula {her.GastoMantenimientoAcumulado:C2} en reparaciones ({her.PorcentajeMantenimientoSobreCosto:0.#}% de su valor de compra {her.CostoAdquisicion:C2}). Conviene evaluar sustitución.");
            }
        }
    }

    // ==================== FASE 4: ECONOMÍA INDUSTRIAL (MATRIZ KRALJIC, PPV, INFLACIÓN Y AUDITORÍA DE AGENCIA) ====================

    public static List<ItemMatrizKraljic> ObtenerAnalisisMatrizKraljic()
    {
        var productos = ObtenerProductos();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        var gastoPorProducto = new Dictionary<int, (decimal GastoTotal, int Frecuencia, int NumProveedores, decimal MejorPrecio, int MejorProvId, string MejorProvNombre)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ocp.ProductoId,
       COALESCE(SUM(ocp.CantidadPedida * ocp.PrecioUnitarioSinIva * 1.16), 0),
       COUNT(DISTINCT oc.Id),
       COUNT(DISTINCT oc.ProveedorId),
       COALESCE(MIN(CASE WHEN ocp.PrecioUnitarioSinIva > 0 THEN ocp.PrecioUnitarioSinIva ELSE NULL END), 0)
FROM OrdenCompraPartidas ocp
JOIN OrdenesCompra oc ON oc.Id = ocp.OrdenCompraId
WHERE ocp.ProductoId > 0 AND oc.Estatus != 'Cancelada'
GROUP BY ocp.ProductoId;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int pid = r.GetInt32(0);
                decimal gasto = (decimal)r.GetDouble(1);
                int frec = r.GetInt32(2);
                int nProvs = r.GetInt32(3);
                decimal minP = (decimal)r.GetDouble(4);
                gastoPorProducto[pid] = (gasto, frec, nProvs, minP, 0, "Sin historial");
            }
        }

        // También sumar cotizaciones de BancoPreciosHistorico para enriquecer número de proveedores y mejor precio
        using (var cmdCot = conn.CreateCommand())
        {
            cmdCot.CommandText = @"
SELECT bp.ProductoId,
       bp.ProveedorId,
       COALESCE(NULLIF(TRIM(pr.NombreComercial), ''), pr.RazonSocial),
       bp.PrecioSinIva
FROM BancoPreciosHistorico bp
JOIN Proveedores pr ON pr.Id = bp.ProveedorId
WHERE bp.ProductoId > 0 AND bp.PrecioSinIva > 0
ORDER BY bp.PrecioSinIva ASC;";
            var provSetByProd = new Dictionary<int, HashSet<int>>();
            using var r = cmdCot.ExecuteReader();
            while (r.Read())
            {
                int pid = r.GetInt32(0);
                int provId = r.GetInt32(1);
                string provNom = r.IsDBNull(2) ? "Proveedor" : r.GetString(2);
                decimal precio = (decimal)r.GetDouble(3);

                if (!provSetByProd.TryGetValue(pid, out var set))
                {
                    set = new HashSet<int>();
                    provSetByProd[pid] = set;
                }
                set.Add(provId);

                if (gastoPorProducto.TryGetValue(pid, out var cur))
                {
                    decimal mejorP = (cur.MejorPrecio <= 0m || precio < cur.MejorPrecio) ? precio : cur.MejorPrecio;
                    int mejorId = (cur.MejorProvId == 0 || precio <= cur.MejorPrecio) ? provId : cur.MejorProvId;
                    string mejorNom = (cur.MejorProvId == 0 || precio <= cur.MejorPrecio) ? provNom : cur.MejorProvNombre;
                    int maxProvs = Math.Max(cur.NumProveedores, set.Count);
                    gastoPorProducto[pid] = (cur.GastoTotal, cur.Frecuencia, maxProvs, mejorP, mejorId, mejorNom);
                }
                else
                {
                    gastoPorProducto[pid] = (0m, 0, set.Count, precio, provId, provNom);
                }
            }
        }

        var valoresImpacto = productos.Select(p =>
        {
            decimal g = gastoPorProducto.TryGetValue(p.Id, out var info) ? info.GastoTotal : 0m;
            decimal valorStock = p.StockActual * p.CostoUnitarioAlmacen;
            return Math.Max(g, valorStock);
        }).Where(v => v > 0m).OrderByDescending(v => v).ToList();

        decimal umbralAltoImpacto = valoresImpacto.Count > 0
            ? valoresImpacto[Math.Min(valoresImpacto.Count - 1, Math.Max(0, valoresImpacto.Count / 3))]
            : 2500m;
        if (umbralAltoImpacto <= 0m) umbralAltoImpacto = 1500m;

        var resultado = new List<ItemMatrizKraljic>();
        foreach (var p in productos)
        {
            gastoPorProducto.TryGetValue(p.Id, out var stat);
            decimal gasto = stat.GastoTotal;
            decimal costoBaseSinIva = p.UltimoCostoCompra > 0m ? Math.Round(p.UltimoCostoCompra / 1.16m, 2) : 0m;
            decimal impactoRef = Math.Max(gasto, Math.Max(p.StockActual, Math.Max(1m, p.StockMinimo)) * p.CostoUnitarioAlmacen);
            int numProvs = stat.NumProveedores;
            decimal mejorPrecio = stat.MejorPrecio > 0m ? stat.MejorPrecio : costoBaseSinIva;
            string mejorProv = stat.MejorProvId > 0 ? stat.MejorProvNombre : (costoBaseSinIva > 0m ? "Precio Catálogo" : "Sin cotizar");

            bool altoImpacto = impactoRef >= umbralAltoImpacto;
            bool altoRiesgo = numProvs <= 1 && (p.StockMinimo > 0 && p.StockActual < p.StockMinimo || string.IsNullOrWhiteSpace(p.Familia) || p.EsHerramienta);
            if (numProvs == 1 && altoImpacto) altoRiesgo = true;

            string cuadrante;
            string estrategia;
            if (altoImpacto && altoRiesgo)
            {
                cuadrante = "Estratégico (Crítico)";
                estrategia = "Alianza con proveedor clave, asegurar stock de seguridad y homologar sustituto.";
            }
            else if (altoImpacto && !altoRiesgo)
            {
                cuadrante = "Apalancado (Volumen / Competencia)";
                estrategia = "Licitar por Familia Genérica entre múltiples proveedores para maximizar ahorro.";
            }
            else if (!altoImpacto && altoRiesgo)
            {
                cuadrante = "Cuello de Botella (Riesgo Operativo)";
                estrategia = "Estandarizar a Familia Genérica o mantener reserva mínima para no frenar obra.";
            }
            else
            {
                cuadrante = "Rutinario / No Crítico";
                estrategia = "Automatizar compra en 1 clic con mejor precio histórico vigente; minimizar burocracia.";
            }

            resultado.Add(new ItemMatrizKraljic
            {
                ProductoId = p.Id,
                Codigo = p.CodigoInterno,
                ProductoNombre = p.Nombre,
                Familia = string.IsNullOrWhiteSpace(p.Familia) ? "General" : p.Familia,
                Categoria = p.CategoriaNombre,
                GastoAcumulado = Math.Round(gasto, 2),
                FrecuenciaCompra = stat.Frecuencia,
                ProveedoresDisponibles = numProvs,
                MejorPrecioHistoricoSinIva = Math.Round(mejorPrecio, 2),
                MejorProveedorId = stat.MejorProvId,
                MejorProveedorNombre = mejorProv,
                StockActual = p.StockActual,
                StockMinimo = p.StockMinimo,
                CuadranteKraljic = cuadrante,
                NivelImpactoFinanciero = altoImpacto ? "Alto" : "Bajo",
                NivelRiesgoSuministro = altoRiesgo ? "Alto (1 o 0 prov.)" : $"Bajo ({numProvs} provs.)",
                EstrategiaRecomendada = estrategia
            });
        }

        return resultado
            .OrderByDescending(x => x.GastoAcumulado)
            .ThenByDescending(x => x.FrecuenciaCompra)
            .ThenBy(x => x.Codigo)
            .ToList();
    }

    public static List<ItemVariacionPrecioPpv> ObtenerAnalisisPpvEInflacionInterna()
    {
        var productos = ObtenerProductos().ToDictionary(p => p.Id, p => p);
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        var comprasPorProd = new Dictionary<int, List<(decimal PrecioUnitario, decimal Cantidad, string Fecha, string Proveedor)>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ocp.ProductoId,
       ocp.PrecioUnitarioSinIva,
       ocp.CantidadPedida,
       oc.Fecha,
       COALESCE(NULLIF(TRIM(pr.NombreComercial), ''), pr.RazonSocial)
FROM OrdenCompraPartidas ocp
JOIN OrdenesCompra oc ON oc.Id = ocp.OrdenCompraId
JOIN Proveedores pr ON pr.Id = oc.ProveedorId
WHERE ocp.ProductoId > 0 AND ocp.PrecioUnitarioSinIva > 0 AND oc.Estatus != 'Cancelada'
ORDER BY oc.Id DESC, ocp.Id DESC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int pid = r.GetInt32(0);
                decimal pu = (decimal)r.GetDouble(1);
                decimal cant = (decimal)r.GetDouble(2);
                string fecha = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                string prov = r.IsDBNull(4) ? "Proveedor" : r.GetString(4);

                if (!comprasPorProd.TryGetValue(pid, out var list))
                {
                    list = new List<(decimal, decimal, string, string)>();
                    comprasPorProd[pid] = list;
                }
                list.Add((pu, cant, fecha, prov));
            }
        }

        var resultado = new List<ItemVariacionPrecioPpv>();
        foreach (var kv in comprasPorProd)
        {
            if (!productos.TryGetValue(kv.Key, out var prod)) continue;
            var hist = kv.Value;
            if (hist.Count == 0) continue;

            var ultima = hist[0];
            decimal promedioHist;
            decimal costoRefSinIva = prod.UltimoCostoCompra > 0m ? Math.Round(prod.UltimoCostoCompra / 1.16m, 2) : ultima.PrecioUnitario;
            if (hist.Count > 1)
            {
                var previas = hist.Skip(1).ToList();
                decimal sumaImporte = previas.Sum(x => x.PrecioUnitario * Math.Max(1m, x.Cantidad));
                decimal sumaCant = previas.Sum(x => Math.Max(1m, x.Cantidad));
                promedioHist = sumaCant > 0m ? sumaImporte / sumaCant : previas.Average(x => x.PrecioUnitario);
            }
            else
            {
                promedioHist = costoRefSinIva;
            }

            var item = new ItemVariacionPrecioPpv
            {
                ProductoId = prod.Id,
                Familia = string.IsNullOrWhiteSpace(prod.Familia) ? "General" : prod.Familia,
                ProductoCodigo = prod.CodigoInterno,
                ProductoNombre = prod.Nombre,
                PrecioPromedioHistorico = Math.Round(promedioHist, 2),
                UltimoPrecioPagado = Math.Round(ultima.PrecioUnitario, 2),
                CantidadCompradaReciente = Math.Round(ultima.Cantidad, 2),
                UltimoProveedor = ultima.Proveedor,
                UltimaFechaCompra = ultima.Fecha
            };

            if (item.VariacionUnitariaPct >= 10m)
                item.DiagnosticoInflacion = "Alerta Inflación (>= +10%) — Licitar por Familia";
            else if (item.VariacionUnitariaPct > 2m)
                item.DiagnosticoInflacion = "Alza Moderada";
            else if (item.VariacionUnitariaPct <= -5m)
                item.DiagnosticoInflacion = "Ahorro Destacado (PPV Positivo)";
            else
                item.DiagnosticoInflacion = "Precio Estable";

            resultado.Add(item);
        }

        if (resultado.Count == 0)
        {
            foreach (var p in productos.Values.Where(x => x.UltimoCostoCompra > 0).Take(25))
            {
                decimal refSinIva = Math.Round(p.UltimoCostoCompra / 1.16m, 2);
                resultado.Add(new ItemVariacionPrecioPpv
                {
                    ProductoId = p.Id,
                    Familia = string.IsNullOrWhiteSpace(p.Familia) ? "General" : p.Familia,
                    ProductoCodigo = p.CodigoInterno,
                    ProductoNombre = p.Nombre,
                    PrecioPromedioHistorico = refSinIva,
                    UltimoPrecioPagado = refSinIva,
                    CantidadCompradaReciente = Math.Max(1m, p.StockActual),
                    UltimoProveedor = "Catálogo Base",
                    UltimaFechaCompra = DateTime.Today.ToString("yyyy-MM-dd"),
                    DiagnosticoInflacion = "Referencia Base"
                });
            }
        }

        return resultado
            .OrderBy(x => x.AhorroOSobreprecioTotalPpv)
            .ThenByDescending(x => Math.Abs(x.VariacionUnitariaPct))
            .ToList();
    }

    public static List<ItemAuditoriaCuadrilla> ObtenerAuditoriaConsumoYMermaPorCuadrilla()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        var mapa = new Dictionary<string, ItemAuditoriaCuadrilla>(StringComparer.OrdinalIgnoreCase);

        ItemAuditoriaCuadrilla ObtenerOCrear(string nombre, string proy = "")
        {
            string key = string.IsNullOrWhiteSpace(nombre) ? "Personal Operativo" : nombre.Trim();
            if (!mapa.TryGetValue(key, out var item))
            {
                item = new ItemAuditoriaCuadrilla
                {
                    ResponsableOCuadrilla = key,
                    ProyectoReciente = string.IsNullOrWhiteSpace(proy) ? "General" : proy
                };
                mapa[key] = item;
            }
            else if (!string.IsNullOrWhiteSpace(proy) && (item.ProyectoReciente == "General" || string.IsNullOrWhiteSpace(item.ProyectoReciente)))
            {
                item.ProyectoReciente = proy;
            }
            return item;
        }

        // 1. Salidas de Almacén
        using (var cmdSal = conn.CreateCommand())
        {
            cmdSal.CommandText = @"
SELECT COALESCE(NULLIF(TRIM(sa.PersonaRecibe), ''), COALESCE(e.Nombre, 'Personal Operativo')),
       COALESCE(p.Nombre, 'General'),
       COUNT(DISTINCT sa.Id),
       COALESCE(SUM(sp.CantidadEntregada - COALESCE(sp.CantidadDevuelta, 0)), 0),
       COALESCE(SUM((sp.CantidadEntregada - COALESCE(sp.CantidadDevuelta, 0)) * COALESCE(sp.CostoUnitarioAlmacen, 0)), 0)
FROM SalidasAlmacen sa
LEFT JOIN Encargados e ON e.Id = sa.EncargadoId
LEFT JOIN SalidaPartidas sp ON sp.SalidaAlmacenId = sa.Id
LEFT JOIN Proyectos p ON p.Id = sa.ProyectoId
GROUP BY COALESCE(NULLIF(TRIM(sa.PersonaRecibe), ''), COALESCE(e.Nombre, 'Personal Operativo'));";
            using var r = cmdSal.ExecuteReader();
            while (r.Read())
            {
                string resp = r.IsDBNull(0) ? "Personal Operativo" : r.GetString(0);
                string proy = r.IsDBNull(1) ? "General" : r.GetString(1);
                var it = ObtenerOCrear(resp, proy);
                it.ValesSalidaCount += r.GetInt32(2);
                it.UnidadesRetiradas += (decimal)r.GetDouble(3);
                it.CostoEstimadoRetirado += (decimal)r.GetDouble(4);
            }
        }

        // 2. Sobrantes de obra reingresados y Ajustes/Mermas en EntradasAlmacen
        using (var cmdSob = conn.CreateCommand())
        {
            cmdSob.CommandText = @"
SELECT ea.Folio, ea.TipoEntrada, ea.ProveedorNombre, COALESCE(SUM(ep.CantidadIngresadaAlmacen), 0)
FROM EntradasAlmacen ea
JOIN EntradaPartidas ep ON ep.EntradaAlmacenId = ea.Id
WHERE ea.TipoEntrada LIKE '%Sobrante%' OR ea.OrdenCompraFolio = 'SOBRANTE-OBRA' OR ea.Folio LIKE 'AJU-%'
GROUP BY ea.Id;";
            using var r = cmdSob.ExecuteReader();
            while (r.Read())
            {
                string folio = r.IsDBNull(0) ? "" : r.GetString(0);
                string tipo = r.IsDBNull(1) ? "" : r.GetString(1);
                string provNom = r.IsDBNull(2) ? "Operación" : r.GetString(2);
                decimal cant = (decimal)r.GetDouble(3);

                string respLimpio = provNom.Replace("Devolvió:", "", StringComparison.OrdinalIgnoreCase)
                                           .Replace("Devolución:", "", StringComparison.OrdinalIgnoreCase)
                                           .Trim();
                var it = ObtenerOCrear(respLimpio);
                if (folio.StartsWith("AJU-", StringComparison.OrdinalIgnoreCase) && cant < 0m)
                {
                    it.IncidenciasOMermasRegistradas += 1;
                }
                else if (cant > 0m)
                {
                    it.UnidadesSobranteReingresadas += cant;
                }
            }
        }

        // 3. Préstamos de herramientas y kits
        using (var cmdPre = conn.CreateCommand())
        {
            cmdPre.CommandText = @"
SELECT COALESCE(NULLIF(TRIM(ph.PersonaRecibe), ''), COALESCE(e.Nombre, 'Técnico')),
       SUM(CASE WHEN ph.Notas LIKE '%KIT-%' THEN 1 ELSE 0 END),
       SUM(CASE WHEN ph.Estatus = 'Activo' OR ph.Estatus LIKE '%Parcial%' THEN 1 ELSE 0 END),
       SUM(CASE WHEN ph.Estatus LIKE '%Incidencia%' THEN 1 ELSE 0 END)
FROM PrestamosHerramienta ph
LEFT JOIN Encargados e ON e.Id = ph.EncargadoId
GROUP BY COALESCE(NULLIF(TRIM(ph.PersonaRecibe), ''), COALESCE(e.Nombre, 'Técnico'));";
            using var r = cmdPre.ExecuteReader();
            while (r.Read())
            {
                string resp = r.IsDBNull(0) ? "Técnico" : r.GetString(0);
                var it = ObtenerOCrear(resp);
                it.KitsDespachados += r.IsDBNull(1) ? 0 : r.GetInt32(1);
                it.HerramientasEnResguardoActivo += r.IsDBNull(2) ? 0 : r.GetInt32(2);
                it.IncidenciasOMermasRegistradas += r.IsDBNull(3) ? 0 : r.GetInt32(3);
            }
        }

        foreach (var it in mapa.Values)
        {
            if (it.IncidenciasOMermasRegistradas > 0)
                it.EstadoControlAgencia = $"Auditar ({it.IncidenciasOMermasRegistradas} incidencia(s))";
            else if (it.HerramientasEnResguardoActivo >= 5)
                it.EstadoControlAgencia = $"Resguardo Alto ({it.HerramientasEnResguardoActivo} vales activos)";
            else if (it.UnidadesSobranteReingresadas > 0)
                it.EstadoControlAgencia = "Excelente (Retorna sobrante)";
            else
                it.EstadoControlAgencia = "Controlado";
        }

        return mapa.Values
            .OrderByDescending(x => x.IncidenciasOMermasRegistradas)
            .ThenByDescending(x => x.HerramientasEnResguardoActivo)
            .ThenByDescending(x => x.CostoEstimadoRetirado)
            .ToList();
    }

    public static (int PartidasAdjudicadas, decimal MontoTotalEstimadoConIva) AutoAdjudicarRutinariosMejorPrecioHistorico(int solicitudId)
    {
        if (solicitudId <= 0) return (0, 0m);
        var sol = ObtenerSolicitudes().FirstOrDefault(s => s.Id == solicitudId);
        if (sol == null) return (0, 0m);

        var kraljicMap = ObtenerAnalisisMatrizKraljic().ToDictionary(k => k.ProductoId, k => k);
        var proveedores = ObtenerProveedores();
        int adjudicados = 0;
        decimal montoAcum = 0m;
        string hoy = DateTime.Today.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        foreach (var sp in sol.Partidas)
        {
            if (sp.TieneOcGenerada || sp.ProductoId <= 0) continue;
            if (!kraljicMap.TryGetValue(sp.ProductoId, out var k)) continue;

            // Si ya tiene cotizaciones capturadas en Opción 1, 2 o 3, elegir la más económica
            int mejorSlot = 0;
            decimal mejorPrecio = decimal.MaxValue;
            if (sp.Proveedor1Id > 0 && sp.PrecioSinIva1 > 0m && sp.PrecioSinIva1 < mejorPrecio)
            {
                mejorPrecio = sp.PrecioSinIva1;
                mejorSlot = 1;
            }
            if (sp.Proveedor2Id > 0 && sp.PrecioSinIva2 > 0m && sp.PrecioSinIva2 < mejorPrecio)
            {
                mejorPrecio = sp.PrecioSinIva2;
                mejorSlot = 2;
            }
            if (sp.Proveedor3Id > 0 && sp.PrecioSinIva3 > 0m && sp.PrecioSinIva3 < mejorPrecio)
            {
                mejorPrecio = sp.PrecioSinIva3;
                mejorSlot = 3;
            }

            if (mejorSlot > 0)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE SolicitudPartidas SET ProveedorGanadorIndice = $slot WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$slot", mejorSlot);
                cmd.Parameters.AddWithValue("$id", sp.Id);
                cmd.ExecuteNonQuery();
                adjudicados++;
                montoAcum += sp.CantidadSolicitada * mejorPrecio * 1.16m;
            }
            else if (k.MejorPrecioHistoricoSinIva > 0m)
            {
                int provUsarId = k.MejorProveedorId > 0
                    ? k.MejorProveedorId
                    : (proveedores.FirstOrDefault()?.Id ?? 0);
                if (provUsarId > 0)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
UPDATE SolicitudPartidas
SET Proveedor1Id = $pv1,
    PrecioSinIva1 = $pr1,
    DiasEntrega1 = 1,
    DiasVigencia1 = 15,
    FechaCotizacion1 = $fc,
    NotasCotizacion1 = 'Auto-cotizado (Mejor Precio Histórico Kraljic)',
    ProveedorGanadorIndice = 1,
    FechaCotizacion = $fc
WHERE Id = $id;";
                    cmd.Parameters.AddWithValue("$pv1", provUsarId);
                    cmd.Parameters.AddWithValue("$pr1", (double)k.MejorPrecioHistoricoSinIva);
                    cmd.Parameters.AddWithValue("$fc", hoy);
                    cmd.Parameters.AddWithValue("$id", sp.Id);
                    cmd.ExecuteNonQuery();
                    adjudicados++;
                    montoAcum += sp.CantidadSolicitada * k.MejorPrecioHistoricoSinIva * 1.16m;
                }
            }
        }

        if (adjudicados > 0)
        {
            RegistrarBitacoraUsuario(
                "Comparativa / Kraljic",
                "Auto-Adjudicación de Insumos Rutinarios",
                sol.Folio,
                $"Se auto-adjudicaron {adjudicados} partida(s) con su mejor precio histórico ({montoAcum:C2} c/IVA).");
        }

        return (adjudicados, Math.Round(montoAcum, 2));
    }

    // ==================== NOTIFICACIONES POR CORREO A GERENTES (GMAIL SMTP -> CORREO CORPORATIVO) ====================

    public static event Action<RegistroNotificacionCorreo>? OnNotificacionCorreoProcesada;

    public static ConfiguracionSmtpCorreo ObtenerConfiguracionSmtpCorreo()
    {
        var cfg = new ConfiguracionSmtpCorreo();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Clave, Valor FROM ConfiguracionSistema WHERE Clave LIKE 'SmtpCorreo_%';";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string k = r.GetString(0);
            string v = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            switch (k)
            {
                case "SmtpCorreo_Activo":
                    cfg.Activo = v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SmtpCorreo_Servidor":
                    if (!string.IsNullOrWhiteSpace(v)) cfg.ServidorSmtp = v.Trim();
                    break;
                case "SmtpCorreo_Puerto":
                    if (int.TryParse(v, out int p) && p > 0) cfg.PuertoSmtp = p;
                    break;
                case "SmtpCorreo_UsarSsl":
                    cfg.UsarSsl = v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SmtpCorreo_RemitenteGmail":
                    cfg.CorreoRemitenteGmail = v.Trim();
                    break;
                case "SmtpCorreo_NombreRemitente":
                    if (!string.IsNullOrWhiteSpace(v)) cfg.NombreMostrarRemitente = v.Trim();
                    break;
                case "SmtpCorreo_ContrasenaApp":
                    cfg.ContrasenaAplicacionGmail = v.Trim();
                    break;
                case "SmtpCorreo_CopiaFijaGerencia":
                    cfg.CorreoCopiaFijaGerencia = v.Trim();
                    break;
                case "SmtpCorreo_TiempoRegistro":
                    if (!string.IsNullOrWhiteSpace(v)) cfg.TiempoAproxRegistroDefault = v.Trim();
                    break;
                case "SmtpCorreo_TiempoComparativa":
                    if (!string.IsNullOrWhiteSpace(v)) cfg.TiempoAproxComparativaDefault = v.Trim();
                    break;
                case "SmtpCorreo_ImapRecepcionActiva":
                    cfg.ImapRecepcionActiva = v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SmtpCorreo_ImapAutoCrearPorAsunto":
                    cfg.ImapAutoCrearPorAsunto = v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "SmtpCorreo_PalabrasClaveAsuntoImap":
                    if (!string.IsNullOrWhiteSpace(v)) cfg.PalabrasClaveAsuntoImap = v.Trim();
                    break;
            }
        }
        return cfg;
    }

    public static void GuardarConfiguracionSmtpCorreo(ConfiguracionSmtpCorreo cfg)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        void SetVal(string k, string v)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO ConfiguracionSistema (Clave, Valor) VALUES ($k, $v);";
            cmd.Parameters.AddWithValue("$k", k);
            cmd.Parameters.AddWithValue("$v", v ?? string.Empty);
            cmd.ExecuteNonQuery();
        }

        SetVal("SmtpCorreo_Activo", cfg.Activo ? "1" : "0");
        SetVal("SmtpCorreo_Servidor", string.IsNullOrWhiteSpace(cfg.ServidorSmtp) ? "smtp.gmail.com" : cfg.ServidorSmtp.Trim());
        SetVal("SmtpCorreo_Puerto", (cfg.PuertoSmtp > 0 ? cfg.PuertoSmtp : 587).ToString());
        SetVal("SmtpCorreo_UsarSsl", cfg.UsarSsl ? "1" : "0");
        SetVal("SmtpCorreo_RemitenteGmail", cfg.CorreoRemitenteGmail?.Trim() ?? string.Empty);
        SetVal("SmtpCorreo_NombreRemitente", string.IsNullOrWhiteSpace(cfg.NombreMostrarRemitente) ? "Compras y Almacén Lebaref" : cfg.NombreMostrarRemitente.Trim());
        SetVal("SmtpCorreo_ContrasenaApp", cfg.ContrasenaAplicacionGmail?.Replace(" ", "").Trim() ?? string.Empty);
        SetVal("SmtpCorreo_CopiaFijaGerencia", cfg.CorreoCopiaFijaGerencia?.Trim() ?? string.Empty);
        SetVal("SmtpCorreo_TiempoRegistro", string.IsNullOrWhiteSpace(cfg.TiempoAproxRegistroDefault) ? "24 a 48 hrs" : cfg.TiempoAproxRegistroDefault.Trim());
        SetVal("SmtpCorreo_TiempoComparativa", string.IsNullOrWhiteSpace(cfg.TiempoAproxComparativaDefault) ? "2 a 4 días hábiles" : cfg.TiempoAproxComparativaDefault.Trim());
        SetVal("SmtpCorreo_ImapRecepcionActiva", cfg.ImapRecepcionActiva ? "1" : "0");
        SetVal("SmtpCorreo_ImapAutoCrearPorAsunto", cfg.ImapAutoCrearPorAsunto ? "1" : "0");
        SetVal("SmtpCorreo_PalabrasClaveAsuntoImap", string.IsNullOrWhiteSpace(cfg.PalabrasClaveAsuntoImap) ? "SOLICITUD, COTIZAR, COTIZACION, PEDIDO, REQUERIMIENTO" : cfg.PalabrasClaveAsuntoImap.Trim());

        tx.Commit();
    }

    public static bool YaSeEnvioNotificacionEtapa(string solicitudFolio, string etapaClave)
    {
        if (string.IsNullOrWhiteSpace(solicitudFolio) || string.IsNullOrWhiteSpace(etapaClave))
            return false;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*) FROM NotificacionesCorreo
WHERE UPPER(TRIM(SolicitudFolio)) = UPPER(TRIM($fol))
  AND UPPER(TRIM(EtapaClave)) = UPPER(TRIM($et));";
        cmd.Parameters.AddWithValue("$fol", solicitudFolio.Trim());
        cmd.Parameters.AddWithValue("$et", etapaClave.Trim());
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static List<RegistroNotificacionCorreo> ObtenerNotificacionesCorreo(string? filtroFolio = null)
    {
        var list = new List<RegistroNotificacionCorreo>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, FechaHora, SolicitudFolio, OrdenCompraFolio, EtapaClave, EtapaNombre,
       GerenteNombre, CorreoDestino, Asunto, MensajePrincipal, CuerpoHtml,
       TiempoOLugarTexto, EstadoEnvio, DetalleResultado
FROM NotificacionesCorreo
WHERE ($f IS NULL OR SolicitudFolio LIKE '%' || $f || '%' OR OrdenCompraFolio LIKE '%' || $f || '%' OR GerenteNombre LIKE '%' || $f || '%')
ORDER BY Id DESC
LIMIT 300;";
        cmd.Parameters.AddWithValue("$f", string.IsNullOrWhiteSpace(filtroFolio) ? DBNull.Value : filtroFolio.Trim());
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RegistroNotificacionCorreo
            {
                Id = r.GetInt32(0),
                FechaHora = r.GetString(1),
                SolicitudFolio = r.GetString(2),
                OrdenCompraFolio = r.GetString(3),
                EtapaClave = r.GetString(4),
                EtapaNombre = r.GetString(5),
                GerenteNombre = r.GetString(6),
                CorreoDestino = r.GetString(7),
                Asunto = r.GetString(8),
                MensajePrincipal = r.GetString(9),
                CuerpoHtml = r.GetString(10),
                TiempoOLugarTexto = r.GetString(11),
                EstadoEnvio = r.GetString(12),
                DetalleResultado = r.GetString(13)
            });
        }
        return list;
    }

    public static RegistroNotificacionCorreo? NotificarCambioEstadoCotizacionGerente(
        int solicitudId,
        string? folioOcRelacionado,
        string etapaClave,
        string? tiempoAproxPersonalizado = null,
        string? lugarEntregaPersonalizado = null)
    {
        try
        {
            var sol = ObtenerSolicitudes().FirstOrDefault(s => s.Id == solicitudId);
            if (sol == null && !string.IsNullOrWhiteSpace(folioOcRelacionado))
            {
                var ocRef = ObtenerOrdenesCompra().FirstOrDefault(o => string.Equals(o.Folio, folioOcRelacionado, StringComparison.OrdinalIgnoreCase));
                if (ocRef != null && ocRef.SolicitudId > 0)
                    sol = ObtenerSolicitudes().FirstOrDefault(s => s.Id == ocRef.SolicitudId);
            }

            string folioSol = sol?.Folio ?? (!string.IsNullOrWhiteSpace(folioOcRelacionado) ? folioOcRelacionado! : "SOL-S/N");
            var cfg = ObtenerConfiguracionSmtpCorreo();
            var encargados = ObtenerEncargados();
            Encargado? gerente = null;
            if (sol != null && sol.EncargadoId > 0)
                gerente = encargados.FirstOrDefault(e => e.Id == sol.EncargadoId);
            if (gerente == null && sol != null && !string.IsNullOrWhiteSpace(sol.EncargadoNombre))
                gerente = encargados.FirstOrDefault(e => string.Equals(e.Nombre, sol.EncargadoNombre, StringComparison.OrdinalIgnoreCase));

            string gerenteNombre = gerente?.Nombre ?? sol?.EncargadoNombre ?? "Gerente Solicitante";
            string correoGerente = gerente?.Correo?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(correoGerente) && !string.IsNullOrWhiteSpace(cfg.CorreoCopiaFijaGerencia))
                correoGerente = cfg.CorreoCopiaFijaGerencia.Trim();

            string etapaNombre;
            string tiempoOLugar;
            string fraseExacta;

            switch (etapaClave)
            {
                case "1_CREADA":
                {
                    etapaNombre = "1. Cotización / Solicitud Creada";
                    bool esUrgente = (sol?.Notas ?? string.Empty).Contains("Urgente", StringComparison.OrdinalIgnoreCase);
                    tiempoOLugar = !string.IsNullOrWhiteSpace(tiempoAproxPersonalizado)
                        ? tiempoAproxPersonalizado.Trim()
                        : (esUrgente ? "4 a 12 hrs (Prioridad Urgente)" : cfg.TiempoAproxRegistroDefault);
                    fraseExacta = $"Tu cotización folio {folioSol} registrada, tiempo aprox de {tiempoOLugar}";
                    break;
                }
                case "2_COMPARATIVA":
                {
                    etapaNombre = "2. En Cuadro Comparativo";
                    tiempoOLugar = !string.IsNullOrWhiteSpace(tiempoAproxPersonalizado)
                        ? tiempoAproxPersonalizado.Trim()
                        : cfg.TiempoAproxComparativaDefault;
                    fraseExacta = $"Tu cotización folio {folioSol} está recibiendo avances, tiempo aprox de entrega {tiempoOLugar}";
                    break;
                }
                case "3_OC_EMITIDA":
                {
                    etapaNombre = "3. Órdenes de Compra Emitidas (Esperando Pago)";
                    tiempoOLugar = !string.IsNullOrWhiteSpace(folioOcRelacionado) ? $"OC: {folioOcRelacionado}" : "En espera de liberación de pago";
                    fraseExacta = $"Tu cotización folio {folioSol} está esperando pago";
                    break;
                }
                case "4_PAGADA":
                {
                    etapaNombre = "4. Pago Realizado";
                    tiempoOLugar = !string.IsNullOrWhiteSpace(tiempoAproxPersonalizado)
                        ? tiempoAproxPersonalizado.Trim()
                        : "2 a 3 días hábiles";
                    fraseExacta = $"La cotización {folioSol} ya fue pagada, tiempo aprox de entrega {tiempoOLugar}";
                    break;
                }
                case "5_CONCLUIDA":
                default:
                {
                    etapaNombre = "5. Material Recibido / Folio Concluido";
                    tiempoOLugar = !string.IsNullOrWhiteSpace(lugarEntregaPersonalizado)
                        ? lugarEntregaPersonalizado.Trim()
                        : "Almacén General";
                    fraseExacta = $"El folio {folioSol} ya fue concluido con entrega en {tiempoOLugar}";
                    break;
                }
            }

            string asunto = $"[Compras Lebaref] {fraseExacta}";
            string htmlBody = ConstruirHtmlNotificacionGerente(sol, folioOcRelacionado, etapaNombre, fraseExacta, gerenteNombre, tiempoOLugar);

            var reg = new RegistroNotificacionCorreo
            {
                FechaHora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                SolicitudFolio = folioSol,
                OrdenCompraFolio = folioOcRelacionado ?? string.Empty,
                EtapaClave = etapaClave,
                EtapaNombre = etapaNombre,
                GerenteNombre = gerenteNombre,
                CorreoDestino = correoGerente,
                Asunto = asunto,
                MensajePrincipal = fraseExacta,
                CuerpoHtml = htmlBody,
                TiempoOLugarTexto = tiempoOLugar,
                EstadoEnvio = "En proceso...",
                DetalleResultado = string.Empty
            };

            using (var conn = new SqliteConnection(ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO NotificacionesCorreo (
    FechaHora, SolicitudFolio, OrdenCompraFolio, EtapaClave, EtapaNombre,
    GerenteNombre, CorreoDestino, Asunto, MensajePrincipal, CuerpoHtml,
    TiempoOLugarTexto, EstadoEnvio, DetalleResultado
) VALUES (
    $fh, $sfol, $ocfol, $ecl, $enom,
    $gnom, $cdes, $asu, $msj, $html,
    $tlug, $est, $det
);
SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$fh", reg.FechaHora);
                cmd.Parameters.AddWithValue("$sfol", reg.SolicitudFolio);
                cmd.Parameters.AddWithValue("$ocfol", reg.OrdenCompraFolio);
                cmd.Parameters.AddWithValue("$ecl", reg.EtapaClave);
                cmd.Parameters.AddWithValue("$enom", reg.EtapaNombre);
                cmd.Parameters.AddWithValue("$gnom", reg.GerenteNombre);
                cmd.Parameters.AddWithValue("$cdes", reg.CorreoDestino);
                cmd.Parameters.AddWithValue("$asu", reg.Asunto);
                cmd.Parameters.AddWithValue("$msj", reg.MensajePrincipal);
                cmd.Parameters.AddWithValue("$html", reg.CuerpoHtml);
                cmd.Parameters.AddWithValue("$tlug", reg.TiempoOLugarTexto);
                cmd.Parameters.AddWithValue("$est", reg.EstadoEnvio);
                cmd.Parameters.AddWithValue("$det", reg.DetalleResultado);
                reg.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await EnviarNotificacionCorreoSmtpInternoAsync(reg, cfg);
            });

            return reg;
        }
        catch
        {
            return null;
        }
    }

    private static string ConstruirHtmlNotificacionGerente(
        Solicitud? sol,
        string? folioOcRelacionado,
        string etapaNombre,
        string fraseExacta,
        string gerenteNombre,
        string tiempoOLugar)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'></head>");
        sb.AppendLine("<body style='font-family: Segoe UI, Arial, sans-serif; color: #1E293B; background-color: #F8FAFC; padding: 18px;'>");
        sb.AppendLine("<div style='max-width: 640px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 8px; overflow: hidden;'>");
        sb.AppendLine("<div style='background: #0F172A; color: #FFFFFF; padding: 14px 18px;'>");
        sb.AppendLine($"<div style='font-size: 11px; text-transform: uppercase; letter-spacing: 0.6px; color: #93C5FD;'>Sistema de Compras y Almacén · {System.Net.WebUtility.HtmlEncode(etapaNombre)}</div>");
        sb.AppendLine($"<div style='font-size: 17px; font-weight: 700; margin-top: 4px;'>{System.Net.WebUtility.HtmlEncode(fraseExacta)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div style='padding: 18px;'>");
        sb.AppendLine($"<p style='margin: 0 0 12px 0; font-size: 14px;'>Hola <strong>{System.Net.WebUtility.HtmlEncode(gerenteNombre)}</strong>,</p>");
        sb.AppendLine($"<div style='background: #EFF6FF; border-left: 4px solid #2563EB; padding: 12px 14px; margin-bottom: 16px; font-size: 14.5px; font-weight: 600; color: #1E3A8A;'>{System.Net.WebUtility.HtmlEncode(fraseExacta)}</div>");

        sb.AppendLine("<table style='width: 100%; border-collapse: collapse; font-size: 13px; margin-bottom: 14px;'>");
        sb.AppendLine($"<tr><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; color: #64748B; width: 38%;'>Folio de Cotización / Solicitud:</td><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; font-weight: 600;'>{System.Net.WebUtility.HtmlEncode(sol?.Folio ?? "N/A")}</td></tr>");
        if (!string.IsNullOrWhiteSpace(folioOcRelacionado))
            sb.AppendLine($"<tr><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; color: #64748B;'>Orden(es) de Compra:</td><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; font-weight: 600;'>{System.Net.WebUtility.HtmlEncode(folioOcRelacionado)}</td></tr>");
        if (sol != null)
        {
            sb.AppendLine($"<tr><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; color: #64748B;'>Departamento:</td><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0;'>{System.Net.WebUtility.HtmlEncode(sol.DepartamentoNombre)}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; color: #64748B;'>Obra / Proyecto Destino:</td><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0;'>{System.Net.WebUtility.HtmlEncode(sol.ProyectoMostrar)}</td></tr>");
        }
        sb.AppendLine($"<tr><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; color: #64748B;'>Tiempo Aprox. / Ubicación Entrega:</td><td style='padding: 6px 8px; border-bottom: 1px solid #E2E8F0; font-weight: 600; color: #047857;'>{System.Net.WebUtility.HtmlEncode(tiempoOLugar)}</td></tr>");
        sb.AppendLine("</table>");

        if (sol != null && sol.Partidas.Count > 0)
        {
            sb.AppendLine("<div style='font-size: 12.5px; font-weight: 700; margin-bottom: 6px; color: #334155;'>Artículos en la Cotización / Solicitud:</div>");
            sb.AppendLine("<table style='width: 100%; border-collapse: collapse; font-size: 12px;'>");
            sb.AppendLine("<tr style='background: #F1F5F9; text-align: left;'><th style='padding: 6px;'>Cant.</th><th style='padding: 6px;'>Unidad</th><th style='padding: 6px;'>Artículo / Concepto</th></tr>");
            foreach (var p in sol.Partidas.Take(20))
            {
                sb.AppendLine($"<tr><td style='padding: 5px 6px; border-bottom: 1px solid #F1F5F9; font-weight: 600;'>{p.CantidadSolicitada:0.##}</td><td style='padding: 5px 6px; border-bottom: 1px solid #F1F5F9;'>{System.Net.WebUtility.HtmlEncode(p.UnidadCompra)}</td><td style='padding: 5px 6px; border-bottom: 1px solid #F1F5F9;'>{System.Net.WebUtility.HtmlEncode(p.ProductoNombre)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine($"<div style='margin-top: 16px; font-size: 11px; color: #94A3B8;'>Mensaje automático generado el {DateTime.Now:dd/MM/yyyy HH:mm} por el Sistema de Compras y Almacén Lebaref.</div>");
        sb.AppendLine("</div></div></body></html>");
        return sb.ToString();
    }

    public static async System.Threading.Tasks.Task<(bool Exito, string Mensaje)> EnviarNotificacionCorreoSmtpInternoAsync(
        RegistroNotificacionCorreo reg,
        ConfiguracionSmtpCorreo? cfgOverride = null)
    {
        var cfg = cfgOverride ?? ObtenerConfiguracionSmtpCorreo();
        string nuevoEstado;
        string detalle;
        bool exito = false;

        if (!cfg.Activo)
        {
            nuevoEstado = "En Bitácora (Envío pausado)";
            detalle = "El envío automático SMTP está desactivado en la configuración.";
        }
        else if (string.IsNullOrWhiteSpace(reg.CorreoDestino))
        {
            nuevoEstado = "Sin Correo de Gerente";
            detalle = $"El gerente '{reg.GerenteNombre}' no tiene correo corporativo asignado en Directorios -> Personal y Departamentos.";
        }
        else if (!cfg.TieneCredencialesCompletas)
        {
            nuevoEstado = "Pendiente Configurar Gmail";
            detalle = "Configure su cuenta Gmail remitente y Contraseña de Aplicación en 'Configurar Correo a Gerentes'.";
        }
        else
        {
            try
            {
                using var mail = new System.Net.Mail.MailMessage();
                mail.From = new System.Net.Mail.MailAddress(cfg.CorreoRemitenteGmail.Trim(), cfg.NombreMostrarRemitente);
                foreach (var dest in reg.CorreoDestino.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string dTrim = dest.Trim();
                    if (dTrim.Contains('@'))
                        mail.To.Add(dTrim);
                }

                if (!string.IsNullOrWhiteSpace(cfg.CorreoCopiaFijaGerencia))
                {
                    foreach (var cc in cfg.CorreoCopiaFijaGerencia.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string ccTrim = cc.Trim();
                        if (ccTrim.Contains('@') && !mail.To.Any(x => string.Equals(x.Address, ccTrim, StringComparison.OrdinalIgnoreCase)))
                            mail.CC.Add(ccTrim);
                    }
                }

                mail.Subject = reg.Asunto;
                mail.Body = reg.CuerpoHtml;
                mail.IsBodyHtml = true;
                mail.BodyEncoding = System.Text.Encoding.UTF8;
                mail.SubjectEncoding = System.Text.Encoding.UTF8;

                using var smtp = new System.Net.Mail.SmtpClient(cfg.ServidorSmtp.Trim(), cfg.PuertoSmtp > 0 ? cfg.PuertoSmtp : 587)
                {
                    EnableSsl = cfg.UsarSsl,
                    DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new System.Net.NetworkCredential(
                        cfg.CorreoRemitenteGmail.Trim(),
                        cfg.ContrasenaAplicacionGmail.Replace(" ", "").Trim()),
                    Timeout = 15000
                };

                await smtp.SendMailAsync(mail);
                nuevoEstado = "Enviado (Gmail SMTP)";
                detalle = $"Enviado desde {cfg.CorreoRemitenteGmail} hacia {reg.CorreoDestino} el {DateTime.Now:HH:mm:ss}.";
                exito = true;
            }
            catch (Exception ex)
            {
                nuevoEstado = "Error SMTP";
                string passLimpia = (cfg.ContrasenaAplicacionGmail ?? string.Empty).Replace(" ", "").Trim();
                bool esGmail = (cfg.ServidorSmtp ?? string.Empty).Contains("gmail", StringComparison.OrdinalIgnoreCase);
                bool parecePasswordNormal = passLimpia.Length != 16 || passLimpia.Any(c => !char.IsLetter(c));

                if (esGmail && (ex.Message.Contains("5.7.0") || ex.Message.Contains("5.7.8") || ex.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase)))
                {
                    if (parecePasswordNormal)
                    {
                        detalle = $"Google rechazó el acceso (535 5.7.8 BadCredentials) porque se ingresó la contraseña normal de la cuenta ({passLimpia.Length} caracteres). Gmail exige una 'Contraseña de Aplicación' de 16 letras (sin números ni símbolos) generada en https://myaccount.google.com/apppasswords";
                    }
                    else
                    {
                        detalle = $"Google rechazó la Contraseña de Aplicación de 16 letras ({ex.Message}). Verifique que pertenezca a {cfg.CorreoRemitenteGmail} y que la Verificación en 2 pasos siga activa en https://myaccount.google.com/apppasswords";
                    }
                }
                else
                {
                    detalle = ex.Message;
                }
            }
        }

        reg.EstadoEnvio = nuevoEstado;
        reg.DetalleResultado = detalle;

        if (reg.Id > 0)
        {
            try
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE NotificacionesCorreo SET EstadoEnvio = $est, DetalleResultado = $det, CorreoDestino = $cor WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$est", nuevoEstado);
                cmd.Parameters.AddWithValue("$det", detalle);
                cmd.Parameters.AddWithValue("$cor", reg.CorreoDestino ?? string.Empty);
                cmd.Parameters.AddWithValue("$id", reg.Id);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignorar error secundario de actualización de bitácora
            }
        }

        try
        {
            OnNotificacionCorreoProcesada?.Invoke(reg);
        }
        catch
        {
            // Ignorar excepción de suscriptor UI
        }

        return (exito, detalle);
    }

    public static async System.Threading.Tasks.Task<(bool Exito, string Mensaje)> ProbarConexionSmtpGmailAsync(
        ConfiguracionSmtpCorreo cfg,
        string correoDestinoPrueba)
    {
        if (!cfg.TieneCredencialesCompletas)
            return (false, "Escriba el correo Gmail remitente y su Contraseña de Aplicación de 16 letras de Google.");

        string destino = string.IsNullOrWhiteSpace(correoDestinoPrueba) ? cfg.CorreoRemitenteGmail.Trim() : correoDestinoPrueba.Trim();
        var regPrueba = new RegistroNotificacionCorreo
        {
            FechaHora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            SolicitudFolio = "SOL-PRUEBA",
            OrdenCompraFolio = "OC-PRUEBA",
            EtapaClave = "1_CREADA",
            EtapaNombre = "Prueba de Conexión Gmail -> Correo Corporativo",
            GerenteNombre = "Gerente (Prueba)",
            CorreoDestino = destino,
            Asunto = $"[Compras Lebaref] Tu cotización folio SOL-PRUEBA registrada, tiempo aprox de {cfg.TiempoAproxRegistroDefault}",
            MensajePrincipal = $"Tu cotización folio SOL-PRUEBA registrada, tiempo aprox de {cfg.TiempoAproxRegistroDefault}",
            CuerpoHtml = ConstruirHtmlNotificacionGerente(null, "OC-PRUEBA", "Prueba de Configuración Gmail SMTP", $"Tu cotización folio SOL-PRUEBA registrada, tiempo aprox de {cfg.TiempoAproxRegistroDefault}", "Gerente (Prueba)", cfg.TiempoAproxRegistroDefault),
            TiempoOLugarTexto = cfg.TiempoAproxRegistroDefault,
            EstadoEnvio = "En proceso...",
            DetalleResultado = string.Empty
        };

        try
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO NotificacionesCorreo (
    FechaHora, SolicitudFolio, OrdenCompraFolio, EtapaClave, EtapaNombre,
    GerenteNombre, CorreoDestino, Asunto, MensajePrincipal, CuerpoHtml,
    TiempoOLugarTexto, EstadoEnvio, DetalleResultado
) VALUES (
    $fh, $sfol, $ocfol, $ecl, $enom,
    $gnom, $cdes, $asu, $msj, $html,
    $tlug, $est, $det
);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$fh", regPrueba.FechaHora);
            cmd.Parameters.AddWithValue("$sfol", regPrueba.SolicitudFolio);
            cmd.Parameters.AddWithValue("$ocfol", regPrueba.OrdenCompraFolio);
            cmd.Parameters.AddWithValue("$ecl", regPrueba.EtapaClave);
            cmd.Parameters.AddWithValue("$enom", regPrueba.EtapaNombre);
            cmd.Parameters.AddWithValue("$gnom", regPrueba.GerenteNombre);
            cmd.Parameters.AddWithValue("$cdes", regPrueba.CorreoDestino);
            cmd.Parameters.AddWithValue("$asu", regPrueba.Asunto);
            cmd.Parameters.AddWithValue("$msj", regPrueba.MensajePrincipal);
            cmd.Parameters.AddWithValue("$html", regPrueba.CuerpoHtml);
            cmd.Parameters.AddWithValue("$tlug", regPrueba.TiempoOLugarTexto);
            cmd.Parameters.AddWithValue("$est", regPrueba.EstadoEnvio);
            cmd.Parameters.AddWithValue("$det", regPrueba.DetalleResultado);
            regPrueba.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch
        {
            // Continuar aunque falle inserción en bitácora
        }

        return await EnviarNotificacionCorreoSmtpInternoAsync(regPrueba, cfg);
    }

    // ==================== RECEPCIÓN AUTOMÁTICA DE SOLICITUDES POR CORREO (GMAIL IMAP + BANDEJA + PLANTILLA) ====================

    public static event Action<CorreoEntranteSolicitud>? OnCorreoSolicitudRecibido;

    public static string GenerarContenidoCsvPlantillaSolicitud()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("FORMATO ESTANDAR DE SOLICITUD DE MATERIAL E INSUMOS (CORREO / EXCEL),,,,,,");
        sb.AppendLine("Folio Web (Opcional),SOL-CORREO,,,,,");
        sb.AppendLine("Departamento / Area,Mantenimiento,,,,,");
        sb.AppendLine("Nombre Solicitante,Nombre del Gerente,,,,,");
        sb.AppendLine("Persona que Recibe,Responsable en Obra,,,,,");
        sb.AppendLine("Proyecto / Obra Destino,Consumo Interno,,,,,");
        sb.AppendLine($"Fecha Requerida,{DateTime.Today.AddDays(3):yyyy-MM-dd},,,,,");
        sb.AppendLine("Nivel de Urgencia,Normal,Opciones: Normal | Urgente,,,,");
        sb.AppendLine("Lugar de Entrega,Recoger en Almacén,Opciones: Recoger en Almacén | Entrega en Obra | Envío Foráneo,,,,");
        sb.AppendLine("Observaciones Generales,Material requerido para actividades de la semana,,,,,");
        sb.AppendLine(",,,,,,");
        sb.AppendLine("DescripcionArticulo,EspecificacionMedida,MarcaModeloRef,Cantidad,UnidadMedida,PrecioRefSinIva,NotasUso");
        sb.AppendLine("\"Tubo de cobre rígido\",\"Tipo L de 1/2 pulg x 6.10 m\",\"IUSA o Nacobre\",4,Tramo,480.00,\"Línea principal\"");
        sb.AppendLine("\"Cable de uso rudo\",\"Calibre 12 AWG 3 hilos 600V\",\"Condumex\",1,Rollo,2150.00,\"Alimentación equipo\"");
        sb.AppendLine("\"Guante de nitrilo anticorte\",\"Talla Grande (L) Nivel 5\",\"Truper\",6,Par,85.00,\"EPP cuadrilla\"");
        return sb.ToString();
    }

    public static async System.Threading.Tasks.Task<(bool Exito, string Mensaje)> EnviarPlantillaSolicitudAGerentesPorCorreoAsync(List<string>? correosEspecificos = null)
    {
        var cfg = ObtenerConfiguracionSmtpCorreo();
        if (!cfg.TieneCredencialesCompletas)
            return (false, "Configure primero su cuenta Gmail y Contraseña de Aplicación de 16 letras.");

        var destinos = (correosEspecificos ?? ObtenerEncargados()
            .Where(e => !string.IsNullOrWhiteSpace(e.Correo) && e.Correo.Contains('@'))
            .Select(e => e.Correo.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (destinos.Count == 0 && !string.IsNullOrWhiteSpace(cfg.CorreoCopiaFijaGerencia))
            destinos.Add(cfg.CorreoCopiaFijaGerencia.Trim());

        if (destinos.Count == 0)
            return (false, "No hay gerentes con correo corporativo registrado en Directorios -> Personal y Departamentos.");

        try
        {
            using var mail = new System.Net.Mail.MailMessage();
            mail.From = new System.Net.Mail.MailAddress(cfg.CorreoRemitenteGmail.Trim(), cfg.NombreMostrarRemitente);
            foreach (var d in destinos)
            {
                foreach (var sub in d.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (sub.Trim().Contains('@'))
                        mail.To.Add(sub.Trim());
                }
            }

            mail.Subject = "Plantilla e Instrucciones para Enviar Solicitudes de Compra por Correo — Compras Lebaref";
            mail.IsBodyHtml = true;
            mail.BodyEncoding = System.Text.Encoding.UTF8;
            mail.SubjectEncoding = System.Text.Encoding.UTF8;

            var sbHtml = new System.Text.StringBuilder();
            sbHtml.AppendLine("<html><body style='font-family: Segoe UI, Arial, sans-serif; color: #1E293B; background: #F8FAFC; padding: 18px;'>");
            sbHtml.AppendLine("<div style='max-width: 660px; margin: 0 auto; background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 10px; overflow: hidden;'>");
            sbHtml.AppendLine("<div style='background: #0F172A; color: #FFFFFF; padding: 14px 20px;'>");
            sbHtml.AppendLine("<div style='font-size: 16px; font-weight: 700;'>Guía Rápida: Cómo enviar Solicitudes de Compra por Correo</div>");
            sbHtml.AppendLine("<div style='font-size: 12px; opacity: 0.85;'>Sistema Automatizado de Compras y Almacén Lebaref</div>");
            sbHtml.AppendLine("</div>");
            sbHtml.AppendLine("<div style='padding: 20px; font-size: 13.5px; line-height: 1.55;'>");
            sbHtml.AppendLine($"<p>Ahora puede enviar sus requerimientos de material directamente al correo <b>{System.Net.WebUtility.HtmlEncode(cfg.CorreoRemitenteGmail)}</b> y el sistema creará su folio <b>SOL-xxxx</b> automáticamente enviándole su confirmación y avances en cada etapa.</p>");
            sbHtml.AppendLine("<div style='background: #EFF6FF; border-left: 4px solid #2563EB; padding: 12px 14px; border-radius: 6px; margin: 14px 0;'>");
            sbHtml.AppendLine("<b>Regla en el Asunto del Correo:</b><br/>");
            sbHtml.AppendLine("Inicie el asunto con la palabra <b>SOLICITUD</b> (o <b>COTIZAR</b> / <b>PEDIDO</b>) seguido del Proyecto u Obra.<br/>");
            sbHtml.AppendLine("<i>Ejemplo de Asunto:</i> <code>SOLICITUD - Obra Norte</code>");
            sbHtml.AppendLine("</div>");
            sbHtml.AppendLine("<p><b>Opción 1 (Rápida en el cuerpo del correo, igual que por WhatsApp):</b><br/>Escriba un artículo por renglón indicando Cantidad, Unidad y Descripción:</p>");
            sbHtml.AppendLine("<pre style='background: #F1F5F9; padding: 10px 14px; border-radius: 6px; font-size: 12.5px;'>Proyecto: Obra Norte\nUrgencia: Normal\n\n5 Pieza Tubo de cobre rígido 1/2\n2 Rollo Cable THW calibre 12 negro\n6 Par Guante de nitrilo anticorte talla L</pre>");
            sbHtml.AppendLine("<p><b>Opción 2 (Con la Plantilla Excel / CSV adjunta a este correo):</b><br/>Abra en Excel el archivo adjunto <code>Plantilla_Solicitud_Compras_Lebaref.csv</code>, capture los artículos, guárdelo y adjúntelo a su correo con el asunto <code>SOLICITUD - [Proyecto]</code>.</p>");
            sbHtml.AppendLine("</div></div></body></html>");
            mail.Body = sbHtml.ToString();

            byte[] csvBytes = new System.Text.UTF8Encoding(true).GetBytes(GenerarContenidoCsvPlantillaSolicitud());
            using var ms = new MemoryStream(csvBytes);
            var attachment = new System.Net.Mail.Attachment(ms, "Plantilla_Solicitud_Compras_Lebaref.csv", "text/csv");
            mail.Attachments.Add(attachment);

            using var smtp = new System.Net.Mail.SmtpClient(cfg.ServidorSmtp.Trim(), cfg.PuertoSmtp > 0 ? cfg.PuertoSmtp : 587)
            {
                EnableSsl = cfg.UsarSsl,
                DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(
                    cfg.CorreoRemitenteGmail.Trim(),
                    cfg.ContrasenaAplicacionGmail.Replace(" ", "").Trim()),
                Timeout = 15000
            };

            await smtp.SendMailAsync(mail);
            return (true, $"Plantilla e instrucciones enviadas a {mail.To.Count} correo(s): {string.Join(", ", mail.To.Select(x => x.Address))}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Envía la tabla comparativa por correo (tabla HTML en el cuerpo y/o PDF adjunto) usando la cuenta Gmail configurada.</summary>
    public static async System.Threading.Tasks.Task<(bool Exito, string Mensaje)> EnviarComparativaPorCorreoAsync(
        string destinatarios,
        string copia,
        string asunto,
        string mensajeIntro,
        string htmlTabla,
        byte[]? pdfAdjunto,
        string nombrePdf)
    {
        var cfg = ObtenerConfiguracionSmtpCorreo();
        if (!cfg.TieneCredencialesCompletas)
            return (false, "Configure primero su cuenta Gmail y Contraseña de Aplicación de 16 letras (Herramientas → Notificaciones por Correo a Gerentes).");

        static IEnumerable<string> Separar(string? s) =>
            (s ?? string.Empty).Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Contains('@'));

        var para = Separar(destinatarios).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (para.Count == 0)
            return (false, "Indique al menos un correo destinatario válido.");

        try
        {
            using var mail = new System.Net.Mail.MailMessage();
            mail.From = new System.Net.Mail.MailAddress(cfg.CorreoRemitenteGmail.Trim(), cfg.NombreMostrarRemitente);
            foreach (var d in para) mail.To.Add(d);
            foreach (var c in Separar(copia).Distinct(StringComparer.OrdinalIgnoreCase)) mail.CC.Add(c);

            mail.Subject = asunto;
            mail.SubjectEncoding = System.Text.Encoding.UTF8;
            mail.BodyEncoding = System.Text.Encoding.UTF8;
            mail.IsBodyHtml = true;

            var sb = new System.Text.StringBuilder();
            sb.Append("<html><body style='font-family:Segoe UI,Arial,sans-serif;color:#1E293B;background:#FFFFFF;padding:12px;'>");
            if (!string.IsNullOrWhiteSpace(mensajeIntro))
            {
                string intro = System.Net.WebUtility.HtmlEncode(mensajeIntro).Replace("\r\n", "<br/>").Replace("\n", "<br/>");
                sb.Append($"<p style='font-size:13.5px;line-height:1.5;margin:0 0 14px 0;'>{intro}</p>");
            }
            if (!string.IsNullOrWhiteSpace(htmlTabla))
                sb.Append(htmlTabla);
            else if (pdfAdjunto != null)
                sb.Append("<p style='font-size:13px;'>Se adjunta el Cuadro Comparativo en PDF.</p>");
            sb.Append($"<p style='font-size:11px;color:#94A3B8;margin-top:18px;'>Enviado desde el Sistema de Compras y Almacén — {System.Net.WebUtility.HtmlEncode(cfg.NombreMostrarRemitente)}</p>");
            sb.Append("</body></html>");
            mail.Body = sb.ToString();

            MemoryStream? msPdf = null;
            if (pdfAdjunto != null && pdfAdjunto.Length > 0)
            {
                msPdf = new MemoryStream(pdfAdjunto);
                mail.Attachments.Add(new System.Net.Mail.Attachment(msPdf, nombrePdf, "application/pdf"));
            }

            using var smtp = new System.Net.Mail.SmtpClient(cfg.ServidorSmtp.Trim(), cfg.PuertoSmtp > 0 ? cfg.PuertoSmtp : 587)
            {
                EnableSsl = cfg.UsarSsl,
                DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(
                    cfg.CorreoRemitenteGmail.Trim(),
                    cfg.ContrasenaAplicacionGmail.Replace(" ", "").Trim()),
                Timeout = 30000
            };

            await smtp.SendMailAsync(mail);
            msPdf?.Dispose();

            string dest = string.Join(", ", mail.To.Select(x => x.Address));
            if (mail.CC.Count > 0) dest += $" (CC: {string.Join(", ", mail.CC.Select(x => x.Address))})";
            return (true, $"Comparativa enviada a: {dest}");
        }
        catch (Exception ex)
        {
            return (false, ex.InnerException != null ? $"{ex.Message}\n{ex.InnerException.Message}" : ex.Message);
        }
    }

    public static List<CorreoEntranteSolicitud> ObtenerCorreosEntrantesSolicitud(string? filtro = null)
    {
        var list = new List<CorreoEntranteSolicitud>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(filtro) || string.Equals(filtro, "TODOS", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = @"
SELECT Id, UidImap, FechaCorreo, RemitenteNombre, RemitenteCorreo, Asunto, CuerpoTexto,
       NombreAdjuntoCsv, ContenidoAdjuntoCsv, CoincidePalabraClaveAsunto,
       EncargadoDetectadoId, EncargadoDetectadoNombre, DepartamentoDetectadoId, DepartamentoDetectadoNombre,
       ProyectoDetectadoId, ProyectoDetectadoNombre, PartidasDetectadasCount, ResumenPartidas,
       EstadoBandeja, SolicitudCreadaId, SolicitudCreadaFolio
FROM CorreosEntrantesSolicitud
ORDER BY Id DESC LIMIT 250;";
        }
        else if (string.Equals(filtro, "PENDIENTES", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = @"
SELECT Id, UidImap, FechaCorreo, RemitenteNombre, RemitenteCorreo, Asunto, CuerpoTexto,
       NombreAdjuntoCsv, ContenidoAdjuntoCsv, CoincidePalabraClaveAsunto,
       EncargadoDetectadoId, EncargadoDetectadoNombre, DepartamentoDetectadoId, DepartamentoDetectadoNombre,
       ProyectoDetectadoId, ProyectoDetectadoNombre, PartidasDetectadasCount, ResumenPartidas,
       EstadoBandeja, SolicitudCreadaId, SolicitudCreadaFolio
FROM CorreosEntrantesSolicitud
WHERE EstadoBandeja = 'En Bandeja (Por Revisar)' AND SolicitudCreadaId <= 0
ORDER BY Id DESC LIMIT 250;";
        }
        else if (string.Equals(filtro, "PROCESADOS", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = @"
SELECT Id, UidImap, FechaCorreo, RemitenteNombre, RemitenteCorreo, Asunto, CuerpoTexto,
       NombreAdjuntoCsv, ContenidoAdjuntoCsv, CoincidePalabraClaveAsunto,
       EncargadoDetectadoId, EncargadoDetectadoNombre, DepartamentoDetectadoId, DepartamentoDetectadoNombre,
       ProyectoDetectadoId, ProyectoDetectadoNombre, PartidasDetectadasCount, ResumenPartidas,
       EstadoBandeja, SolicitudCreadaId, SolicitudCreadaFolio
FROM CorreosEntrantesSolicitud
WHERE SolicitudCreadaId > 0
ORDER BY Id DESC LIMIT 250;";
        }
        else
        {
            cmd.CommandText = @"
SELECT Id, UidImap, FechaCorreo, RemitenteNombre, RemitenteCorreo, Asunto, CuerpoTexto,
       NombreAdjuntoCsv, ContenidoAdjuntoCsv, CoincidePalabraClaveAsunto,
       EncargadoDetectadoId, EncargadoDetectadoNombre, DepartamentoDetectadoId, DepartamentoDetectadoNombre,
       ProyectoDetectadoId, ProyectoDetectadoNombre, PartidasDetectadasCount, ResumenPartidas,
       EstadoBandeja, SolicitudCreadaId, SolicitudCreadaFolio
FROM CorreosEntrantesSolicitud
WHERE Asunto LIKE $f OR RemitenteNombre LIKE $f OR RemitenteCorreo LIKE $f OR SolicitudCreadaFolio LIKE $f OR ResumenPartidas LIKE $f
ORDER BY Id DESC LIMIT 250;";
            cmd.Parameters.AddWithValue("$f", $"%{filtro.Trim()}%");
        }

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CorreoEntranteSolicitud
            {
                Id = r.GetInt32(0),
                UidImap = r.GetString(1),
                FechaCorreo = r.GetString(2),
                RemitenteNombre = r.GetString(3),
                RemitenteCorreo = r.GetString(4),
                Asunto = r.GetString(5),
                CuerpoTexto = r.GetString(6),
                NombreAdjuntoCsv = r.GetString(7),
                ContenidoAdjuntoCsv = r.GetString(8),
                CoincidePalabraClaveAsunto = r.GetInt32(9) != 0,
                EncargadoDetectadoId = r.GetInt32(10),
                EncargadoDetectadoNombre = r.GetString(11),
                DepartamentoDetectadoId = r.GetInt32(12),
                DepartamentoDetectadoNombre = r.GetString(13),
                ProyectoDetectadoId = r.GetInt32(14),
                ProyectoDetectadoNombre = r.GetString(15),
                PartidasDetectadasCount = r.GetInt32(16),
                ResumenPartidas = r.GetString(17),
                EstadoBandeja = r.GetString(18),
                SolicitudCreadaId = r.GetInt32(19),
                SolicitudCreadaFolio = r.GetString(20)
            });
        }
        return list;
    }

    public static void DescartarCorreoEntranteSolicitud(int correoId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CorreosEntrantesSolicitud SET EstadoBandeja = 'Descartado (No es solicitud)' WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", correoId);
        cmd.ExecuteNonQuery();
    }

    public static (List<SolicitudPartida> Partidas, int DepartamentoId, string DepartamentoNombre, int EncargadoId, string EncargadoNombre, int ProyectoId, string ProyectoNombre, string NotasDetectadas)
        ExtraerPartidasYMetadatosDeCorreo(string asunto, string remitenteCorreo, string remitenteNombre, string cuerpoTexto, string contenidoCsvAdjunto)
    {
        var departamentos = ObtenerDepartamentos();
        var encargados = ObtenerEncargados();
        var proyectos = ObtenerProyectos();
        var productos = ObtenerProductos();

        Encargado? encMatch = null;
        if (!string.IsNullOrWhiteSpace(remitenteCorreo))
        {
            encMatch = encargados.FirstOrDefault(e =>
                !string.IsNullOrWhiteSpace(e.Correo) &&
                e.Correo.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(c => string.Equals(c.Trim(), remitenteCorreo.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
        if (encMatch == null && !string.IsNullOrWhiteSpace(remitenteNombre))
        {
            encMatch = encargados.FirstOrDefault(e =>
                remitenteNombre.Contains(e.Nombre, StringComparison.OrdinalIgnoreCase) ||
                e.Nombre.Contains(remitenteNombre, StringComparison.OrdinalIgnoreCase));
        }

        Departamento? depMatch = encMatch != null
            ? departamentos.FirstOrDefault(d => d.Id == encMatch.DepartamentoId)
            : null;

        Proyecto? proyMatch = null;
        string textoCombinadoMeta = $"{asunto}\n{cuerpoTexto}";
        foreach (var pr in proyectos.OrderByDescending(x => x.Nombre.Length))
        {
            if ((!string.IsNullOrWhiteSpace(pr.Nombre) && textoCombinadoMeta.Contains(pr.Nombre, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(pr.Codigo) && textoCombinadoMeta.Contains(pr.Codigo, StringComparison.OrdinalIgnoreCase)))
            {
                proyMatch = pr;
                break;
            }
        }

        var partidas = new List<SolicitudPartida>();
        string notasExtra = $"Recibido por correo: \"{asunto}\" ({remitenteCorreo})";

        void AgregarPartidaDetectada(string descRaw, decimal cantidad, string unidadRaw, string notaPartida)
        {
            string descLimpia = (descRaw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(descLimpia) || descLimpia.Length < 2) return;
            if (cantidad <= 0m) cantidad = 1m;
            string unidadNorm = NormalizarUnidadMedida(string.IsNullOrWhiteSpace(unidadRaw) ? "Pieza" : unidadRaw.Trim());

            var palabras = descLimpia.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2)
                .ToList();

            var mejorProd = productos
                .Select(p => new
                {
                    Producto = p,
                    Exacto = string.Equals(p.Nombre, descLimpia, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(p.CodigoInterno, descLimpia, StringComparison.OrdinalIgnoreCase),
                    Score = palabras.Count(w =>
                        p.Nombre.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.CodigoInterno.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.Marca.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.Modelo.Contains(w, StringComparison.OrdinalIgnoreCase))
                })
                .OrderByDescending(x => x.Exacto)
                .ThenByDescending(x => x.Score)
                .FirstOrDefault();

            if (mejorProd != null && (mejorProd.Exacto || (palabras.Count > 0 && mejorProd.Score >= Math.Max(2, palabras.Count))))
            {
                var p = mejorProd.Producto;
                partidas.Add(new SolicitudPartida
                {
                    ProductoId = p.Id,
                    ProductoCodigo = p.CodigoInterno,
                    ProductoNombre = p.Nombre,
                    DescripcionLibre = p.Nombre,
                    Familia = p.Familia,
                    UnidadCompra = string.IsNullOrWhiteSpace(unidadRaw) ? p.UnidadCompra : unidadNorm,
                    CantidadSolicitada = cantidad,
                    StockActualAlmacen = p.StockActual,
                    NotasPartida = notaPartida,
                    IncluirEnCotizacion = true
                });
            }
            else
            {
                partidas.Add(new SolicitudPartida
                {
                    ProductoId = 0,
                    ProductoCodigo = "CONCEPTO-LIBRE",
                    ProductoNombre = descLimpia,
                    DescripcionLibre = descLimpia,
                    Familia = "Conceptos por Cotizar",
                    UnidadCompra = unidadNorm,
                    CantidadSolicitada = cantidad,
                    NotasPartida = notaPartida,
                    IncluirEnCotizacion = true
                });
            }
        }

        // 1. Si hay CSV adjunto o plantilla CSV en el cuerpo
        string csvAnalizar = !string.IsNullOrWhiteSpace(contenidoCsvAdjunto)
            ? contenidoCsvAdjunto
            : (cuerpoTexto.Contains("DescripcionArticulo", StringComparison.OrdinalIgnoreCase) ? cuerpoTexto : string.Empty);

        if (!string.IsNullOrWhiteSpace(csvAnalizar))
        {
            var lineasCsv = csvAnalizar.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            char delim = lineasCsv.Take(15).Sum(l => l.Count(c => c == ';')) > lineasCsv.Take(15).Sum(l => l.Count(c => c == ',')) ? ';' : ',';
            bool enTabla = false;
            foreach (var rawLine in lineasCsv)
            {
                var cols = rawLine.Split(delim).Select(x => x.Trim().Trim('"')).ToList();
                if (cols.Count == 0 || string.IsNullOrWhiteSpace(cols[0])) continue;
                string c0 = cols[0];
                if (c0.Equals("DescripcionArticulo", StringComparison.OrdinalIgnoreCase) ||
                    c0.Equals("Descripcion", StringComparison.OrdinalIgnoreCase) ||
                    c0.Equals("Descripción", StringComparison.OrdinalIgnoreCase))
                {
                    enTabla = true;
                    continue;
                }
                if (!enTabla)
                {
                    string val = cols.Count > 1 ? cols[1] : string.Empty;
                    if (c0.Contains("Departamento", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val))
                    {
                        var d = departamentos.FirstOrDefault(x => x.Nombre.Contains(val, StringComparison.OrdinalIgnoreCase) || x.Codigo.Contains(val, StringComparison.OrdinalIgnoreCase));
                        if (d != null) depMatch = d;
                    }
                    else if (c0.Contains("Solicitante", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val))
                    {
                        var e = encargados.FirstOrDefault(x => x.Nombre.Contains(val, StringComparison.OrdinalIgnoreCase));
                        if (e != null) encMatch = e;
                    }
                    else if ((c0.Contains("Proyecto", StringComparison.OrdinalIgnoreCase) || c0.Contains("Obra", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(val))
                    {
                        var pr = proyectos.FirstOrDefault(x => x.Nombre.Contains(val, StringComparison.OrdinalIgnoreCase) || x.Codigo.Contains(val, StringComparison.OrdinalIgnoreCase));
                        if (pr != null) proyMatch = pr;
                    }
                    else if (c0.Contains("Urgencia", StringComparison.OrdinalIgnoreCase) && val.Contains("Urgente", StringComparison.OrdinalIgnoreCase))
                    {
                        notasExtra += " | Prioridad: Urgente";
                    }
                    continue;
                }

                string descArt = c0;
                string espec = cols.Count > 1 ? cols[1] : string.Empty;
                string marca = cols.Count > 2 ? cols[2] : string.Empty;
                decimal.TryParse(cols.Count > 3 ? cols[3] : "1", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cantCsv);
                if (cantCsv <= 0m) cantCsv = 1m;
                string uniCsv = cols.Count > 4 && !string.IsNullOrWhiteSpace(cols[4]) ? cols[4] : "Pieza";
                string descComp = descArt;
                if (!string.IsNullOrWhiteSpace(espec)) descComp += $" ({espec})";
                if (!string.IsNullOrWhiteSpace(marca)) descComp += $" [Ref: {marca}]";
                AgregarPartidaDetectada(descComp, cantCsv, uniCsv, "Plantilla Correo CSV");
            }
        }

        // 2. Si no hubo partidas por CSV, analizar renglones del cuerpo del correo (formato lista WhatsApp / tabla)
        if (partidas.Count == 0 && !string.IsNullOrWhiteSpace(cuerpoTexto))
        {
            string[] unidadesConocidas =
            {
                "pza", "pzas", "pieza", "piezas", "tramo", "tramos", "metro", "metros", "m", "ml",
                "rollo", "rollos", "saco", "sacos", "bulto", "bultos", "caja", "cajas", "par", "pares",
                "juego", "juegos", "kit", "kits", "litro", "litros", "lt", "galon", "galones", "cubeta", "cubetas",
                "kg", "kilo", "kilos", "servicio", "servicios", "lote", "paquete", "paquetes"
            };

            var lineas = cuerpoTexto.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lineas)
            {
                string l = raw.Trim();
                if (string.IsNullOrWhiteSpace(l)) continue;
                if (l.StartsWith(">") || l.StartsWith("De:", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("From:", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Enviado", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Saludos", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Atentamente", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Quedo ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Buen día", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Buenos días", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Buenas tardes", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Hola", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Favor de ", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("escribió:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Metadatos clave: valor en el cuerpo del correo
                if (l.StartsWith("Proyecto:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Obra:", StringComparison.OrdinalIgnoreCase))
                {
                    string valP = l[(l.IndexOf(':') + 1)..].Trim();
                    var pr = proyectos.FirstOrDefault(x => x.Nombre.Contains(valP, StringComparison.OrdinalIgnoreCase) || x.Codigo.Contains(valP, StringComparison.OrdinalIgnoreCase));
                    if (pr != null) proyMatch = pr;
                    continue;
                }
                if (l.StartsWith("Departamento:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Área:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Area:", StringComparison.OrdinalIgnoreCase))
                {
                    string valD = l[(l.IndexOf(':') + 1)..].Trim();
                    var dp = departamentos.FirstOrDefault(x => x.Nombre.Contains(valD, StringComparison.OrdinalIgnoreCase) || x.Codigo.Contains(valD, StringComparison.OrdinalIgnoreCase));
                    if (dp != null) depMatch = dp;
                    continue;
                }
                if (l.StartsWith("Urgencia:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Prioridad:", StringComparison.OrdinalIgnoreCase))
                {
                    string valU = l[(l.IndexOf(':') + 1)..].Trim();
                    if (valU.Contains("Urgente", StringComparison.OrdinalIgnoreCase))
                        notasExtra += " | Prioridad: Urgente";
                    continue;
                }

                // Quitar viñetas iniciales (- , * , • )
                string limpia = System.Text.RegularExpressions.Regex.Replace(l, @"^[\-\*\•\u2022]+\s*", "").Trim();

                // Caso A: Empieza con cantidad numérica (ej. "5 pza Tubo de cobre 1/2" o "4 tornillo inox")
                var mCant = System.Text.RegularExpressions.Regex.Match(limpia, @"^(\d+(?:[\.,]\d+)?)\s+(?:x\s+)?(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mCant.Success)
                {
                    decimal.TryParse(mCant.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cant);
                    string resto = mCant.Groups[2].Value.Trim();
                    string unidad = "Pieza";

                    var partesResto = resto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (partesResto.Length >= 2)
                    {
                        string primeraPalabra = partesResto[0].TrimEnd('.', ',').ToLowerInvariant();
                        if (unidadesConocidas.Contains(primeraPalabra))
                        {
                            unidad = NormalizarUnidadMedida(primeraPalabra);
                            resto = string.Join(" ", partesResto.Skip(1)).Trim();
                            if (resto.StartsWith("de ", StringComparison.OrdinalIgnoreCase))
                                resto = resto[3..].Trim();
                        }
                    }

                    AgregarPartidaDetectada(resto, cant > 0 ? cant : 1m, unidad, $"Correo: {limpia}");
                }
            }
        }

        if (depMatch == null && encMatch != null)
            depMatch = departamentos.FirstOrDefault(d => d.Id == encMatch.DepartamentoId);
        if (depMatch == null)
            depMatch = departamentos.FirstOrDefault();
        if (encMatch == null && depMatch != null)
            encMatch = encargados.FirstOrDefault(e => e.DepartamentoId == depMatch.Id) ?? encargados.FirstOrDefault();

        return (
            partidas,
            depMatch?.Id ?? 0,
            depMatch?.Nombre ?? "Por Asignar",
            encMatch?.Id ?? 0,
            encMatch?.Nombre ?? (!string.IsNullOrWhiteSpace(remitenteNombre) ? remitenteNombre : remitenteCorreo),
            proyMatch?.Id ?? 0,
            proyMatch?.Nombre ?? "Consumo Interno / General",
            notasExtra
        );
    }

    public static Solicitud? ConvertirCorreoBandejaEnSolicitud(
        int correoId,
        int? depOverrideId = null,
        int? encOverrideId = null,
        int? proyOverrideId = null,
        string? cuerpoOverride = null,
        bool notificarEtapa1AlGerente = true)
    {
        var correo = ObtenerCorreosEntrantesSolicitud().FirstOrDefault(c => c.Id == correoId);
        if (correo == null) return null;

        string textoUsar = !string.IsNullOrWhiteSpace(cuerpoOverride) ? cuerpoOverride : correo.CuerpoTexto;
        var extraido = ExtraerPartidasYMetadatosDeCorreo(
            correo.Asunto,
            correo.RemitenteCorreo,
            correo.RemitenteNombre,
            textoUsar,
            correo.ContenidoAdjuntoCsv);

        var partidas = extraido.Partidas;
        if (partidas.Count == 0 && !string.IsNullOrWhiteSpace(textoUsar))
        {
            // Si el texto era formato libre sin cantidades numéricas explícitas, convertir cada línea útil en 1 pieza
            foreach (var rawLine in textoUsar.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string lin = rawLine.Trim();
                if (lin.Length >= 3 && !lin.StartsWith(">") && !lin.StartsWith("Saludos", StringComparison.OrdinalIgnoreCase))
                {
                    partidas.Add(new SolicitudPartida
                    {
                        ProductoId = 0,
                        ProductoCodigo = "CONCEPTO-LIBRE",
                        ProductoNombre = lin,
                        DescripcionLibre = lin,
                        Familia = "Conceptos por Cotizar",
                        UnidadCompra = "Pieza",
                        CantidadSolicitada = 1m,
                        NotasPartida = "Correo formato libre",
                        IncluirEnCotizacion = true
                    });
                }
            }
        }

        if (partidas.Count == 0)
            return null;

        int depId = depOverrideId.HasValue && depOverrideId.Value > 0 ? depOverrideId.Value : extraido.DepartamentoId;
        int encId = encOverrideId.HasValue && encOverrideId.Value > 0 ? encOverrideId.Value : extraido.EncargadoId;
        int proyId = proyOverrideId.HasValue && proyOverrideId.Value >= 0 ? proyOverrideId.Value : extraido.ProyectoId;

        var sol = CrearSolicitud(
            depId,
            encId,
            proyId,
            "Correo",
            extraido.NotasDetectadas,
            partidas,
            "Requerimiento de Compra",
            null,
            null,
            notificarEtapa1AlGerente);

        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE CorreosEntrantesSolicitud
SET EstadoBandeja = $est,
    SolicitudCreadaId = $sid,
    SolicitudCreadaFolio = $sfol,
    PartidasDetectadasCount = $cnt
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$est", $"Solicitud Creada ({sol.Folio})");
            cmd.Parameters.AddWithValue("$sid", sol.Id);
            cmd.Parameters.AddWithValue("$sfol", sol.Folio);
            cmd.Parameters.AddWithValue("$cnt", partidas.Count);
            cmd.Parameters.AddWithValue("$id", correo.Id);
            cmd.ExecuteNonQuery();
        }

        return sol;
    }

    private static string DecodificarHeaderRfc2047(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            return System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=",
                m =>
                {
                    string charset = m.Groups[1].Value;
                    string mode = m.Groups[2].Value.ToUpperInvariant();
                    string data = m.Groups[3].Value;
                    System.Text.Encoding enc;
                    try { enc = System.Text.Encoding.GetEncoding(charset); }
                    catch { enc = System.Text.Encoding.UTF8; }

                    if (mode == "B")
                    {
                        byte[] bytes = Convert.FromBase64String(data);
                        return enc.GetString(bytes);
                    }
                    else
                    {
                        var bytes = new List<byte>();
                        for (int i = 0; i < data.Length; i++)
                        {
                            if (data[i] == '_')
                                bytes.Add((byte)' ');
                            else if (data[i] == '=' && i + 2 < data.Length &&
                                     byte.TryParse(data.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                            {
                                bytes.Add(b);
                                i += 2;
                            }
                            else
                                bytes.Add((byte)data[i]);
                        }
                        return enc.GetString(bytes.ToArray());
                    }
                });
        }
        catch
        {
            return raw;
        }
    }

    private static string DecodificarTransferEncoding(string body, string encoding, string charset = "utf-8")
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        System.Text.Encoding enc;
        try { enc = System.Text.Encoding.GetEncoding(string.IsNullOrWhiteSpace(charset) ? "utf-8" : charset.Trim().Trim('"')); }
        catch { enc = System.Text.Encoding.UTF8; }

        string mode = (encoding ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            if (mode == "base64")
            {
                string cleanB64 = System.Text.RegularExpressions.Regex.Replace(body, @"\s+", "");
                byte[] bytes = Convert.FromBase64String(cleanB64);
                return enc.GetString(bytes);
            }
            if (mode == "quoted-printable")
            {
                string softRemoved = body.Replace("=\r\n", "").Replace("=\n", "");
                var bytes = new List<byte>(softRemoved.Length);
                for (int i = 0; i < softRemoved.Length; i++)
                {
                    if (softRemoved[i] == '=' && i + 2 < softRemoved.Length &&
                        byte.TryParse(softRemoved.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    {
                        bytes.Add(b);
                        i += 2;
                    }
                    else
                    {
                        bytes.Add((byte)softRemoved[i]);
                    }
                }
                return enc.GetString(bytes.ToArray());
            }
        }
        catch
        {
            // Fallback al texto original
        }
        return body;
    }

    private static string LimpiarHtmlATextoConSaltos(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        string s = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<(br|p|div|tr|li|h[1-6])[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<td[^>]*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]+>", "");
        s = System.Net.WebUtility.HtmlDecode(s);
        return s.Trim();
    }

    private static (string Asunto, string RemitenteNombre, string RemitenteCorreo, string FechaStr, string CuerpoTexto, string NombreCsv, string ContenidoCsv)
        ParsearMensajeMimeRfc822(string rawMime)
    {
        string asunto = string.Empty;
        string remNombre = string.Empty;
        string remCorreo = string.Empty;
        string fechaStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string nombreCsv = string.Empty;
        string contenidoCsv = string.Empty;
        var textosPlain = new List<string>();
        var textosHtml = new List<string>();

        int splitIdx = rawMime.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int sepLen = 4;
        if (splitIdx < 0)
        {
            splitIdx = rawMime.IndexOf("\n\n", StringComparison.Ordinal);
            sepLen = 2;
        }
        if (splitIdx < 0) return (asunto, remNombre, remCorreo, fechaStr, rawMime, nombreCsv, contenidoCsv);

        string headersRaw = rawMime[..splitIdx];
        string bodyRaw = rawMime[(splitIdx + sepLen)..];

        // Unfold multiline headers
        string headersUnfolded = System.Text.RegularExpressions.Regex.Replace(headersRaw, @"\r?\n[ \t]+", " ");
        string contentType = "text/plain";
        string transferEnc = "";

        foreach (var hLine in headersUnfolded.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = hLine.IndexOf(':');
            if (colon <= 0) continue;
            string hName = hLine[..colon].Trim();
            string hVal = hLine[(colon + 1)..].Trim();

            if (hName.Equals("Subject", StringComparison.OrdinalIgnoreCase))
                asunto = DecodificarHeaderRfc2047(hVal).Trim();
            else if (hName.Equals("From", StringComparison.OrdinalIgnoreCase))
            {
                string fromDec = DecodificarHeaderRfc2047(hVal).Trim();
                var mMail = System.Text.RegularExpressions.Regex.Match(fromDec, @"<([^>]+@[^>]+)>");
                if (mMail.Success)
                {
                    remCorreo = mMail.Groups[1].Value.Trim();
                    remNombre = fromDec[..mMail.Index].Trim().Trim('"', '\'');
                }
                else
                {
                    remCorreo = fromDec.Trim();
                    remNombre = fromDec.Trim();
                }
            }
            else if (hName.Equals("Date", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTimeOffset.TryParse(hVal, out var dto))
                    fechaStr = dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            else if (hName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                contentType = hVal;
            else if (hName.Equals("Content-Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                transferEnc = hVal;
        }

        void ProcesarParteMime(string ctHeader, string cteHeader, string cdHeader, string partBody)
        {
            var mBoundary = System.Text.RegularExpressions.Regex.Match(ctHeader, @"boundary=""?([^"";\r\n]+)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (ctHeader.Contains("multipart/", StringComparison.OrdinalIgnoreCase) && mBoundary.Success)
            {
                string boundary = "--" + mBoundary.Groups[1].Value.Trim();
                var subParts = partBody.Split(new[] { boundary }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var sp in subParts)
                {
                    string spTrim = sp.TrimStart('\r', '\n');
                    if (spTrim.StartsWith("--") || string.IsNullOrWhiteSpace(spTrim)) continue;
                    int sIdx = spTrim.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    int sLen = 4;
                    if (sIdx < 0)
                    {
                        sIdx = spTrim.IndexOf("\n\n", StringComparison.Ordinal);
                        sLen = 2;
                    }
                    if (sIdx < 0) continue;

                    string subHeaders = System.Text.RegularExpressions.Regex.Replace(spTrim[..sIdx], @"\r?\n[ \t]+", " ");
                    string subBody = spTrim[(sIdx + sLen)..];
                    string subCt = "text/plain", subCte = "", subCd = "";
                    foreach (var sh in subHeaders.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int cIdx = sh.IndexOf(':');
                        if (cIdx <= 0) continue;
                        string k = sh[..cIdx].Trim();
                        string v = sh[(cIdx + 1)..].Trim();
                        if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) subCt = v;
                        else if (k.Equals("Content-Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) subCte = v;
                        else if (k.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)) subCd = v;
                    }
                    ProcesarParteMime(subCt, subCte, subCd, subBody);
                }
                return;
            }

            string charset = "utf-8";
            var mChar = System.Text.RegularExpressions.Regex.Match(ctHeader, @"charset=""?([^"";\s]+)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mChar.Success) charset = mChar.Groups[1].Value;

            string decoded = DecodificarTransferEncoding(partBody, cteHeader, charset);

            // Detectar adjunto CSV
            string fileMeta = $"{ctHeader} {cdHeader}";
            if (fileMeta.Contains(".csv", StringComparison.OrdinalIgnoreCase) || ctHeader.Contains("text/csv", StringComparison.OrdinalIgnoreCase))
            {
                var mFn = System.Text.RegularExpressions.Regex.Match(fileMeta, @"(?:filename|name)=""?([^"";\r\n]+\.csv)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                nombreCsv = mFn.Success ? DecodificarHeaderRfc2047(mFn.Groups[1].Value) : "Solicitud_Adjunta.csv";
                contenidoCsv = decoded.Trim();
                return;
            }

            if (ctHeader.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
                textosPlain.Add(decoded.Trim());
            else if (ctHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                textosHtml.Add(LimpiarHtmlATextoConSaltos(decoded));
        }

        ProcesarParteMime(contentType, transferEnc, "", bodyRaw);
        string cuerpoFinal = textosPlain.Count > 0
            ? string.Join("\n", textosPlain.Where(x => !string.IsNullOrWhiteSpace(x)))
            : string.Join("\n", textosHtml.Where(x => !string.IsNullOrWhiteSpace(x)));

        return (asunto, remNombre, remCorreo, fechaStr, cuerpoFinal.Trim(), nombreCsv, contenidoCsv);
    }

    public static async System.Threading.Tasks.Task<(bool Exito, string Mensaje, int NuevosEnBandeja, int SolicitudesAutoCreadas)> SincronizarCorreosEntrantesImapAsync()
    {
        var cfg = ObtenerConfiguracionSmtpCorreo();
        if (!cfg.TieneCredencialesCompletas)
            return (false, "Configure primero su cuenta Gmail y Contraseña de Aplicación de 16 letras.", 0, 0);

        return await System.Threading.Tasks.Task.Run(() =>
        {
            int nuevosBandeja = 0;
            int autoCreadas = 0;
            try
            {
                var uidsExistentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var connChk = new SqliteConnection(ConnectionString))
                {
                    connChk.Open();
                    using var cmdChk = connChk.CreateCommand();
                    cmdChk.CommandText = "SELECT UidImap FROM CorreosEntrantesSolicitud;";
                    using var rChk = cmdChk.ExecuteReader();
                    while (rChk.Read())
                        uidsExistentes.Add(rChk.GetString(0));
                }

                string hostImap = cfg.ServidorSmtp.Contains("gmail", StringComparison.OrdinalIgnoreCase)
                    ? "imap.gmail.com"
                    : cfg.ServidorSmtp.Replace("smtp.", "imap.", StringComparison.OrdinalIgnoreCase);

                using var tcp = new System.Net.Sockets.TcpClient();
                tcp.ReceiveTimeout = 15000;
                tcp.SendTimeout = 15000;
                tcp.Connect(hostImap, 993);

                using var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false);
                ssl.AuthenticateAsClient(hostImap);
                using var reader = new StreamReader(ssl, System.Text.Encoding.Latin1, false, 8192, leaveOpen: true);
                using var writer = new StreamWriter(ssl, new System.Text.UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

                // Leer saludo inicial IMAP
                _ = reader.ReadLine();

                string EnviarComandoImap(string tag, string cmdTexto)
                {
                    writer.WriteLine($"{tag} {cmdTexto}");
                    var sbResp = new System.Text.StringBuilder();
                    while (true)
                    {
                        string? line = reader.ReadLine();
                        if (line == null) break;
                        sbResp.AppendLine(line);
                        if (line.StartsWith($"{tag} OK", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith($"{tag} NO", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith($"{tag} BAD", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                    return sbResp.ToString();
                }

                string userClean = cfg.CorreoRemitenteGmail.Trim();
                string passClean = cfg.ContrasenaAplicacionGmail.Replace(" ", "").Trim();
                string respLogin = EnviarComandoImap("A01", $"LOGIN \"{userClean}\" \"{passClean}\"");
                if (!respLogin.Contains("A01 OK", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "No se pudo autenticar en IMAP (imap.gmail.com:993). Verifique su Contraseña de Aplicación de 16 letras.", 0, 0);
                }

                _ = EnviarComandoImap("A02", "SELECT INBOX");

                string fechaSince = DateTime.Today.AddDays(-7).ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
                string respSearch = EnviarComandoImap("A03", $"UID SEARCH SINCE {fechaSince}");

                var uidsNuevos = new List<string>();
                foreach (var line in respSearch.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase))
                    {
                        var partes = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in partes.Skip(2))
                        {
                            if (long.TryParse(p, out _) && !uidsExistentes.Contains(p))
                                uidsNuevos.Add(p);
                        }
                    }
                }

                // Procesar los últimos 20 correos no sincronizados
                var uidsProcesar = uidsNuevos.TakeLast(20).ToList();
                var palabrasClave = (cfg.PalabrasClaveAsuntoImap ?? "SOLICITUD, COTIZAR, COTIZACION, PEDIDO, REQUERIMIENTO")
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

                int cmdNum = 10;
                foreach (var uid in uidsProcesar)
                {
                    cmdNum++;
                    string tag = $"F{cmdNum}";
                    writer.WriteLine($"{tag} UID FETCH {uid} (BODY.PEEK[])");

                    var sbRawMsg = new System.Text.StringBuilder();
                    while (true)
                    {
                        string? line = reader.ReadLine();
                        if (line == null) break;
                        if (line.StartsWith($"{tag} OK", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith($"{tag} NO", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith($"{tag} BAD", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                        if (line.StartsWith($"* ", StringComparison.Ordinal) && line.Contains("FETCH (", StringComparison.OrdinalIgnoreCase))
                            continue;
                        sbRawMsg.AppendLine(line);
                    }

                    string rawMime = sbRawMsg.ToString().TrimEnd();
                    if (rawMime.EndsWith(")"))
                        rawMime = rawMime[..^1].TrimEnd();
                    if (string.IsNullOrWhiteSpace(rawMime))
                        continue;

                    var parsed = ParsearMensajeMimeRfc822(rawMime);

                    // Ignorar correos automáticos salientes generados por nuestro propio sistema ("[Compras Lebaref]")
                    if (parsed.Asunto.StartsWith("[Compras Lebaref]", StringComparison.OrdinalIgnoreCase) ||
                        parsed.Asunto.StartsWith("Plantilla e Instrucciones para Enviar Solicitudes", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool coincideAsunto = palabrasClave.Any(k => parsed.Asunto.Contains(k, StringComparison.OrdinalIgnoreCase));
                    var extraido = ExtraerPartidasYMetadatosDeCorreo(
                        parsed.Asunto,
                        parsed.RemitenteCorreo,
                        parsed.RemitenteNombre,
                        parsed.CuerpoTexto,
                        parsed.ContenidoCsv);

                    // Solo guardar en la Bandeja correos que coincidan con el asunto, o que vengan de un Gerente registrado, o tengan archivo CSV
                    bool vieneDeGerenteRegistrado = extraido.EncargadoId > 0 &&
                        ObtenerEncargados().Any(e => !string.IsNullOrWhiteSpace(e.Correo) &&
                                                     e.Correo.Contains(parsed.RemitenteCorreo, StringComparison.OrdinalIgnoreCase));
                    if (!coincideAsunto && !vieneDeGerenteRegistrado && string.IsNullOrWhiteSpace(parsed.NombreCsv))
                        continue;

                    string resumenPartidas = extraido.Partidas.Count > 0
                        ? string.Join(", ", extraido.Partidas.Take(4).Select(p => $"{p.CantidadSolicitada:0.##} {p.UnidadCompra} {p.ProductoNombre}"))
                        : "(Sin partidas estructuradas — Revisar texto en Bandeja)";

                    var regCorreo = new CorreoEntranteSolicitud
                    {
                        UidImap = uid,
                        FechaCorreo = parsed.FechaStr,
                        RemitenteNombre = string.IsNullOrWhiteSpace(parsed.RemitenteNombre) ? extraido.EncargadoNombre : parsed.RemitenteNombre,
                        RemitenteCorreo = parsed.RemitenteCorreo,
                        Asunto = string.IsNullOrWhiteSpace(parsed.Asunto) ? "(Sin asunto)" : parsed.Asunto,
                        CuerpoTexto = parsed.CuerpoTexto,
                        NombreAdjuntoCsv = parsed.NombreCsv,
                        ContenidoAdjuntoCsv = parsed.ContenidoCsv,
                        CoincidePalabraClaveAsunto = coincideAsunto,
                        EncargadoDetectadoId = extraido.EncargadoId,
                        EncargadoDetectadoNombre = extraido.EncargadoNombre,
                        DepartamentoDetectadoId = extraido.DepartamentoId,
                        DepartamentoDetectadoNombre = extraido.DepartamentoNombre,
                        ProyectoDetectadoId = extraido.ProyectoId,
                        ProyectoDetectadoNombre = extraido.ProyectoNombre,
                        PartidasDetectadasCount = extraido.Partidas.Count,
                        ResumenPartidas = resumenPartidas,
                        EstadoBandeja = "En Bandeja (Por Revisar)"
                    };

                    // Si el asunto coincide con las palabras clave y tiene partidas válidas y está activa la auto-creación: crear SOL-xxxx de inmediato
                    if (cfg.ImapAutoCrearPorAsunto && coincideAsunto && extraido.Partidas.Count > 0 && extraido.DepartamentoId > 0 && extraido.EncargadoId > 0)
                    {
                        var solAuto = CrearSolicitud(
                            extraido.DepartamentoId,
                            extraido.EncargadoId,
                            extraido.ProyectoId,
                            "Correo",
                            extraido.NotasDetectadas,
                            extraido.Partidas,
                            "Requerimiento de Compra",
                            null,
                            null,
                            notificarCorreo: true);
                        regCorreo.EstadoBandeja = $"Auto-Creada ({solAuto.Folio})";
                        regCorreo.SolicitudCreadaId = solAuto.Id;
                        regCorreo.SolicitudCreadaFolio = solAuto.Folio;
                        autoCreadas++;
                    }
                    else
                    {
                        nuevosBandeja++;
                    }

                    using (var connIns = new SqliteConnection(ConnectionString))
                    {
                        connIns.Open();
                        using var cmdIns = connIns.CreateCommand();
                        cmdIns.CommandText = @"
INSERT OR IGNORE INTO CorreosEntrantesSolicitud (
    UidImap, FechaCorreo, RemitenteNombre, RemitenteCorreo, Asunto, CuerpoTexto,
    NombreAdjuntoCsv, ContenidoAdjuntoCsv, CoincidePalabraClaveAsunto,
    EncargadoDetectadoId, EncargadoDetectadoNombre, DepartamentoDetectadoId, DepartamentoDetectadoNombre,
    ProyectoDetectadoId, ProyectoDetectadoNombre, PartidasDetectadasCount, ResumenPartidas,
    EstadoBandeja, SolicitudCreadaId, SolicitudCreadaFolio
) VALUES (
    $uid, $fec, $rnom, $rcor, $asu, $cue,
    $ncsv, $ccsv, $coinc,
    $eid, $enom, $did, $dnom,
    $pid, $pnom, $pcnt, $pres,
    $est, $sid, $sfol
);
SELECT last_insert_rowid();";
                        cmdIns.Parameters.AddWithValue("$uid", regCorreo.UidImap);
                        cmdIns.Parameters.AddWithValue("$fec", regCorreo.FechaCorreo);
                        cmdIns.Parameters.AddWithValue("$rnom", regCorreo.RemitenteNombre);
                        cmdIns.Parameters.AddWithValue("$rcor", regCorreo.RemitenteCorreo);
                        cmdIns.Parameters.AddWithValue("$asu", regCorreo.Asunto);
                        cmdIns.Parameters.AddWithValue("$cue", regCorreo.CuerpoTexto);
                        cmdIns.Parameters.AddWithValue("$ncsv", regCorreo.NombreAdjuntoCsv);
                        cmdIns.Parameters.AddWithValue("$ccsv", regCorreo.ContenidoAdjuntoCsv);
                        cmdIns.Parameters.AddWithValue("$coinc", regCorreo.CoincidePalabraClaveAsunto ? 1 : 0);
                        cmdIns.Parameters.AddWithValue("$eid", regCorreo.EncargadoDetectadoId);
                        cmdIns.Parameters.AddWithValue("$enom", regCorreo.EncargadoDetectadoNombre);
                        cmdIns.Parameters.AddWithValue("$did", regCorreo.DepartamentoDetectadoId);
                        cmdIns.Parameters.AddWithValue("$dnom", regCorreo.DepartamentoDetectadoNombre);
                        cmdIns.Parameters.AddWithValue("$pid", regCorreo.ProyectoDetectadoId);
                        cmdIns.Parameters.AddWithValue("$pnom", regCorreo.ProyectoDetectadoNombre);
                        cmdIns.Parameters.AddWithValue("$pcnt", regCorreo.PartidasDetectadasCount);
                        cmdIns.Parameters.AddWithValue("$pres", regCorreo.ResumenPartidas);
                        cmdIns.Parameters.AddWithValue("$est", regCorreo.EstadoBandeja);
                        cmdIns.Parameters.AddWithValue("$sid", regCorreo.SolicitudCreadaId);
                        cmdIns.Parameters.AddWithValue("$sfol", regCorreo.SolicitudCreadaFolio);
                        regCorreo.Id = Convert.ToInt32(cmdIns.ExecuteScalar());
                    }

                    try { OnCorreoSolicitudRecibido?.Invoke(regCorreo); } catch { }
                }

                _ = EnviarComandoImap("A99", "LOGOUT");
                return (true, $"Sincronización completada: {autoCreadas} solicitud(es) auto-creada(s) y {nuevosBandeja} correo(s) nuevo(s) en bandeja de revisión.", nuevosBandeja, autoCreadas);
            }
            catch (Exception ex)
            {
                return (false, $"Error al revisar buzón IMAP: {ex.Message}", nuevosBandeja, autoCreadas);
            }
        });
    }
}
