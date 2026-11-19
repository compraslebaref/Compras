using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Services;

namespace SistemaComprasAlmacen;

public partial class App : Application
{
    private bool _cambiandoSesionUsuario;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            RegistrarError(ex.Exception, "DispatcherUnhandledException");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            RegistrarError(ex.ExceptionObject as Exception, "AppDomainUnhandledException");
        };

        base.OnStartup(e);
        DatabaseService.Initialize();
        XmlCfdiService.GenerarFacturaXmlDemoEnCarpeta();
        RegistrarEstabilizadorGlobalComboBoxes();
        MostrarLoginYAbrirSistema();
    }

    public void MostrarLoginYAbrirSistema()
    {
        var login = new LoginWindow();
        if (login.ShowDialog() == true)
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Closed += (_, _) =>
            {
                if (!_cambiandoSesionUsuario)
                    Shutdown();
            };
            main.Show();
        }
        else
        {
            Shutdown();
        }
    }

    public void CambiarUsuarioReabriendoLogin(Window ventanaActual)
    {
        _cambiandoSesionUsuario = true;
        try
        {
            ventanaActual.Close();
        }
        finally
        {
            _cambiandoSesionUsuario = false;
        }
        MostrarLoginYAbrirSistema();
    }

    private static void RegistrarEstabilizadorGlobalComboBoxes()
    {
        EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnComboBoxLoadedGlobal));
        EventManager.RegisterClassHandler(typeof(ComboBox), Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnComboBoxSelectionChangedGlobal));
    }

    private static void OnComboBoxLoadedGlobal(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;

        combo.FontSize = 11.5;
        ScrollViewer.SetCanContentScroll(combo, false);
        ScrollViewer.SetHorizontalScrollBarVisibility(combo, ScrollBarVisibility.Disabled);

        combo.DropDownOpened -= OnComboBoxDropDownOpenedGlobal;
        combo.DropDownOpened += OnComboBoxDropDownOpenedGlobal;

        AjustarTextosInternosComboBox(combo);
    }

    private static void OnComboBoxSelectionChangedGlobal(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        combo.Dispatcher.BeginInvoke(new Action(() => AjustarTextosInternosComboBox(combo)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void OnComboBoxDropDownOpenedGlobal(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo) return;

        double actualW = combo.ActualWidth > 0 ? combo.ActualWidth : (double.IsNaN(combo.Width) ? 240 : combo.Width);
        double targetWidth = actualW < 130
            ? Math.Max(actualW, 145)
            : (actualW < 280 ? Math.Max(actualW, 285) : Math.Round(actualW));

        if (combo.Template?.FindName("PART_Popup", combo) is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            popupChild.Width = targetWidth;
            popupChild.MinWidth = targetWidth;
            popupChild.MaxWidth = targetWidth;

            combo.Dispatcher.BeginInvoke(new Action(() =>
            {
                EstilizarArbolVisualPopup(popupChild);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static void AjustarTextosInternosComboBox(ComboBox combo)
    {
        if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox tbEdit)
        {
            tbEdit.FontSize = 11.5;
        }
        EstilizarTextBlocksHijos(combo);
    }

    private static void EstilizarTextBlocksHijos(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb)
            {
                tb.FontSize = 11.5;
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            EstilizarTextBlocksHijos(child);
        }
    }

    private static void EstilizarArbolVisualPopup(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
            {
                sv.CanContentScroll = false;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            else if (child is ComboBoxItem cbi)
            {
                cbi.FontSize = 11.5;
                if (cbi.ToolTip == null && cbi.DataContext != null)
                {
                    string txt = cbi.Content?.ToString() ?? cbi.DataContext.ToString() ?? string.Empty;
                    if (txt.Length > 28)
                        cbi.ToolTip = txt;
                }
            }
            else if (child is TextBlock tb)
            {
                tb.FontSize = 11.5;
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            EstilizarArbolVisualPopup(child);
        }
    }

    public static void RegistrarError(Exception? ex, string origen = "App")
    {
        if (ex == null) return;
        try
        {
            System.IO.Directory.CreateDirectory(DatabaseService.LogsFolder);
            string archivo = System.IO.Path.Combine(DatabaseService.LogsFolder, $"errores_{DateTime.Now:yyyyMMdd}.log");
            string entrada = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{origen}] {ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}\r\n" + new string('-', 80) + "\r\n";
            System.IO.File.AppendAllText(archivo, entrada);
        }
        catch
        {
            // El registro de errores nunca debe provocar un fallo adicional.
        }
    }
}
