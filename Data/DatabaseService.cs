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
    public static readonly string DbPath = Path.Combine(BaseFolder, "compras_almacen.db");

    private static string ConnectionString => $"Data Source={DbPath}";

    public static void Initialize()
    {
        Directory.CreateDirectory(BaseFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(XmlFolder);
        Directory.CreateDirectory(BackupsFolder);

        RestaurarDatosRepositorioSiEsNecesario();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

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
    StockMinimo REAL NOT NULL DEFAULT 5,
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

CREATE INDEX IF NOT EXISTS IDX_Productos_CodigoInterno ON Productos(CodigoInterno);
CREATE INDEX IF NOT EXISTS IDX_Productos_Nombre ON Productos(Nombre);
CREATE INDEX IF NOT EXISTS IDX_Productos_CategoriaId ON Productos(CategoriaId);
CREATE INDEX IF NOT EXISTS IDX_OrdenCompraPartidas_OcId ON OrdenCompraPartidas(OrdenCompraId);
CREATE INDEX IF NOT EXISTS IDX_OrdenCompraPartidas_ProdId ON OrdenCompraPartidas(ProductoId);
CREATE INDEX IF NOT EXISTS IDX_FacturaConceptos_FacId ON FacturaConceptos(FacturaXmlId);
";
        cmd.ExecuteNonQuery();

        AsegurarColumnaExiste(conn, "Categorias", "EtiquetaCampo1", "TEXT NOT NULL DEFAULT 'Capacidad Eléctrica / Tipo'");
        AsegurarColumnaExiste(conn, "Categorias", "EtiquetaCampo2", "TEXT NOT NULL DEFAULT 'Especificación / Material'");
        AsegurarColumnaExiste(conn, "Categorias", "EtiquetaCampo3", "TEXT NOT NULL DEFAULT 'Detalle Adicional / Color'");

        AsegurarColumnaExiste(conn, "Productos", "Marca", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Modelo", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Medidas", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "CapacidadElectrica", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Especificacion1", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "Productos", "Especificacion2", "TEXT NOT NULL DEFAULT ''");

        SeedInitialDataIfEmpty(conn);
        MigrarCategoriasYReclasificarCatalogo(conn);
        conn.Close();
        SincronizarFacturasYOrdenesExistentes();
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
SELECT e.Id, e.DepartamentoId, d.Nombre, e.Nombre, e.Puesto, e.Telefono
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
                Telefono = r.GetString(5)
            });
        }
        return list;
    }

    public static void GuardarEncargado(int departamentoId, string nombre, string puesto, string telefono, int id = 0)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (id > 0)
        {
            cmd.CommandText = "UPDATE Encargados SET DepartamentoId=$d, Nombre=$n, Puesto=$p, Telefono=$t WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Encargados (DepartamentoId, Nombre, Puesto, Telefono) VALUES ($d, $n, $p, $t)";
        }
        cmd.Parameters.AddWithValue("$d", departamentoId);
        cmd.Parameters.AddWithValue("$n", nombre.Trim());
        cmd.Parameters.AddWithValue("$p", puesto.Trim());
        cmd.Parameters.AddWithValue("$t", telefono.Trim());
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
       p.Marca, p.Modelo, p.Medidas, p.CapacidadElectrica, p.Especificacion1, p.Especificacion2
FROM Productos p
LEFT JOIN Categorias c ON c.Id = p.CategoriaId
WHERE ($cat IS NULL OR p.CategoriaId = $cat)
  AND ($f IS NULL OR p.Nombre LIKE '%' || $f || '%'
       OR p.CodigoInterno LIKE '%' || $f || '%'
       OR p.CodigoBarras LIKE '%' || $f || '%'
       OR p.Descripcion LIKE '%' || $f || '%'
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
                Especificacion2 = r.IsDBNull(22) ? string.Empty : r.GetString(22)
            });
        }
        return list;
    }

    public static Producto? BuscarProductoPorCodigo(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        string clean = codigo.Trim();
        return ObtenerProductos().FirstOrDefault(p =>
            string.Equals(p.CodigoInterno, clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.CodigoBarras, clean, StringComparison.OrdinalIgnoreCase));
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

        if (p.Id > 0)
        {
            cmd.CommandText = @"
UPDATE Productos SET
    CodigoInterno=$ci, CodigoBarras=$cb, Nombre=$nom, Descripcion=$desc, CategoriaId=$cat,
    Marca=$mar, Modelo=$mod, Medidas=$med, CapacidadElectrica=$cap, Especificacion1=$esp1, Especificacion2=$esp2,
    UnidadCompra=$uc, UnidadAlmacen=$ua, FactorConversion=$fc, StockActual=$sa,
    StockMinimo=$sm, UltimoCostoCompra=$ucc, Ubicacion=$ubi, ImagenRuta=$img, EsHerramienta=$her
WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", p.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO Productos (CodigoInterno, CodigoBarras, Nombre, Descripcion, CategoriaId, Marca, Modelo, Medidas, CapacidadElectrica, Especificacion1, Especificacion2, UnidadCompra, UnidadAlmacen, FactorConversion, StockActual, StockReservadoProyecto, StockMinimo, UltimoCostoCompra, Ubicacion, ImagenRuta, EsHerramienta)
VALUES ($ci, $cb, $nom, $desc, $cat, $mar, $mod, $med, $cap, $esp1, $esp2, $uc, $ua, $fc, $sa, 0, $sm, $ucc, $ubi, $img, $her);
SELECT last_insert_rowid();";
        }

        cmd.Parameters.AddWithValue("$ci", p.CodigoInterno.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$cb", p.CodigoBarras.Trim());
        cmd.Parameters.AddWithValue("$nom", p.Nombre.Trim());
        cmd.Parameters.AddWithValue("$desc", (p.Descripcion ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$cat", p.CategoriaId > 0 ? p.CategoriaId : 1);
        cmd.Parameters.AddWithValue("$mar", (p.Marca ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$mod", (p.Modelo ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$med", (p.Medidas ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$cap", (p.CapacidadElectrica ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$esp1", (p.Especificacion1 ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$esp2", (p.Especificacion2 ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$uc", p.UnidadCompra.Trim());
        cmd.Parameters.AddWithValue("$ua", p.UnidadAlmacen.Trim());
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

        tx.Commit();
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

        if (ContieneAlguno(u, "CASCO DE SEGURIDAD", "BARBOQUEJO", "LENTES DE SEGURIDAD", "LENTE CON RECUBRIMIENTO", "GUANTE", "PONCHO UNITALLA", "BANDA DE PLÁSTICOPROHIBIDO", "PROHIBIDO"))
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

        if (ContieneAlguno(u, "FLEXOMETRO", "VENTILADOR PEDESTAL", "SOPLADORA", "DESARMADOR", "ASPIRADORA", "HIDROLAVADORA", "LAVA-1500P", "ESCOBA JAR", "ESCOBA METÁLICA", "PUNTAS DE IMPACTO", "DADOS MAG", "SEGUETA", "BROQUERO", "BROCA", "MULTÍMETRO", "MULTIMETRO", "CABLES DE PRUEBA", "MACHETE", "PALA CUADRADA", "LINTERNA", "LAMPARA PORTATIL"))
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

    public static List<HerramientaActivo> ObtenerHerramientas()
    {
        var list = new List<HerramientaActivo>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT h.Id, h.ProductoId, COALESCE(p.Nombre, 'Herramienta'), h.CodigoIndividual, h.NumeroSerie, h.MarcaModelo,
       h.Estado, h.Ubicacion, h.EncargadoActual, h.ProyectoActual, h.NotasEstado
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
                NotasEstado = r.GetString(10)
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
UPDATE HerramientasActivos SET ProductoId=$pid, CodigoIndividual=$ci, NumeroSerie=$ns, MarcaModelo=$mm, Estado=$est, Ubicacion=$ubi, NotasEstado=$not
WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", h.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO HerramientasActivos (ProductoId, CodigoIndividual, NumeroSerie, MarcaModelo, Estado, Ubicacion, EncargadoActual, ProyectoActual, NotasEstado)
VALUES ($pid, $ci, $ns, $mm, $est, $ubi, '', '', $not)";
        }
        cmd.Parameters.AddWithValue("$pid", h.ProductoId);
        cmd.Parameters.AddWithValue("$ci", h.CodigoIndividual.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$ns", h.NumeroSerie.Trim());
        cmd.Parameters.AddWithValue("$mm", h.MarcaModelo.Trim());
        cmd.Parameters.AddWithValue("$est", h.Estado);
        cmd.Parameters.AddWithValue("$ubi", h.Ubicacion.Trim());
        cmd.Parameters.AddWithValue("$not", h.NotasEstado.Trim());
        cmd.ExecuteNonQuery();
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
       s.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Sin proyecto'), s.Origen, s.Estatus, s.Notas
FROM Solicitudes s
LEFT JOIN Departamentos d ON d.Id = s.DepartamentoId
LEFT JOIN Encargados e ON e.Id = s.EncargadoId
LEFT JOIN Proyectos pr ON pr.Id = p.ProyectoId
ORDER BY s.Id DESC".Replace("p.ProyectoId", "s.ProyectoId");
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
                Notas = r.GetString(11)
            });
        }

        foreach (var sol in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT sp.Id, sp.SolicitudId, sp.ProductoId, COALESCE(p.CodigoInterno, 'PRD'), COALESCE(p.Nombre, 'Producto eliminado'), COALESCE(p.UnidadCompra, 'Pza'), sp.CantidadSolicitada, COALESCE(p.StockActual, 0), sp.NotasPartida
FROM SolicitudPartidas sp
LEFT JOIN Productos p ON p.Id = sp.ProductoId
WHERE sp.SolicitudId = $sid";
            pCmd.Parameters.AddWithValue("$sid", sol.Id);
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                sol.Partidas.Add(new SolicitudPartida
                {
                    Id = pr.GetInt32(0),
                    SolicitudId = pr.GetInt32(1),
                    ProductoId = pr.GetInt32(2),
                    ProductoCodigo = pr.GetString(3),
                    ProductoNombre = pr.GetString(4),
                    UnidadCompra = pr.GetString(5),
                    CantidadSolicitada = Convert.ToDecimal(pr.GetDouble(6)),
                    StockActualAlmacen = Convert.ToDecimal(pr.GetDouble(7)),
                    NotasPartida = pr.GetString(8)
                });
            }
        }
        return list;
    }

    public static Solicitud CrearSolicitud(int departamentoId, int encargadoId, int proyectoId, string origen, string notas, List<SolicitudPartida> partidas)
    {
        string folio = GenerarSiguienteFolio("SOL", "Solicitudes");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO Solicitudes (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, Origen, Estatus, Notas)
VALUES ($f, $fe, $d, $e, $p, $or, 'Pendiente', $n);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$f", folio);
        cmd.Parameters.AddWithValue("$fe", fecha);
        cmd.Parameters.AddWithValue("$d", departamentoId);
        cmd.Parameters.AddWithValue("$e", encargadoId);
        cmd.Parameters.AddWithValue("$p", proyectoId);
        cmd.Parameters.AddWithValue("$or", origen);
        cmd.Parameters.AddWithValue("$n", notas.Trim());
        int solId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var item in partidas)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO SolicitudPartidas (SolicitudId, ProductoId, CantidadSolicitada, NotasPartida)
VALUES ($sid, $pid, $cant, $not)";
            pCmd.Parameters.AddWithValue("$sid", solId);
            pCmd.Parameters.AddWithValue("$pid", item.ProductoId);
            pCmd.Parameters.AddWithValue("$cant", (double)item.CantidadSolicitada);
            pCmd.Parameters.AddWithValue("$not", item.NotasPartida ?? string.Empty);
            pCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return ObtenerSolicitudes().First(s => s.Id == solId);
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
       oc.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Sin proyecto'), oc.DepartamentoNombre, oc.EncargadoNombre,
       oc.TiempoEntregaDias, oc.CostoEnvioConIva, oc.Subtotal, oc.Iva, oc.TotalConIva, oc.Estatus, oc.Notas
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
                    Notas = r.GetString(20)
                });
            }
        }

        if (list.Count == 0)
            return list;

        var ocDict = list.ToDictionary(o => o.Id);
        using (var pCmd = conn.CreateCommand())
        {
            pCmd.CommandText = @"
SELECT ocp.Id, ocp.OrdenCompraId, ocp.ProductoId, COALESCE(p.CodigoInterno, 'PRD'), COALESCE(p.Nombre, 'Producto'),
       COALESCE(p.UnidadCompra, 'Pza'), COALESCE(p.UnidadAlmacen, 'Pza'), COALESCE(p.FactorConversion, 1),
       ocp.CantidadPedida, ocp.CantidadRecibida, ocp.PrecioUnitarioSinIva
FROM OrdenCompraPartidas ocp
INNER JOIN Productos p ON p.Id = ocp.ProductoId";
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                int ocId = pr.GetInt32(1);
                if (!ocDict.TryGetValue(ocId, out var oc))
                    continue;

                var part = new OrdenCompraPartida
                {
                    Id = pr.GetInt32(0),
                    OrdenCompraId = ocId,
                    ProductoId = pr.GetInt32(2),
                    ProductoCodigo = pr.GetString(3),
                    ProductoNombre = pr.GetString(4),
                    UnidadCompra = pr.GetString(5),
                    UnidadAlmacen = pr.GetString(6),
                    FactorConversion = Convert.ToDecimal(pr.GetDouble(7)),
                    CantidadPedida = Convert.ToDecimal(pr.GetDouble(8)),
                    CantidadRecibida = Convert.ToDecimal(pr.GetDouble(9)),
                    PrecioUnitarioSinIva = Convert.ToDecimal(pr.GetDouble(10))
                };
                part.CantidadRecibirAhora = part.CantidadPendiente;
                oc.Partidas.Add(part);
            }
        }

        return list;
    }

    public static List<OrdenCompra> GenerarOrdenesDesdeComparativa(
        Solicitud solicitud,
        string folioComparativa,
        List<CotizacionRenglonComparativo> renglones,
        Dictionary<int, (Proveedor? prov, decimal fleteConIva, int diasEntrega)> configColumnas)
    {
        var creadas = new List<OrdenCompra>();
        var gruposPorColumna = renglones
            .Where(r => r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorIndice <= 3)
            .GroupBy(r => r.ProveedorGanadorIndice);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        foreach (var grupo in gruposPorColumna)
        {
            int colIdx = grupo.Key;
            if (!configColumnas.TryGetValue(colIdx, out var cfg) || cfg.prov == null)
                continue;

            decimal subtotalPartidas = 0m;
            var partidasOc = new List<(int prodId, decimal cant, decimal precioSinIva)>();

            foreach (var reng in grupo)
            {
                decimal precioSinIva = colIdx switch
                {
                    1 => reng.PrecioSinIva1,
                    2 => reng.PrecioSinIva2,
                    3 => reng.PrecioSinIva3,
                    _ => reng.PrecioSinIva1
                };
                subtotalPartidas += Math.Round(reng.Cantidad * precioSinIva, 2);
                partidasOc.Add((reng.ProductoId, reng.Cantidad, precioSinIva));
            }

            decimal fleteSinIva = Math.Round(cfg.fleteConIva / 1.16m, 2);
            decimal subtotalTotal = Math.Round(subtotalPartidas + fleteSinIva, 2);
            decimal ivaTotal = Math.Round(subtotalTotal * 0.16m, 2);
            decimal totalConIva = Math.Round(subtotalTotal + ivaTotal, 2);

            string folioOc = GenerarSiguienteFolio("OC", "OrdenesCompra");
            string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO OrdenesCompra (Folio, Fecha, SolicitudId, CotizacionFolio, ProveedorId, ProyectoId, DepartamentoNombre, EncargadoNombre, TiempoEntregaDias, CostoEnvioConIva, Subtotal, Iva, TotalConIva, Estatus, Notas)
VALUES ($fol, $fec, $sid, $cfol, $pvid, $prid, $dep, $enc, $dias, $flete, $sub, $iva, $tot, 'Emitida', $not);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$fol", folioOc);
            cmd.Parameters.AddWithValue("$fec", fecha);
            cmd.Parameters.AddWithValue("$sid", solicitud.Id);
            cmd.Parameters.AddWithValue("$cfol", folioComparativa);
            cmd.Parameters.AddWithValue("$pvid", cfg.prov.Id);
            cmd.Parameters.AddWithValue("$prid", solicitud.ProyectoId);
            cmd.Parameters.AddWithValue("$dep", solicitud.DepartamentoNombre);
            cmd.Parameters.AddWithValue("$enc", solicitud.EncargadoNombre);
            cmd.Parameters.AddWithValue("$dias", cfg.diasEntrega);
            cmd.Parameters.AddWithValue("$flete", (double)cfg.fleteConIva);
            cmd.Parameters.AddWithValue("$sub", (double)subtotalTotal);
            cmd.Parameters.AddWithValue("$iva", (double)ivaTotal);
            cmd.Parameters.AddWithValue("$tot", (double)totalConIva);
            cmd.Parameters.AddWithValue("$not", $"Generada desde comparativa {folioComparativa} ({solicitud.Folio})");
            int ocId = Convert.ToInt32(cmd.ExecuteScalar());

            foreach (var p in partidasOc)
            {
                using var pCmd = conn.CreateCommand();
                pCmd.Transaction = tx;
                pCmd.CommandText = @"
INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva)
VALUES ($ocid, $pid, $cant, 0, $precio)";
                pCmd.Parameters.AddWithValue("$ocid", ocId);
                pCmd.Parameters.AddWithValue("$pid", p.prodId);
                pCmd.Parameters.AddWithValue("$cant", (double)p.cant);
                pCmd.Parameters.AddWithValue("$precio", (double)p.precioSinIva);
                pCmd.ExecuteNonQuery();
            }

            using var upSol = conn.CreateCommand();
            upSol.Transaction = tx;
            upSol.CommandText = "UPDATE Solicitudes SET Estatus = 'OC Generada' WHERE Id = $sid";
            upSol.Parameters.AddWithValue("$sid", solicitud.Id);
            upSol.ExecuteNonQuery();

            tx.Commit();
            creadas.Add(ObtenerOrdenesCompra().First(o => o.Id == ocId));
        }

        return creadas;
    }

    public static EntradaAlmacen RegistrarRecepcionOrdenCompra(
        OrdenCompra oc,
        List<(OrdenCompraPartida partida, decimal cantidadRecibirAhora)> recepcion,
        string notas)
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
    UltimoCostoCompra = $costo
WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$ca", (double)cantAlmacen);
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

        tx.Commit();
        return ObtenerEntradasAlmacen().First(e => e.Id == entId);
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

    public static SalidaAlmacen RegistrarSalidaAlmacen(int departamentoId, int encargadoId, int proyectoId, string notas, List<SalidaPartida> partidas)
    {
        string folio = GenerarSiguienteFolio("SAL", "SalidasAlmacen");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        decimal costoTotal = partidas.Sum(p => p.ImporteCosto);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO SalidasAlmacen (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, CostoTotalEstimado, Notas)
VALUES ($fol, $fec, $dep, $enc, $proy, $costo, $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folio);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$dep", departamentoId);
        cmd.Parameters.AddWithValue("$enc", encargadoId);
        cmd.Parameters.AddWithValue("$proy", proyectoId);
        cmd.Parameters.AddWithValue("$costo", (double)costoTotal);
        cmd.Parameters.AddWithValue("$not", notas.Trim());
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
            upProd.CommandText = "UPDATE Productos SET StockActual = MAX(0, StockActual - $cant) WHERE Id = $pid";
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
       s.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Sin proyecto'), s.CostoTotalEstimado, s.Notas
FROM SalidasAlmacen s
LEFT JOIN Departamentos d ON d.Id = s.DepartamentoId
LEFT JOIN Encargados e ON e.Id = s.EncargadoId
LEFT JOIN Proyectos pr ON pr.Id = s.ProyectoId
ORDER BY s.Id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new SalidaAlmacen
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
                CostoTotalEstimado = Convert.ToDecimal(r.GetDouble(9)),
                Notas = r.GetString(10)
            });
        }

        foreach (var sal in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT sp.Id, sp.SalidaAlmacenId, sp.ProductoId, COALESCE(p.CodigoInterno, 'PRD'), COALESCE(p.Nombre, 'Producto'),
       sp.CantidadEntregada, sp.UnidadAlmacen, sp.CostoUnitarioAlmacen
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
                    CostoUnitarioAlmacen = Convert.ToDecimal(pr.GetDouble(7))
                });
            }
        }
        return list;
    }

    // ==================== PRÉSTAMO Y DEVOLUCIÓN DE HERRAMIENTAS ====================

    public static PrestamoHerramienta RegistrarPrestamoHerramienta(
        int departamentoId,
        int encargadoId,
        int proyectoId,
        string notas,
        List<HerramientaActivo> herramientas)
    {
        string folio = GenerarSiguienteFolio("PRE", "PrestamosHerramienta");
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var enc = ObtenerEncargados().FirstOrDefault(e => e.Id == encargadoId);
        var proy = ObtenerProyectos().FirstOrDefault(p => p.Id == proyectoId);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO PrestamosHerramienta (Folio, FechaSalida, FechaDevolucion, DepartamentoId, EncargadoId, ProyectoId, Estatus, Notas)
VALUES ($fol, $fec, '', $dep, $enc, $proy, 'Activo', $not);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fol", folio);
        cmd.Parameters.AddWithValue("$fec", fecha);
        cmd.Parameters.AddWithValue("$dep", departamentoId);
        cmd.Parameters.AddWithValue("$enc", encargadoId);
        cmd.Parameters.AddWithValue("$proy", proyectoId);
        cmd.Parameters.AddWithValue("$not", notas.Trim());
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
            upHer.Parameters.AddWithValue("$encNom", enc?.Nombre ?? "");
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
            "Bueno" => "Disponible",
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

        // Revisar si todas las herramientas del préstamo ya fueron devueltas
        using var chk = conn.CreateCommand();
        chk.Transaction = tx;
        chk.CommandText = "SELECT COUNT(*) FROM PrestamoPartidas WHERE PrestamoHerramientaId = $pid AND EstadoDevolucion = 'Pendiente'";
        chk.Parameters.AddWithValue("$pid", prestamoId);
        int pend = Convert.ToInt32(chk.ExecuteScalar());
        if (pend == 0)
        {
            using var upPre = conn.CreateCommand();
            upPre.Transaction = tx;
            upPre.CommandText = "UPDATE PrestamosHerramienta SET Estatus = 'Devuelto Completo', FechaDevolucion = $fec WHERE Id = $pid";
            upPre.Parameters.AddWithValue("$fec", fecha);
            upPre.Parameters.AddWithValue("$pid", prestamoId);
            upPre.ExecuteNonQuery();
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
       p.EncargadoId, COALESCE(e.Nombre, 'Sin encargado'), p.ProyectoId, COALESCE(pr.Codigo || ' - ' || pr.Nombre, 'Sin proyecto'), p.Estatus, p.Notas
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
                Notas = r.GetString(11)
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

        var provExistente = ObtenerProveedores().FirstOrDefault(p => p.Rfc.Trim().Equals(rfcNorm, StringComparison.OrdinalIgnoreCase));
        if (provExistente != null)
            return provExistente.Id;

        GuardarProveedor(new Proveedor
        {
            Rfc = rfcNorm,
            RazonSocial = string.IsNullOrWhiteSpace(emisorNombre) ? rfcNorm : emisorNombre.Trim(),
            NombreComercial = string.IsNullOrWhiteSpace(emisorNombre) ? rfcNorm : emisorNombre.Trim(),
            Notas = "Registrado automáticamente desde Factura XML CFDI"
        });

        return ObtenerProveedores().First(p => p.Rfc.Trim().Equals(rfcNorm, StringComparison.OrdinalIgnoreCase)).Id;
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

            var porCoincidencia = todosProductos.FirstOrDefault(p =>
                (p.Nombre.Length >= 6 && desc.Contains(p.Nombre, StringComparison.OrdinalIgnoreCase)) ||
                (desc.Length >= 6 && p.Nombre.Contains(desc, StringComparison.OrdinalIgnoreCase)));
            if (porCoincidencia != null)
                return porCoincidencia;
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
        string unidad = string.IsNullOrWhiteSpace(c.Unidad) ? "PZA" : c.Unidad.Trim();
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
            StockMinimo = 5m,
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

        var facturasDeEstaOc = ObtenerFacturasXml().Where(f => f.OrdenCompraId == ordenCompraId).OrderBy(f => f.Id).ToList();
        if (facturasDeEstaOc.Count == 0) return;

        var primeraFac = facturasDeEstaOc.First();
        int provId = AsegurarProveedorPorRfc(primeraFac.EmisorRfc, primeraFac.EmisorNombre);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // Conservar cantidades ya recibidas en almacén por ProductoId
        var recibidasPorProducto = new Dictionary<int, decimal>();
        using (var qRec = conn.CreateCommand())
        {
            qRec.CommandText = "SELECT ProductoId, SUM(CantidadRecibida) FROM OrdenCompraPartidas WHERE OrdenCompraId = $ocid GROUP BY ProductoId";
            qRec.Parameters.AddWithValue("$ocid", ordenCompraId);
            using var r = qRec.ExecuteReader();
            while (r.Read())
            {
                recibidasPorProducto[r.GetInt32(0)] = Convert.ToDecimal(r.GetDouble(1));
            }
        }

        using var tx = conn.BeginTransaction();

        decimal subtotalSum = facturasDeEstaOc.Sum(f => Math.Max(0m, f.Subtotal - f.Descuento));
        decimal ivaSum = facturasDeEstaOc.Sum(f => f.Iva);
        decimal totalSum = facturasDeEstaOc.Sum(f => f.Total);
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
            upOc.Parameters.AddWithValue("$sub", (double)subtotalSum);
            upOc.Parameters.AddWithValue("$iva", (double)ivaSum);
            upOc.Parameters.AddWithValue("$tot", (double)totalSum);
            upOc.Parameters.AddWithValue("$not", $"Asociada a Factura(s) XML: {foliosFacturas}");
            upOc.Parameters.AddWithValue("$ocid", ordenCompraId);
            upOc.ExecuteNonQuery();
        }

        using (var delPart = conn.CreateCommand())
        {
            delPart.Transaction = tx;
            delPart.CommandText = "DELETE FROM OrdenCompraPartidas WHERE OrdenCompraId = $ocid";
            delPart.Parameters.AddWithValue("$ocid", ordenCompraId);
            delPart.ExecuteNonQuery();
        }

        foreach (var fac in facturasDeEstaOc)
        {
            foreach (var c in fac.Conceptos.Where(x => x.ProductoIdVinculado.HasValue && x.ProductoIdVinculado.Value > 0))
            {
                int prodId = c.ProductoIdVinculado!.Value;
                decimal cantPedida = c.Cantidad > 0m ? c.Cantidad : 1m;
                decimal cantRecibida = 0m;
                if (recibidasPorProducto.TryGetValue(prodId, out decimal disp) && disp > 0m)
                {
                    cantRecibida = Math.Min(cantPedida, disp);
                    recibidasPorProducto[prodId] = Math.Max(0m, disp - cantRecibida);
                }

                decimal precioSinIva = CalcularPrecioUnitarioSinIvaParaOc(c);

                using var insPart = conn.CreateCommand();
                insPart.Transaction = tx;
                insPart.CommandText = @"
INSERT INTO OrdenCompraPartidas (OrdenCompraId, ProductoId, CantidadPedida, CantidadRecibida, PrecioUnitarioSinIva)
VALUES ($ocid, $pid, $cant, $rec, $precio)";
                insPart.Parameters.AddWithValue("$ocid", ordenCompraId);
                insPart.Parameters.AddWithValue("$pid", prodId);
                insPart.Parameters.AddWithValue("$cant", (double)cantPedida);
                insPart.Parameters.AddWithValue("$rec", (double)cantRecibida);
                insPart.Parameters.AddWithValue("$precio", (double)precioSinIva);
                insPart.ExecuteNonQuery();
            }
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

    public static FacturaXml GuardarFacturaXml(FacturaXml factura)
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
        string dest = Path.Combine(BackupsFolder, $"respaldo_compras_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        File.Copy(DbPath, dest, true);
        return dest;
    }
}
