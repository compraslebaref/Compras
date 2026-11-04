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
    Mensaje TEXT NOT NULL,
    Leida INTEGER NOT NULL DEFAULT 0
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
        AsegurarColumnaExiste(conn, "Solicitudes", "TipoSolicitud", "TEXT NOT NULL DEFAULT 'Requerimiento de Compra'");
        AsegurarColumnaExiste(conn, "OrdenesCompra", "CondicionPago", "TEXT NOT NULL DEFAULT 'Contado (Transferencia previa)'");
        AsegurarColumnaExiste(conn, "OrdenesCompra", "FechaProgramadaPago", "TEXT NOT NULL DEFAULT ''");

        // Columnas de persona abierta que recibe y fecha compromiso en Salidas y Préstamos
        AsegurarColumnaExiste(conn, "SalidasAlmacen", "PersonaRecibe", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "SalidaPartidas", "CantidadDevuelta", "REAL NOT NULL DEFAULT 0");
        AsegurarColumnaExiste(conn, "PrestamosHerramienta", "PersonaRecibe", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "PrestamosHerramienta", "FechaCompromiso", "TEXT NOT NULL DEFAULT ''");

        // Columnas de EventosCalendario y BitacoraAdministrador
        AsegurarColumnaExiste(conn, "EventosCalendario", "Descripcion", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "EventosCalendario", "ReferenciaFolio", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "BitacoraAdministrador", "Referencia", "TEXT NOT NULL DEFAULT ''");
        AsegurarColumnaExiste(conn, "BitacoraAdministrador", "Motivo", "TEXT NOT NULL DEFAULT ''");

        using (var pinCmd = conn.CreateCommand())
        {
            pinCmd.CommandText = "INSERT OR IGNORE INTO ConfiguracionSistema (Clave, Valor) VALUES ('AdminPin', '1234');";
            pinCmd.ExecuteNonQuery();
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
       COALESCE(NULLIF(s.TipoSolicitud, ''), 'Requerimiento de Compra')
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
                TipoSolicitud = r.GetString(12)
            });
        }

        foreach (var sol in list)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
SELECT sp.Id, sp.SolicitudId, sp.ProductoId, COALESCE(p.CodigoInterno, 'CONCEPTO'),
       CASE WHEN sp.ProductoId > 0 AND p.Nombre IS NOT NULL THEN p.Nombre ELSE COALESCE(NULLIF(sp.DescripcionLibre, ''), 'Concepto Abierto') END,
       CASE WHEN sp.ProductoId > 0 AND p.UnidadCompra IS NOT NULL THEN p.UnidadCompra ELSE COALESCE(NULLIF(sp.UnidadLibre, ''), 'Pieza') END,
       sp.CantidadSolicitada, COALESCE(p.StockActual, 0), sp.NotasPartida,
       COALESCE(sp.Proveedor1Id, 0), COALESCE(sp.PrecioSinIva1, 0), COALESCE(sp.DiasEntrega1, 2), COALESCE(sp.FleteConIva1, 0), COALESCE(sp.NotasCotizacion1, ''),
       COALESCE(sp.Proveedor2Id, 0), COALESCE(sp.PrecioSinIva2, 0), COALESCE(sp.DiasEntrega2, 3), COALESCE(sp.FleteConIva2, 0), COALESCE(sp.NotasCotizacion2, ''),
       COALESCE(sp.Proveedor3Id, 0), COALESCE(sp.PrecioSinIva3, 0), COALESCE(sp.DiasEntrega3, 1), COALESCE(sp.FleteConIva3, 0), COALESCE(sp.NotasCotizacion3, ''),
       COALESCE(sp.ProveedorGanadorIndice, 0), COALESCE(sp.OrdenCompraId, 0), COALESCE(sp.OrdenCompraFolio, ''),
       COALESCE(sp.DescripcionLibre, ''), COALESCE(p.Familia, ''),
       COALESCE(sp.IncluirEnCotizacion, 1), COALESCE(sp.DiasVigenciaCotizacion, 15), COALESCE(sp.FechaCotizacion, '')
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
                    Proveedor2Id = pr.GetInt32(14),
                    PrecioSinIva2 = Convert.ToDecimal(pr.GetDouble(15)),
                    DiasEntrega2 = pr.GetInt32(16),
                    FleteConIva2 = Convert.ToDecimal(pr.GetDouble(17)),
                    NotasCotizacion2 = pr.GetString(18),
                    Proveedor3Id = pr.GetInt32(19),
                    PrecioSinIva3 = Convert.ToDecimal(pr.GetDouble(20)),
                    DiasEntrega3 = pr.GetInt32(21),
                    FleteConIva3 = Convert.ToDecimal(pr.GetDouble(22)),
                    NotasCotizacion3 = pr.GetString(23),
                    ProveedorGanadorIndice = pr.GetInt32(24),
                    OrdenCompraId = pr.GetInt32(25),
                    OrdenCompraFolio = pr.GetString(26),
                    IncluirEnCotizacion = pr.GetInt32(29) != 0,
                    DiasVigenciaCotizacion = pr.GetInt32(30) > 0 ? pr.GetInt32(30) : 15,
                    FechaCotizacion = pr.GetString(31)
                });
            }
        }
        return list;
    }

    public static Solicitud CrearSolicitud(int departamentoId, int encargadoId, int proyectoId, string origen, string notas, List<SolicitudPartida> partidas, string tipoSolicitud = "Requerimiento de Compra", string? folioPersonalizado = null)
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
INSERT INTO Solicitudes (Folio, Fecha, DepartamentoId, EncargadoId, ProyectoId, Origen, Estatus, Notas, TipoSolicitud)
VALUES ($f, $fe, $d, $e, $p, $or, 'Pendiente', $n, $ts);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$f", folio);
        cmd.Parameters.AddWithValue("$fe", fecha);
        cmd.Parameters.AddWithValue("$d", departamentoId);
        cmd.Parameters.AddWithValue("$e", encargadoId);
        cmd.Parameters.AddWithValue("$p", proyectoId);
        cmd.Parameters.AddWithValue("$or", origen);
        cmd.Parameters.AddWithValue("$n", notas.Trim());
        cmd.Parameters.AddWithValue("$ts", tipoLim);
        int solId = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var item in partidas)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = @"
INSERT INTO SolicitudPartidas (SolicitudId, ProductoId, DescripcionLibre, UnidadLibre, CantidadSolicitada, NotasPartida, PrecioSinIva1, NotasCotizacion1, IncluirEnCotizacion, DiasVigenciaCotizacion)
VALUES ($sid, $pid, $dlib, $ulib, $cant, $not, $pr1, $nc1, $inc, $vig)";
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
        return ObtenerSolicitudes().First(s => s.Id == solId);
    }

    public static Solicitud ActualizarSolicitudExistente(int solicitudId, int departamentoId, int encargadoId, int proyectoId, string origen, string notas, List<SolicitudPartida> partidas, string tipoSolicitud = "Requerimiento de Compra")
    {
        var solPrevia = ObtenerSolicitudes().FirstOrDefault(s => s.Id == solicitudId);
        if (solPrevia != null && solPrevia.Estatus == "OC Generada")
        {
            throw new InvalidOperationException("No se puede modificar una Solicitud que ya tiene todas sus Órdenes de Compra generadas.");
        }

        static string ClavePartida(SolicitudPartida sp) =>
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
SET DepartamentoId = $d, EncargadoId = $e, ProyectoId = $p, Origen = $or, Notas = $n, TipoSolicitud = $ts
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$d", departamentoId);
            cmd.Parameters.AddWithValue("$e", encargadoId);
            cmd.Parameters.AddWithValue("$p", proyectoId);
            cmd.Parameters.AddWithValue("$or", origen);
            cmd.Parameters.AddWithValue("$n", (notas ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("$ts", tipoLim);
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
    Proveedor1Id, PrecioSinIva1, DiasEntrega1, FleteConIva1, NotasCotizacion1,
    Proveedor2Id, PrecioSinIva2, DiasEntrega2, FleteConIva2, NotasCotizacion2,
    Proveedor3Id, PrecioSinIva3, DiasEntrega3, FleteConIva3, NotasCotizacion3,
    ProveedorGanadorIndice, OrdenCompraId, OrdenCompraFolio,
    IncluirEnCotizacion, DiasVigenciaCotizacion, FechaCotizacion
)
VALUES (
    $sid, $pid, $dlib, $ulib, $cant, $not,
    $pv1, $pr1, $d1, $fl1, $nc1,
    $pv2, $pr2, $d2, $fl2, $nc2,
    $pv3, $pr3, $d3, $fl3, $nc3,
    $gan, 0, '',
    $inc, $vig, $fcot
)";
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

            pCmd.Parameters.AddWithValue("$pv2", prev?.Proveedor2Id ?? item.Proveedor2Id);
            pCmd.Parameters.AddWithValue("$pr2", (double)(prev?.PrecioSinIva2 ?? item.PrecioSinIva2));
            pCmd.Parameters.AddWithValue("$d2", prev?.DiasEntrega2 ?? (item.DiasEntrega2 > 0 ? item.DiasEntrega2 : 3));
            pCmd.Parameters.AddWithValue("$fl2", (double)(prev?.FleteConIva2 ?? item.FleteConIva2));
            pCmd.Parameters.AddWithValue("$nc2", prev?.NotasCotizacion2 ?? item.NotasCotizacion2 ?? string.Empty);

            pCmd.Parameters.AddWithValue("$pv3", prev?.Proveedor3Id ?? item.Proveedor3Id);
            pCmd.Parameters.AddWithValue("$pr3", (double)(prev?.PrecioSinIva3 ?? item.PrecioSinIva3));
            pCmd.Parameters.AddWithValue("$d3", prev?.DiasEntrega3 ?? (item.DiasEntrega3 > 0 ? item.DiasEntrega3 : 1));
            pCmd.Parameters.AddWithValue("$fl3", (double)(prev?.FleteConIva3 ?? item.FleteConIva3));
            pCmd.Parameters.AddWithValue("$nc3", prev?.NotasCotizacion3 ?? item.NotasCotizacion3 ?? string.Empty);

            pCmd.Parameters.AddWithValue("$gan", prev?.ProveedorGanadorIndice ?? item.ProveedorGanadorIndice);
            pCmd.Parameters.AddWithValue("$inc", item.IncluirEnCotizacion ? 1 : 0);
            pCmd.Parameters.AddWithValue("$vig", prev?.DiasVigenciaCotizacion ?? (item.DiasVigenciaCotizacion > 0 ? item.DiasVigenciaCotizacion : 15));
            pCmd.Parameters.AddWithValue("$fcot", prev?.FechaCotizacion ?? item.FechaCotizacion ?? string.Empty);
            pCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return ObtenerSolicitudes().First(s => s.Id == solicitudId);
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

    public static Solicitud AgregarOIncrementarPartidaEnSolicitud(int solicitudId, int productoId, decimal cantidadAgregar, string notasPartida = "")
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

        int? partidaExistenteId = null;
        decimal cantExistente = 0m;
        using (var findCmd = conn.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = "SELECT Id, CantidadSolicitada FROM SolicitudPartidas WHERE SolicitudId = $sid AND ProductoId = $pid LIMIT 1";
            findCmd.Parameters.AddWithValue("$sid", solicitudId);
            findCmd.Parameters.AddWithValue("$pid", productoId);
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
INSERT INTO SolicitudPartidas (SolicitudId, ProductoId, CantidadSolicitada, NotasPartida)
VALUES ($sid, $pid, $cant, $not)";
            insCmd.Parameters.AddWithValue("$sid", solicitudId);
            insCmd.Parameters.AddWithValue("$pid", productoId);
            insCmd.Parameters.AddWithValue("$cant", (double)cantidadAgregar);
            insCmd.Parameters.AddWithValue("$not", notasPartida ?? string.Empty);
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
       CASE WHEN ocp.ProductoId > 0 AND p.UnidadCompra IS NOT NULL THEN p.UnidadCompra ELSE COALESCE(NULLIF(ocp.UnidadLibre, ''), 'Pieza') END,
       COALESCE(p.UnidadAlmacen, COALESCE(NULLIF(ocp.UnidadLibre, ''), 'Pieza')), COALESCE(p.FactorConversion, 1),
       ocp.CantidadPedida, ocp.CantidadRecibida, ocp.PrecioUnitarioSinIva, COALESCE(ocp.DescripcionLibre, '')
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
                var part = new OrdenCompraPartida
                {
                    Id = pr.GetInt32(0),
                    OrdenCompraId = ocId,
                    ProductoId = prodId,
                    ProductoCodigo = prodId > 0 ? pr.GetString(3) : "CONCEPTO",
                    ProductoNombre = nom,
                    DescripcionLibre = !string.IsNullOrWhiteSpace(dLib) ? dLib : nom,
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

        if (idsCandidatos.Count == 0)
            return resultados;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // 1. Buscar en Órdenes de Compra de todos los productos candidatos (familia / similares)
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
                    ReferenciaOrigen = $"Histórico {folioOc} ([{prd.CodigoInterno}] {prd.Nombre})"
                });
            }
        }

        // 2. Complementar con ProductoProveedorMapa (Facturas XML)
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
                    ReferenciaOrigen = $"Factura XML ({clave} - [{prd.CodigoInterno}] {prd.Nombre})"
                });
            }
        }

        // 3. Complementar con BancoPreciosHistorico (cotizaciones registradas con vigencia, incluso sin compra)
        using (var cmdBp = conn.CreateCommand())
        {
            cmdBp.CommandText = @"
SELECT ProductoId, DescripcionConcepto, Familia, ProveedorId, PrecioSinIva, DiasEntrega, DiasVigencia, FechaCotizacion, OrigenFolio
FROM BancoPreciosHistorico
WHERE PrecioSinIva > 0 AND ProveedorId > 0
ORDER BY Id DESC";
            using var rb = cmdBp.ExecuteReader();
            while (rb.Read())
            {
                int pid = rb.GetInt32(0);
                string descC = rb.GetString(1);
                string famC = rb.GetString(2);
                int pvId = rb.GetInt32(3);
                if (!provDict.TryGetValue(pvId, out var prv))
                    continue;

                bool coincide = false;
                if (pid > 0 && idsCandidatos.Contains(pid))
                    coincide = true;
                else if (!string.IsNullOrWhiteSpace(famBase) && famC.Equals(famBase, StringComparison.OrdinalIgnoreCase))
                    coincide = true;
                else if (palabrasClave.Count > 0 && palabrasClave.Any(w => descC.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    coincide = true;

                if (!coincide)
                    continue;

                if (resultados.Any(x => x.ProductoId == pid && x.ProveedorId == pvId && x.ProductoNombre.Equals(descC, StringComparison.OrdinalIgnoreCase)))
                    continue;

                decimal precio = Math.Round(Convert.ToDecimal(rb.GetDouble(4)), 2);
                int diasEnt = rb.GetInt32(5);
                int diasVig = rb.GetInt32(6) > 0 ? rb.GetInt32(6) : 15;
                string fCot = rb.GetString(7);
                string origFol = rb.GetString(8);

                string vigTag = "Vigente";
                if (DateTime.TryParse(fCot, out var dtCot))
                {
                    int rest = (dtCot.Date.AddDays(diasVig) - DateTime.Today).Days;
                    vigTag = rest >= 0 ? $"Vigente ({rest}d)" : $"Vencido hace {Math.Abs(rest)}d";
                }

                prodDict.TryGetValue(pid, out var prd);
                string codMostrar = prd?.CodigoInterno ?? "COT";
                string nomMostrar = prd?.Nombre ?? (string.IsNullOrWhiteSpace(descC) ? "Concepto Cotizado" : descC);

                resultados.Add(new ReferenciaSimilarHistorica
                {
                    ProductoId = pid,
                    ProductoCodigo = codMostrar,
                    ProductoNombre = nomMostrar,
                    Familia = !string.IsNullOrWhiteSpace(famC) ? famC : (prd?.Familia ?? ""),
                    ProveedorId = pvId,
                    ProveedorNombre = prv.NombreMostrar,
                    PrecioSinIva = precio,
                    DiasEntrega = diasEnt > 0 ? diasEnt : 2,
                    ReferenciaOrigen = $"Cotización {origFol} [{vigTag}] ({fCot})"
                });
            }
        }

        // Ordenar priorizando el mejor precio unitario sin IVA
        return resultados
            .OrderBy(x => x.PrecioSinIva)
            .ThenByDescending(x => x.ProductoId == productoId ? 1 : 0)
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
VALUES ($spid, $num, $pv, $pr, $de, $fl, $dv, $fc, $not)";
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
        cmd.ExecuteNonQuery();
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

    public static void RegistrarPrecioEnBancoHistorico(int productoId, string descripcionConcepto, string familia, int proveedorId, decimal precioSinIva, int diasEntrega, int diasVigencia, string origenFolio)
    {
        if (proveedorId <= 0 || precioSinIva <= 0m) return;
        string fechaHoy = DateTime.Today.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using (var del = conn.CreateCommand())
        {
            del.CommandText = @"
DELETE FROM BancoPreciosHistorico
WHERE ProveedorId = $pv AND FechaCotizacion = $fc AND (
    ($pid > 0 AND ProductoId = $pid) OR
    ($pid <= 0 AND UPPER(TRIM(DescripcionConcepto)) = UPPER(TRIM($desc)))
)";
            del.Parameters.AddWithValue("$pv", proveedorId);
            del.Parameters.AddWithValue("$fc", fechaHoy);
            del.Parameters.AddWithValue("$pid", productoId);
            del.Parameters.AddWithValue("$desc", descripcionConcepto ?? string.Empty);
            del.ExecuteNonQuery();
        }

        using var ins = conn.CreateCommand();
        ins.CommandText = @"
INSERT INTO BancoPreciosHistorico (ProductoId, DescripcionConcepto, Familia, ProveedorId, PrecioSinIva, DiasEntrega, DiasVigencia, FechaCotizacion, OrigenFolio)
VALUES ($pid, $desc, $fam, $pv, $pr, $de, $dv, $fc, $fol)";
        ins.Parameters.AddWithValue("$pid", productoId);
        ins.Parameters.AddWithValue("$desc", (descripcionConcepto ?? string.Empty).Trim());
        ins.Parameters.AddWithValue("$fam", (familia ?? string.Empty).Trim());
        ins.Parameters.AddWithValue("$pv", proveedorId);
        ins.Parameters.AddWithValue("$pr", (double)precioSinIva);
        ins.Parameters.AddWithValue("$de", Math.Max(1, diasEntrega));
        ins.Parameters.AddWithValue("$dv", diasVigencia > 0 ? diasVigencia : 15);
        ins.Parameters.AddWithValue("$fc", fechaHoy);
        ins.Parameters.AddWithValue("$fol", string.IsNullOrWhiteSpace(origenFolio) ? "COT" : origenFolio.Trim());
        ins.ExecuteNonQuery();
    }

    public static void GuardarCotizacionPartida(CotizacionRenglonComparativo r, string origenFolio = "COT")
    {
        if (r.SolicitudPartidaId <= 0) return;

        bool tieneAlgunaCotizacion =
            (r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0) ||
            (r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0) ||
            (r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0) ||
            r.ProveedoresExtra.Any(x => x.ProveedorId > 0 && x.PrecioSinIva > 0);

        if (tieneAlgunaCotizacion && string.IsNullOrWhiteSpace(r.FechaCotizacion))
        {
            r.FechaCotizacion = DateTime.Today.ToString("yyyy-MM-dd");
        }
        int vigencia = r.DiasVigenciaCotizacion > 0 ? r.DiasVigenciaCotizacion : 15;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE SolicitudPartidas
SET CantidadSolicitada = $cant,
    Proveedor1Id = $pv1, PrecioSinIva1 = $pr1, DiasEntrega1 = $d1, FleteConIva1 = $fl1, NotasCotizacion1 = $nc1,
    Proveedor2Id = $pv2, PrecioSinIva2 = $pr2, DiasEntrega2 = $d2, FleteConIva2 = $fl2, NotasCotizacion2 = $nc2,
    Proveedor3Id = $pv3, PrecioSinIva3 = $pr3, DiasEntrega3 = $d3, FleteConIva3 = $fl3, NotasCotizacion3 = $nc3,
    ProveedorGanadorIndice = $gan,
    DiasVigenciaCotizacion = $vig,
    FechaCotizacion = $fcot
WHERE Id = $id";
        cmd.Parameters.AddWithValue("$cant", (double)r.Cantidad);
        cmd.Parameters.AddWithValue("$pv1", r.Proveedor1Id);
        cmd.Parameters.AddWithValue("$pr1", (double)r.PrecioSinIva1);
        cmd.Parameters.AddWithValue("$d1", r.DiasEntrega1);
        cmd.Parameters.AddWithValue("$fl1", (double)r.FleteConIva1);
        cmd.Parameters.AddWithValue("$nc1", r.NotasCotizacion1 ?? string.Empty);

        cmd.Parameters.AddWithValue("$pv2", r.Proveedor2Id);
        cmd.Parameters.AddWithValue("$pr2", (double)r.PrecioSinIva2);
        cmd.Parameters.AddWithValue("$d2", r.DiasEntrega2);
        cmd.Parameters.AddWithValue("$fl2", (double)r.FleteConIva2);
        cmd.Parameters.AddWithValue("$nc2", r.NotasCotizacion2 ?? string.Empty);

        cmd.Parameters.AddWithValue("$pv3", r.Proveedor3Id);
        cmd.Parameters.AddWithValue("$pr3", (double)r.PrecioSinIva3);
        cmd.Parameters.AddWithValue("$d3", r.DiasEntrega3);
        cmd.Parameters.AddWithValue("$fl3", (double)r.FleteConIva3);
        cmd.Parameters.AddWithValue("$nc3", r.NotasCotizacion3 ?? string.Empty);

        cmd.Parameters.AddWithValue("$gan", r.ProveedorGanadorIndice);
        cmd.Parameters.AddWithValue("$vig", vigencia);
        cmd.Parameters.AddWithValue("$fcot", r.FechaCotizacion ?? string.Empty);
        cmd.Parameters.AddWithValue("$id", r.SolicitudPartidaId);
        cmd.ExecuteNonQuery();

        string descConcepto = !string.IsNullOrWhiteSpace(r.DescripcionLibre) ? r.DescripcionLibre : r.ProductoNombre;
        if (r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0)
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, r.Proveedor1Id, r.PrecioSinIva1, r.DiasEntrega1, vigencia, origenFolio);
        if (r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0)
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, r.Proveedor2Id, r.PrecioSinIva2, r.DiasEntrega2, vigencia, origenFolio);
        if (r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0)
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, r.Proveedor3Id, r.PrecioSinIva3, r.DiasEntrega3, vigencia, origenFolio);

        foreach (var ex in r.ProveedoresExtra.Where(x => x.ProveedorId > 0 && x.PrecioSinIva > 0))
        {
            RegistrarPrecioEnBancoHistorico(r.ProductoId, descConcepto, r.Familia, ex.ProveedorId, ex.PrecioSinIva, ex.DiasEntrega, vigencia, origenFolio);
        }
    }

    public static void GuardarBorradorComparativa(int solicitudId, List<CotizacionRenglonComparativo> renglones, string folioComparativa = "COT")
    {
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
    }

    public static List<OrdenCompra> GenerarOrdenesDesdeComparativa(
        Solicitud solicitud,
        string folioComparativa,
        List<CotizacionRenglonComparativo> renglones)
    {
        // Primero guardar el borrador de todas las cotizaciones capturadas
        GuardarBorradorComparativa(solicitud.Id, renglones, folioComparativa);

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
WHERE Id = $spid";
                    upSp.Parameters.AddWithValue("$ocid", ocId);
                    upSp.Parameters.AddWithValue("$ocfol", folioOc);
                    upSp.Parameters.AddWithValue("$spid", p.solicitudPartidaId);
                    upSp.ExecuteNonQuery();
                }
            }

            tx.Commit();
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

        return creadas;
    }

    public static void ActualizarEstatusYCondicionOrdenCompra(int ocId, string nuevoEstatus, string condicionPago, string fechaProgramadaPago)
    {
        if (ocId <= 0) return;
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE OrdenesCompra
SET Estatus = $est,
    CondicionPago = $cp,
    FechaProgramadaPago = $fp
WHERE Id = $id";
        cmd.Parameters.AddWithValue("$est", string.IsNullOrWhiteSpace(nuevoEstatus) ? "Emitida" : nuevoEstatus.Trim());
        cmd.Parameters.AddWithValue("$cp", string.IsNullOrWhiteSpace(condicionPago) ? "Contado (Transferencia previa)" : condicionPago.Trim());
        cmd.Parameters.AddWithValue("$fp", (fechaProgramadaPago ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$id", ocId);
        cmd.ExecuteNonQuery();
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
        bool reservarParaProyecto = false)
    {
        // Si alguna partida por recibir era un Concepto Abierto (ProductoId <= 0), darla de alta en el Catálogo (PRD-xxxx) ahora que ya se compró/recibió
        foreach (var (partida, cantRecibir) in recepcion.Where(x => x.cantidadRecibirAhora > 0))
        {
            if (partida.ProductoId <= 0)
            {
                string nombreReal = !string.IsNullOrWhiteSpace(partida.DescripcionLibre) ? partida.DescripcionLibre.Trim() : partida.ProductoNombre.Trim();
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
                    StockMinimo = 5m,
                    UltimoCostoCompra = partida.PrecioUnitarioConIva,
                    Ubicacion = "Almacén General"
                };
                int nuevoProdId = GuardarProducto(nuevoProd);
                partida.ProductoId = nuevoProdId;
                partida.ProductoCodigo = nuevoProd.CodigoInterno;
                partida.ProductoNombre = nuevoProd.Nombre;

                if (!string.IsNullOrWhiteSpace(oc.ProveedorRfc) && partida.PrecioUnitarioSinIva > 0m)
                {
                    GuardarMapaProductoProveedor(oc.ProveedorRfc, nuevoProd.CodigoInterno, nuevoProd.Nombre, nuevoProdId, partida.PrecioUnitarioSinIva);
                }

                using var connUp = new SqliteConnection(ConnectionString);
                connUp.Open();
                using var upOcp = connUp.CreateCommand();
                upOcp.CommandText = @"
UPDATE OrdenCompraPartidas SET ProductoId = $pid, DescripcionLibre = $nom WHERE Id = $id;
UPDATE SolicitudPartidas SET ProductoId = $pid WHERE OrdenCompraId = $ocid AND ProductoId <= 0;";
                upOcp.Parameters.AddWithValue("$pid", nuevoProdId);
                upOcp.Parameters.AddWithValue("$nom", nombreReal);
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
    FactorConversion = $fc,
    UltimoCostoCompra = $costo
WHERE Id = $pid";
            upProd.Parameters.AddWithValue("$ca", (double)cantAlmacen);
            upProd.Parameters.AddWithValue("$res", (double)cantReserva);
            upProd.Parameters.AddWithValue("$fc", (double)factor);
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
        SincronizarHerramientasCatalogoConDirectorio(conn);
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

        var provExistente = ObtenerProveedores().FirstOrDefault(p => p.Rfc.Trim().Equals(rfcNorm, StringComparison.OrdinalIgnoreCase));
        if (provExistente != null)
            return provExistente.Id;

        string razonLimpia = string.IsNullOrWhiteSpace(emisorNombre) ? rfcNorm : emisorNombre.Trim();
        GuardarProveedor(new Proveedor
        {
            Rfc = rfcNorm,
            RazonSocial = razonLimpia,
            NombreComercial = Proveedor.ExtraerNombreComercialCorto(razonLimpia),
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

        decimal totalPedidasNuevas = 0m;
        decimal totalRecibidasConservadas = 0m;

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

                totalPedidasNuevas += cantPedida;
                totalRecibidasConservadas += cantRecibida;

                decimal precioSinIva = CalcularPrecioUnitarioSinIvaParaOc(c);
                decimal costoConIva = Math.Round(precioSinIva * 1.16m, 2);

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

        if (totalRecibidasConservadas > 0m)
        {
            string estRec = totalRecibidasConservadas >= totalPedidasNuevas - 0.001m ? "Recibida Completa" : "Recibida Parcialmente";
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

    public static FacturaXml GuardarFacturaXml(FacturaXml factura, bool ingresarAlmacenAutomaticamente = false)
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

        if (ingresarAlmacenAutomaticamente && factura.OrdenCompraId.HasValue && factura.OrdenCompraId.Value > 0)
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

    // ==================== PERFIL ADMINISTRADOR Y BITÁCORA ====================

    public static string ObtenerPinAdministrador()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Valor FROM ConfiguracionSistema WHERE Clave = 'AdminPin' LIMIT 1";
        var res = cmd.ExecuteScalar()?.ToString();
        return string.IsNullOrWhiteSpace(res) ? "1234" : res.Trim();
    }

    public static bool ValidarPinAdministrador(string pinIngresado)
    {
        string pinActual = ObtenerPinAdministrador();
        return !string.IsNullOrWhiteSpace(pinIngresado) && pinIngresado.Trim() == pinActual;
    }

    public static void CambiarPinAdministrador(string nuevoPin)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ('AdminPin', $pin)
ON CONFLICT(Clave) DO UPDATE SET Valor = $pin";
        cmd.Parameters.AddWithValue("$pin", nuevoPin.Trim());
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
INSERT INTO EventosCalendario (Fecha, Titulo, Descripcion, Categoria, Prioridad, Completado, ReferenciaFolio)
VALUES ($fec, $tit, $desc, $cat, $prio, $comp, $ref)";
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

    public static List<EventoCalendario> ObtenerEventosCalendarioUnificados(string? filtroFecha = null)
    {
        var lista = new List<EventoCalendario>();

        // 1. Eventos manuales guardados en la tabla EventosCalendario
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Fecha, Titulo, Descripcion, Categoria, Prioridad, Completado, ReferenciaFolio
FROM EventosCalendario
ORDER BY Fecha ASC, Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
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
                lista.Add(new EventoCalendario
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
                lista.Add(new EventoCalendario
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

                lista.Add(new EventoCalendario
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
                lista.Add(new EventoCalendario
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
            lista.Add(new EventoCalendario
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
        string mensaje)
    {
        try
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO NotificacionesCheckpoint
(FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, Mensaje, Leida)
VALUES ($fh, $et, $cp, $fol, $ea, $en, $ro, $rd, $msg, 0);";
            cmd.Parameters.AddWithValue("$fh", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("$et", etapa ?? string.Empty);
            cmd.Parameters.AddWithValue("$cp", checkpointCodigo ?? string.Empty);
            cmd.Parameters.AddWithValue("$fol", folioReferencia ?? string.Empty);
            cmd.Parameters.AddWithValue("$ea", estatusAnterior ?? string.Empty);
            cmd.Parameters.AddWithValue("$en", estatusNuevo ?? string.Empty);
            cmd.Parameters.AddWithValue("$ro", string.IsNullOrWhiteSpace(rolOrigen) ? "Compras" : rolOrigen);
            cmd.Parameters.AddWithValue("$rd", string.IsNullOrWhiteSpace(rolesDestino) ? "Almacén" : rolesDestino);
            cmd.Parameters.AddWithValue("$msg", mensaje ?? string.Empty);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // No interrumpir flujo operativo por registro de notificación
        }
    }

    public static List<NotificacionCheckpoint> ObtenerNotificacionesCheckpoint(string? rolFiltro = null, int limite = 80)
    {
        var lista = new List<NotificacionCheckpoint>();
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(rolFiltro) || string.Equals(rolFiltro, "Todos", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = @"
SELECT Id, FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, Mensaje, Leida
FROM NotificacionesCheckpoint
ORDER BY Id DESC
LIMIT $lim;";
        }
        else
        {
            cmd.CommandText = @"
SELECT Id, FechaHora, Etapa, CheckpointCodigo, FolioReferencia, EstatusAnterior, EstatusNuevo, RolOrigen, RolesDestino, Mensaje, Leida
FROM NotificacionesCheckpoint
WHERE RolesDestino LIKE $rol OR RolOrigen LIKE $rol
ORDER BY Id DESC
LIMIT $lim;";
            cmd.Parameters.AddWithValue("$rol", $"%{rolFiltro.Trim()}%");
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
                Leida = r.GetInt32(10) != 0
            });
        }
        return lista;
    }

    public static int ContarNotificacionesNoLeidas(string rolDestino)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM NotificacionesCheckpoint WHERE Leida=0 AND RolesDestino LIKE $rol";
        cmd.Parameters.AddWithValue("$rol", $"%{rolDestino.Trim()}%");
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public static void MarcarNotificacionesLeidas(string? rolDestino = null)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(rolDestino))
        {
            cmd.CommandText = "UPDATE NotificacionesCheckpoint SET Leida=1 WHERE Leida=0";
        }
        else
        {
            cmd.CommandText = "UPDATE NotificacionesCheckpoint SET Leida=1 WHERE Leida=0 AND RolesDestino LIKE $rol";
            cmd.Parameters.AddWithValue("$rol", $"%{rolDestino.Trim()}%");
        }
        cmd.ExecuteNonQuery();
    }
}
