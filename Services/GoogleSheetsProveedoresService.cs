using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen.Services;

/// <summary>
/// Genera el catálogo vivo "¿A quién cotizar?" cruzando Categorías × Proveedores
/// a partir de las compras y cotizaciones del ERP, y lo sincroniza bidireccionalmente
/// con la hoja oficial de Google Sheets en Google Drive.
/// </summary>
public static class GoogleSheetsProveedoresService
{
    public const string DefaultSpreadsheetId = "1AbYtP9Li1fw_4CjESRV8rhu7a7HrOage0XdnXkIS01I";
    private const string ConfigKeySheetId = "GoogleSheets_ProveedoresId";
    private const string ConfigKeyWebAppUrl = "GoogleSheets_ProveedoresWebAppUrl";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static readonly string[] CategoriasOrden =
    {
        "Refrigeración y Aire Acondicionado (HVAC)",
        "Eléctrico, Iluminación y Control",
        "Plomería, Tubería y Conexiones",
        "Tornillería, Fijación y Herrajes",
        "Herramientas, Medición y Maquinaria",
        "Soldadura, Químicos y Consumibles Técnicos",
        "Pintura, Impermeabilizantes y Obra",
        "Seguridad Industrial y EPP",
        "Limpieza, Higiene y Jardinería",
        "Papelería, Oficina y Comedor",
        "Flotilla / Automotriz"
    };

    private static readonly Dictionary<string, (string Rol, string[] CategoriasBase, string DescripcionGiro)> GirosBase =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AME"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Material eléctrico industrial, cables, iluminación LED, tableros y control"),
            ["AMESA"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Material eléctrico industrial, gabinetes IP66, guardamotores, mando y señalización"),
            ["DICISA"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Distribución, control, automatización, riel DIN, canaleta, relevadores Schneider/Omron"),
            ["SECONTROL"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Automatización industrial, fuentes Omron, relevadores Schneider, botoneras y control"),
            ["DICI ILUMINACION"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Iluminación comercial/industrial e interruptores seccionadores"),
            ["JWJ"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Iluminación LED, lámparas suburbanas y material eléctrico"),
            ["STEREN"] = ("Especialista", new[] { "Eléctrico, Iluminación y Control" }, "Electrónica, conectores, alcohol isopropílico, accesorios de medición y cómputo"),
            ["PERSSA"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Refacciones HVAC, contactores, gases, soldadura de plata, limpiadores de serpentín, vacuómetros"),
            ["THERMOKOLD"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Equipos y kits de instalación HVAC, tubería cobre/aluminio y refacciones"),
            ["REFRIMART DE MEXICO"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Tubería de cobre, gases refrigerantes, aislamiento, soldadura y accesorios HVAC"),
            ["REFRIGERACION Y ACCESORIOS"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Adaptadores, válvulas y accesorios de refrigeración y aire acondicionado"),
            ["TECNIREAA"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)", "Soldadura, Químicos y Consumibles Técnicos" }, "Capacitores HVAC, recarga de nitrógeno, varilla de plata y fundente"),
            ["ServiClimas"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Equipos de aire acondicionado, serpentines y refacciones originales"),
            ["TECNICENTRO"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Bombas de condensado y refacciones electrónicas/mecánicas para climas"),
            ["GLASSFIBER"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)", "Pintura, Impermeabilizantes y Obra" }, "Difusores lineales, rejillas, ductería y aislamiento térmico/acústico de fibra de vidrio"),
            ["ARMEE"] = ("Especialista", new[] { "Refrigeración y Aire Acondicionado (HVAC)" }, "Ventiladores industriales, extractores y movimiento de aire"),
            ["NIPLITO"] = ("Especialista", new[] { "Plomería, Tubería y Conexiones" }, "Plomería comercial e industrial, PVC Cédula 40/80, válvulas, fluxómetros y conexiones"),
            ["EL NIPLITO DEL SURESTE"] = ("Especialista", new[] { "Plomería, Tubería y Conexiones" }, "Plomería comercial e industrial, PVC Cédula 40/80, válvulas, fluxómetros y conexiones"),
            ["IMPULSORA HIDRAULICA"] = ("Especialista", new[] { "Plomería, Tubería y Conexiones" }, "Tubería y conexiones hidráulicas de PVC Cédula 80, bridas y válvulas industriales"),
            ["Tubos Monterrey"] = ("Especialista", new[] { "Plomería, Tubería y Conexiones", "Tornillería, Fijación y Herrajes" }, "Tubería industrial PVC/acero, bridas, perfiles estructurales y conexiones"),
            ["CEREBA"] = ("Especialista", new[] { "Plomería, Tubería y Conexiones" }, "Grifería institucional, fluxómetros de pedal, llaves economizadoras y mangueras Coflex"),
            ["DMT"] = ("Especialista", new[] { "Tornillería, Fijación y Herrajes" }, "Mayorista de tornillería métrica/estándar, pijas autotaladrantes, anclas y fijación"),
            ["DBS"] = ("Especialista", new[] { "Tornillería, Fijación y Herrajes" }, "Baleros, bandas industriales Gates, transmisión de potencia y herrajes"),
            ["LAMINAS Y ACEROS"] = ("Especialista", new[] { "Tornillería, Fijación y Herrajes", "Pintura, Impermeabilizantes y Obra" }, "Ángulos, soleras, perfiles de acero, láminas y materiales estructurales"),
            ["MECALUX"] = ("Especialista", new[] { "Tornillería, Fijación y Herrajes" }, "Racks selectivos, puntales, largueros y sistemas de almacenaje industrial"),
            ["RP VI COLOR Y RECUBRIMIENTOS"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Pinturas, recubrimientos e impermeabilizantes (Imperprim, Impercoat, rollo SBS)"),
            ["ALPINA"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Impermeabilizantes Fester, aditivos (Festermix) y químicos para construcción"),
            ["SI PANEL"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Tablaroca, placa de yeso, Glass Mat exterior, postes metálicos y tornillería para panel"),
            ["OBREK"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Construcción ligera, placas de yeso/exterior, perfiles y acabados de obra"),
            ["Centro Tablaroquero"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Tablaroca, postes metálicos, pasta Redimix, cintas y herramienta para tablaroca"),
            ["MADERAS BAJCE"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Madera, triplay, polines y conectores/esquineros para carpintería y cimbra"),
            ["CONSORCIO DEL MAYAB"] = ("Especialista", new[] { "Pintura, Impermeabilizantes y Obra" }, "Materiales para construcción y obra civil"),
            ["EVANS R&R"] = ("Especialista", new[] { "Herramientas, Medición y Maquinaria" }, "Hidrolavadoras, bombas de agua, compresores y maquinaria ligera"),
            ["EMPRESAS LARY"] = ("Especialista", new[] { "Seguridad Industrial y EPP" }, "Calzado industrial, botas dieléctricas/borceguí y equipo de protección personal"),
            ["SHARON VIRIDIANA ALCOCER LEON"] = ("Alternativa", new[] { "Seguridad Industrial y EPP" }, "Uniformes, chalecos y artículos de seguridad"),
            ["MATERIALES DE LIMPIEZA, JARCERIA Y PRODUCTOS QUIMICOS"] = ("Especialista", new[] { "Limpieza, Higiene y Jardinería" }, "Jarcería industrial, químicos de limpieza a granel, escobas y recogedores"),
            ["OFFICE DEPOT DE MEXICO"] = ("Especialista", new[] { "Papelería, Oficina y Comedor" }, "Papelería, carpetas, consumibles de impresión, equipo de oficina y terminales"),
            ["TONY PAPELERIA"] = ("Especialista", new[] { "Papelería, Oficina y Comedor" }, "Papelería general, artículos escolares/oficina y gafetes"),
            ["Office Max"] = ("Especialista", new[] { "Papelería, Oficina y Comedor" }, "Papelería, artículos de oficina y mobiliario"),
            ["INDUSTRIAS ORDOÑEZ"] = ("Especialista", new[] { "Papelería, Oficina y Comedor" }, "Mobiliario de oficina y sillas ejecutivas/operativas"),
            ["VAQUEIROS"] = ("Multicategoría", Array.Empty<string>(), "Ferretería mayorista general (eléctrico, plomería, herramienta, pintura, tornillería y limpieza)"),
            ["VAQUEIRO SERVICIOS EMPRESARIALES"] = ("Multicategoría", Array.Empty<string>(), "Suministros ferreteros, abrasivos, consumibles y limpieza empresarial"),
            ["OSITO"] = ("Multicategoría", Array.Empty<string>(), "Ferretería general (plomería PVC/CPVC, herrajes, solventes y herramienta Truper/Pretul)"),
            ["FERRARA MATERIALES"] = ("Multicategoría", Array.Empty<string>(), "Ferretería y materiales (extensiones uso rudo, herramienta manual y material de obra)"),
            ["CASA FERNANDEZ"] = ("Multicategoría", Array.Empty<string>(), "Ferretería y plomería (PVC sanitario, abrazaderas, selladores Duretan y pijas)"),
            ["HOME DEPOT"] = ("Tienda / Emergencia", Array.Empty<string>(), "Compra rápida / urgencias en tienda (herramienta, eléctrico, plomería y consumibles)"),
            ["AUTOZONE DE MEXICO"] = ("Especialista", new[] { "Flotilla / Automotriz" }, "Acumuladores, lubricantes, accesorios automotrices y cuidado de flotilla"),
            ["CHEDRAUI"] = ("Tienda / Emergencia", new[] { "Limpieza, Higiene y Jardinería", "Papelería, Oficina y Comedor" }, "Insumos de limpieza, papel higiénico, bolsas y comedor/cafetería"),
            ["WALMART"] = ("Tienda / Emergencia", new[] { "Limpieza, Higiene y Jardinería", "Papelería, Oficina y Comedor" }, "Insumos de higiene, toallas de papel, desechables y comedor"),
            ["CASA SANTOS LUGO"] = ("Tienda / Emergencia", new[] { "Limpieza, Higiene y Jardinería", "Papelería, Oficina y Comedor" }, "Cloro, químicos de limpieza, desechables e insumos de comedor a mayoreo")
        };

    public static (string SpreadsheetId, string WebAppUrl) ObtenerConfiguracion()
    {
        string sheetId = DefaultSpreadsheetId;
        string webAppUrl = string.Empty;
        try
        {
            using var conn = new SqliteConnection($"Data Source={DatabaseService.DbPath};Default Timeout=5");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Clave, Valor FROM ConfiguracionSistema WHERE Clave IN ($k1, $k2)";
            cmd.Parameters.AddWithValue("$k1", ConfigKeySheetId);
            cmd.Parameters.AddWithValue("$k2", ConfigKeyWebAppUrl);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string k = r.GetString(0);
                string v = r.GetString(1);
                if (k == ConfigKeySheetId && !string.IsNullOrWhiteSpace(v)) sheetId = ExtraerIdDeUrlOId(v);
                if (k == ConfigKeyWebAppUrl) webAppUrl = v?.Trim() ?? string.Empty;
            }
        }
        catch { }
        return (sheetId, webAppUrl);
    }

    public static void GuardarConfiguracion(string spreadsheetIdOUrl, string webAppUrl)
    {
        string idLimpio = ExtraerIdDeUrlOId(spreadsheetIdOUrl);
        if (string.IsNullOrWhiteSpace(idLimpio)) idLimpio = DefaultSpreadsheetId;

        using var conn = new SqliteConnection($"Data Source={DatabaseService.DbPath};Default Timeout=5");
        conn.Open();
        foreach (var (k, v) in new[] { (ConfigKeySheetId, idLimpio), (ConfigKeyWebAppUrl, webAppUrl?.Trim() ?? string.Empty) })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO ConfiguracionSistema (Clave, Valor) VALUES ($k, $v)
ON CONFLICT(Clave) DO UPDATE SET Valor = excluded.Valor;";
            cmd.Parameters.AddWithValue("$k", k);
            cmd.Parameters.AddWithValue("$v", v);
            cmd.ExecuteNonQuery();
        }
    }

    public static string ExtraerIdDeUrlOId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DefaultSpreadsheetId;
        string s = input.Trim();
        var m = Regex.Match(s, @"/spreadsheets/d/([a-zA-Z0-9-_]+)");
        return m.Success ? m.Groups[1].Value : s;
    }

    public static string ObtenerUrlHoja()
    {
        var (id, _) = ObtenerConfiguracion();
        return $"https://docs.google.com/spreadsheets/d/{id}/edit";
    }

    public static void AbrirHojaEnNavegador()
    {
        Process.Start(new ProcessStartInfo(ObtenerUrlHoja()) { UseShellExecute = true });
    }

    public static string? ClasificarConcepto(string? texto, string? familia, string? catActual)
    {
        string t = $"{texto} {familia}".ToUpperInvariant();
        if (t.Contains("ESTO ES UN EJEMPLO") || t.Contains("PRUEBA DEMO")) return null;

        bool Any(params string[] keys) => keys.Any(k => t.Contains(k, StringComparison.Ordinal));

        if (Any("REFRIGERANTE", "R-410", "R410", "R-32", "R-22", "NITROGENO", "SERPENTIN", "CONDENSADO", "DIFUSOR LINEAL", "HVAC", "AIRE ACONDICIONADO", "MINISPLIT", "TUBO COBRE", "COBRE FLEXIBLE", "ARMAFLEX", "VACUOMETRO", "CAPACITOR DE ALUMINIO", "CAPACITOR HVAC", "TUERCA CÓNICA", "FLARE", "ADAPTADOR ERO", "MOTOR VENTILADOR", "EXTRACTOR"))
            return "Refrigeración y Aire Acondicionado (HVAC)";
        if (Any("TABLAROCA", "PLACA DE YESO", "PLACA DE FIBRA", "GLASS MAT", "REDIMIX", "PERFACINTA", "CINTA DE PAPEL", "CANAL DE AMARRE", "CANAL METÁLICO", "POSTE METÁLICO", "POSTE 6.", "IMPERPRIM", "IMPERMEABILI", "IMPERCOAT", "NAYA SBS", "FESTER", "THINER", "THINNER", "AGUARRAS", "BROCHA", "RODILLO", "PINTURA", "PINT HGAR", "CINTA MASKING", "MADERA", "TRIPLAY"))
            return "Pintura, Impermeabilizantes y Obra";
        if (!t.Contains("CANALETA") && Any("PVC", "CPVC", "HIDRÁULIC", "HIDRAULIC", "SANITARI", "CÉDULA 80", "CEDULA 80", "CÉDULA 40", "CEDULA 40", "CED 80", "NIPLE", "COFLEX", "FLUXÓMETRO", "FLUXOMETRO", "LLAVE ECONOMIZADORA", "LLAVE ANGULAR", "VÁLVULA DE ESFERA", "BRIDA PVC", "REDUCCIÓN BUSHING", "REDUCCIÓN CAMPANA"))
            return "Plomería, Tubería y Conexiones";
        if (Any("BOTA", "BORCEGUI", "CASCO", "CHALECO", "GUANTE", "LENTE DE SEGURIDAD", "TAPON AUDITIVO", "ARNES"))
            return "Seguridad Industrial y EPP";
        if (Any("TORNILLO", "TUERCA", "ARANDELA", "TAQUETE", "PIJA", "PRIS ALLEN", "ABRAZADERA", "ANGULAR ", "PUNTAL TD", "DIAGONAL U", "ESQUINERO DE UNION", "CONECTOR MET", "BANDA GATES", "BALERO"))
            return "Tornillería, Fijación y Herrajes";
        if (Any("SOLDADURA DE PLATA", "VARILLA SOLDADURA", "VARITA DE PLATA", "FUNDENTE", "PEGAMENTO", "DURETAN", "SILICON", "SELLADOR", "SOLVENTE DIELECTRICO", "ALCOHOL ISOPROPILICO", "ACIDO MURIATICO", "ÁCIDO MURIATICO", "ESTOPA", "LIJA ", "LUBRICANTE", "CINTA TEFLON", "UNISAL"))
            return "Soldadura, Químicos y Consumibles Técnicos";
        if (Any("FLEXOMETRO", "FLEXÓMETRO", "DESARMADOR", "SERRUCHO", "HIDROLAVADORA", "ASPIRADORA", "SOPLADORA", "TALADRO", "ROTOMARTILLO", "ESMERIL", "BROCA", "DISCO DE CORTE", "PINZA ELECTRICISTA", "MULTIMETRO"))
            return "Herramientas, Medición y Maquinaria";
        if (Any("PAPEL HIGIÉNICO", "PH SUAVEL", "SUAVEL", "MICROFIBRA", "BOLSA NEGRA", "BOLSAS PARA BASURA", "BOLS BAS", "ESCOBA", "RECOGEDOR", "CEPILLO", "CLORALEX", "BLANQUEADOR", "LIMPIADOR", "CLEANER", "FUMIGADOR", "INSECTICIDA", "HERBICESPED", "AIR WICK", "FIBRA VERDE", "TOALLA SCOTT"))
            return "Limpieza, Higiene y Jardinería";
        if (Any("FOLDER", "BOLIG", "HOJA ", "PROT HOJA", "INDICES", "BITÁCORA", "BITACORA", "CARPETA", "GAFETE", "LECTOR TARJETAS CLIP", "STARLINK", "SILLA EJECUTIVA", "SERVILLETA", "SERVI BOLSA", "SPLENDA", "ENDUL", "GATORADE", "TENEDOR", "CUCHA"))
            return "Papelería, Oficina y Comedor";
        if (Any("CABLE", "CONDULET", "INTERRUPTOR", "CONTACTOR", "RELEVADOR", "GUARDAMOTOR", "RIEL DIN", "CANALETA", "GABINETE METÁLICO", "PILOTO LED", "PULSADOR", "FUENTE DE ALIMENTACIÓN", "LUMINARI", "CAMPANA INDUSTRIAL LED", "LAMPARA", "LÁMPARA", "FOCO", "EXTENSION ELECTRICA", "CINTA AISLANTE", "CINTA DE AISLAR", "APAGADOR", "PILA ALCALINA", "VENTILADOR"))
            return "Eléctrico, Iluminación y Control";

        if (!string.IsNullOrWhiteSpace(catActual) && CategoriasOrden.Contains(catActual) && catActual != "Eléctrico, Iluminación y Control")
            return catActual;
        return "Eléctrico, Iluminación y Control";
    }

    public static (List<object[]> AQuienCotizar, List<object[]> Directorio, List<object[]> Resumen) ConstruirTablasDesdeErp()
    {
        var proveedores = DatabaseService.ObtenerProveedores();
        var provDict = proveedores.ToDictionary(p => p.Id);
        var ocsPorProv = proveedores.ToDictionary(p => p.Id, _ => new HashSet<int>());
        var cotPorProv = proveedores.ToDictionary(p => p.Id, _ => 0);

        var stats = new Dictionary<(string Cat, int Pid), (HashSet<int> Ocs, int Cot, List<string> Ejemplos)>();
        (HashSet<int> Ocs, int Cot, List<string> Ejemplos) GetStat(string cat, int pid)
        {
            if (!stats.TryGetValue((cat, pid), out var st))
            {
                st = (new HashSet<int>(), 0, new List<string>());
                stats[(cat, pid)] = st;
            }
            return st;
        }

        using (var conn = new SqliteConnection($"Data Source={DatabaseService.DbPath};Default Timeout=5"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COALESCE(cat.Nombre, ''), COALESCE(p.Familia, ''), oc.ProveedorId, oc.Id,
       COALESCE(NULLIF(p.Nombre,''), ocp.DescripcionLibre)
FROM OrdenesCompra oc
JOIN OrdenCompraPartidas ocp ON ocp.OrdenCompraId = oc.Id
LEFT JOIN Productos p ON p.Id = ocp.ProductoId
LEFT JOIN Categorias cat ON cat.Id = p.CategoriaId
WHERE oc.Estatus != 'Cancelada'";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int pid = r.GetInt32(2);
                    if (!provDict.ContainsKey(pid)) continue;
                    string? catReal = ClasificarConcepto(r.GetString(4), r.GetString(1), r.GetString(0));
                    if (catReal == null) continue;
                    int ocId = r.GetInt32(3);
                    ocsPorProv[pid].Add(ocId);
                    var st = GetStat(catReal, pid);
                    st.Ocs.Add(ocId);
                    string ej = Regex.Replace(r.GetString(4).Trim(), @"\s+(FP:|Fecha Ped\.:).*$", "");
                    if (!string.IsNullOrWhiteSpace(ej) && st.Ejemplos.Count < 4 && !st.Ejemplos.Contains(ej))
                        st.Ejemplos.Add(ej.Length > 52 ? ej[..52] : ej);
                }
            }

            using (var cmdCot = conn.CreateCommand())
            {
                cmdCot.CommandText = "SELECT ProveedorId, COALESCE(Familia,''), COALESCE(DescripcionConcepto,'') FROM BancoPreciosHistorico WHERE PrecioSinIva > 0";
                using var r = cmdCot.ExecuteReader();
                while (r.Read())
                {
                    int pid = r.GetInt32(0);
                    if (!provDict.ContainsKey(pid)) continue;
                    string desc = r.GetString(2).Trim();
                    string? catReal = ClasificarConcepto(desc, r.GetString(1), "");
                    if (catReal == null) continue;
                    cotPorProv[pid]++;
                    var st = GetStat(catReal, pid);
                    stats[(catReal, pid)] = (st.Ocs, st.Cot + 1, st.Ejemplos);
                    if (!string.IsNullOrWhiteSpace(desc) && st.Ejemplos.Count < 4 && !st.Ejemplos.Contains(desc))
                        st.Ejemplos.Add(desc.Length > 52 ? desc[..52] : desc);
                }
            }
        }

        foreach (var p in proveedores)
        {
            string nom = (string.IsNullOrWhiteSpace(p.NombreComercial) ? p.RazonSocial : p.NombreComercial).Trim();
            if (GirosBase.TryGetValue(nom, out var g))
            {
                foreach (var cb in g.CategoriasBase) _ = GetStat(cb, p.Id);
            }
            if (!string.IsNullOrWhiteSpace(p.Notas))
            {
                foreach (var cat in CategoriasOrden)
                {
                    if (p.Notas.Contains(cat, StringComparison.OrdinalIgnoreCase))
                        _ = GetStat(cat, p.Id);
                }
            }
        }

        var tiendasConsumo = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CHEDRAUI", "WALMART", "CASA SANTOS LUGO" };
        var soloPapeleria = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OFFICE DEPOT DE MEXICO", "TONY PAPELERIA", "Office Max", "INDUSTRIAS ORDOÑEZ" };

        var porCat = new Dictionary<string, List<(int Prio, int NegOcs, int NegCot, string Nom, Proveedor Prov, string RolTxt, string GiroDesc, (HashSet<int> Ocs, int Cot, List<string> Ejemplos) St)>>();
        foreach (var kv in stats)
        {
            var (cat, pid) = kv.Key;
            var st = kv.Value;
            var p = provDict[pid];
            string nom = (string.IsNullOrWhiteSpace(p.NombreComercial) ? p.RazonSocial : p.NombreComercial).Trim();
            if (tiendasConsumo.Contains(nom) && cat != "Limpieza, Higiene y Jardinería" && cat != "Papelería, Oficina y Comedor" && st.Ocs.Count <= 1) continue;
            if (soloPapeleria.Contains(nom) && cat != "Papelería, Oficina y Comedor" && st.Ocs.Count <= 2) continue;

            GirosBase.TryGetValue(nom, out var giro);
            string rolBase = giro.Rol ?? "Alternativa";
            string[] catsBase = giro.CategoriasBase ?? Array.Empty<string>();
            string descGiro = giro.DescripcionGiro ?? p.Notas ?? string.Empty;

            int prio;
            string rolTxt;
            if (rolBase == "Tienda / Emergencia") { prio = 3; rolTxt = "3. Tienda / Compra Rápida"; }
            else if (catsBase.Contains(cat, StringComparer.OrdinalIgnoreCase) || (rolBase == "Multicategoría" && st.Ocs.Count >= 5)) { prio = 1; rolTxt = "1. Especialista / Principal"; }
            else { prio = 2; rolTxt = "2. Alternativa / Complementario"; }

            if (!porCat.TryGetValue(cat, out var lista)) { lista = new(); porCat[cat] = lista; }
            lista.Add((prio, -st.Ocs.Count, -st.Cot, nom, p, rolTxt, descGiro, st));
        }

        var rowsAq = new List<object[]>
        {
            new object[] { "Categoría de Material", "Prioridad para Cotizar", "Proveedor", "Teléfono / WhatsApp", "Contacto / Vendedor", "Correo de Cotización", "¿Qué cotizarle? (Giro + Artículos reales del ERP)", "Historial en ERP", "Entrega / Pago" }
        };
        var rowsRes = new List<object[]>
        {
            new object[] { "Categoría de Material", "1. Proveedores Principales / Especialistas (Cotizar primero)", "2. Proveedores Alternativos / Respaldo", "3. Tiendas / Emergencia", "Total Proveedores" }
        };

        var provCats = proveedores.ToDictionary(p => p.Id, _ => new List<string>());
        int rowIdx = 2;
        foreach (var cat in porCat.Keys.OrderBy(k => { int i = Array.IndexOf(CategoriasOrden, k); return i >= 0 ? i : 99; }))
        {
            var items = porCat[cat].OrderBy(x => x.Prio).ThenBy(x => x.NegOcs).ThenBy(x => x.NegCot).ThenBy(x => x.Nom).ToList();
            var princ = new List<string>();
            var alt = new List<string>();
            var tien = new List<string>();

            foreach (var it in items)
            {
                if (!provCats[it.Prov.Id].Contains(cat)) provCats[it.Prov.Id].Add(cat);
                if (it.Prio == 1) princ.Add(it.Nom);
                else if (it.Prio == 2) alt.Add(it.Nom);
                else tien.Add(it.Nom);

                string ej = string.Join("; ", it.St.Ejemplos.Take(3));
                string que = !string.IsNullOrWhiteSpace(it.GiroDesc) && !string.IsNullOrWhiteSpace(ej)
                    ? $"{it.GiroDesc} | Ej: {ej}"
                    : (!string.IsNullOrWhiteSpace(it.GiroDesc) ? it.GiroDesc : (string.IsNullOrWhiteSpace(ej) ? "Giro registrado en catálogo" : ej));

                var hParts = new List<string>();
                if (it.St.Ocs.Count > 0) hParts.Add($"{it.St.Ocs.Count} OC(s)");
                if (it.St.Cot > 0) hParts.Add($"{it.St.Cot} cotiz.");
                string hist = hParts.Count > 0 ? string.Join(" · ", hParts) : "Catálogo (sin OC aún)";

                rowsAq.Add(new object[]
                {
                    cat,
                    it.RolTxt,
                    it.Nom,
                    $"=IFERROR(VLOOKUP(C{rowIdx}, Directorio_Proveedores!$B$2:$G$200, 4, FALSE), \"\")",
                    $"=IFERROR(VLOOKUP(C{rowIdx}, Directorio_Proveedores!$B$2:$G$200, 5, FALSE), \"\")",
                    $"=IFERROR(VLOOKUP(C{rowIdx}, Directorio_Proveedores!$B$2:$G$200, 6, FALSE), \"\")",
                    que,
                    hist,
                    $"{(it.Prov.DiasEntregaPromedio > 0 ? it.Prov.DiasEntregaPromedio : 3)}d · {(string.IsNullOrWhiteSpace(it.Prov.CondicionesPago) ? "Contado" : it.Prov.CondicionesPago)}"
                });
                rowIdx++;
            }

            rowsRes.Add(new object[]
            {
                cat,
                princ.Count > 0 ? string.Join(", ", princ) : "—",
                alt.Count > 0 ? string.Join(", ", alt) : "—",
                tien.Count > 0 ? string.Join(", ", tien) : "—",
                items.Count
            });
        }

        var rowsDir = new List<object[]>
        {
            new object[] { "ID ERP", "Proveedor", "Razón Social", "RFC", "Teléfono / WhatsApp", "Contacto / Vendedor", "Correo de Cotización", "Condiciones Pago", "Días Entrega", "Categorías donde Cotiza", "Total OCs ERP", "Cotizaciones ERP" }
        };
        foreach (var p in proveedores.OrderBy(x => ((string.IsNullOrWhiteSpace(x.NombreComercial) ? x.RazonSocial : x.NombreComercial) ?? string.Empty).ToUpperInvariant()))
        {
            string nom = (string.IsNullOrWhiteSpace(p.NombreComercial) ? p.RazonSocial : p.NombreComercial).Trim();
            rowsDir.Add(new object[]
            {
                p.Id,
                nom,
                p.RazonSocial ?? string.Empty,
                string.Equals(p.Rfc, "XAXX010101000", StringComparison.OrdinalIgnoreCase) ? string.Empty : (p.Rfc ?? string.Empty),
                p.Telefono ?? string.Empty,
                p.Contacto ?? string.Empty,
                p.Correo ?? string.Empty,
                string.IsNullOrWhiteSpace(p.CondicionesPago) ? "Contado" : p.CondicionesPago,
                p.DiasEntregaPromedio > 0 ? p.DiasEntregaPromedio : 3,
                string.Join(" | ", provCats[p.Id]),
                ocsPorProv[p.Id].Count,
                cotPorProv[p.Id]
            });
        }

        return (rowsAq, rowsDir, rowsRes);
    }

    /// <summary>
    /// Sincroniza bidireccionalmente con el conector Google Apps Script instalado en la hoja:
    /// 1) Importa teléfonos, contactos y correos nuevos capturados en Google Sheets hacia SQLite.
    /// 2) Sube las 3 pestañas recalculadas desde el ERP hacia Google Sheets.
    /// </summary>
    public static async Task<(bool Ok, string Mensaje, int ContactosImportados)> SincronizarConWebAppAsync(string webAppUrl)
    {
        if (string.IsNullOrWhiteSpace(webAppUrl) || !webAppUrl.StartsWith("https://script.google.com/", StringComparison.OrdinalIgnoreCase))
            return (false, "Pegue primero la URL de la Aplicación Web de Google Apps Script (https://script.google.com/macros/s/.../exec).", 0);

        int actualizadosLocal = 0;
        try
        {
            // Paso 1: Leer Directorio_Proveedores actual desde Sheets para rescatar teléfonos/contactos escritos por auxiliares
            var respGet = await Http.GetAsync(webAppUrl.Trim() + "?accion=leer_directorio");
            if (respGet.IsSuccessStatusCode)
            {
                string jsonDir = await respGet.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonDir);
                if (doc.RootElement.TryGetProperty("directorio", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    using var conn = new SqliteConnection($"Data Source={DatabaseService.DbPath};Default Timeout=5");
                    conn.Open();
                    foreach (var item in arr.EnumerateArray())
                    {
                        int id = item.TryGetProperty("id", out var pId) && pId.TryGetInt32(out int parsedId) ? parsedId : 0;
                        if (id <= 0) continue;
                        string tel = item.TryGetProperty("telefono", out var pTel) ? (pTel.GetString() ?? "").Trim() : "";
                        string con = item.TryGetProperty("contacto", out var pCon) ? (pCon.GetString() ?? "").Trim() : "";
                        string cor = item.TryGetProperty("correo", out var pCor) ? (pCor.GetString() ?? "").Trim() : "";
                        if (string.IsNullOrWhiteSpace(tel) && string.IsNullOrWhiteSpace(con) && string.IsNullOrWhiteSpace(cor)) continue;

                        using var up = conn.CreateCommand();
                        up.CommandText = @"
UPDATE Proveedores
SET Telefono = CASE WHEN $tel != '' THEN $tel ELSE Telefono END,
    Contacto = CASE WHEN $con != '' THEN $con ELSE Contacto END,
    Correo   = CASE WHEN $cor != '' THEN $cor ELSE Correo END
WHERE Id = $id AND (
    ($tel != '' AND COALESCE(Telefono,'') != $tel) OR
    ($con != '' AND COALESCE(Contacto,'') != $con) OR
    ($cor != '' AND COALESCE(Correo,'') != $cor)
);";
                        up.Parameters.AddWithValue("$tel", tel);
                        up.Parameters.AddWithValue("$con", con);
                        up.Parameters.AddWithValue("$cor", cor);
                        up.Parameters.AddWithValue("$id", id);
                        actualizadosLocal += up.ExecuteNonQuery();
                    }
                }
            }

            // Paso 2: Recalcular tablas con los datos combinados y empujarlas a las 3 pestañas de Google Sheets
            var (aq, dir, res) = ConstruirTablasDesdeErp();
            string payload = JsonSerializer.Serialize(new
            {
                a_quien_cotizar = aq,
                directorio = dir,
                resumen = res
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var respPost = await Http.PostAsync(webAppUrl.Trim(), content);
            string bodyPost = await respPost.Content.ReadAsStringAsync();
            if (!respPost.IsSuccessStatusCode)
                return (false, $"Google Apps Script respondió HTTP {(int)respPost.StatusCode}: {bodyPost}", actualizadosLocal);

            return (true, $"Sincronización completada: {dir.Count - 1} proveedores y {aq.Count - 1} cruces por categoría actualizados en Google Sheets ({actualizadosLocal} contacto(s)/teléfono(s) importados desde la hoja al ERP).", actualizadosLocal);
        }
        catch (Exception ex)
        {
            return (false, $"Error al conectar con Google Sheets: {ex.Message}", actualizadosLocal);
        }
    }

    public static string CodigoAppsScriptConector => @"function doGet(e) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sh = ss.getSheetByName('Directorio_Proveedores');
  var out = [];
  if (sh && sh.getLastRow() > 1) {
    var vals = sh.getRange(2, 1, sh.getLastRow() - 1, 7).getValues();
    for (var i = 0; i < vals.length; i++) {
      out.push({ id: Number(vals[i][0]) || 0, proveedor: String(vals[i][1] || ''), telefono: String(vals[i][4] || ''), contacto: String(vals[i][5] || ''), correo: String(vals[i][6] || '') });
    }
  }
  return ContentService.createTextOutput(JSON.stringify({ ok: true, directorio: out })).setMimeType(ContentService.MimeType.JSON);
}

function doPost(e) {
  var data = JSON.parse(e.postData.contents);
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  escribirTabla_(ss, 'Directorio_Proveedores', data.directorio);
  escribirTabla_(ss, 'A_Quien_Cotizar', data.a_quien_cotizar);
  escribirTabla_(ss, 'Resumen_Por_Categoria', data.resumen);
  return ContentService.createTextOutput(JSON.stringify({ ok: true })).setMimeType(ContentService.MimeType.JSON);
}

function escribirTabla_(ss, nombre, filas) {
  if (!filas || !filas.length) return;
  var sh = ss.getSheetByName(nombre) || ss.insertSheet(nombre);
  if (sh.getLastRow() > filas.length) {
    sh.getRange(filas.length + 1, 1, sh.getLastRow() - filas.length, sh.getMaxColumns()).clearContent();
  }
  sh.getRange(1, 1, filas.length, filas[0].length).setValues(filas);
}";
}
