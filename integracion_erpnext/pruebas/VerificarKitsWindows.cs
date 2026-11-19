// Solo entorno ficticio local. Conserva registros DEMO para revisión.
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Lebaref.Windows;

class KitSmoke
{
    [STAThread] static int Main(string[] args) { try { Check(args[0], args[1]).GetAwaiter().GetResult(); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } }
    static string V(Dictionary<string, object> d, string k) { return ErpClient.Value(d,k); }
    static Dictionary<string, object> D(params object[] values) { var d=new Dictionary<string,object>(); for(int i=0;i<values.Length;i+=2) d[(string)values[i]]=values[i+1]; return d; }
    static async Task<string> Save(ErpClient c,string a,Dictionary<string,object> d,string source="") { return V(await c.Execute(a,source,Guid.NewGuid().ToString(),d),"folio"); }
    static void Expect(bool value,string reason) { if(!value) throw new Exception(reason); }
    static async Task Check(string path,string imagePath)
    {
        var users=new JavaScriptSerializer().Deserialize<List<Dictionary<string,string>>>(File.ReadAllText(path));
        using(var manager=new ErpClient()) using(var stock=new ErpClient()) using(var buyer=new ErpClient())
        {
            foreach(var p in new[]{Tuple.Create(manager,"autoriza@"),Tuple.Create(stock,"almacen@"),Tuple.Create(buyer,"compras@")}) {var u=users.First(r=>r["email"].StartsWith(p.Item2));await p.Item1.SignIn(u["email"],u["password"]);}
            var seed=await manager.Detail("Material Request","MAT-MR-2026-00001"); var item=ErpClient.Rows(seed,"items")[0];
            string suffix=DateTime.Now.ToString("MMddHHmmss"), today=DateTime.Today.ToString("yyyy-MM-dd");
            var tools=new List<string>();
            for(int i=1;i<=3;i++) tools.Add(await Save(manager,"herramienta",D("codigo","DEMO-KH-"+suffix+"-"+i,"company",V(seed,"company"),"articulo",V(item,"item_code"),"ubicacion","Gabinete de kits","marca_modelo","Herramienta de prueba")));
            string kit=await Save(manager,"kit",D("codigo","KIT-DEMO-"+suffix,"nombre","Kit eléctrico de prueba","company",V(seed,"company"),"especialidad","Eléctrico","ubicacion","Gabinete K-1","rows",new[]{D("herramienta",tools[0]),D("herramienta",tools[1])}));
            var rows=ErpClient.Rows(await stock.Schema("prestamo_kit",kit),"rows"); rows[1]["incluir"]="No";
            var loanData=D("obra",V(item,"project"),"responsable","Responsable ficticio","recibe","Operario ficticio","compromiso",DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"),"rows",rows);
            string token=Guid.NewGuid().ToString(); var result=await stock.Execute("prestamo_kit",kit,token,loanData); string loan=V(result,"folio");
            Expect(V(await stock.Execute("prestamo_kit",kit,token,loanData),"folio")==loan,"Préstamo duplicado");
            Expect(ErpClient.Rows(await stock.Detail("Lebaref Prestamo",loan),"items").Count==1,"Selección de kit incorrecta");
            try { await Save(manager,"composicion_kit",D("motivo","Prueba de bloqueo","rows",new[]{D("herramienta",tools[0])}),kit); throw new Exception("Kit prestado modificable"); } catch(ErpException e) {if(!e.Message.Contains("prestadas"))throw;}
            var pending=ErpClient.Rows(await stock.Schema("retorno_herramienta",loan),"rows")[0];
            await Save(stock,"retorno_herramienta",D("recibe","Almacén ficticio","rows",new[]{D("id",V(pending,"id"),"estado","Bueno","observaciones","")}),loan);
            await Save(manager,"composicion_kit",D("motivo","Cambio ficticio de una pieza","rows",new[]{D("herramienta",tools[0]),D("herramienta",tools[2])}),kit);
            var warranty=D("fecha_compra",DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd"),"tiene_garantia","Sí","fin_garantia",DateTime.Today.AddDays(60).ToString("yyyy-MM-dd"),"costo_adquisicion","1000.00","referencia","FACTURA-DEMO","motivo","Registro ficticio de garantía");
            await Save(manager,"garantia_herramienta",warranty,tools[0]);
            var service=D("fecha",today,"proveedor","Taller ficticio","referencia","MANT-DEMO-1","monto","600.00","bajo_garantia","No","motivo","Reparación ficticia");
            string maintenance=await Save(stock,"mantenimiento_herramienta",service,tools[0]);
            await Save(manager,"anular_mantenimiento",D("motivo","Importe ficticio corregido"),maintenance);
            service["monto"]="200.00"; string corrected=await Save(stock,"mantenimiento_herramienta",service,tools[0]);
            var history=ErpClient.Rows(await buyer.EquipmentDetail("Lebaref Herramienta",tools[0]),"rows");
            Expect(history.Any(r=>V(r,"campo")=="Mantenimiento / adquisición" && V(r,"valor")=="20.0%"),"Acumulado incorrecto");
            Expect(history.Any(r=>V(r,"valor")=="ANULADO"),"Historial de anulación ausente");
            foreach(var type in new[]{"Lebaref Kit","Lebaref Composicion Kit","Lebaref Garantia Herramienta","Lebaref Mantenimiento Herramienta","Lebaref Anulacion Mantenimiento"}) await buyer.List(type,new[]{"name"});
            rows=ErpClient.Rows(await stock.Schema("prestamo_kit",kit),"rows"); rows[1]["incluir"]="No"; loanData["rows"]=rows;
            string active=await Save(stock,"prestamo_kit",loanData,kit);
            using(var image=BarcodeLabel.Render(kit,"KIT","Kit eléctrico de prueba","Gabinete K-1")) image.Save(imagePath,ImageFormat.Png);
            Expect(V(await buyer.Detail("Lebaref Kit",kit),"name")==kit,"Búsqueda exacta por código incorrecta");
            await manager.SignOut(); await stock.SignOut(); await buyer.SignOut();
            Console.WriteLine("Windows HTTP: kit, préstamo selectivo, bloqueo, retorno, composición, garantía, mantenimiento y anulación correctos.");
            Console.WriteLine("Kit="+kit+"; Herramienta="+tools[0]+"; Préstamo activo="+active+"; Mantenimiento corregido="+corrected);
            Console.WriteLine("Etiqueta PNG="+imagePath);
        }
    }
}
