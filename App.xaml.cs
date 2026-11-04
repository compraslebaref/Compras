using System;
using System.Windows;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Services;

namespace SistemaComprasAlmacen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            RegistrarError(ex.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            RegistrarError(ex.ExceptionObject as Exception);
        };

        base.OnStartup(e);
        DatabaseService.Initialize();
        XmlCfdiService.GenerarFacturaXmlDemoEnCarpeta();
    }

    private static void RegistrarError(Exception? ex)
    {
        try
        {
            var ruta = System.IO.Path.Combine(AppContext.BaseDirectory, "error.log");
            System.IO.File.AppendAllText(ruta, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch
        {
            // El registro de errores nunca debe provocar un fallo adicional.
        }
    }
}
