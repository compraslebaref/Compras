using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SistemaComprasAlmacen.Services;

/// <summary>
/// Reemplazo de <see cref="System.Windows.MessageBox"/> con estilo nativo Fluent (Windows 11):
/// respeta tema claro/oscuro, color de acento del sistema y material Mica.
/// Mantiene la misma firma síncrona para no alterar la lógica existente.
/// </summary>
public static class DialogoFluent
{
    public static MessageBoxResult Show(string mensaje) =>
        Show(null, mensaje, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string mensaje, string titulo) =>
        Show(null, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string mensaje) =>
        Show(owner, mensaje, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string mensaje, string titulo) =>
        Show(owner, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono) =>
        Show(null, mensaje, titulo, botones, icono);

    public static MessageBoxResult Show(Window? owner, string mensaje, string titulo, MessageBoxButton botones) =>
        Show(owner, mensaje, titulo, botones, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono)
    {
        owner ??= Application.Current?.MainWindow;
        if (owner != null && !owner.IsLoaded) owner = null;

        var resultado = botones == MessageBoxButton.YesNo || botones == MessageBoxButton.YesNoCancel
            ? MessageBoxResult.No
            : (botones == MessageBoxButton.OKCancel ? MessageBoxResult.Cancel : MessageBoxResult.OK);

        var win = new FluentWindow
        {
            Title = string.IsNullOrWhiteSpace(titulo) ? "Lebaref" : titulo,
            Width = 460,
            MaxWidth = 620,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
            Owner = owner
        };
        win.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        win.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var barra = new TitleBar
        {
            Title = win.Title,
            ShowMaximize = false,
            ShowMinimize = false
        };
        Grid.SetRow(barra, 0);
        root.Children.Add(barra);

        // Cuerpo: icono + mensaje
        var cuerpo = new Grid { Margin = new Thickness(24, 12, 24, 20) };
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (simbolo, claveColor) = icono switch
        {
            MessageBoxImage.Error => (SymbolRegular.ErrorCircle24, "SystemFillColorCriticalBrush"),
            MessageBoxImage.Warning => (SymbolRegular.Warning24, "SystemFillColorCautionBrush"),
            MessageBoxImage.Question => (SymbolRegular.QuestionCircle24, "AccentFillColorDefaultBrush"),
            MessageBoxImage.Information => (SymbolRegular.Info24, "AccentFillColorDefaultBrush"),
            _ => (SymbolRegular.Empty, "AccentFillColorDefaultBrush")
        };

        if (simbolo != SymbolRegular.Empty)
        {
            var ic = new SymbolIcon
            {
                Symbol = simbolo,
                FontSize = 32,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            ic.SetResourceReference(Control.ForegroundProperty, claveColor);
            cuerpo.Children.Add(ic);
        }

        var txt = new System.Windows.Controls.TextBlock
        {
            Text = mensaje,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            MaxHeight = 420
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = txt,
            MaxHeight = 420
        };
        Grid.SetColumn(scroll, 1);
        cuerpo.Children.Add(scroll);
        Grid.SetRow(cuerpo, 1);
        root.Children.Add(cuerpo);

        // Pie de botones
        var pie = new Border { Padding = new Thickness(20, 14, 20, 14) };
        pie.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        void Agregar(string texto, MessageBoxResult valor, bool primario, bool esCancel)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = texto,
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                Appearance = primario ? ControlAppearance.Primary : ControlAppearance.Secondary,
                IsDefault = primario,
                IsCancel = esCancel
            };
            b.Click += (_, _) => { resultado = valor; win.Close(); };
            panel.Children.Add(b);
        }

        switch (botones)
        {
            case MessageBoxButton.OK:
                Agregar("Aceptar", MessageBoxResult.OK, true, true);
                break;
            case MessageBoxButton.OKCancel:
                Agregar("Aceptar", MessageBoxResult.OK, true, false);
                Agregar("Cancelar", MessageBoxResult.Cancel, false, true);
                break;
            case MessageBoxButton.YesNo:
                Agregar("Sí", MessageBoxResult.Yes, true, false);
                Agregar("No", MessageBoxResult.No, false, true);
                break;
            case MessageBoxButton.YesNoCancel:
                Agregar("Sí", MessageBoxResult.Yes, true, false);
                Agregar("No", MessageBoxResult.No, false, false);
                Agregar("Cancelar", MessageBoxResult.Cancel, false, true);
                break;
        }

        pie.Child = panel;
        Grid.SetRow(pie, 2);
        root.Children.Add(pie);

        win.Content = root;
        win.ShowDialog();
        return resultado;
    }
}
