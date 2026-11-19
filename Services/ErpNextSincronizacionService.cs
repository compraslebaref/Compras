using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen.Services;

public sealed class ResumenEstadoErpNext
{
    public string EmpresaPrincipal { get; init; } = string.Empty;
    public int ProveedoresErp { get; init; }
    public int ArticulosErp { get; init; }
    public int ObrasErp { get; init; }
    public int SolicitudesErp { get; init; }
    public int OrdenesCompraErp { get; init; }
    public int ProveedoresLocales { get; init; }
    public int ArticulosLocales { get; init; }
    public int ObrasLocales { get; init; }
}

public sealed class ResultadoMigracionErpNext
{
    public int ProveedoresCreados { get; set; }
    public int ProveedoresYaExistentes { get; set; }
    public int ObrasCreadas { get; set; }
    public int ObrasYaExistentes { get; set; }
    public int ArticulosCreados { get; set; }
    public int ArticulosYaExistentes { get; set; }
    public int HerramientasCreadas { get; set; }
    public int VehiculosCreados { get; set; }
    public List<string> Advertencias { get; } = new();
}

/// <summary>
/// Servicio no destructivo (solo lectura en SQLite) para consultar el estado del servidor ERPNext
/// y migrar/sincronizar catálogos maestros reales (Proveedores, Obras, Artículos, Herramientas y Flotilla)
/// usando exclusivamente los endpoints oficiales de CONTRATO-API.md.
/// </summary>
public static class ErpNextSincronizacionService
{
    public static async Task<ResumenEstadoErpNext> ObtenerResumenComparativoAsync(ErpNextApiClient client, CancellationToken ct = default)
    {
        var provLocal = DatabaseService.ObtenerProveedores();
        var prodLocal = DatabaseService.ObtenerProductos();
        var proyLocal = DatabaseService.ObtenerProyectos();

        var provErp = await ContarSeguroAsync(client, "Supplier", ct);
        var prodErp = await ContarSeguroAsync(client, "Item", ct);
        var obrasErp = await ContarSeguroAsync(client, "Project", ct);
        var solErp = await ContarSeguroAsync(client, "Material Request", ct);
        var ocErp = await ContarSeguroAsync(client, "Purchase Order", ct);

        string empresa = string.Empty;
        try
        {
            var companies = await client.ListAsync("Company", new[] { "name" }, limitPageLength: 5, ct: ct);
            empresa = companies.FirstOrDefault()?["name"]?.ToString() ?? string.Empty;
        }
        catch { }

        return new ResumenEstadoErpNext
        {
            EmpresaPrincipal = empresa,
            ProveedoresErp = provErp,
            ArticulosErp = prodErp,
            ObrasErp = obrasErp,
            SolicitudesErp = solErp,
            OrdenesCompraErp = ocErp,
            ProveedoresLocales = provLocal.Count,
            ArticulosLocales = prodLocal.Count,
            ObrasLocales = proyLocal.Count
        };
    }

    private static async Task<int> ContarSeguroAsync(ErpNextApiClient client, string doctype, CancellationToken ct)
    {
        try
        {
            var rows = await client.ListAsync(doctype, new[] { "name" }, limitPageLength: 1000, ct: ct);
            return rows.Count;
        }
        catch
        {
            return 0;
        }
    }

    public static async Task<ResultadoMigracionErpNext> SincronizarCatalogosMaestrosAsync(
        ErpNextApiClient client,
        IProgress<string>? progreso = null,
        CancellationToken ct = default)
    {
        var res = new ResultadoMigracionErpNext();

        progreso?.Report("Consultando esquemas y catálogos base del servidor ERPNext...");
        var schemaArt = await client.SchemaAsync("articulo", "", ct);
        var schemaProv = await client.SchemaAsync("proveedor", "", ct);
        var schemaObra = await client.SchemaAsync("obra", "", ct);

        string grupoProveedorDefault = ExtraerPrimeraOpcionCampo(schemaProv, "supplier_group") ?? "All Supplier Groups";
        string grupoArticuloDefault = ExtraerPrimeraOpcionCampo(schemaArt, "item_group") ?? "Products";
        var uomsDisponibles = ExtraerOpcionesCampo(schemaArt, "stock_uom");
        string uomFallback = uomsDisponibles.FirstOrDefault(u => u.Equals("Nos", StringComparison.OrdinalIgnoreCase) || u.Equals("Pieza", StringComparison.OrdinalIgnoreCase) || u.Equals("Unit", StringComparison.OrdinalIgnoreCase))
                             ?? uomsDisponibles.FirstOrDefault()
                             ?? "Nos";
        string empresaDefault = ExtraerPrimeraOpcionCampo(schemaObra, "company") ?? string.Empty;

        // 1. Proveedores
        progreso?.Report("Verificando proveedores existentes en ERPNext...");
        var provsRemotos = await client.ListAsync("Supplier", new[] { "name", "supplier_name" }, limitPageLength: 1000, ct: ct);
        var nombresProvRemotos = new HashSet<string>(
            provsRemotos.SelectMany(r => new[] { r.GetValueOrDefault("name")?.ToString() ?? "", r.GetValueOrDefault("supplier_name")?.ToString() ?? "" })
                        .Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);

        var proveedoresLocales = DatabaseService.ObtenerProveedores();
        foreach (var p in proveedoresLocales)
        {
            ct.ThrowIfCancellationRequested();
            string nombre = (!string.IsNullOrWhiteSpace(p.NombreComercial) ? p.NombreComercial : p.RazonSocial).Trim();
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            if (nombresProvRemotos.Contains(nombre))
            {
                res.ProveedoresYaExistentes++;
                continue;
            }

            try
            {
                string rfc = string.Equals(p.Rfc, "XAXX010101000", StringComparison.OrdinalIgnoreCase) ? string.Empty : (p.Rfc ?? string.Empty).Trim();
                await client.ExecuteAsync("proveedor", new Dictionary<string, object?>
                {
                    ["supplier_name"] = nombre,
                    ["supplier_group"] = grupoProveedorDefault,
                    ["tax_id"] = rfc
                }, "", GenerarUuidDeterministico($"SUP:{nombre}"), ct);
                nombresProvRemotos.Add(nombre);
                res.ProveedoresCreados++;
                progreso?.Report($"Proveedor sincronizado: {nombre}");
            }
            catch (Exception ex)
            {
                if (res.Advertencias.Count < 25)
                    res.Advertencias.Add($"Proveedor '{nombre}': {ex.Message}");
            }
        }

        // 2. Proyectos / Obras
        if (!string.IsNullOrWhiteSpace(empresaDefault))
        {
            progreso?.Report("Verificando obras y proyectos en ERPNext...");
            var obrasRemotas = await client.ListAsync("Project", new[] { "name", "project_name" }, limitPageLength: 1000, ct: ct);
            var nombresObraRemotos = new HashSet<string>(
                obrasRemotas.SelectMany(r => new[] { r.GetValueOrDefault("name")?.ToString() ?? "", r.GetValueOrDefault("project_name")?.ToString() ?? "" })
                            .Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

            var proyectosLocales = DatabaseService.ObtenerProyectos();
            foreach (var proy in proyectosLocales)
            {
                ct.ThrowIfCancellationRequested();
                string nomObra = (proy.Nombre ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nomObra)) continue;

                if (nombresObraRemotos.Contains(nomObra))
                {
                    res.ObrasYaExistentes++;
                    continue;
                }

                try
                {
                    await client.ExecuteAsync("obra", new Dictionary<string, object?>
                    {
                        ["project_name"] = nomObra,
                        ["company"] = empresaDefault
                    }, "", GenerarUuidDeterministico($"PRJ:{nomObra}"), ct);
                    nombresObraRemotos.Add(nomObra);
                    res.ObrasCreadas++;
                }
                catch (Exception ex)
                {
                    if (res.Advertencias.Count < 25)
                        res.Advertencias.Add($"Obra '{nomObra}': {ex.Message}");
                }
            }
        }

        // 3. Productos / Artículos
        progreso?.Report("Verificando catálogo de artículos en ERPNext...");
        var itemsRemotos = await client.ListAsync("Item", new[] { "name", "item_code", "item_name" }, limitPageLength: 1000, ct: ct);
        var codigosRemotos = new HashSet<string>(
            itemsRemotos.SelectMany(r => new[] { r.GetValueOrDefault("name")?.ToString() ?? "", r.GetValueOrDefault("item_code")?.ToString() ?? "" })
                        .Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);

        var productosLocales = DatabaseService.ObtenerProductos();
        int procesados = 0;
        foreach (var prod in productosLocales)
        {
            ct.ThrowIfCancellationRequested();
            procesados++;
            string codigo = string.IsNullOrWhiteSpace(prod.CodigoInterno) ? $"PRD-{prod.Id:D4}" : prod.CodigoInterno.Trim();
            string nombre = (prod.Nombre ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            if (codigosRemotos.Contains(codigo))
            {
                res.ArticulosYaExistentes++;
                continue;
            }

            string uomElegida = MapearUomErp(prod.UnidadAlmacen, uomsDisponibles, uomFallback);
            try
            {
                await client.ExecuteAsync("articulo", new Dictionary<string, object?>
                {
                    ["item_code"] = codigo,
                    ["item_name"] = nombre.Length > 140 ? nombre[..140] : nombre,
                    ["item_group"] = grupoArticuloDefault,
                    ["stock_uom"] = uomElegida
                }, "", GenerarUuidDeterministico($"ITM:{codigo}"), ct);
                codigosRemotos.Add(codigo);
                res.ArticulosCreados++;

                if (res.ArticulosCreados % 20 == 0)
                    progreso?.Report($"Artículos creados en ERPNext: {res.ArticulosCreados} ({procesados}/{productosLocales.Count})...");
            }
            catch (Exception ex)
            {
                if (res.Advertencias.Count < 25)
                    res.Advertencias.Add($"Artículo '{codigo}': {ex.Message}");
            }
        }

        // 4. Herramientas Individuales (HER-xxxx)
        if (!string.IsNullOrWhiteSpace(empresaDefault))
        {
            try
            {
                progreso?.Report("Verificando herramientas individuales (HER-xxxx) en ERPNext...");
                var herRemotas = await client.ListAsync("Lebaref Herramienta", new[] { "name", "codigo" }, limitPageLength: 1000, ct: ct);
                var codsHerRemotos = new HashSet<string>(
                    herRemotas.SelectMany(r => new[] { r.GetValueOrDefault("name")?.ToString() ?? "", r.GetValueOrDefault("codigo")?.ToString() ?? "" })
                              .Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase);

                var prodPorId = productosLocales.ToDictionary(x => x.Id);
                foreach (var h in DatabaseService.ObtenerHerramientas())
                {
                    ct.ThrowIfCancellationRequested();
                    string codHer = (h.CodigoIndividual ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(codHer) || codsHerRemotos.Contains(codHer)) continue;
                    if (!prodPorId.TryGetValue(h.ProductoId, out var pBase)) continue;
                    string itemCode = string.IsNullOrWhiteSpace(pBase.CodigoInterno) ? $"PRD-{pBase.Id:D4}" : pBase.CodigoInterno.Trim();
                    if (!codigosRemotos.Contains(itemCode)) continue;

                    string marcaMod = string.IsNullOrWhiteSpace(h.MarcaModelo) ? pBase.Nombre : h.MarcaModelo.Trim();
                    if (marcaMod.Length > 140) marcaMod = marcaMod[..140];
                    string serieTxt = string.IsNullOrWhiteSpace(h.NumeroSerie) ? codHer : h.NumeroSerie.Trim();
                    if (serieTxt.Length > 140) serieTxt = serieTxt[..140];

                    try
                    {
                        await client.ExecuteAsync("herramienta", new Dictionary<string, object?>
                        {
                            ["codigo"] = codHer,
                            ["company"] = empresaDefault,
                            ["articulo"] = itemCode,
                            ["serie"] = serieTxt,
                            ["marca_modelo"] = marcaMod,
                            ["ubicacion"] = string.IsNullOrWhiteSpace(h.Ubicacion) ? "GABINETE-1" : h.Ubicacion.Trim()
                        }, "", GenerarUuidDeterministico($"HER:{codHer}"), ct);
                        codsHerRemotos.Add(codHer);
                        res.HerramientasCreadas++;
                    }
                    catch (Exception ex)
                    {
                        if (res.Advertencias.Count < 25)
                            res.Advertencias.Add($"Herramienta '{codHer}': {ex.Message}");
                    }
                }
            }
            catch { }

            // 5. Flotilla Vehicular (VEH-xxxx)
            try
            {
                progreso?.Report("Verificando unidades de flotilla (VEH-xxxx) en ERPNext...");
                var vehRemotos = await client.ListAsync("Lebaref Vehiculo", new[] { "name", "codigo" }, limitPageLength: 1000, ct: ct);
                var codsVehRemotos = new HashSet<string>(
                    vehRemotos.SelectMany(r => new[] { r.GetValueOrDefault("name")?.ToString() ?? "", r.GetValueOrDefault("codigo")?.ToString() ?? "" })
                              .Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var v in DatabaseService.ObtenerVehiculosFlotilla())
                {
                    ct.ThrowIfCancellationRequested();
                    string codVeh = (v.CodigoVehiculo ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(codVeh) || codsVehRemotos.Contains(codVeh)) continue;

                    string comb = v.TipoCombustible?.Contains("Diésel", StringComparison.OrdinalIgnoreCase) == true || v.TipoCombustible?.Contains("Diesel", StringComparison.OrdinalIgnoreCase) == true
                        ? "Diésel"
                        : (v.TipoCombustible?.Contains("Premium", StringComparison.OrdinalIgnoreCase) == true ? "Gasolina Premium" : "Gasolina Regular");
                    string fecSeg = DateTime.TryParse(v.VigenciaSeguro, out var dSeg) ? dSeg.ToString("yyyy-MM-dd") : DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd");
                    int kmIni = Math.Max(0, v.KilometrajeActual > 0 ? v.KilometrajeActual : v.KilometrajeInicial);
                    int interv = v.IntervaloServicioKm > 0 ? v.IntervaloServicioKm : 10000;
                    int proxKm = v.ProximoServicioKm > kmIni ? v.ProximoServicioKm : kmIni + interv;

                    try
                    {
                        await client.ExecuteAsync("vehiculo", new Dictionary<string, object?>
                        {
                            ["codigo"] = codVeh,
                            ["placas"] = string.IsNullOrWhiteSpace(v.Placas) ? "SIN-PLACA" : v.Placas.Trim(),
                            ["company"] = empresaDefault,
                            ["marca_modelo"] = string.IsNullOrWhiteSpace(v.MarcaModelo) ? codVeh : v.MarcaModelo.Trim(),
                            ["responsable"] = string.IsNullOrWhiteSpace(v.ResponsableHabitual) ? "Operaciones" : v.ResponsableHabitual.Trim(),
                            ["km"] = kmIni.ToString(),
                            ["fecha"] = DateTime.Today.ToString("yyyy-MM-dd"),
                            ["intervalo"] = interv.ToString(),
                            ["proximo_servicio"] = proxKm.ToString(),
                            ["capacidad"] = Math.Max(10, (int)Math.Round(v.CapacidadTanqueLitros > 0 ? v.CapacidadTanqueLitros : 60m)).ToString(),
                            ["combustible"] = comb,
                            ["seguro"] = fecSeg
                        }, "", GenerarUuidDeterministico($"VEH:{codVeh}"), ct);
                        codsVehRemotos.Add(codVeh);
                        res.VehiculosCreados++;
                    }
                    catch (Exception ex)
                    {
                        if (res.Advertencias.Count < 25)
                            res.Advertencias.Add($"Vehículo '{codVeh}': {ex.Message}");
                    }
                }
            }
            catch { }
        }

        progreso?.Report("Sincronización de catálogos maestros finalizada.");
        return res;
    }

    private static string MapearUomErp(string? unidadLocal, List<string> disponiblesErp, string fallback)
    {
        if (disponiblesErp.Count == 0) return fallback;
        string u = (unidadLocal ?? string.Empty).Trim();
        var exacto = disponiblesErp.FirstOrDefault(x => x.Equals(u, StringComparison.OrdinalIgnoreCase));
        if (exacto != null) return exacto;

        string[] equivalentes = u.ToLowerInvariant() switch
        {
            "pieza" or "pza" or "unidad" => new[] { "Pieza", "Nos", "Unit" },
            "metro" or "m" => new[] { "Meter", "Metro", "m" },
            "litro" or "lt" => new[] { "Litre", "Litro", "L" },
            "kilogramo" or "kg" => new[] { "Kg", "Kilogram" },
            "caja" => new[] { "Box", "Caja" },
            "par" => new[] { "Pair", "Par" },
            "juego" or "kit" => new[] { "Set", "Juego" },
            "rollo" => new[] { "Roll", "Rollo", "Nos" },
            _ => Array.Empty<string>()
        };

        foreach (var eq in equivalentes)
        {
            var m = disponiblesErp.FirstOrDefault(x => x.Equals(eq, StringComparison.OrdinalIgnoreCase));
            if (m != null) return m;
        }
        return fallback;
    }

    private static List<string> ExtraerOpcionesCampo(Dictionary<string, object?> schema, string fieldName)
    {
        var lista = new List<string>();
        if (!schema.TryGetValue("fields", out var fieldsObj) || fieldsObj is not IEnumerable<object?> fields)
            return lista;

        foreach (var f in fields)
        {
            if (f is Dictionary<string, object?> fd &&
                string.Equals(fd.GetValueOrDefault("name")?.ToString(), fieldName, StringComparison.OrdinalIgnoreCase) &&
                fd.TryGetValue("choices", out var chObj) && chObj is IEnumerable<object?> choices)
            {
                foreach (var c in choices)
                {
                    string s = c?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(s))
                        lista.Add(s);
                }
            }
        }
        return lista;
    }

    private static string? ExtraerPrimeraOpcionCampo(Dictionary<string, object?> schema, string fieldName) =>
        ExtraerOpcionesCampo(schema, fieldName).FirstOrDefault();

    private static string GenerarUuidDeterministico(string semilla)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("LEBAREF_SYNC_V1:" + semilla));
        byte[] bytes16 = hash[..16];
        bytes16[6] = (byte)((bytes16[6] & 0x0F) | 0x40); // UUID v4 format
        bytes16[8] = (byte)((bytes16[8] & 0x3F) | 0x80);
        return new Guid(bytes16).ToString("D");
    }
}
