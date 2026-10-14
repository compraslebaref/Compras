using System;
using System.Windows;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Services;

namespace SistemaComprasAlmacen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DatabaseService.Initialize();
        XmlCfdiService.GenerarFacturaXmlDemoEnCarpeta();
    }
}
