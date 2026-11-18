// Solo entorno ficticio local. Esta prueba conserva registros DEMO para revisión.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Lebaref.Windows;

class EquipmentSmoke
{
    static int Main(string[] args)
    {
        try { Check(args[0]).GetAwaiter().GetResult(); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
    }
    static Dictionary<string, object> D(params object[] values)
    {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < values.Length; i += 2) result[(string)values[i]] = values[i + 1];
        return result;
    }
    static string V(Dictionary<string, object> d, string k) { return ErpClient.Value(d, k); }
    static async Task<string> Save(ErpClient c, string a, Dictionary<string, object> d, string source = "")
    { return V(await c.Execute(a, source, Guid.NewGuid().ToString(), d), "folio"); }
    static void Expect(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task<string> Race(ErpClient c, Dictionary<string, object> data)
    {
        try { return await Save(c, "prestamo", data); }
        catch (ErpException e) { if (!e.Message.Contains("no está disponible") && !e.Message.Contains("ya está prestada")) throw; return ""; }
    }
    static async Task Check(string path)
    {
        var accounts = new JavaScriptSerializer().Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(path));
        using (var manager = new ErpClient()) using (var stock = new ErpClient()) using (var buyer = new ErpClient())
        {
            foreach (var pair in new[] { Tuple.Create(manager, "autoriza@"), Tuple.Create(stock, "almacen@"), Tuple.Create(buyer, "compras@") })
            {
                var account = accounts.First(x => x["email"].StartsWith(pair.Item2));
                await pair.Item1.SignIn(account["email"], account["password"]);
            }
            var seed = await manager.Detail("Material Request", "MAT-MR-2026-00001");
            var item = ErpClient.Rows(seed, "items")[0];
            string suffix = DateTime.Now.ToString("MMddHHmmss"), date = DateTime.Today.ToString("yyyy-MM-dd");
            var tools = new List<string>();
            for (int i = 1; i <= 3; i++)
                tools.Add(await Save(manager, "herramienta", D("codigo", "DEMO-HER-" + suffix + "-" + i, "company", V(seed, "company"), "articulo", V(item, "item_code"), "serie", "DEMO-" + i, "marca_modelo", "Herramienta ficticia", "ubicacion", "Gabinete de prueba")));
            var data = D("company", V(seed, "company"), "obra", V(item, "project"), "responsable", "Responsable ficticio", "recibe", "Operario ficticio", "compromiso", DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"), "rows", new[] { D("herramienta", tools[0]), D("herramienta", tools[1]) });
            string token = Guid.NewGuid().ToString();
            var simultaneous = await Task.WhenAll(stock.Execute("prestamo", "", token, data), stock.Execute("prestamo", "", token, data));
            string loan = V(simultaneous[0], "folio");
            Expect(V(simultaneous[1], "folio") == loan, "El reintento simultáneo duplicó el préstamo");
            var schema = await stock.Schema("retorno_herramienta", loan);
            var rows = ErpClient.Rows(schema, "rows");
            await Save(stock, "retorno_herramienta", D("recibe", "Almacén ficticio", "rows", new[] { D("id", V(rows[0], "id"), "estado", "Bueno", "observaciones", "Inspección de prueba") }), loan);
            Expect(V(await stock.Detail("Lebaref Prestamo", loan), "estado") == "Devuelto parcial", "No quedó parcial");
            await Save(stock, "retorno_herramienta", D("recibe", "Almacén ficticio", "rows", new[] { D("id", V(rows[1], "id"), "estado", "Dañada", "observaciones", "Cable dañado ficticio") }), loan);
            Expect(V(await stock.Detail("Lebaref Herramienta", tools[1]), "estado") == "Dañada", "El daño no se conservó");
            await Save(manager, "estado_herramienta", D("estado", "Disponible", "ubicacion", "Gabinete de prueba", "motivo", "Reparación ficticia revisada"), tools[1]);
            data["rows"] = new[] { D("herramienta", tools[2]) };
            var races = await Task.WhenAll(Race(stock, data), Race(manager, data));
            Expect(races.Count(x => x.Length > 0) == 1, "Dos sesiones prestaron la misma herramienta");
            string activeLoan = races.First(x => x.Length > 0);
            for (int round = 0; round < 5; round++)
            {
                var pendingReturn = ErpClient.Rows(await stock.Schema("retorno_herramienta", activeLoan), "rows")[0];
                await Save(stock, "retorno_herramienta", D("recibe", "Almacén ficticio", "rows", new[] { D("id", V(pendingReturn, "id"), "estado", "Bueno", "observaciones", "Ronda de concurrencia") }), activeLoan);
                races = await Task.WhenAll(Race(stock, data), Race(manager, data));
                Expect(races.Count(x => x.Length > 0) == 1, "Concurrencia incorrecta en ronda " + round);
                activeLoan = races.First(x => x.Length > 0);
            }
            string vehicle = await Save(manager, "vehiculo", D("codigo", "DEMO-VEH-" + suffix, "placas", "DEMO" + suffix, "company", V(seed, "company"), "marca_modelo", "Vehículo de prueba", "responsable", "Conductor ficticio", "km", "1000", "fecha", date, "intervalo", "10000", "proximo_servicio", "1100", "capacidad", "60", "combustible", "Gasolina Regular", "seguro", DateTime.Today.AddDays(20).ToString("yyyy-MM-dd")));
            var expense = D("fecha", date, "tipo", "Combustible", "km", "1100", "litros", "10", "tanque_lleno", "Sí", "monto", "240.00", "proveedor", "Proveedor ficticio", "referencia", "TICKET-DEMO-1", "descripcion", "Carga ficticia");
            string charge = await Save(stock, "gasto_vehiculo", expense, vehicle);
            try { await Save(stock, "gasto_vehiculo", expense, vehicle); throw new Exception("Comprobante duplicado admitido"); }
            catch (ErpException e) { if (!e.Message.Contains("ya tiene un gasto")) throw; }
            string review = await Save(stock, "revision_vehiculo", D("fecha", date, "km", "1200", "inspector", "Inspector ficticio", "nivel", "3/4", "llantas", "OK", "fluidos", "OK", "frenos_luces", "Atención", "carroceria", "OK", "observaciones", "Revisión de luz ficticia"), vehicle);
            expense["tipo"] = "Mantenimiento preventivo"; expense["km"] = "1200"; expense["litros"] = "0"; expense["monto"] = "1000.00"; expense["referencia"] = "SERVICIO-DEMO-1";
            await Save(stock, "gasto_vehiculo", expense, vehicle);
            await Save(manager, "estado_vehiculo", D("estado", "En taller", "responsable", "Conductor ficticio", "seguro", DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"), "motivo", "Revisión ficticia"), vehicle);
            var history = await buyer.EquipmentDetail("Lebaref Vehiculo", vehicle);
            Expect(ErpClient.Rows(history, "rows").Any(r => V(r, "campo") == "Gasto total registrado" && Decimal.Parse(V(r, "valor")) == 1240m), "Total de gastos incorrecto");
            foreach (var dt in new[] { "Lebaref Herramienta", "Lebaref Prestamo", "Lebaref Retorno", "Lebaref Vehiculo", "Lebaref Gasto Vehiculo", "Lebaref Revision Vehiculo", "Lebaref Estado Herramienta", "Lebaref Estado Vehiculo" })
                await buyer.List(dt, new[] { "name" });
            await manager.SignOut(); await stock.SignOut(); await buyer.SignOut();
            Console.WriteLine("HTTP Windows: préstamos, retornos, incidencias, flotilla e historial correctos.");
            Console.WriteLine("Concurrencia: mismo intento devuelve un folio; seis rondas con dos usuarios impiden prestar dos veces la misma herramienta.");
            Console.WriteLine("Herramientas=" + String.Join(", ", tools));
            Console.WriteLine("Préstamo terminado=" + loan + "; Préstamo activo=" + activeLoan);
            Console.WriteLine("Vehículo=" + vehicle + "; Gasto=" + charge + "; Revisión=" + review);
        }
    }
}
