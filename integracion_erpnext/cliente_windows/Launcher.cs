using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

class Launcher : Form
{
    private readonly Label message = new Label { Dock = DockStyle.Fill, Padding = new Padding(20), Text = "Comprobando el servidor de Lebaref…" };
    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new Launcher()); }
    Launcher()
    {
        Text = "Iniciar Lebaref"; ClientSize = new Size(470, 150); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11F); Controls.Add(message);
        Shown += async (s, e) => await Start();
    }
    private async Task Start()
    {
        try
        {
            var folder = AppDomain.CurrentDomain.BaseDirectory;
            var app = Path.Combine(folder, "Lebaref-Integrado.exe");
            if (!File.Exists(app)) throw new Exception("Coloca Lebaref-Integrado.exe en esta misma carpeta.");
            var settings = Path.Combine(folder, "lebaref-servidor.txt");
            string endpoint = File.Exists(settings) ? File.ReadAllText(settings).Trim() : "http://127.0.0.1:8186/";
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)) throw new Exception("Dirección de servidor inválida.");
            if (uri.IsLoopback && uri.Port == 8186)
            {
                using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) })
                {
                    bool ready = await Ping(http);
                    if (!ready)
                    {
                        using (var process = Process.Start(new ProcessStartInfo("schtasks.exe", "/Run /TN Lebaref-Pruebas-WSL") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden }))
                        {
                            await Task.Run(() => process.WaitForExit());
                            if (process.ExitCode != 0) throw new Exception("Esta PC no tiene disponible el servicio de pruebas. Consulta la guía de instalación.");
                        }
                        for (int n = 0; n < 60 && !ready; n++)
                        {
                            message.Text = "Iniciando ERPNext local. Puede tardar hasta dos minutos…";
                            await Task.Delay(2000); ready = await Ping(http);
                        }
                    }
                    if (!ready) throw new Exception("El servidor todavía no responde. Revisa el estado del entorno según la guía.");
                }
            }
            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true }); Close();
        }
        catch (Exception error) { message.Text = error.Message; }
    }
    private static async Task<bool> Ping(HttpClient http)
    {
        try { using (var response = await http.GetAsync("http://127.0.0.1:8186/api/method/ping")) { return response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("pong"); } }
        catch (HttpRequestException) { return false; } catch (TaskCanceledException) { return false; }
    }
}
