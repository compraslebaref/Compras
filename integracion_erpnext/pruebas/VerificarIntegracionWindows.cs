using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Lebaref.Windows;
class IntegrationSmoke
{
    static int Main(string[] args) { try { Check(args[0]).GetAwaiter().GetResult(); return 0; } catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; } }
    static string V(Dictionary<string,object> d, string k) { return ErpClient.Value(d,k); }
    static Dictionary<string,object> D(params object[] pairs) { var d = new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2) d[(string)pairs[i]]=pairs[i+1]; return d; }
    static async Task<string> Save(ErpClient c,string a,Dictionary<string,object> d,string f="") { return V(await c.Execute(a,f,Guid.NewGuid().ToString(),d),"folio"); }
    static async Task Check(string path)
    {
        var users = new JavaScriptSerializer().Deserialize<List<Dictionary<string,string>>>(File.ReadAllText(path));
        using(var buyer=new ErpClient()) using(var manager=new ErpClient()) using(var stock=new ErpClient())
        {
            foreach(var pair in new[]{Tuple.Create(buyer,"compras@"),Tuple.Create(manager,"autoriza@"),Tuple.Create(stock,"almacen@")}) {
                var u=users.First(r=>r["email"].StartsWith(pair.Item2)); await pair.Item1.SignIn(u["email"],u["password"]);
            }
            var seed=await buyer.Detail("Material Request","MAT-MR-2026-00001");
            var item=ErpClient.Rows(seed,"items")[0];
            var schema=await manager.Schema("proveedor", "");
            var choices=(System.Collections.IEnumerable)ErpClient.Rows(schema,"fields")[1]["choices"];
            string supplier=await Save(manager,"proveedor",D("supplier_name","Proveedor prueba integral Windows "+DateTime.Now.ToString("HHmmss"),"supplier_group",choices.Cast<object>().First().ToString(),"tax_id","AAA010101AAA"));
            string token=Guid.NewGuid().ToString();
            var requestData=D("company",V(seed,"company"),"title","Prueba integral Windows","schedule_date",DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"),"rows",new[]{D("item_code",V(item,"item_code"),"qty","4","warehouse",V(item,"warehouse"),"project",V(item,"project"),"custom_lebaref_partida","Integral Windows")});
            string request=V(await buyer.Execute("solicitud","",token,requestData),"folio");
            if(V(await buyer.Execute("solicitud","",token,requestData),"folio")!=request) throw new Exception("Duplicado solicitud");
            string rfq=await Save(buyer,"peticion",D("suppliers",supplier),request);
            schema=await buyer.Schema("oferta",rfq); var rows=ErpClient.Rows(schema,"rows");
            foreach(var r in rows) { r["rate"]="100"; r["lead_time_days"]="2"; }
            await Save(buyer,"oferta",D("supplier",supplier,"valid_till",DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),"rows",rows),rfq);
            var comparison=await buyer.Compare(request);
            var selection=ErpClient.Rows(comparison,"renglones").ToDictionary(r=>V(r,"id"),r=>V(ErpClient.Rows(r,"opciones")[0],"id"));
            string order=V((await buyer.CreateDraftOrders(request,selection))[0],"folio");
            try { await buyer.Execute("autorizar",order,Guid.NewGuid().ToString(),D("confirmar","AUTORIZAR")); throw new Exception("Permiso auxiliar incorrecto"); }
            catch(ErpException e) { if(!e.Message.Contains("permiso")) throw; }
            await Save(manager,"autorizar",D("confirmar","AUTORIZAR"),order);
            schema=await stock.Schema("recibir",order);
            var receiptData=D("rows",new[]{D("id",V(ErpClient.Rows(schema,"rows")[0],"id"),"qty","2")});
            token=Guid.NewGuid().ToString();
            string receipt=V(await stock.Execute("recibir",order,token,receiptData),"folio");
            if(V(await stock.Execute("recibir",order,token,receiptData),"folio")!=receipt) throw new Exception("Duplicado recepción");
            var detail=await stock.Detail("Purchase Receipt",receipt);
            if(V(ErpClient.Rows(detail,"items")[0],"custom_lebaref_partida")!="Integral Windows") throw new Exception("Partida perdida");
            schema=await manager.Schema("almacen", "");
            choices=(System.Collections.IEnumerable)ErpClient.Rows(schema,"fields")[2]["choices"];
            string warehouse=await Save(manager,"almacen",D("warehouse_name","Obra prueba Windows "+DateTime.Now.ToString("HHmmss"),"company",V(seed,"company"),"parent_warehouse",choices.Cast<object>().First().ToString()));
            string transfer=await Save(stock,"entregar",D("company",V(seed,"company"),"project",V(item,"project"),"from_warehouse",V(item,"warehouse"),"to_warehouse",warehouse,"remarks","Prueba integral Windows","rows",new[]{D("item_code",V(item,"item_code"),"qty","1","custom_lebaref_partida","Integral Windows")}));
            schema=await stock.Schema("devolver",transfer);
            string returned=await Save(stock,"devolver",D("rows",new[]{D("id",V(ErpClient.Rows(schema,"rows")[0],"id"),"qty","1")}),transfer);
            await stock.List("Bin",new[]{"item_code","warehouse","actual_qty","reserved_qty","ordered_qty"});
            await buyer.List("Lebaref CFDI",new[]{"name","recepcion","proveedor","moneda","total"});
            await buyer.SignOut(); await manager.SignOut(); await stock.SignOut();
            Console.WriteLine("HTTP + cliente C#: altas de proveedor/almacén, solicitud, RFQ, oferta, comparativa, orden, permisos, recepción parcial, reintentos, entrega y retorno correctos.");
            Console.WriteLine("Solicitud="+request+"; Orden="+order+"; Recepción="+receipt+"; Entrega="+transfer+"; Retorno="+returned);
        }
    }
}
