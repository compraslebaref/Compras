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
    ColorHex TEXT NOT NULL DEFAULT '#0078D4'
);

CREATE TABLE IF NOT EXISTS Productos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CodigoInterno TEXT NOT NULL UNIQUE,
    CodigoBarras TEXT NOT NULL DEFAULT '',
    Nombre TEXT NOT NULL,
    Descripcion TEXT NOT NULL DEFAULT '',
    CategoriaId INTEGER NOT NULL,
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
";
        cmd.ExecuteNonQuery();

        SeedInitialDataIfEmpty(conn);
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
        return $"{prefijo}-{DateTime.Now:yyyy}-{next:D4}";
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
        cmd.CommandText = "SELECT Id, Nombre, Icono, ColorHex FROM Categorias ORDER BY Id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Categoria
            {
                Id = r.GetInt32(0),
                Nombre = r.GetString(1),
                Icono = r.GetString(2),
                ColorHex = r.GetString(3)
            });
        }
        return list;
    }

    public static void GuardarCategoria(string nombre, int id = 0)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (id > 0)
        {
            cmd.CommandText = "UPDATE Categorias SET Nombre=$n WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Categorias (Nombre, Icono, ColorHex) VALUES ($n, 'Box24', '#0078D4')";
        }
        cmd.Parameters.AddWithValue("$n", nombre.Trim());
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
       p.StockMinimo, p.UltimoCostoCompra, p.Ubicacion, p.ImagenRuta, p.EsHerramienta
FROM Productos p
LEFT JOIN Categorias c ON c.Id = p.CategoriaId
WHERE ($cat IS NULL OR p.CategoriaId = $cat)
  AND ($f IS NULL OR p.Nombre LIKE '%' || $f || '%' OR p.CodigoInterno LIKE '%' || $f || '%' OR p.CodigoBarras LIKE '%' || $f || '%' OR p.Descripcion LIKE '%' || $f || '%')
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
                EsHerramienta = r.GetInt32(16) == 1
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
    UnidadCompra=$uc, UnidadAlmacen=$ua, FactorConversion=$fc, StockActual=$sa,
    StockMinimo=$sm, UltimoCostoCompra=$ucc, Ubicacion=$ubi, ImagenRuta=$img, EsHerramienta=$her
WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", p.Id);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO Productos (CodigoInterno, CodigoBarras, Nombre, Descripcion, CategoriaId, UnidadCompra, UnidadAlmacen, FactorConversion, StockActual, StockReservadoProyecto, StockMinimo, UltimoCostoCompra, Ubicacion, ImagenRuta, EsHerramienta)
VALUES ($ci, $cb, $nom, $desc, $cat, $uc, $ua, $fc, $sa, 0, $sm, $ucc, $ubi, $img, $her);
SELECT last_insert_rowid();";
        }

        cmd.Parameters.AddWithValue("$ci", p.CodigoInterno.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$cb", p.CodigoBarras.Trim());
        cmd.Parameters.AddWithValue("$nom", p.Nombre.Trim());
        cmd.Parameters.AddWithValue("$desc", p.Descripcion.Trim());
        cmd.Parameters.AddWithValue("$cat", p.CategoriaId > 0 ? p.CategoriaId : 1);
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Productos WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
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
        using var cmd = conn.CreateCommand();
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

        foreach (var oc in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT ocp.Id, ocp.OrdenCompraId, ocp.ProductoId, COALESCE(p.CodigoInterno, 'PRD'), COALESCE(p.Nombre, 'Producto'),
       COALESCE(p.UnidadCompra, 'Pza'), COALESCE(p.UnidadAlmacen, 'Pza'), COALESCE(p.FactorConversion, 1),
       ocp.CantidadPedida, ocp.CantidadRecibida, ocp.PrecioUnitarioSinIva
FROM OrdenCompraPartidas ocp
LEFT JOIN Productos p ON p.Id = ocp.ProductoId
WHERE ocp.OrdenCompraId = $ocid";
            pCmd.Parameters.AddWithValue("$ocid", oc.Id);
            using var pr = pCmd.ExecuteReader();
            while (pr.Read())
            {
                var part = new OrdenCompraPartida
                {
                    Id = pr.GetInt32(0),
                    OrdenCompraId = pr.GetInt32(1),
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
SELECT ep.Id, ep.EntradaAlmacenId, ep.ProductoId, p.CodigoInterno, p.Nombre,
       ep.CantidadCompraRecibida, ep.UnidadCompra, ep.FactorConversion,
       ep.CantidadIngresadaAlmacen, ep.UnidadAlmacen, ep.CostoUnitarioCompraConIva
FROM EntradaPartidas ep
JOIN Productos p ON p.Id = ep.ProductoId
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

    public static int? BuscarProductoVinculadoXml(string emisorRfc, string noIdentificacion, string descripcion)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ProductoId FROM ProductoProveedorMapa
WHERE EmisorRfc = $rfc AND (ClaveProveedor = $clave OR DescripcionProveedor = $desc)
LIMIT 1";
        cmd.Parameters.AddWithValue("$rfc", emisorRfc.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$clave", noIdentificacion.Trim());
        cmd.Parameters.AddWithValue("$desc", descripcion.Trim());
        var res = cmd.ExecuteScalar();
        return res == null || res == DBNull.Value ? null : Convert.ToInt32(res);
    }

    public static void GuardarMapaProductoProveedor(string emisorRfc, string clave, string descripcion, int productoId, decimal ultimoPrecioSinIva)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM ProductoProveedorMapa WHERE EmisorRfc = $rfc AND (ClaveProveedor = $c AND DescripcionProveedor = $d)";
        del.Parameters.AddWithValue("$rfc", emisorRfc.Trim().ToUpperInvariant());
        del.Parameters.AddWithValue("$c", clave.Trim());
        del.Parameters.AddWithValue("$d", descripcion.Trim());
        del.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ProductoProveedorMapa (EmisorRfc, ClaveProveedor, DescripcionProveedor, ProductoId, UltimoPrecioSinIva)
VALUES ($rfc, $c, $d, $pid, $p)";
        cmd.Parameters.AddWithValue("$rfc", emisorRfc.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$c", clave.Trim());
        cmd.Parameters.AddWithValue("$d", descripcion.Trim());
        cmd.Parameters.AddWithValue("$pid", productoId);
        cmd.Parameters.AddWithValue("$p", (double)ultimoPrecioSinIva);
        cmd.ExecuteNonQuery();
    }

    public static FacturaXml GuardarFacturaXml(FacturaXml factura)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // Verificar si el proveedor ya existe por RFC; si no, darlo de alta automáticamente
        using (var chkProv = conn.CreateCommand())
        {
            chkProv.CommandText = "SELECT COUNT(*) FROM Proveedores WHERE Rfc = $rfc";
            chkProv.Parameters.AddWithValue("$rfc", factura.EmisorRfc.Trim().ToUpperInvariant());
            if (Convert.ToInt32(chkProv.ExecuteScalar()) == 0)
            {
                GuardarProveedor(new Proveedor
                {
                    Rfc = factura.EmisorRfc,
                    RazonSocial = factura.EmisorNombre,
                    NombreComercial = factura.EmisorNombre,
                    Notas = "Registrado automáticamente desde Factura XML CFDI"
                });
            }
        }

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
                decimal costoConIva = Math.Round(c.ValorUnitario * 1.16m, 2);
                using var upP = conn.CreateCommand();
                upP.Transaction = tx;
                upP.CommandText = "UPDATE Productos SET UltimoCostoCompra = $costo WHERE Id = $pid";
                upP.Parameters.AddWithValue("$costo", (double)costoConIva);
                upP.Parameters.AddWithValue("$pid", c.ProductoIdVinculado.Value);
                upP.ExecuteNonQuery();
            }
        }

        tx.Commit();
        return ObtenerFacturasXml().First(f => f.Id == facId);
    }

    public static List<FacturaXml> ObtenerFacturasXml()
    {
        var list = new List<FacturaXml>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Uuid, SerieFolio, FechaTimbrado, EmisorRfc, EmisorNombre, Moneda, TipoCambio,
       Subtotal, Descuento, Iva, Total, OrdenCompraId, OrdenCompraFolio, RutaArchivoXml
FROM FacturasXml
ORDER BY Id DESC";
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
                RutaArchivoXml = r.GetString(14)
            });
        }

        var productosDict = ObtenerProductos().ToDictionary(p => p.Id, p => p.Nombre);
        foreach (var f in list)
        {
            using var cCmd = conn.CreateCommand();
            cCmd.CommandText = @"
SELECT Id, FacturaXmlId, ClaveProdServ, NoIdentificacion, Descripcion, Unidad, Cantidad, ValorUnitario, Importe, IvaConcepto, ProductoIdVinculado
FROM FacturaConceptos
WHERE FacturaXmlId = $fid";
            cCmd.Parameters.AddWithValue("$fid", f.Id);
            using var cr = cCmd.ExecuteReader();
            while (cr.Read())
            {
                int? pid = cr.IsDBNull(10) ? null : cr.GetInt32(10);
                f.Conceptos.Add(new FacturaConcepto
                {
                    Id = cr.GetInt32(0),
                    FacturaXmlId = cr.GetInt32(1),
                    ClaveProdServ = cr.GetString(2),
                    NoIdentificacion = cr.GetString(3),
                    Descripcion = cr.GetString(4),
                    Unidad = cr.GetString(5),
                    Cantidad = Convert.ToDecimal(cr.GetDouble(6)),
                    ValorUnitario = Convert.ToDecimal(cr.GetDouble(7)),
                    Importe = Convert.ToDecimal(cr.GetDouble(8)),
                    IvaConcepto = Convert.ToDecimal(cr.GetDouble(9)),
                    ProductoIdVinculado = pid,
                    ProductoVinculadoNombre = pid.HasValue && productosDict.TryGetValue(pid.Value, out var pNom) ? pNom : "Sin vincular"
                });
            }
        }
        return list;
    }

    public static string CrearRespaldoBaseDatos()
    {
        string dest = Path.Combine(BackupsFolder, $"respaldo_compras_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        File.Copy(DbPath, dest, true);
        return dest;
    }
}
