using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SistemaComprasAlmacen.Data;

namespace SistemaComprasAlmacen.Services;

public sealed class ErpNextException : Exception
{
    public int StatusCode { get; }

    public ErpNextException(string message, int statusCode = 0) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed class ErpNextUserInfo
{
    public string Identidad { get; init; } = string.Empty;
    public string NombreCompleto { get; init; } = string.Empty;
    public string PerfilRol { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = new();
}

/// <summary>
/// Cliente HTTP/.NET 8 para comunicarse con el servidor ERPNext (lebaref_compras)
/// siguiendo exactamente integracion_erpnext/CONTRATO-API.md.
/// </summary>
public sealed class ErpNextApiClient : IDisposable
{
    public const string DefaultLocalUrl = "http://127.0.0.1:8186/";

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();

    public Uri BaseAddress => _http.BaseAddress!;

    public ErpNextApiClient(string? customEndpoint = null)
    {
        string endpoint = string.IsNullOrWhiteSpace(customEndpoint)
            ? ObtenerUrlServidorConfigurada()
            : NormalizarEndpoint(customEndpoint);

        if (!ValidarEndpointSeguro(endpoint, out var uri, out var motivoError))
            throw new ErpNextException(motivoError);

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseProxy = false,
            AllowAutoRedirect = false
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(18)
        };
    }

    public static string ObtenerArchivoServidorTxt() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lebaref-servidor.txt");

    public static string ObtenerUrlServidorConfigurada()
    {
        try
        {
            string archivo = ObtenerArchivoServidorTxt();
            if (File.Exists(archivo))
            {
                string txt = File.ReadAllText(archivo).Trim();
                if (!string.IsNullOrWhiteSpace(txt))
                    return NormalizarEndpoint(txt);
            }
        }
        catch
        {
            // Continuar con ConfiguracionSistema
        }

        string dbUrl = DatabaseService.ObtenerConfiguracionTexto("ErpNextServerUrl", DefaultLocalUrl);
        return NormalizarEndpoint(string.IsNullOrWhiteSpace(dbUrl) ? DefaultLocalUrl : dbUrl);
    }

    public static void GuardarUrlServidorConfigurada(string nuevaUrl)
    {
        string normalizada = NormalizarEndpoint(nuevaUrl);
        if (!ValidarEndpointSeguro(normalizada, out _, out var error))
            throw new ErpNextException(error);

        DatabaseService.GuardarConfiguracionTexto("ErpNextServerUrl", normalizada);
        try
        {
            File.WriteAllText(ObtenerArchivoServidorTxt(), normalizada);
        }
        catch
        {
            // No bloquear si el directorio de ejecución es de solo lectura
        }
    }

    public static string NormalizarEndpoint(string raw)
    {
        string s = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(s))
            return DefaultLocalUrl;
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://" + s;
        }
        if (!s.EndsWith("/", StringComparison.Ordinal))
            s += "/";
        return s;
    }

    public static bool ValidarEndpointSeguro(string endpoint, out Uri? uri, out string mensajeError)
    {
        mensajeError = string.Empty;
        if (!Uri.TryCreate(NormalizarEndpoint(endpoint), UriKind.Absolute, out uri) || uri == null)
        {
            mensajeError = "La dirección del servidor ERPNext no tiene un formato válido.";
            return false;
        }

        bool esHttps = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        bool esHttpLocal = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
        if (!esHttps && !esHttpLocal)
        {
            mensajeError = "Por seguridad, use HTTPS para el servidor de oficina o HTTP local (127.0.0.1 / localhost) en esta PC.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
        {
            mensajeError = "Escriba únicamente el dominio y puerto raíz del servidor (ej. http://127.0.0.1:8186/ o https://erp.lebaref.mx/).";
            return false;
        }

        return true;
    }

    public static async Task<(bool Disponible, string Detalle)> ProbarConexionServidorAsync(string? endpoint = null, int timeoutMs = 2500)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(endpoint) ? ObtenerUrlServidorConfigurada() : NormalizarEndpoint(endpoint);
            if (!ValidarEndpointSeguro(url, out var uri, out var err))
                return (false, err);

            using var cts = new CancellationTokenSource(timeoutMs);
            using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = await client.GetAsync("api/method/ping", cts.Token);
            if (!resp.IsSuccessStatusCode)
                return (false, $"HTTP {(int)resp.StatusCode}");

            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            string msg = doc.RootElement.TryGetProperty("message", out var m) ? (m.GetString() ?? "") : "";
            return string.Equals(msg, "pong", StringComparison.OrdinalIgnoreCase)
                ? (true, url.TrimEnd('/'))
                : (false, "Respuesta inesperada del servidor");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<ErpNextUserInfo> SignInAsync(string user, string password, CancellationToken ct = default)
    {
        using (var fields = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usr"] = user.Trim(),
            ["pwd"] = password
        }))
        {
            using var loginResp = await _http.PostAsync("api/method/login", fields, ct);
            using var _ = await LeerJsonAsync(loginResp, ct);
        }

        string identity;
        using (var whoResp = await _http.GetAsync("api/method/frappe.auth.get_logged_user", ct))
        using (var whoDoc = await LeerJsonAsync(whoResp, ct))
        {
            identity = ObtenerString(whoDoc.RootElement, "message");
            if (string.IsNullOrWhiteSpace(identity) || string.Equals(identity, "Guest", StringComparison.OrdinalIgnoreCase))
                throw new ErpNextException("El servidor ERPNext no inició una sesión válida para esta cuenta.", 401);
        }

        string fullName = identity;
        string roleProfile = string.Empty;
        var roles = new List<string>();

        try
        {
            using var usrResp = await _http.GetAsync("api/resource/User/" + Uri.EscapeDataString(identity), ct);
            if (usrResp.IsSuccessStatusCode)
            {
                using var usrDoc = await LeerJsonAsync(usrResp, ct);
                if (usrDoc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    string fn = ObtenerString(data, "full_name");
                    if (!string.IsNullOrWhiteSpace(fn)) fullName = fn;
                    roleProfile = ObtenerString(data, "role_profile_name");

                    if (data.TryGetProperty("roles", out var rolesArr) && rolesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var rItem in rolesArr.EnumerateArray())
                        {
                            string rn = ObtenerString(rItem, "role");
                            if (!string.IsNullOrWhiteSpace(rn))
                                roles.Add(rn);
                        }
                    }
                }
            }
        }
        catch
        {
            // Algunas cuentas restringidas pueden no tener lectura completa sobre User; inferimos por esquemas si hace falta
        }

        if (roles.Count == 0 && !string.Equals(identity, "Administrator", StringComparison.OrdinalIgnoreCase))
        {
            roles = await InferirRolesPorSondeoPermisosAsync(ct);
        }

        return new ErpNextUserInfo
        {
            Identidad = identity,
            NombreCompleto = fullName,
            PerfilRol = roleProfile,
            Roles = roles
        };
    }

    private async Task<List<string>> InferirRolesPorSondeoPermisosAsync(CancellationToken ct)
    {
        var detectados = new List<string>();
        try
        {
            using var r1 = await _http.GetAsync("api/resource/Supplier?limit_page_length=1", ct);
            if (r1.IsSuccessStatusCode) detectados.Add("Purchase User");
        }
        catch { }

        try
        {
            using var r2 = await _http.GetAsync("api/method/lebaref_compras.operaciones.esquema?accion=articulo&folio=", ct);
            if (r2.IsSuccessStatusCode) detectados.Add("Purchase Manager");
        }
        catch { }

        try
        {
            using var r3 = await _http.GetAsync("api/method/lebaref_compras.operaciones.esquema?accion=entregar&folio=", ct);
            if (r3.IsSuccessStatusCode) detectados.Add("Stock User");
        }
        catch { }

        return detectados;
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>());
            using var resp = await _http.PostAsync("api/method/logout", content, ct);
        }
        catch
        {
            // Ignorar errores de red al cerrar sesión
        }
    }

    public async Task<List<Dictionary<string, object?>>> ListAsync(
        string doctype,
        string[] fields,
        string? filtersJson = null,
        int limitStart = 0,
        int limitPageLength = 100,
        CancellationToken ct = default)
    {
        string fieldsJson = JsonSerializer.Serialize(fields);
        string url = $"api/resource/{Uri.EscapeDataString(doctype)}" +
                     $"?fields={Uri.EscapeDataString(fieldsJson)}" +
                     $"&order_by=modified%20desc&limit_start={Math.Max(0, limitStart)}&limit_page_length={Math.Clamp(limitPageLength, 1, 1000)}";

        if (!string.IsNullOrWhiteSpace(filtersJson))
            url += $"&filters={Uri.EscapeDataString(filtersJson)}";
        else if (doctype == "Material Request")
            url += "&filters=" + Uri.EscapeDataString("[[\"material_request_type\",\"=\",\"Purchase\"]]");

        using var resp = await _http.GetAsync(url, ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return ExtraerFilas(doc.RootElement, "data");
    }

    public async Task<Dictionary<string, object?>> DetailAsync(string doctype, string folio, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/resource/{Uri.EscapeDataString(doctype)}/{Uri.EscapeDataString(folio)}", ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return doc.RootElement.TryGetProperty("data", out var data)
            ? ConvertirObjeto(data)
            : new Dictionary<string, object?>();
    }

    public async Task<Dictionary<string, object?>> SchemaAsync(string action, string folio = "", CancellationToken ct = default)
    {
        string url = $"api/method/lebaref_compras.operaciones.esquema?accion={Uri.EscapeDataString(action)}&folio={Uri.EscapeDataString(folio ?? string.Empty)}";
        using var resp = await _http.GetAsync(url, ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return doc.RootElement.TryGetProperty("message", out var msg)
            ? ConvertirObjeto(msg)
            : new Dictionary<string, object?>();
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(
        string action,
        Dictionary<string, object?> data,
        string folio = "",
        string? operationToken = null,
        CancellationToken ct = default)
    {
        string token = string.IsNullOrWhiteSpace(operationToken) ? Guid.NewGuid().ToString("D") : operationToken.Trim();
        string jsonDatos = JsonSerializer.Serialize(data);

        using var fields = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["accion"] = action,
            ["folio"] = folio ?? string.Empty,
            ["operacion"] = token,
            ["datos"] = jsonDatos
        });

        using var resp = await _http.PostAsync("api/method/lebaref_compras.operaciones.ejecutar", fields, ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return doc.RootElement.TryGetProperty("message", out var msg)
            ? ConvertirObjeto(msg)
            : new Dictionary<string, object?>();
    }

    public async Task<Dictionary<string, object?>> CompareAsync(string solicitudFolio, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/method/lebaref_compras.comparativa.consultar?solicitud={Uri.EscapeDataString(solicitudFolio)}", ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return doc.RootElement.TryGetProperty("message", out var msg)
            ? ConvertirObjeto(msg)
            : new Dictionary<string, object?>();
    }

    public async Task<List<Dictionary<string, object?>>> CreateDraftOrdersAsync(
        string solicitudFolio,
        IDictionary<string, string> selection,
        CancellationToken ct = default)
    {
        using var fields = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["solicitud"] = solicitudFolio,
            ["seleccion"] = JsonSerializer.Serialize(selection)
        });
        using var resp = await _http.PostAsync("api/method/lebaref_compras.comparativa.generar_ordenes", fields, ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return ExtraerFilas(doc.RootElement, "message");
    }

    public async Task<Dictionary<string, object?>> EquipmentDetailAsync(string type, string folio, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/method/lebaref_compras.equipos.detalle?tipo={Uri.EscapeDataString(type)}&folio={Uri.EscapeDataString(folio)}", ct);
        using var doc = await LeerJsonAsync(resp, ct);
        return doc.RootElement.TryGetProperty("message", out var msg)
            ? ConvertirObjeto(msg)
            : new Dictionary<string, object?>();
    }

    private static async Task<JsonDocument> LeerJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        int code = (int)response.StatusCode;
        string body = await response.Content.ReadAsStringAsync(ct);

        switch (code)
        {
            case 401:
                throw new ErpNextException("Usuario o contraseña de ERPNext incorrectos, o la sesión expiró.", 401);
            case 403:
                throw new ErpNextException("Tu cuenta no tiene permiso en ERPNext para realizar esta operación.", 403);
            case 404:
                throw new ErpNextException("ERPNext no encontró el documento o registro solicitado.", 404);
            case 429:
                throw new ErpNextException("Demasiados intentos seguidos en ERPNext. Espera unos segundos y vuelve a intentarlo.", 429);
            case 417:
                throw new ErpNextException(ExtraerMensajeValidacionFrappe(body), 417);
        }

        if (!response.IsSuccessStatusCode)
            throw new ErpNextException($"El servidor ERPNext devolvió el código HTTP {code}.", code);

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private static string ExtraerMensajeValidacionFrappe(string body)
    {
        const string fallback = "ERPNext rechazó la operación por validación de negocio. Revisa los datos capturados.";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("_server_messages", out var srvProp))
            {
                string rawArr = srvProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(rawArr))
                {
                    var items = JsonSerializer.Deserialize<string[]>(rawArr);
                    if (items != null && items.Length > 0)
                    {
                        using var inner = JsonDocument.Parse(items.Last());
                        string msg = ObtenerString(inner.RootElement, "message");
                        string limpio = WebUtility.HtmlDecode(Regex.Replace(msg, "<[^>]*>", "")).Trim();
                        if (!string.IsNullOrWhiteSpace(limpio))
                            return limpio.Length > 380 ? limpio[..380] : limpio;
                    }
                }
            }
        }
        catch
        {
            // Usar mensaje de respaldo
        }
        return fallback;
    }

    public static string ObtenerString(JsonElement elem, string prop)
    {
        if (elem.ValueKind != JsonValueKind.Object || !elem.TryGetProperty(prop, out var val))
            return string.Empty;
        return val.ValueKind switch
        {
            JsonValueKind.String => val.GetString() ?? string.Empty,
            JsonValueKind.Number => val.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static List<Dictionary<string, object?>> ExtraerFilas(JsonElement root, string prop)
    {
        var filas = new List<Dictionary<string, object?>>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return filas;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
                filas.Add(ConvertirObjeto(item));
        }
        return filas;
    }

    private static Dictionary<string, object?> ConvertirObjeto(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (obj.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var prop in obj.EnumerateObject())
        {
            dict[prop.Name] = ConvertirValor(prop.Value);
        }
        return dict;
    }

    private static object? ConvertirValor(JsonElement val) => val.ValueKind switch
    {
        JsonValueKind.String => val.GetString(),
        JsonValueKind.Number => val.TryGetInt64(out long l) ? l : (val.TryGetDecimal(out decimal d) ? d : val.GetDouble()),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => val.EnumerateArray().Select(ConvertirValor).ToList(),
        JsonValueKind.Object => ConvertirObjeto(val),
        _ => null
    };

    public void Dispose() => _http.Dispose();
}
