using System;
using System.Windows;
using System.Windows.Controls;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Services;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SistemaComprasAlmacen;

public partial class MainWindow
{
    private void BtnAbrirSheetsProveedores_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GoogleSheetsProveedoresService.AbrirHojaEnNavegador();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se pudo abrir el navegador:\n{ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnSincronizarSheetsProveedores_Click(object sender, RoutedEventArgs e)
    {
        var (sheetId, webAppUrl) = GoogleSheetsProveedoresService.ObtenerConfiguracion();

        var win = new Window
        {
            Title = "Catálogo ¿A quién cotizar? — Sincronización con Google Sheets",
            Width = 650,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        root.Children.Add(new TextBlock
        {
            Text = "Catálogo de Proveedores por Categoría (Google Sheets)",
            FontSize = 16.5,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Sus auxiliares pueden consultar en tiempo real a qué proveedor cotizar cada categoría de material junto con su teléfono, contacto y artículos históricos. Si capturan teléfonos en la pestaña 'Directorio_Proveedores' de Google Sheets, al sincronizar también se guardarán en el ERP.",
            FontSize = 12,
            Opacity = 0.78,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14)
        });

        root.Children.Add(new TextBlock { Text = "ID o enlace de la hoja de Google Sheets:", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var txtSheetId = new System.Windows.Controls.TextBox { Text = sheetId, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(txtSheetId);

        var dockScript = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var btnCopiarScript = new System.Windows.Controls.Button { Content = "📋 Copiar código Apps Script (1 sola vez)", FontSize = 11, Padding = new Thickness(8, 2, 8, 2) };
        DockPanel.SetDock(btnCopiarScript, Dock.Right);
        dockScript.Children.Add(btnCopiarScript);
        dockScript.Children.Add(new TextBlock { Text = "URL de sincronización automática (Apps Script Web App):", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(dockScript);

        var txtWebApp = new System.Windows.Controls.TextBox
        {
            Text = webAppUrl,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(txtWebApp);

        root.Children.Add(new TextBlock
        {
            Text = "Para activar el botón de sincronización directa desde cualquier PC: abra la hoja → Extensiones → Apps Script → pegue el código copiado → Implementar → Nueva implementación → Aplicación web (Acceso: Cualquier persona) y pegue aquí la URL '/exec'.",
            FontSize = 11,
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var pnlBtns = new DockPanel();
        var btnAbrir = new Wpf.Ui.Controls.Button { Content = "🌐 Abrir Hoja en Navegador", Padding = new Thickness(12, 6, 12, 6) };
        DockPanel.SetDock(btnAbrir, Dock.Left);
        pnlBtns.Children.Add(btnAbrir);

        var pnlDer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnGuardar = new System.Windows.Controls.Button { Content = "Guardar enlace", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSync = new Wpf.Ui.Controls.Button { Content = "🔄 Sincronizar Ahora", Appearance = ControlAppearance.Primary, Padding = new Thickness(14, 6, 14, 6) };
        pnlDer.Children.Add(btnGuardar);
        pnlDer.Children.Add(btnSync);
        DockPanel.SetDock(pnlDer, Dock.Right);
        pnlBtns.Children.Add(pnlDer);
        root.Children.Add(pnlBtns);

        btnCopiarScript.Click += (_, _) =>
        {
            Clipboard.SetText(GoogleSheetsProveedoresService.CodigoAppsScriptConector);
            MostrarToastDiscreto("Código copiado. Péguelo en Extensiones → Apps Script dentro de la hoja de Google Sheets.", "Código Apps Script Copiado");
        };

        btnAbrir.Click += (_, _) =>
        {
            GoogleSheetsProveedoresService.GuardarConfiguracion(txtSheetId.Text, txtWebApp.Text);
            GoogleSheetsProveedoresService.AbrirHojaEnNavegador();
        };

        btnGuardar.Click += (_, _) =>
        {
            GoogleSheetsProveedoresService.GuardarConfiguracion(txtSheetId.Text, txtWebApp.Text);
            MostrarToastDiscreto("Configuración de Google Sheets guardada.", "Guardado");
            win.Close();
        };

        btnSync.Click += async (_, _) =>
        {
            GoogleSheetsProveedoresService.GuardarConfiguracion(txtSheetId.Text, txtWebApp.Text);
            btnSync.IsEnabled = false;
            btnSync.Content = "Sincronizando...";
            try
            {
                var (ok, msj, importados) = await GoogleSheetsProveedoresService.SincronizarConWebAppAsync(txtWebApp.Text);
                if (importados > 0)
                {
                    DgDirProveedores.ItemsSource = DatabaseService.ObtenerProveedores();
                }
                DialogoFluent.Show(win, msj, ok ? "Google Sheets Sincronizado" : "Configuración Requerida", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Information);
                if (ok) win.Close();
            }
            finally
            {
                btnSync.IsEnabled = true;
                btnSync.Content = "🔄 Sincronizar Ahora";
            }
        };

        win.Content = root;
        win.ShowDialog();
    }
}
