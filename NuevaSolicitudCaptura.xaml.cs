using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;
using Boton = Wpf.Ui.Controls.Button;
using CampoTexto = Wpf.Ui.Controls.TextBox;

namespace SistemaComprasAlmacen;

public partial class NuevaSolicitudCaptura : UserControl
{
    private readonly IReadOnlyList<Producto> _productos;
    private readonly List<CapturaPartida> _partidas = new();
    private bool? _usaPartidas;

    public event Action? DatosConfirmados;
    public event Action? Cancelada;
    public event Action<Solicitud>? Guardada;

    public NuevaSolicitudCaptura(IReadOnlyList<Departamento> departamentos, IReadOnlyList<Proyecto> proyectos,
        IReadOnlyList<Producto> productos)
    {
        _productos = productos;
        InitializeComponent();
        CboDepartamento.ItemsSource = departamentos;
        CboProyecto.Items.Add("Consumo interno de departamento");
        foreach (var proyecto in proyectos)
            CboProyecto.Items.Add(proyecto);
        CboProyecto.SelectedIndex = 0;
    }

    private void Departamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CboSolicitante.ItemsSource = CboDepartamento.SelectedItem is Departamento departamento
            ? DatabaseService.ObtenerEncargados(departamento.Id)
            : null;
        CboSolicitante.SelectedIndex = -1;
    }

    private void Notificacion_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlTiempo != null)
            PnlTiempo.IsEnabled = ChkNotificar.IsChecked == true;
    }

    private void Continuar_Click(object sender, RoutedEventArgs e)
    {
        if (CboDepartamento.SelectedItem is not Departamento departamento ||
            CboSolicitante.SelectedItem is not Encargado solicitante)
        {
            TxtError.Text = "Selecciona el departamento y el solicitante.";
            return;
        }

        bool usaPartidas = ChkPartidas.IsChecked == true;
        if (PasoDatos.Visibility == Visibility.Visible)
        {
            _usaPartidas = usaPartidas;
            if (!usaPartidas) CrearPartida();
            TxtResumen.Text = $"{departamento.Nombre} · {solicitante.Nombre} · {CboProyecto.SelectedItem}";
            DatosConfirmados?.Invoke();
            return;
        }

        foreach (var partida in _partidas)
        {
            if (!string.IsNullOrWhiteSpace(partida.Articulo.Text) && !AgregarArticulo(partida))
                return;
        }

        if (!_partidas.Any(p => p.Articulos.Count > 0))
        {
            TxtError.Text = "Agrega al menos un artículo.";
            return;
        }

        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var articulosSolicitud = new List<SolicitudPartida>();
        foreach (var partida in _partidas)
        {
            string nombre = usaPartidas ? partida.Nombre.Text.Trim() : string.Empty;
            if (usaPartidas && (nombre.Length == 0 || partida.Articulos.Count == 0 || !nombres.Add(nombre)))
            {
                TxtError.Text = "Escribe un nombre distinto y agrega al menos un artículo en cada partida.";
                partida.Panel.BringIntoView();
                partida.Nombre.Focus();
                return;
            }

            foreach (var articulo in partida.Articulos)
                articulo.NombrePartida = nombre;
            articulosSolicitud.AddRange(partida.Articulos);
        }

        string tipo = (string)((ComboBoxItem)CboTipo.SelectedItem).Content;
        BtnContinuar.IsEnabled = false;
        Solicitud solicitud;
        try
        {
            solicitud = DatabaseService.CrearSolicitud(departamento.Id, solicitante.Id,
                (CboProyecto.SelectedItem as Proyecto)?.Id ?? 0, "Catálogo", TxtNotas.Text,
                articulosSolicitud, tipo, tiempoAproxRespuesta: CboTiempo.Text.Trim(),
                notificarCorreo: ChkNotificar.IsChecked == true, usaPartidas: usaPartidas);
        }
        catch (Exception ex)
        {
            TxtError.Text = ex.Message;
            BtnContinuar.IsEnabled = true;
            return;
        }
        Guardada?.Invoke(solicitud);
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        if (PasoArticulos.Visibility == Visibility.Visible &&
            _partidas.Any(p => p.Articulos.Count > 0 || !string.IsNullOrWhiteSpace(p.Articulo.Text) ||
                !string.IsNullOrWhiteSpace(p.Nombre.Text)) &&
            MessageBox.Show(Window.GetWindow(this), "¿Descartar la solicitud en captura?", "Cancelar solicitud",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        Cancelada?.Invoke();
    }

    public void MostrarArticulos()
    {
        PasoDatos.Visibility = Visibility.Collapsed;
        PasoArticulos.Visibility = Visibility.Visible;
        BtnCrearPartida.Visibility = _usaPartidas == true ? Visibility.Visible : Visibility.Collapsed;
        TituloPaso.Text = "Artículos de la nueva solicitud";
        BtnContinuar.Content = "Guardar solicitud";
        TxtError.Text = string.Empty;
        AreaCaptura.ScrollToTop();
        Loaded += (_, _) =>
        {
            if (_partidas.Count == 0) BtnCrearPartida.Focus();
            else _partidas[0].Articulo.Focus();
        };
    }

    private void CrearPartida_Click(object sender, RoutedEventArgs e)
    {
        var partida = CrearPartida();
        partida.Panel.BringIntoView();
        partida.Nombre.Focus();
    }

    private CapturaPartida CrearPartida()
    {
        var partida = new CapturaPartida();
        _partidas.Add(partida);
        var borde = new Border
        {
            Child = partida.Panel, Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12)
        };
        borde.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        borde.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        PnlArticulos.Children.Add(borde);

        if (_usaPartidas == true)
        {
            var cabecera = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var quitar = new Boton { Content = "Quitar partida", Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(quitar, Dock.Right);
            quitar.Click += (_, _) =>
            {
                if ((partida.Articulos.Count > 0 || !string.IsNullOrWhiteSpace(partida.Articulo.Text)) &&
                    MessageBox.Show(Window.GetWindow(this), "¿Quitar la partida y sus artículos?", "Quitar partida",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                _partidas.Remove(partida);
                PnlArticulos.Children.Remove(borde);
                TxtError.Text = string.Empty;
            };
            cabecera.Children.Add(quitar);
            cabecera.Children.Add(partida.Nombre);
            partida.Panel.Children.Add(cabecera);
        }

        partida.Panel.Children.Add(partida.Lista);
        var agregar = new Boton { Content = "+ Agregar artículo", VerticalAlignment = VerticalAlignment.Bottom };
        agregar.Click += (_, _) => AgregarArticulo(partida);
        partida.Panel.Children.Add(FilaArticulo(
            CampoConEtiqueta("Artículo", partida.Articulo),
            CampoConEtiqueta("Unidad", partida.Unidad),
            CampoConEtiqueta("Cantidad", partida.Cantidad), agregar));
        partida.Panel.Children.Add(partida.Sugerencias);

        partida.Articulo.TextChanged += (_, _) => BuscarProductos(partida);
        partida.Articulo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && partida.Sugerencias.Items.Count > 0)
            {
                partida.Sugerencias.SelectedIndex = 0;
                if (partida.Sugerencias.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem fila)
                    fila.Focus();
                else
                    partida.Sugerencias.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                partida.Unidad.Focus();
                e.Handled = true;
            }
        };
        partida.Sugerencias.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject origen &&
                ItemsControl.ContainerFromElement(partida.Sugerencias, origen) is ListBoxItem fila &&
                fila.Content is Producto producto)
            {
                partida.Sugerencias.SelectedItem = producto;
                SeleccionarProducto(partida);
                e.Handled = true;
            }
        };
        partida.Sugerencias.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            SeleccionarProducto(partida);
            e.Handled = true;
        };
        partida.Cantidad.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AgregarArticulo(partida);
            e.Handled = true;
        };
        return partida;
    }

    private static StackPanel CampoConEtiqueta(string etiqueta, Control campo)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = etiqueta, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(campo);
        return panel;
    }

    private static Grid FilaArticulo(params UIElement[] celdas)
    {
        var fila = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        double[] anchos = { 1, 115, 75, 140 };
        for (int i = 0; i < celdas.Length; i++)
        {
            fila.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(anchos[i], i == 0 ? GridUnitType.Star : GridUnitType.Pixel)
            });
            Grid.SetColumn(celdas[i], i);
            if (celdas[i] is FrameworkElement celda)
                celda.Margin = new Thickness(0, 0, i == celdas.Length - 1 ? 0 : 8, 0);
            fila.Children.Add(celdas[i]);
        }
        return fila;
    }

    private void BuscarProductos(CapturaPartida partida)
    {
        string texto = partida.Articulo.Text.Trim();
        if (!string.Equals(partida.Producto?.Nombre, texto, StringComparison.Ordinal))
            partida.Producto = null;

        var coincidencias = partida.Producto == null && texto.Length >= 2
            ? _productos.Where(p => p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                                    p.CodigoInterno.Contains(texto, StringComparison.OrdinalIgnoreCase)).Take(6).ToList()
            : new List<Producto>();
        partida.Sugerencias.ItemsSource = coincidencias;
        partida.Sugerencias.Visibility = coincidencias.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SeleccionarProducto(CapturaPartida partida)
    {
        if (partida.Sugerencias.SelectedItem is not Producto producto) return;
        partida.Producto = producto;
        partida.Articulo.Text = producto.Nombre;
        partida.Unidad.Text = producto.UnidadCompra;
        partida.Sugerencias.Visibility = Visibility.Collapsed;
        partida.Unidad.Focus();
    }

    private bool AgregarArticulo(CapturaPartida partida)
    {
        string descripcion = partida.Articulo.Text.Trim();
        string unidad = partida.Unidad.Text.Trim();
        if (descripcion.Length == 0 || unidad.Length == 0 ||
            !decimal.TryParse(partida.Cantidad.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out decimal cantidad) || cantidad <= 0)
        {
            TxtError.Text = "Escribe el artículo, la unidad y una cantidad mayor que cero.";
            partida.Panel.BringIntoView();
            if (descripcion.Length == 0) partida.Articulo.Focus();
            else if (unidad.Length == 0) partida.Unidad.Focus();
            else partida.Cantidad.Focus();
            return false;
        }

        var producto = partida.Producto ?? _productos.FirstOrDefault(p =>
            string.Equals(p.CodigoInterno, descripcion, StringComparison.OrdinalIgnoreCase));
        partida.Articulos.Add(new SolicitudPartida
        {
            ProductoId = producto?.Id ?? 0,
            ProductoCodigo = producto?.CodigoInterno ?? "CONCEPTO",
            ProductoNombre = producto?.Nombre ?? descripcion,
            DescripcionLibre = producto == null ? descripcion : string.Empty,
            UnidadCompra = DatabaseService.NormalizarUnidadMedida(unidad),
            CantidadSolicitada = cantidad
        });
        partida.Articulo.Clear();
        partida.Unidad.Text = "Pieza";
        partida.Cantidad.Text = "1";
        TxtError.Text = string.Empty;
        MostrarArticulos(partida);
        partida.Articulo.Focus();
        return true;
    }

    private static void MostrarArticulos(CapturaPartida partida)
    {
        partida.Lista.Children.Clear();
        foreach (var articulo in partida.Articulos)
        {
            var quitar = new Boton { Content = "Quitar", HorizontalAlignment = HorizontalAlignment.Left };
            quitar.Click += (_, _) =>
            {
                partida.Articulos.Remove(articulo);
                MostrarArticulos(partida);
            };
            partida.Lista.Children.Add(FilaArticulo(
                new TextBlock { Text = articulo.ProductoNombre, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = articulo.UnidadCompra, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = $"{articulo.CantidadSolicitada:0.####}", VerticalAlignment = VerticalAlignment.Center },
                quitar));
        }
    }

    private sealed class CapturaPartida
    {
        public StackPanel Panel { get; } = new();
        public StackPanel Lista { get; } = new();
        public CampoTexto Nombre { get; } = new() { PlaceholderText = "Nombre de la partida, por ejemplo Eléctrico" };
        public CampoTexto Articulo { get; } = new() { PlaceholderText = "Nombre o código del artículo" };
        public ComboBox Unidad { get; } = new() { IsEditable = true, ItemsSource = DatabaseService.UnidadesMedidaEstandar, Text = "Pieza" };
        public CampoTexto Cantidad { get; } = new() { Text = "1" };
        public ListBox Sugerencias { get; } = new() { MaxHeight = 135, Visibility = Visibility.Collapsed };
        public List<SolicitudPartida> Articulos { get; } = new();
        public Producto? Producto { get; set; }
    }
}
