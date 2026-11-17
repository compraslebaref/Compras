using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Lebaref.Windows
{
    internal sealed class ErpClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public ErpClient()
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseProxy = false,
                AllowAutoRedirect = false
            };
            http = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://127.0.0.1:8186/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async Task<string> SignIn(string user, string password)
        {
            using (var fields = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "usr", user }, { "pwd", password }
            }))
            {
                await Read(await http.PostAsync("api/method/login", fields));
            }

            var reply = await Read(await http.GetAsync("api/method/frappe.auth.get_logged_user"));
            var identity = Value(reply, "message");
            if (String.IsNullOrEmpty(identity) || identity == "Guest")
                throw new ErpException("El servidor no inició una sesión para esta cuenta.");
            return identity;
        }

        public async Task<IList<Dictionary<string, object>>> List(string doctype, string[] fields)
        {
            var url = "api/resource/" + Uri.EscapeDataString(doctype)
                + "?fields=" + Uri.EscapeDataString(json.Serialize(fields))
                + "&order_by=modified%20desc&limit_page_length=100";
            if (doctype == "Material Request")
                url += "&filters=" + Uri.EscapeDataString("[[\"material_request_type\",\"=\",\"Purchase\"]]");
            var reply = await Read(await http.GetAsync(url));
            return Rows(reply, "data");
        }

        public async Task<Dictionary<string, object>> Detail(string doctype, string folio)
        {
            var reply = await Read(await http.GetAsync(
                "api/resource/" + Uri.EscapeDataString(doctype) + "/" + Uri.EscapeDataString(folio)));
            return (Dictionary<string, object>)reply["data"];
        }

        public async Task<Dictionary<string, object>> Compare(string folio)
        {
            var reply = await Read(await http.GetAsync(
                "api/method/lebaref_compras.comparativa.consultar?solicitud=" + Uri.EscapeDataString(folio)));
            return (Dictionary<string, object>)reply["message"];
        }

        public async Task<IList<Dictionary<string, object>>> CreateDraftOrders(
            string folio, IDictionary<string, string> selection)
        {
            using (var fields = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "solicitud", folio }, { "seleccion", json.Serialize(selection) }
            }))
            {
                var reply = await Read(await http.PostAsync(
                    "api/method/lebaref_compras.comparativa.generar_ordenes", fields));
                return Rows(reply, "message");
            }
        }

        public async Task SignOut()
        {
            using (var content = new FormUrlEncodedContent(new Dictionary<string, string>()))
                await Read(await http.PostAsync("api/method/logout", content));
        }

        private async Task<Dictionary<string, object>> Read(HttpResponseMessage response)
        {
            using (response)
            {
                switch ((int)response.StatusCode)
                {
                    case 401: throw new ErpException("Revisa el usuario y la contraseña o vuelve a iniciar sesión.");
                    case 403: throw new ErpException("La cuenta no tiene permiso para consultar esta información.");
                    case 404: throw new ErpException("ERPNext no encontró el documento solicitado.");
                    case 429: throw new ErpException("Demasiados intentos. Espera un momento y vuelve a intentarlo.");
                }
                if ((int)response.StatusCode == 417)
                    throw new ErpException(await ValidationMessage(response));
                if (!response.IsSuccessStatusCode)
                    throw new ErpException("ERPNext devolvió el código " + (int)response.StatusCode + ".");
                return json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
            }
        }

        private async Task<string> ValidationMessage(HttpResponseMessage response)
        {
            const string fallback = "ERPNext rechazó la operación. Actualiza los datos y revisa la selección.";
            try
            {
                var payload = json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                var messages = json.Deserialize<string[]>(Value(payload, "_server_messages"));
                if (messages == null || messages.Length == 0) return fallback;
                var message = Value(json.Deserialize<Dictionary<string, object>>(messages.Last()), "message");
                var plain = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(message, "<[^>]*>", ""));
                return String.IsNullOrWhiteSpace(plain) ? fallback : plain.Substring(0, Math.Min(plain.Length, 375));
            }
            catch (ArgumentException) { return fallback; }
            catch (InvalidOperationException) { return fallback; }
        }

        public static IList<Dictionary<string, object>> Rows(Dictionary<string, object> document, string key)
        {
            object value;
            if (!document.TryGetValue(key, out value) || value == null)
                return new List<Dictionary<string, object>>();
            return ((System.Collections.IEnumerable)value).Cast<Dictionary<string, object>>().ToList();
        }

        public static string Value(Dictionary<string, object> document, string key)
        {
            object value;
            return document.TryGetValue(key, out value) ? Convert.ToString(value) : String.Empty;
        }

        public void Dispose() { http.Dispose(); }
    }

    internal sealed class ErpException : Exception
    {
        public ErpException(string message) : base(message) { }
    }
}
