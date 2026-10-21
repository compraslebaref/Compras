using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;
using SistemaComprasAlmacen.Services;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SistemaComprasAlmacen;

public partial class MainWindow : FluentWindow
{
    private const int CatalogoTamanoLote = 48;
    private bool _sesionAdminActiva = false;
    private bool _actualizandoCombosBase = false;
    private string? _filtroFechaAgendaDashboard = null;
    private bool _sincronizandoProductoAnalisis = false;
    private Producto _productoEnEdicion = new();
    private List<Producto> _cacheProductos = new();
    private List<Producto> _catalogoFiltrado = new();
    private readonly ObservableCollection<Producto> _catalogoVisible = new();
    private readonly DispatcherTimer _timerBusquedaCatalogo;
    private int _proveedorEditandoId = 0;
    private int _proyectoEditandoId = 0;
    private int _encargadoEditandoId = 0;
    private int _departamentoEditandoId = 0;
    private int _herramientaEditandoId = 0;
    private int _categoriaEditandoId = 0;
    private int _solicitudEditandoId = 0;
    private bool _sincronizandoSolicitudes = false;
    private bool _cargandoDetalleComparativa = false;
    private bool _actualizandoPreciosIvaComp = false;
    private readonly List<(Producto producto, decimal cantidad)> _carritoTienda = new();
    private readonly ObservableCollection<SolicitudPartida> _partidasNuevaSolicitud = new();
    private readonly ObservableCollection<CotizacionRenglonComparativo> _renglonesComparativa = new();
    private readonly ObservableCollection<SalidaPartida> _partidasSalidaPos = new();
    private readonly ObservableCollection<PartidaCompraSinFactura> _partidasCompraSinFactura = new();
    private List<PrestamoVistaFila> _todosPrestamosFilas = new();
    private List<MovimientoAlmacenFila> _todosMovimientosAlmacen = new();
    private FacturaXml? _facturaXmlEnPantalla;
    private bool _actualizandoCombosXml = false;
    private bool _modoAltaNuevoProducto = false;
    private readonly Dictionary<ComboBox, string> _placeholdersCombos = new();
    private readonly Dictionary<ComboBox, TextBlock> _textBlocksPlaceholderCombos = new();

    public class PrestamoVistaFila
    {
        public int PrestamoId { get; set; }
        public int PrestamoPartidaId { get; set; }
        public int HerramientaId { get; set; }
        public string FolioVale { get; set; } = string.Empty;
        public string CodigoIndividual { get; set; } = string.Empty;
        public string HerramientaDescripcion { get; set; } = string.Empty;
        public string DepartamentoNombre { get; set; } = string.Empty;
        public string EncargadoNombre { get; set; } = string.Empty;
        public string ProyectoNombre { get; set; } = string.Empty;
        public string EstadoDevolucion { get; set; } = string.Empty;
        public PrestamoHerramienta PrestamoOriginal { get; set; } = null!;
    }

    public class MovimientoAlmacenFila
    {
        public string Folio { get; set; } = string.Empty;
        public string TipoMovimiento { get; set; } = string.Empty;
        public string Fecha { get; set; } = string.Empty;
        public string ResponsableOProveedor { get; set; } = string.Empty;
        public string ProyectoNombre { get; set; } = string.Empty;
        public string DetalleResumen { get; set; } = string.Empty;
        public EntradaAlmacen? EntradaOriginal { get; set; }
        public SalidaAlmacen? SalidaOriginal { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();

        // Fijar tema claro (Blanco) permanentemente
        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, false);

        IcCatalogoVisual.ItemsSource = _catalogoVisible;
        DgPartidasNuevaSolicitud.ItemsSource = _partidasNuevaSolicitud;
        DgComparativa.ItemsSource = _renglonesComparativa;
        DgPartidasSalidaPos.ItemsSource = _partidasSalidaPos;
        DgPartidasCompraSinFactura.ItemsSource = _partidasCompraSinFactura;

        if (DpEventoFecha != null) DpEventoFecha.SelectedDate = DateTime.Today;
        if (DpPreFechaCompromiso != null) DpPreFechaCompromiso.SelectedDate = DateTime.Today.AddDays(7);

        _timerBusquedaCatalogo = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _timerBusquedaCatalogo.Tick += (_, _) =>
        {
            _timerBusquedaCatalogo.Stop();
            RefrescarCatalogoVisual();
        };

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += async (_, _) =>
        {
            await EjecutarConIndicadorCargaAsync("Cargando sistema y catálogos...", () =>
            {
                ConfigurarPlaceholdersInicialesCombos();
                CargarTodoElSistema();
                PonerEditorProductoEnReposo();
                IniciarNuevaSolicitudVacia();
                PonerFormulariosDirectoriosEnBlanco();
            });
            TxtOmniScanner.Focus();
        };
    }

    private void ConfigurarPlaceholdersInicialesCombos()
    {
        // Tab 1: Catálogo
        EstablecerPlaceholderCombo(CboProdCategoria, "Seleccione la categoría del artículo...");
        EstablecerPlaceholderCombo(CboProdFamilia, "Seleccione o escriba familia (ej. JABON, MICROFIBRA)...");

        // Tab 2: Solicitudes
        EstablecerPlaceholderCombo(CboSolDepartamento, "Seleccione departamento solicitante...");
        EstablecerPlaceholderCombo(CboSolEncargado, "Seleccione primero un departamento...");
        EstablecerPlaceholderCombo(CboSolProyecto, "Seleccione proyecto o área destino...");
        EstablecerPlaceholderCombo(CboSolProductoAgregar, "Busque arriba o despliegue para elegir artículo del catálogo...");

        // Tab 3: Comparativa
        EstablecerPlaceholderCombo(CboCompSolicitud, "Seleccione una Solicitud / Carrito para cotizar...");
        EstablecerPlaceholderCombo(CboCompReferenciasSimilares, "Seleccione una compra previa similar si desea aplicarla...");
        EstablecerPlaceholderCombo(CboCompProv1, "Seleccione proveedor para Opción 1...");
        EstablecerPlaceholderCombo(CboCompProv2, "Seleccione proveedor para Opción 2...");
        EstablecerPlaceholderCombo(CboCompProv3, "Seleccione proveedor para Opción 3...");

        // Tab 5: Almacén POS y Préstamos
        EstablecerPlaceholderCombo(CboSalDepartamento, "Seleccione depto. que aprueba...");
        EstablecerPlaceholderCombo(CboSalEncargado, "Seleccione o escriba quién recibe...");
        EstablecerPlaceholderCombo(CboSalProyecto, "Seleccione proyecto destino...");
        EstablecerPlaceholderCombo(CboSalProductoPos, "Escanee código o seleccione material de almacén...");
        EstablecerPlaceholderCombo(CboPreDepartamento, "Seleccione depto. que aprueba...");
        EstablecerPlaceholderCombo(CboPrePersonaRecibe, "Seleccione o escriba quién recibe...");
        EstablecerPlaceholderCombo(CboPreProyecto, "Seleccione proyecto destino...");
        EstablecerPlaceholderCombo(CboHerramientaDisponible, "Seleccione herramienta disponible (HER-xxxx)...");

        // Tab 6: Facturas XML y Compras sin Factura
        EstablecerPlaceholderCombo(CboXmlProyecto, "Seleccione proyecto destino...");
        EstablecerPlaceholderCombo(CboXmlOrdenCompra, "Seleccione OC o crear nueva al cargar XML...");
        EstablecerPlaceholderCombo(CboXmlVincularProducto, "Seleccione producto del catálogo para vincular...");
        EstablecerPlaceholderCombo(CboSinFacProveedor, "Seleccione o escriba el proveedor de la nota/ticket...");
        EstablecerPlaceholderCombo(CboSinFacProyecto, "Seleccione proyecto destino o Stock General...");
        EstablecerPlaceholderCombo(CboSinFacProducto, "Busque arriba o seleccione el artículo comprado...");

        // Tab 7: Análisis
        EstablecerPlaceholderCombo(CboProductoAnalisis, "Busque o seleccione un producto para graficar su historial...");

        // Tab 8: Directorios
        EstablecerPlaceholderCombo(CboProyEstatus, "Seleccione estatus (Activo / Pausado / Cerrado)...");
        EstablecerPlaceholderCombo(CboAltaEncargadoDepto, "Seleccione departamento al que pertenece...");
        EstablecerPlaceholderCombo(CboDirHerProducto, "Seleccione producto base (herramienta)...");
        EstablecerPlaceholderCombo(CboDirHerEstado, "Seleccione estado físico de la herramienta...");
    }

    private void EstablecerPlaceholderCombo(ComboBox? combo, string placeholder)
    {
        if (combo == null) return;

        bool yaRegistrado = _placeholdersCombos.ContainsKey(combo);
        _placeholdersCombos[combo] = placeholder;

        if (_textBlocksPlaceholderCombos.TryGetValue(combo, out var tbExistente))
        {
            tbExistente.Text = placeholder;
            ActualizarVisibilidadPlaceholderCombo(combo);
            return;
        }

        if (!yaRegistrado)
        {
            combo.Loaded += (_, _) => AsegurarTextBlockPlaceholderEnCombo(combo);
            combo.SelectionChanged += (_, _) => ActualizarVisibilidadPlaceholderCombo(combo);
            combo.DropDownOpened += (_, _) => ActualizarVisibilidadPlaceholderCombo(combo);
            combo.DropDownClosed += (_, _) => ActualizarVisibilidadPlaceholderCombo(combo);
            combo.GotKeyboardFocus += (_, _) => ActualizarVisibilidadPlaceholderCombo(combo);
            combo.LostKeyboardFocus += (_, _) => ActualizarVisibilidadPlaceholderCombo(combo);
            combo.IsVisibleChanged += (_, _) =>
            {
                if (combo.IsVisible) AsegurarTextBlockPlaceholderEnCombo(combo);
            };
            combo.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((_, _) => ActualizarVisibilidadPlaceholderCombo(combo)));
        }

        if (combo.IsLoaded)
        {
            AsegurarTextBlockPlaceholderEnCombo(combo);
        }
    }

    private void AsegurarTextBlockPlaceholderEnCombo(ComboBox combo)
    {
        if (!_textBlocksPlaceholderCombos.TryGetValue(combo, out var tb))
        {
            combo.ApplyTemplate();
            var grid = BuscarPrimerGridEnVisualTree(combo);
            if (grid == null) return;

            tb = new TextBlock
            {
                Text = _placeholdersCombos.GetValueOrDefault(combo, string.Empty),
                Foreground = new SolidColorBrush(Color.FromRgb(125, 135, 150)),
                FontStyle = FontStyles.Italic,
                FontSize = combo.FontSize > 0 ? Math.Min(12, combo.FontSize) : 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(11, 0, 30, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };

            Grid.SetColumn(tb, 0);
            if (grid.ColumnDefinitions.Count > 1)
                Grid.SetColumnSpan(tb, grid.ColumnDefinitions.Count - 1);
            Panel.SetZIndex(tb, 50);

            grid.Children.Add(tb);
            _textBlocksPlaceholderCombos[combo] = tb;
        }
        else if (VisualTreeHelper.GetParent(tb) == null)
        {
            _textBlocksPlaceholderCombos.Remove(combo);
            AsegurarTextBlockPlaceholderEnCombo(combo);
            return;
        }

        tb.Text = _placeholdersCombos.GetValueOrDefault(combo, string.Empty);
        ActualizarVisibilidadPlaceholderCombo(combo);
    }

    private static Grid? BuscarPrimerGridEnVisualTree(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Grid g)
                return g;
            var sub = BuscarPrimerGridEnVisualTree(child);
            if (sub != null)
                return sub;
        }
        return null;
    }

    private void ActualizarVisibilidadPlaceholderCombo(ComboBox? combo)
    {
        if (combo == null || !_textBlocksPlaceholderCombos.TryGetValue(combo, out var tb))
            return;

        bool sinItem = combo.SelectedItem == null && combo.SelectedIndex < 0;
        bool sinTexto = string.IsNullOrWhiteSpace(combo.Text);
        bool focoEnEditable = combo.IsEditable && combo.IsKeyboardFocusWithin;

        tb.Visibility = (sinItem && sinTexto && !focoEnEditable)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task EjecutarConIndicadorCargaAsync(string mensaje, Action accion)
    {
        try
        {
            TxtMensajeCargando.Text = mensaje;
            OverlayCargando.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = Cursors.Wait;
            await Task.Delay(25);
            accion();
        }
        finally
        {
            OverlayCargando.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            TxtOmniScanner.Focus();
            TxtOmniScanner.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && MainTabControl.SelectedIndex == 1)
        {
            if (Clipboard.ContainsImage())
            {
                PegarFotoEnProductoActual();
                e.Handled = true;
            }
        }
    }

    private void CargarTodoElSistema(bool conservarPosicionCatalogo = false)
    {
        CargarCombosBase();
        RefrescarDashboard();
        RefrescarCatalogoVisual(conservarPosicionCatalogo);
        RefrescarSolicitudes();
        RefrescarOrdenesYEntradas();
        RefrescarAlmacenPosYPrestamos();
        RefrescarFacturasXml();
        RefrescarAnalisis();
        RefrescarDirectorios();
    }

    private void CargarCombosBase()
    {
        _actualizandoCombosBase = true;
        try
        {
            var deptos = DatabaseService.ObtenerDepartamentos();
            var proyectos = DatabaseService.ObtenerProyectos();
            var categorias = DatabaseService.ObtenerCategorias();
            var proveedores = DatabaseService.ObtenerProveedores();
            _cacheProductos = DatabaseService.ObtenerProductos();
            var productos = _cacheProductos;

            // Filtro categorías en catálogo
            int catFiltroPrevio = (CboFiltroCategoria.SelectedItem as Categoria)?.Id ?? 0;
            var catsConTodas = new List<Categoria> { new() { Id = 0, Nombre = "Todas las categorías" } };
            catsConTodas.AddRange(categorias);
            CboFiltroCategoria.ItemsSource = catsConTodas;
            CboFiltroCategoria.SelectedItem = catsConTodas.FirstOrDefault(c => c.Id == catFiltroPrevio) ?? catsConTodas[0];

            int catProdPrevia = (CboProdCategoria.SelectedItem as Categoria)?.Id ?? 0;
            CboProdCategoria.ItemsSource = categorias;
            CboProdCategoria.SelectedItem = catProdPrevia > 0 ? categorias.FirstOrDefault(c => c.Id == catProdPrevia) : null;

            // Familias / Agrupadores genéricos de productos
            var familias = DatabaseService.ObtenerFamiliasProductos();
            if (CboFiltroFamilia != null)
            {
                string famFiltroPrevia = CboFiltroFamilia.SelectedItem as string ?? "Todas las familias";
                var famsConTodas = new List<string> { "Todas las familias" };
                famsConTodas.AddRange(familias);
                CboFiltroFamilia.ItemsSource = famsConTodas;
                CboFiltroFamilia.SelectedItem = famsConTodas.FirstOrDefault(f => f.Equals(famFiltroPrevia, StringComparison.OrdinalIgnoreCase)) ?? famsConTodas[0];
            }
            if (CboProdFamilia != null)
            {
                string textoFamActual = CboProdFamilia.Text;
                CboProdFamilia.ItemsSource = familias;
                CboProdFamilia.SelectedIndex = -1;
                CboProdFamilia.Text = textoFamActual;
            }

            // Solicitudes (en blanco por defecto si no se está editando)
            int solDepPrev = (CboSolDepartamento.SelectedItem as Departamento)?.Id ?? 0;
            int solProyPrev = (CboSolProyecto.SelectedItem as Proyecto)?.Id ?? 0;
            CboSolDepartamento.ItemsSource = deptos;
            CboSolDepartamento.SelectedItem = solDepPrev > 0 ? deptos.FirstOrDefault(d => d.Id == solDepPrev) : null;
            CboSolProyecto.ItemsSource = proyectos;
            CboSolProyecto.SelectedItem = solProyPrev > 0 ? proyectos.FirstOrDefault(p => p.Id == solProyPrev) : null;
            FiltrarComboProductoSolicitud();

            // Comparativa proveedores (en blanco por defecto si no hay opción asignada)
            CboCompProv1.ItemsSource = proveedores;
            CboCompProv2.ItemsSource = proveedores;
            CboCompProv3.ItemsSource = proveedores;

            // Almacén POS (Salida de Material y Préstamo de Herramienta - en blanco por defecto)
            int salDepPrev = (CboSalDepartamento.SelectedItem as Departamento)?.Id ?? 0;
            int salProyPrev = (CboSalProyecto.SelectedItem as Proyecto)?.Id ?? 0;
            int salProdPrev = (CboSalProductoPos.SelectedItem as Producto)?.Id ?? 0;
            CboSalDepartamento.ItemsSource = deptos;
            CboSalDepartamento.SelectedItem = salDepPrev > 0 ? deptos.FirstOrDefault(d => d.Id == salDepPrev) : null;
            CboSalProyecto.ItemsSource = proyectos;
            CboSalProyecto.SelectedItem = salProyPrev > 0 ? proyectos.FirstOrDefault(p => p.Id == salProyPrev) : null;
            var prodsNoHer = productos.Where(p => !p.EsHerramienta).ToList();
            CboSalProductoPos.ItemsSource = prodsNoHer;
            CboSalProductoPos.SelectedItem = salProdPrev > 0 ? prodsNoHer.FirstOrDefault(p => p.Id == salProdPrev) : null;

            if (CboPreDepartamento != null)
            {
                int preDepPrev = (CboPreDepartamento.SelectedItem as Departamento)?.Id ?? 0;
                CboPreDepartamento.ItemsSource = deptos;
                CboPreDepartamento.SelectedItem = preDepPrev > 0 ? deptos.FirstOrDefault(d => d.Id == preDepPrev) : null;
            }
            if (CboPreProyecto != null)
            {
                int preProyPrev = (CboPreProyecto.SelectedItem as Proyecto)?.Id ?? 0;
                CboPreProyecto.ItemsSource = proyectos;
                CboPreProyecto.SelectedItem = preProyPrev > 0 ? proyectos.FirstOrDefault(p => p.Id == preProyPrev) : null;
            }

            // Compras sin Factura (Nota / Ticket - en blanco por defecto)
            if (CboSinFacProveedor != null)
            {
                int sfProvPrev = (CboSinFacProveedor.SelectedItem as Proveedor)?.Id ?? 0;
                string sfProvTxt = CboSinFacProveedor.Text;
                CboSinFacProveedor.ItemsSource = proveedores;
                CboSinFacProveedor.SelectedItem = sfProvPrev > 0 ? proveedores.FirstOrDefault(p => p.Id == sfProvPrev) : null;
                if (sfProvPrev == 0) CboSinFacProveedor.Text = sfProvTxt;
            }
            if (CboSinFacProyecto != null)
            {
                int sfProyPrev = (CboSinFacProyecto.SelectedItem as Proyecto)?.Id ?? 0;
                CboSinFacProyecto.ItemsSource = proyectos;
                CboSinFacProyecto.SelectedItem = sfProyPrev > 0 ? proyectos.FirstOrDefault(p => p.Id == sfProyPrev) : null;
            }
            FiltrarComboProductoSinFactura();
            FiltrarComboProductoAnalisis();

            // XML y Directorios (en blanco por defecto cuando no se está editando)
            int xmlProdPrev = (CboXmlVincularProducto.SelectedItem as Producto)?.Id ?? 0;
            CboXmlVincularProducto.ItemsSource = productos;
            CboXmlVincularProducto.SelectedItem = xmlProdPrev > 0 ? productos.FirstOrDefault(p => p.Id == xmlProdPrev) : null;

            int encDepPrev = (CboAltaEncargadoDepto.SelectedItem as Departamento)?.Id ?? 0;
            CboAltaEncargadoDepto.ItemsSource = deptos;
            CboAltaEncargadoDepto.SelectedItem = (_encargadoEditandoId > 0 && encDepPrev > 0) ? deptos.FirstOrDefault(d => d.Id == encDepPrev) : null;

            int dirHerProdPrev = (CboDirHerProducto.SelectedItem as Producto)?.Id ?? 0;
            var prodsHer = productos.Where(p => p.EsHerramienta).ToList();
            CboDirHerProducto.ItemsSource = prodsHer;
            CboDirHerProducto.SelectedItem = (_herramientaEditandoId > 0 && dirHerProdPrev > 0) ? prodsHer.FirstOrDefault(p => p.Id == dirHerProdPrev) : null;
        }
        finally
        {
            _actualizandoCombosBase = false;
        }
    }

    // ==================== NAVEGACIÓN Y PERFIL ADMINISTRADOR ====================

    private void CambiarSeccion(int index)
    {
        MainTabControl.SelectedIndex = index;
        var botones = new[] { NavDashboard, NavCatalogo, NavSolicitudes, NavComparativa, NavOrdenes, NavAlmacenPos, NavFacturasXml, NavAnalisis, NavDirectorios };
        for (int i = 0; i < botones.Length; i++)
        {
            botones[i].Appearance = i == index ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int idx))
        {
            CambiarSeccion(idx);
        }
    }

    private void BtnIrRecepciones_Click(object sender, RoutedEventArgs e) => CambiarSeccion(4);
    private void BtnIrAlmacenPos_Click(object sender, RoutedEventArgs e) => CambiarSeccion(5);

    private void BtnPerfilAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (_sesionAdminActiva)
        {
            var resp = System.Windows.MessageBox.Show(this,
                "La sesión de Perfil Administrador se encuentra ACTIVA (desbloqueada).\n\n¿Desea bloquear nuevamente la sesión de Administrador para que vuelva a solicitar el PIN en acciones críticas?",
                "Perfil Administrador Activo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp == System.Windows.MessageBoxResult.Yes)
            {
                _sesionAdminActiva = false;
                if (BtnPerfilAdmin != null)
                {
                    BtnPerfilAdmin.Content = "🔒 Perfil: Administrador";
                    BtnPerfilAdmin.Appearance = ControlAppearance.Secondary;
                }
            }
            return;
        }

        if (SolicitarAutorizacionAdmin("Activar sesión de Perfil Administrador", false))
        {
            _sesionAdminActiva = true;
            if (BtnPerfilAdmin != null)
            {
                BtnPerfilAdmin.Content = "🔓 Admin Activo";
                BtnPerfilAdmin.Appearance = ControlAppearance.Primary;
            }
        }
    }

    private void BtnCambiarPinAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (!SolicitarAutorizacionAdmin("Autorizar cambio de PIN de Administrador", false))
            return;

        var win = new Window
        {
            Title = "Cambiar PIN de Administrador",
            Width = 390,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "🔑 Definir Nuevo PIN de Administrador",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Ingrese la nueva clave o PIN (mínimo 4 caracteres):",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var pwdBox = new System.Windows.Controls.PasswordBox
        {
            FontSize = 15,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(pwdBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button
        {
            Content = "Cancelar",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnSave = new System.Windows.Controls.Button
        {
            Content = "Guardar Nuevo PIN",
            Padding = new Thickness(14, 6, 14, 6),
            FontWeight = FontWeights.SemiBold
        };

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(pwdBox.Password) || pwdBox.Password.Trim().Length < 4)
            {
                System.Windows.MessageBox.Show(win, "El PIN debe tener al menos 4 caracteres.", "PIN inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DatabaseService.CambiarPinAdministrador(pwdBox.Password.Trim());
            DatabaseService.RegistrarBitacoraAdmin("Cambio de PIN", "ConfiguracionSistema", "El Administrador actualizó el PIN de seguridad.");
            win.DialogResult = true;
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnSave);
        root.Children.Add(btnPanel);
        win.Content = root;
        win.Loaded += (_, _) => pwdBox.Focus();

        if (win.ShowDialog() == true)
        {
            System.Windows.MessageBox.Show(this, "El PIN de Administrador se actualizó correctamente.", "PIN Actualizado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool SolicitarAutorizacionAdmin(string accionDescripcion, bool permitirSesionActiva = true)
    {
        if (permitirSesionActiva && _sesionAdminActiva)
        {
            DatabaseService.RegistrarBitacoraAdmin(accionDescripcion, "Sesión Admin Activa", "Autorizado con sesión de Administrador desbloqueada.");
            return true;
        }

        var win = new Window
        {
            Title = "Autorización de Administrador Requerida",
            Width = 420,
            Height = 275,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "🔒 Clave de Administrador",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 58, 138)),
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Acción: {accionDescripcion}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Ingrese el PIN de Administrador para continuar (PIN inicial por defecto: 1234):",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var pwdBox = new System.Windows.Controls.PasswordBox
        {
            FontSize = 16,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(pwdBox);

        var chkMantener = new CheckBox
        {
            Content = "Mantener sesión de Administrador desbloqueada durante esta sesión",
            FontSize = 11.5,
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(chkMantener);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button
        {
            Content = "Cancelar",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnOk = new System.Windows.Controls.Button
        {
            Content = "Autorizar",
            Padding = new Thickness(16, 6, 16, 6),
            FontWeight = FontWeights.Bold,
            IsDefault = true
        };

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click += (_, _) =>
        {
            if (!DatabaseService.ValidarPinAdministrador(pwdBox.Password))
            {
                System.Windows.MessageBox.Show(win, "PIN de Administrador incorrecto.", "Acceso denegado", MessageBoxButton.OK, MessageBoxImage.Error);
                pwdBox.Clear();
                pwdBox.Focus();
                return;
            }

            if (chkMantener.IsChecked == true)
            {
                _sesionAdminActiva = true;
                if (BtnPerfilAdmin != null)
                {
                    BtnPerfilAdmin.Content = "🔓 Admin Activo";
                    BtnPerfilAdmin.Appearance = ControlAppearance.Primary;
                }
            }

            DatabaseService.RegistrarBitacoraAdmin(accionDescripcion, "PIN Admin", "Autorizado mediante PIN de Administrador.");
            win.DialogResult = true;
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);
        win.Content = root;
        win.Loaded += (_, _) => pwdBox.Focus();

        return win.ShowDialog() == true;
    }

    // ==================== OMNIBARRA DE ESCANEO INTELIGENTE (DATAMAX S30W) ====================

    private void TxtOmniScanner_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ProcesarCodigoEscaneado(TxtOmniScanner.Text);
            e.Handled = true;
        }
    }

    private void BtnProcesarOmniScanner_Click(object sender, RoutedEventArgs e)
    {
        ProcesarCodigoEscaneado(TxtOmniScanner.Text);
    }

    private static string NormalizarCodigoEscaneado(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return string.Empty;

        string s = rawInput.Trim().ToUpperInvariant();
        // Las pistolas de código de barras en teclado Español/Latinoamérica suelen enviar ' o / o ? o _ en lugar de -
        s = s.Replace('\'', '-')
             .Replace('’', '-')
             .Replace('´', '-')
             .Replace('`', '-')
             .Replace('/', '-')
             .Replace('?', '-')
             .Replace('_', '-');

        while (s.Contains("--"))
            s = s.Replace("--", "-");

        return s;
    }

    private static bool CoincideFolioFlexible(string folioReal, string codigoBuscado)
    {
        if (string.IsNullOrWhiteSpace(folioReal) || string.IsNullOrWhiteSpace(codigoBuscado))
            return false;

        string f = NormalizarCodigoEscaneado(folioReal);
        string c = NormalizarCodigoEscaneado(codigoBuscado);

        if (f.Equals(c, StringComparison.OrdinalIgnoreCase))
            return true;

        // Permitir buscar "ENT-0001" o "ENT-1" y que coincida con "ENT-2026-0001"
        var partesF = f.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var partesC = c.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (partesF.Length >= 2 && partesC.Length >= 2 &&
            partesF[0].Equals(partesC[0], StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(partesF[^1], out int numF) && int.TryParse(partesC[^1], out int numC) && numF == numC)
            {
                if (partesC.Length == 2 || partesF[1].Equals(partesC[1], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool EsPrefijoDocumento(string code)
    {
        return code.StartsWith("ENT-") ||
               code.StartsWith("SAL-") ||
               code.StartsWith("OC-") ||
               code.StartsWith("PRE-") ||
               code.StartsWith("HER-") ||
               code.StartsWith("SOL-");
    }

    private void ProcesarCodigoEscaneado(string? rawInput)
    {
        string code = NormalizarCodigoEscaneado(rawInput);
        if (string.IsNullOrWhiteSpace(code))
            return;

        TxtOmniScanner.Clear();
        TxtUltimoEscaneo.Text = $"Último escaneo: {code}";

        // 1. ¿Es el folio de una Orden de Compra (OC-xxxx)? -> Abrir recepción parcial/total de esa OC
        if (code.StartsWith("OC-"))
        {
            var ocs = DatabaseService.ObtenerOrdenesCompra();
            var oc = ocs.FirstOrDefault(o => CoincideFolioFlexible(o.Folio, code));
            if (oc != null)
            {
                CambiarSeccion(4);
                DgOrdenesCompra.SelectedItem = DgOrdenesCompra.Items.Cast<OrdenCompra>().FirstOrDefault(x => x.Id == oc.Id);
                System.Windows.MessageBox.Show(this,
                    $"Orden de Compra {oc.Folio} ({oc.ProveedorNombre}) localizada.\nEstatus actual: {oc.Estatus}.\n\nLista para registrar recepción parcial o total en almacén.",
                    "Escaneo de Orden de Compra", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        // 2. ¿Es un Vale de Préstamo (PRE-xxxx) o Código Individual de Herramienta (HER-xxxx)?
        if (code.StartsWith("PRE-") || code.StartsWith("HER-"))
        {
            CambiarSeccion(5);
            RefrescarAlmacenPosYPrestamos();

            var filas = _todosPrestamosFilas.Count > 0
                ? _todosPrestamosFilas
                : DgPrestamosHerramientas.Items.Cast<PrestamoVistaFila>().ToList();

            var match = filas.FirstOrDefault(f =>
                (CoincideFolioFlexible(f.FolioVale, code) || CoincideFolioFlexible(f.CodigoIndividual, code)) &&
                f.EstadoDevolucion == "Pendiente")
                ?? filas.FirstOrDefault(f => CoincideFolioFlexible(f.FolioVale, code) || CoincideFolioFlexible(f.CodigoIndividual, code));

            if (match != null)
            {
                DgPrestamosHerramientas.SelectedItem = match;
                DgPrestamosHerramientas.ScrollIntoView(match);
                System.Windows.MessageBox.Show(this,
                    $"Resguardo localizado: {match.FolioVale}\nHerramienta: {match.CodigoIndividual} ({match.HerramientaDescripcion})\nEncargado: {match.EncargadoNombre}\n\nSeleccione el estado de retorno (Bueno / Dañado / Extraviado) y haga clic en 'Registrar Devolución'.",
                    "Escaneo de Herramienta / Resguardo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Si es una herramienta disponible, seleccionarla para prestarla
            var disp = CboHerramientaDisponible.Items.Cast<HerramientaActivo>().FirstOrDefault(h => CoincideFolioFlexible(h.CodigoIndividual, code));
            if (disp != null)
            {
                CboHerramientaDisponible.SelectedItem = disp;
                System.Windows.MessageBox.Show(this,
                    $"Herramienta {disp.CodigoIndividual} ({disp.ProductoNombre}) disponible y seleccionada para préstamo.",
                    "Herramienta Disponible", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        // 3. ¿Es una Solicitud de Compra (SOL-xxxx)? -> Abrir en Comparativa
        if (code.StartsWith("SOL-"))
        {
            var sols = DatabaseService.ObtenerSolicitudes();
            var sol = sols.FirstOrDefault(s => CoincideFolioFlexible(s.Folio, code));
            if (sol != null)
            {
                CambiarSeccion(3);
                CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(x => x.Id == sol.Id);
                return;
            }
        }

        // 4. ¿Es un Vale de Entrada (ENT-xxxx) o Vale de Salida (SAL-xxxx)? -> Localizar y abrir su vista previa impresa
        if (code.StartsWith("ENT-"))
        {
            var ent = DatabaseService.ObtenerEntradasAlmacen().FirstOrDefault(x => CoincideFolioFlexible(x.Folio, code));
            if (ent != null)
            {
                if (MainTabControl.SelectedIndex == 5)
                {
                    RefrescarAlmacenPosYPrestamos();
                    var filaMov = DgMovimientosAlmacen.Items.Cast<MovimientoAlmacenFila>().FirstOrDefault(m => m.TipoMovimiento == "ENTRADA" && m.Folio == ent.Folio);
                    if (filaMov != null)
                    {
                        DgMovimientosAlmacen.SelectedItem = filaMov;
                        DgMovimientosAlmacen.ScrollIntoView(filaMov);
                    }
                }
                else if (MainTabControl.SelectedIndex == 4)
                {
                    var filaEnt = DgEntradasHistorial.Items.Cast<EntradaAlmacen>().FirstOrDefault(x => x.Id == ent.Id);
                    if (filaEnt != null)
                    {
                        DgEntradasHistorial.SelectedItem = filaEnt;
                        DgEntradasHistorial.ScrollIntoView(filaEnt);
                    }
                }
                BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent);
                return;
            }
        }
        if (code.StartsWith("SAL-"))
        {
            var sal = DatabaseService.ObtenerSalidasAlmacen().FirstOrDefault(x => CoincideFolioFlexible(x.Folio, code));
            if (sal != null)
            {
                CambiarSeccion(5);
                RefrescarAlmacenPosYPrestamos();
                var filaMov = DgMovimientosAlmacen.Items.Cast<MovimientoAlmacenFila>().FirstOrDefault(m => m.TipoMovimiento == "SALIDA" && m.Folio == sal.Folio);
                if (filaMov != null)
                {
                    DgMovimientosAlmacen.SelectedItem = filaMov;
                    DgMovimientosAlmacen.ScrollIntoView(filaMov);
                }
                BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, sal);
                return;
            }
        }

        // 5. ¿Es un Producto del Catálogo (PRD-xxxx o Código de Barras EAN/UPC)?
        var prod = DatabaseService.BuscarProductoPorCodigo(code)
                   ?? (rawInput != null ? DatabaseService.BuscarProductoPorCodigo(rawInput.Trim()) : null);
        if (prod != null)
        {
            // Si estamos en Solicitudes (Tab 2), agregar directo al carrito/solicitud en edición
            if (MainTabControl.SelectedIndex == 2)
            {
                decimal cant = ParseDecimal(TxtSolCantidadAgregar.Text, 1m);
                AgregarProductoAPartidasSolicitud(prod, cant, "Escáner Código de Barras");
                return;
            }

            // Si estamos en Almacén POS (Tab 5), agregar directo como caja registradora
            if (MainTabControl.SelectedIndex == 5)
            {
                CboSalProductoPos.SelectedItem = CboSalProductoPos.Items.Cast<Producto>().FirstOrDefault(x => x.Id == prod.Id);
                AgregarProductoAValeSalidaPos(prod, 1m);
                return;
            }

            // Si estamos en Recepción de OC (Tab 4), incrementar cantidad recibida de ese producto
            if (MainTabControl.SelectedIndex == 4 && DgPartidasRecepcionOc.ItemsSource is IEnumerable<OrdenCompraPartida> listOc)
            {
                var part = listOc.FirstOrDefault(p => p.ProductoId == prod.Id);
                if (part != null)
                {
                    part.CantidadRecibirAhora += 1;
                    DgPartidasRecepcionOc.Items.Refresh();
                    return;
                }
            }

            // En cualquier otra pantalla, agregar al carrito y mostrar en catálogo
            AgregarAlCarritoTienda(prod, 1m);
            CargarProductoEnEditor(prod);
            return;
        }

        System.Windows.MessageBox.Show(this,
            $"No se encontró ningún documento o producto con el código '{code}'.\n\nFormatos soportados:\n• Entrada de Almacén: ENT-2026-0001\n• Salida de Almacén: SAL-2026-0001\n• Orden de Compra: OC-2026-0001\n• Préstamo / Herramienta: PRE-2026-0001 / HER-0001\n• Producto: Código de barras o PRD-0001",
            "Código no registrado", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ==================== TAB 0: DASHBOARD Y CALENDARIO DE EVENTOS ====================

    private void RefrescarDashboard()
    {
        var sols = DatabaseService.ObtenerSolicitudes();
        var ocs = DatabaseService.ObtenerOrdenesCompra();
        var prods = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        var her = DatabaseService.ObtenerHerramientas();

        KpiSolicitudes.Text = sols.Count(s => s.Estatus == "Pendiente").ToString();
        KpiOcsAbiertas.Text = ocs.Count(o => o.Estatus != "Recibida Completa" && o.Estatus != "Cancelada" && o.Estatus != "Cerrada Parcial Definitiva" && o.Estatus != "Cerrada Parcial (Admin)").ToString();
        KpiStockBajo.Text = prods.Count(p => p.StockBajo).ToString();
        KpiHerramientasPrestadas.Text = her.Count(h => h.Estado == "En Préstamo").ToString();
        KpiGastoTotal.Text = ocs.Where(o => o.Estatus != "Cancelada").Sum(o => o.TotalConIva).ToString("C2", CultureInfo.GetCultureInfo("es-MX"));

        DgDashboardOcs.ItemsSource = ocs.Where(o => o.Estatus != "Recibida Completa" && o.Estatus != "Cancelada" && o.Estatus != "Cerrada Parcial Definitiva" && o.Estatus != "Cerrada Parcial (Admin)").ToList();
        DgDashboardAlertas.ItemsSource = prods.Where(p => p.StockBajo).ToList();

        RefrescarCalendarioYAgendaDashboard();
    }

    private void RefrescarCalendarioYAgendaDashboard()
    {
        if (DgDashboardAgenda == null) return;
        var eventos = DatabaseService.ObtenerEventosCalendarioUnificados(_filtroFechaAgendaDashboard);
        DgDashboardAgenda.ItemsSource = eventos;
    }

    private void CalDashboard_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (CalDashboard.SelectedDate.HasValue)
        {
            _filtroFechaAgendaDashboard = CalDashboard.SelectedDate.Value.ToString("yyyy-MM-dd");
            if (DpEventoFecha != null)
                DpEventoFecha.SelectedDate = CalDashboard.SelectedDate.Value;
        }
        else
        {
            _filtroFechaAgendaDashboard = null;
        }
        RefrescarCalendarioYAgendaDashboard();
    }

    private void BtnVerTodasFechasCalendario_Click(object sender, RoutedEventArgs e)
    {
        _filtroFechaAgendaDashboard = null;
        if (CalDashboard != null) CalDashboard.SelectedDate = null;
        RefrescarCalendarioYAgendaDashboard();
    }

    private void BtnAgregarEventoCalendario_Click(object sender, RoutedEventArgs e)
    {
        string titulo = TxtEventoTitulo?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(titulo))
        {
            System.Windows.MessageBox.Show(this, "Escriba el título o descripción del recordatorio o evento.", "Falta descripción", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtEventoTitulo?.Focus();
            return;
        }

        string fecha = (DpEventoFecha?.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
        string categoria = (CboEventoCategoria?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Recordatorio";
        string prioridad = (CboEventoPrioridad?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Media";

        DatabaseService.GuardarEventoCalendario(new EventoCalendario
        {
            Fecha = fecha,
            Titulo = titulo,
            Categoria = categoria,
            Prioridad = prioridad
        });

        TxtEventoTitulo?.Clear();
        RefrescarCalendarioYAgendaDashboard();
    }

    private void BtnCompletarEventoCalendario_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EventoCalendario ev }) return;

        if (!ev.EsManual)
        {
            if (ev.ReferenciaFolio.StartsWith("OC-", StringComparison.OrdinalIgnoreCase))
            {
                ProcesarCodigoEscaneado(ev.ReferenciaFolio);
            }
            else if (ev.ReferenciaFolio.StartsWith("PRE-", StringComparison.OrdinalIgnoreCase))
            {
                ProcesarCodigoEscaneado(ev.ReferenciaFolio);
            }
            return;
        }

        DatabaseService.AlternarCompletadoEventoCalendario(ev.Id, !ev.Completado);
        RefrescarCalendarioYAgendaDashboard();
    }

    private void BtnEliminarEventoCalendario_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EventoCalendario ev }) return;

        if (!ev.EsManual)
        {
            System.Windows.MessageBox.Show(this, "Los eventos automáticos de Órdenes de Compra o Préstamos se actualizan automáticamente al recibir la OC o devolver la herramienta.", "Evento automático", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar recordatorio '{ev.Titulo}' del calendario"))
            return;

        DatabaseService.EliminarEventoCalendario(ev.Id);
        RefrescarCalendarioYAgendaDashboard();
    }

    // ==================== TAB 1: CATÁLOGO VISUAL Y EDITOR ====================

    private void RefrescarCatalogoVisual(bool conservarPosicion = false)
    {
        if (_cacheProductos.Count == 0)
        {
            _cacheProductos = DatabaseService.ObtenerProductos();
        }

        double offsetPrevio = SvCatalogoVisual?.VerticalOffset ?? 0;
        int cantidadPrevia = _catalogoVisible.Count;

        string filtroRaw = TxtBuscarCatalogo?.Text?.Trim() ?? string.Empty;
        string filtroNorm = NormalizarCodigoEscaneado(filtroRaw);

        // Si el usuario escaneó un folio de documento (ENT-, SAL-, OC-, PRE-, HER-, SOL-) en el buscador del catálogo y el código está completo
        if (filtroNorm.Length >= 8 && EsPrefijoDocumento(filtroNorm))
        {
            bool existeDoc =
                (filtroNorm.StartsWith("ENT-") && DatabaseService.ObtenerEntradasAlmacen().Any(x => x.Folio.Equals(filtroNorm, StringComparison.OrdinalIgnoreCase))) ||
                (filtroNorm.StartsWith("SAL-") && DatabaseService.ObtenerSalidasAlmacen().Any(x => x.Folio.Equals(filtroNorm, StringComparison.OrdinalIgnoreCase))) ||
                (filtroNorm.StartsWith("OC-") && DatabaseService.ObtenerOrdenesCompra().Any(x => x.Folio.Equals(filtroNorm, StringComparison.OrdinalIgnoreCase))) ||
                ((filtroNorm.StartsWith("PRE-") || filtroNorm.StartsWith("HER-")) && _todosPrestamosFilas.Any(x => x.FolioVale.Equals(filtroNorm, StringComparison.OrdinalIgnoreCase) || x.CodigoIndividual.Equals(filtroNorm, StringComparison.OrdinalIgnoreCase)));

            if (existeDoc)
            {
                TxtBuscarCatalogo!.Clear();
                ProcesarCodigoEscaneado(filtroNorm);
                return;
            }
        }

        int catId = (CboFiltroCategoria?.SelectedItem as Categoria)?.Id ?? 0;
        string famFiltro = CboFiltroFamilia?.SelectedItem as string ?? "Todas las familias";

        IEnumerable<Producto> query = _cacheProductos;
        if (catId > 0)
        {
            query = query.Where(p => p.CategoriaId == catId);
        }
        if (!string.IsNullOrWhiteSpace(famFiltro) && !famFiltro.Equals("Todas las familias", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => string.Equals(p.Familia, famFiltro, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filtroRaw))
        {
            query = query.Where(p =>
                p.Nombre.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Familia.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoInterno.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoInterno.Contains(filtroNorm, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoBarras.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoBarras.Contains(filtroNorm, StringComparison.OrdinalIgnoreCase) ||
                p.Marca.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Modelo.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Medidas.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.CapacidadElectrica.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Especificacion1.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Especificacion2.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.CategoriaNombre.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Descripcion.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase));
        }

        _catalogoFiltrado = query.ToList();
        ActualizarSeleccionCatalogo();

        if (conservarPosicion && _catalogoVisible.Count > 0)
        {
            int idxSel = _productoEnEdicion.Id > 0
                ? _catalogoFiltrado.FindIndex(p => p.Id == _productoEnEdicion.Id)
                : -1;
            int metaVisibles = Math.Min(
                _catalogoFiltrado.Count,
                Math.Max(Math.Max(CatalogoTamanoLote, cantidadPrevia), idxSel >= 0 ? idxSel + 12 : 0));

            bool mismosIdsEnCabeza = _catalogoVisible.Count <= metaVisibles &&
                                     _catalogoVisible.Select((p, idx) => _catalogoFiltrado[idx].Id == p.Id).All(x => x);

            if (mismosIdsEnCabeza)
            {
                for (int i = 0; i < _catalogoVisible.Count; i++)
                {
                    if (!ReferenceEquals(_catalogoVisible[i], _catalogoFiltrado[i]))
                    {
                        _catalogoVisible[i] = _catalogoFiltrado[i];
                    }
                }
                for (int i = _catalogoVisible.Count; i < metaVisibles; i++)
                {
                    _catalogoVisible.Add(_catalogoFiltrado[i]);
                }
            }
            else
            {
                _catalogoVisible.Clear();
                for (int i = 0; i < metaVisibles; i++)
                {
                    _catalogoVisible.Add(_catalogoFiltrado[i]);
                }
            }

            ActualizarTextoContadorCatalogo();
            SvCatalogoVisual?.ScrollToVerticalOffset(offsetPrevio);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                SvCatalogoVisual?.ScrollToVerticalOffset(offsetPrevio);
            }));
            return;
        }

        _catalogoVisible.Clear();
        int cantidadInicial = Math.Min(CatalogoTamanoLote, _catalogoFiltrado.Count);
        for (int i = 0; i < cantidadInicial; i++)
        {
            _catalogoVisible.Add(_catalogoFiltrado[i]);
        }

        SvCatalogoVisual?.ScrollToTop();
        ActualizarTextoContadorCatalogo();
    }

    private void CargarMasProductosCatalogo()
    {
        if (_catalogoVisible.Count >= _catalogoFiltrado.Count)
            return;

        int inicio = _catalogoVisible.Count;
        int fin = Math.Min(inicio + CatalogoTamanoLote, _catalogoFiltrado.Count);
        for (int i = inicio; i < fin; i++)
        {
            _catalogoVisible.Add(_catalogoFiltrado[i]);
        }

        ActualizarTextoContadorCatalogo();
    }

    private void ActualizarTextoContadorCatalogo()
    {
        if (TxtContadorCatalogo == null) return;
        TxtContadorCatalogo.Text = _catalogoVisible.Count < _catalogoFiltrado.Count
            ? $"{_catalogoVisible.Count} de {_catalogoFiltrado.Count} productos"
            : $"{_catalogoFiltrado.Count} productos";
    }

    private void ActualizarSeleccionCatalogo()
    {
        int idSel = _productoEnEdicion.Id;
        foreach (var item in _cacheProductos)
        {
            item.Seleccionado = idSel > 0 && item.Id == idSel;
        }
    }

    private void SvCatalogoVisual_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange <= 0) return;
        if (_catalogoVisible.Count < _catalogoFiltrado.Count &&
            e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 280)
        {
            CargarMasProductosCatalogo();
        }
    }

    private void TxtBuscarCatalogo_TextChanged(object sender, TextChangedEventArgs e)
    {
        _timerBusquedaCatalogo.Stop();
        _timerBusquedaCatalogo.Start();
    }

    private void TxtBuscarCatalogo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _timerBusquedaCatalogo.Stop();
            string raw = TxtBuscarCatalogo.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(raw);
            if (string.IsNullOrWhiteSpace(code))
                return;

            // Si es un documento (ENT-, SAL-, OC-, PRE-, HER-, SOL-), procesarlo con el escáner global
            if (EsPrefijoDocumento(code))
            {
                TxtBuscarCatalogo.Clear();
                ProcesarCodigoEscaneado(code);
                e.Handled = true;
                return;
            }

            // Si coincide exactamente con el código de barras o código interno de un producto, seleccionarlo y agregarlo
            var prodExacto = DatabaseService.BuscarProductoPorCodigo(code) ?? DatabaseService.BuscarProductoPorCodigo(raw);
            if (prodExacto != null)
            {
                RefrescarCatalogoVisual();
                CargarProductoEnEditor(prodExacto);
                e.Handled = true;
                return;
            }

            RefrescarCatalogoVisual();
            e.Handled = true;
        }
    }

    private void CboFiltroCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _actualizandoCombosBase) return;
        RefrescarCatalogoVisual();
    }

    private void CboFiltroFamilia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _actualizandoCombosBase) return;
        RefrescarCatalogoVisual();
    }

    private void BtnGestionarFamilias_Click(object sender, RoutedEventArgs e)
    {
        var familias = DatabaseService.ObtenerFamiliasProductos();
        var win = new Window
        {
            Title = "Gestión Manual de Familias / Agrupadores de Productos",
            Width = 540,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "🗂️ Crear, Renombrar o Unificar Familias de Productos",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Las Familias agrupan productos equivalentes de distintas marcas o presentaciones (ej. 'JABON', 'MICROFIBRA', 'GAS REFRIGERANTE') para comparar sus precios históricos juntos.",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        // Sección 1: Crear / Asignar nueva familia al producto actual en edición
        var bdrNueva = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spNueva = new StackPanel();
        spNueva.Children.Add(new TextBlock
        {
            Text = "1. Crear nueva Familia o asignar al producto actual:",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var gridNueva = new Grid();
        gridNueva.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridNueva.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtNuevaFam = new System.Windows.Controls.TextBox
        {
            Text = CboProdFamilia?.Text ?? string.Empty,
            FontSize = 12.5,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnUsarNueva = new System.Windows.Controls.Button
        {
            Content = "Asignar al Producto Actual",
            Padding = new Thickness(10, 4, 10, 4),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(txtNuevaFam, 0);
        Grid.SetColumn(btnUsarNueva, 1);
        gridNueva.Children.Add(txtNuevaFam);
        gridNueva.Children.Add(btnUsarNueva);
        spNueva.Children.Add(gridNueva);
        bdrNueva.Child = spNueva;
        root.Children.Add(bdrNueva);

        // Sección 2: Renombrar o Unificar una familia existente en todo el catálogo
        var bdrRenombrar = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var spRen = new StackPanel();
        spRen.Children.Add(new TextBlock
        {
            Text = "2. Renombrar o Unificar una Familia en todo el Catálogo:",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });
        spRen.Children.Add(new TextBlock
        {
            Text = "Familia actual a modificar:",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        });
        var cboFamOrigen = new System.Windows.Controls.ComboBox
        {
            ItemsSource = familias,
            SelectedItem = familias.FirstOrDefault(f => f.Equals(CboProdFamilia?.Text, StringComparison.OrdinalIgnoreCase)) ?? familias.FirstOrDefault(),
            Margin = new Thickness(0, 0, 0, 8)
        };
        spRen.Children.Add(cboFamOrigen);

        spRen.Children.Add(new TextBlock
        {
            Text = "Nuevo nombre de Familia (si escribe una existente, se unificarán):",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        });
        var gridRen = new Grid();
        gridRen.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridRen.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtFamDestino = new System.Windows.Controls.TextBox
        {
            Text = cboFamOrigen.SelectedItem as string ?? string.Empty,
            FontSize = 12.5,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cboFamOrigen.SelectionChanged += (_, _) =>
        {
            if (cboFamOrigen.SelectedItem is string s)
                txtFamDestino.Text = s;
        };
        var btnAplicarRenombre = new System.Windows.Controls.Button
        {
            Content = "Renombrar en Catálogo",
            Padding = new Thickness(10, 4, 10, 4),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(txtFamDestino, 0);
        Grid.SetColumn(btnAplicarRenombre, 1);
        gridRen.Children.Add(txtFamDestino);
        gridRen.Children.Add(btnAplicarRenombre);
        spRen.Children.Add(gridRen);
        bdrRenombrar.Child = spRen;
        root.Children.Add(bdrRenombrar);

        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = "Cerrar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 5, 16, 5)
        };
        btnCerrar.Click += (_, _) => win.Close();
        root.Children.Add(btnCerrar);

        btnUsarNueva.Click += (_, _) =>
        {
            string fam = txtNuevaFam.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(fam))
            {
                System.Windows.MessageBox.Show(win, "Escriba el nombre de la familia.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DatabaseService.GuardarFamiliaPersonalizada(fam);
            CargarCombosBase();
            if (CboProdFamilia != null)
            {
                CboProdFamilia.Text = fam;
            }
            if (_productoEnEdicion.Id > 0)
            {
                _productoEnEdicion.Familia = fam;
                DatabaseService.GuardarProducto(_productoEnEdicion);
                CargarTodoElSistema(conservarPosicionCatalogo: true);
                System.Windows.MessageBox.Show(win, $"Se guardó la familia '{fam}' y se asignó al producto {_productoEnEdicion.CodigoInterno}.", "Familia Guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(win, $"Se creó y guardó la familia '{fam}' en el catálogo de familias.", "Familia Creada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            win.Close();
        };

        btnAplicarRenombre.Click += (_, _) =>
        {
            string origen = (cboFamOrigen.SelectedItem as string)?.Trim() ?? string.Empty;
            string destino = txtFamDestino.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            {
                System.Windows.MessageBox.Show(win, "Seleccione la familia actual y escriba el nuevo nombre.", "Datos requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int afectados = DatabaseService.RenombrarFamiliaMasivo(origen, destino);
            if (CboProdFamilia != null && string.Equals(CboProdFamilia.Text, origen, StringComparison.OrdinalIgnoreCase))
            {
                CboProdFamilia.Text = destino;
            }
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            System.Windows.MessageBox.Show(win, $"Se actualizaron {afectados} productos de la familia '{origen}' a '{destino}'.", "Familia Actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
            win.Close();
        };

        win.Content = root;
        win.ShowDialog();
    }

    private void CboProdCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboProdCategoria?.SelectedItem is Categoria cat)
        {
            ActualizarEtiquetasFichaPorCategoria(cat);
        }
    }

    private void ActualizarEtiquetasFichaPorCategoria(Categoria? cat)
    {
        if (LblProdCampo1 == null || LblProdCampo2 == null || LblProdCampo3 == null)
            return;

        LblProdCampo1.Text = !string.IsNullOrWhiteSpace(cat?.EtiquetaCampo1)
            ? cat.EtiquetaCampo1
            : "Capacidad Eléctrica / Tipo";
        LblProdCampo2.Text = !string.IsNullOrWhiteSpace(cat?.EtiquetaCampo2)
            ? cat.EtiquetaCampo2
            : "Especificación / Material";
        LblProdCampo3.Text = !string.IsNullOrWhiteSpace(cat?.EtiquetaCampo3)
            ? cat.EtiquetaCampo3
            : "Detalle Adicional / Color";
    }

    private void BtnAutocompletarFichaProducto_Click(object sender, RoutedEventArgs e)
    {
        string nombre = TxtProdNombre.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre))
        {
            System.Windows.MessageBox.Show(this, "Escriba primero el nombre del producto para detectar su categoría y especificaciones técnicas.", "Nombre vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var info = DatabaseService.ClasificarYExtraerEspecificacionesProducto(nombre, TxtProdDescripcion.Text);
        var cat = CboProdCategoria.Items.Cast<Categoria>().FirstOrDefault(c => c.Id == info.CategoriaId);
        if (cat != null)
        {
            CboProdCategoria.SelectedItem = cat;
            ActualizarEtiquetasFichaPorCategoria(cat);
        }

        if (!string.IsNullOrWhiteSpace(info.Marca)) TxtProdMarca.Text = info.Marca;
        if (!string.IsNullOrWhiteSpace(info.Modelo)) TxtProdModelo.Text = info.Modelo;
        if (!string.IsNullOrWhiteSpace(info.Medidas)) TxtProdMedidas.Text = info.Medidas;
        if (!string.IsNullOrWhiteSpace(info.CapacidadElectrica)) TxtProdCapacidadElectrica.Text = info.CapacidadElectrica;
        if (!string.IsNullOrWhiteSpace(info.Especificacion1)) TxtProdEspecificacion1.Text = info.Especificacion1;
        if (!string.IsNullOrWhiteSpace(info.Especificacion2)) TxtProdEspecificacion2.Text = info.Especificacion2;
        if (CboProdFamilia != null)
        {
            string famSugerida = DatabaseService.ExtraerFamiliaSugerida(nombre);
            if (!string.IsNullOrWhiteSpace(famSugerida))
                CboProdFamilia.Text = famSugerida;
        }
    }

    private void CboCarritoSolicitudDestino_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoSolicitudes) return;
        ActualizarIndicadorCarritoSuperior();
    }

    private void ActualizarIndicadorCarritoSuperior()
    {
        if (TxtStatusCarrito == null) return;

        if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud sol && sol.Id > 0)
        {
            TxtStatusCarrito.Text = $"{sol.Folio}: {sol.TotalArticulos:0.##} art. ({sol.TotalSkus} SKUs)";
        }
        else
        {
            decimal totalItems = _carritoTienda.Sum(x => x.cantidad);
            TxtStatusCarrito.Text = $"Carrito: {totalItems:0.##} art. ({_carritoTienda.Count} SKUs)";
        }
    }

    private void AgregarAlCarritoTienda(Producto p, decimal cantidad)
    {
        if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud solDestino && solDestino.Id > 0 && solDestino.EsEditable)
        {
            try
            {
                var solActualizada = DatabaseService.AgregarOIncrementarPartidaEnSolicitud(solDestino.Id, p.Id, cantidad, "Catálogo Visual");
                RefrescarSolicitudes(solActualizada.Id);
                RefrescarDashboard();
                TxtStatusCarrito.Text = $"{solActualizada.Folio}: {solActualizada.TotalArticulos:0.##} art. ({solActualizada.TotalSkus} SKUs)";
                return;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var idx = _carritoTienda.FindIndex(x => x.producto.Id == p.Id);
        if (idx >= 0)
            _carritoTienda[idx] = (p, _carritoTienda[idx].cantidad + cantidad);
        else
            _carritoTienda.Add((p, cantidad));

        ActualizarIndicadorCarritoSuperior();
    }

    private void BtnAgregarProductoCarrito_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Producto p)
        {
            AgregarAlCarritoTienda(p, 1m);
            e.Handled = true;
        }
    }

    private void BtnEditarProductoCatalogo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Producto p)
        {
            CargarProductoEnEditor(p);
            e.Handled = true;
        }
    }

    private void CardProductoCatalogo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Producto p)
        {
            CargarProductoEnEditor(p);
        }
    }

    private void CargarProductoEnEditor(Producto p)
    {
        _modoAltaNuevoProducto = false;
        _productoEnEdicion = p;
        TxtProdCodigoInterno.Text = p.CodigoInterno;
        TxtProdCodigoBarras.Text = p.CodigoBarras;
        TxtProdNombre.Text = p.Nombre;
        TxtProdMarca.Text = p.Marca;
        TxtProdModelo.Text = p.Modelo;
        TxtProdMedidas.Text = p.Medidas;
        TxtProdCapacidadElectrica.Text = p.CapacidadElectrica;
        TxtProdEspecificacion1.Text = p.Especificacion1;
        TxtProdEspecificacion2.Text = p.Especificacion2;
        TxtProdDescripcion.Text = p.Descripcion;
        TxtProdUnidadCompra.Text = p.UnidadCompra;
        TxtProdUnidadAlmacen.Text = p.UnidadAlmacen;
        TxtProdFactor.Text = p.FactorConversion.ToString("0.##", CultureInfo.InvariantCulture);
        TxtProdStock.Text = p.StockActual.ToString("0.##", CultureInfo.InvariantCulture);
        TxtProdStockMin.Text = p.StockMinimo.ToString("0.##", CultureInfo.InvariantCulture);
        TxtProdCosto.Text = p.UltimoCostoCompra.ToString("0.00", CultureInfo.InvariantCulture);
        TxtProdUbicacion.Text = p.Ubicacion;
        ChkProdEsHerramienta.IsChecked = p.EsHerramienta;

        var cat = CboProdCategoria.Items.Cast<Categoria>().FirstOrDefault(c => c.Id == p.CategoriaId);
        CboProdCategoria.SelectedItem = cat;
        ActualizarEtiquetasFichaPorCategoria(cat);

        if (CboProdFamilia != null)
        {
            CboProdFamilia.Text = !string.IsNullOrWhiteSpace(p.Familia)
                ? p.Familia
                : DatabaseService.ExtraerFamiliaSugerida(p.Nombre);
        }

        ImgPreviewProducto.Source = ImageStandardizerService.CargarImagenSegura(p.ImagenRuta);
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(p.CodigoEfectivo);
        ActualizarSeleccionCatalogo();
        ActualizarBannerModoEditorProducto(p);
    }

    private void PonerEditorProductoEnReposo()
    {
        _modoAltaNuevoProducto = false;
        _productoEnEdicion = new Producto { Id = 0 };
        TxtProdCodigoInterno.Text = string.Empty;
        TxtProdCodigoBarras.Text = string.Empty;
        TxtProdNombre.Text = string.Empty;
        if (CboProdCategoria != null) CboProdCategoria.SelectedIndex = -1;
        if (CboProdFamilia != null)
        {
            CboProdFamilia.SelectedIndex = -1;
            CboProdFamilia.Text = string.Empty;
        }
        TxtProdMarca.Text = string.Empty;
        TxtProdModelo.Text = string.Empty;
        TxtProdMedidas.Text = string.Empty;
        TxtProdCapacidadElectrica.Text = string.Empty;
        TxtProdEspecificacion1.Text = string.Empty;
        TxtProdEspecificacion2.Text = string.Empty;
        TxtProdDescripcion.Text = string.Empty;
        TxtProdUnidadCompra.Text = string.Empty;
        TxtProdUnidadAlmacen.Text = string.Empty;
        TxtProdFactor.Text = string.Empty;
        TxtProdStock.Text = string.Empty;
        TxtProdStockMin.Text = string.Empty;
        TxtProdCosto.Text = string.Empty;
        TxtProdUbicacion.Text = string.Empty;
        ChkProdEsHerramienta.IsChecked = false;
        ImgPreviewProducto.Source = null;
        ImgBarcodeProductoEditor.Source = null;
        ActualizarEtiquetasFichaPorCategoria(null);
        ActualizarSeleccionCatalogo();
        ActualizarBannerModoEditorProducto(null);
    }

    private void LimpiarEditorProducto()
    {
        _modoAltaNuevoProducto = true;
        string sig = DatabaseService.GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
        _productoEnEdicion = new Producto { Id = 0, CodigoInterno = sig, CodigoBarras = sig };
        TxtProdCodigoInterno.Text = sig;
        TxtProdCodigoBarras.Text = sig;
        TxtProdNombre.Text = string.Empty;
        if (CboProdCategoria != null) CboProdCategoria.SelectedIndex = -1;
        if (CboProdFamilia != null)
        {
            CboProdFamilia.SelectedIndex = -1;
            CboProdFamilia.Text = string.Empty;
        }
        TxtProdMarca.Text = string.Empty;
        TxtProdModelo.Text = string.Empty;
        TxtProdMedidas.Text = string.Empty;
        TxtProdCapacidadElectrica.Text = string.Empty;
        TxtProdEspecificacion1.Text = string.Empty;
        TxtProdEspecificacion2.Text = string.Empty;
        TxtProdDescripcion.Text = string.Empty;
        TxtProdUnidadCompra.Text = string.Empty;
        TxtProdUnidadAlmacen.Text = string.Empty;
        TxtProdFactor.Text = string.Empty;
        TxtProdStock.Text = string.Empty;
        TxtProdStockMin.Text = string.Empty;
        TxtProdCosto.Text = string.Empty;
        TxtProdUbicacion.Text = string.Empty;
        ChkProdEsHerramienta.IsChecked = false;
        ImgPreviewProducto.Source = null;
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(sig);
        ActualizarEtiquetasFichaPorCategoria(null);
        ActualizarSeleccionCatalogo();
        ActualizarBannerModoEditorProducto(null);
    }

    private void ActualizarBannerModoEditorProducto(Producto? prodEditando)
    {
        if (BdrModoEditorProducto == null || TxtModoEditorProductoTitulo == null || TxtModoEditorProductoSubtitulo == null || BtnGuardarProductoCatalogo == null)
            return;

        if ((prodEditando == null || prodEditando.Id == 0) && !_modoAltaNuevoProducto)
        {
            BdrModoEditorProducto.Background = new SolidColorBrush(Color.FromRgb(243, 244, 246));
            BdrModoEditorProducto.BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219));
            TxtModoEditorProductoTitulo.Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81));
            TxtModoEditorProductoSubtitulo.Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99));
            TxtModoEditorProductoTitulo.Text = "ℹ️ SIN PRODUCTO SELECCIONADO";
            TxtModoEditorProductoSubtitulo.Text = "Seleccione una tarjeta del catálogo a la izquierda para editarla, o presione '+ Nuevo Producto' para iniciar un alta nueva.";
            BtnGuardarProductoCatalogo.Content = "➕ Crear Producto Nuevo en Catálogo";
        }
        else if (prodEditando == null || prodEditando.Id == 0)
        {
            string codigoNuevo = TxtProdCodigoInterno?.Text ?? "PRD-NUEVO";
            BdrModoEditorProducto.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
            BdrModoEditorProducto.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            TxtModoEditorProductoTitulo.Foreground = new SolidColorBrush(Color.FromRgb(29, 78, 216));
            TxtModoEditorProductoSubtitulo.Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175));
            TxtModoEditorProductoTitulo.Text = $"✨ MODO: ALTA DE NUEVO PRODUCTO ({codigoNuevo})";
            TxtModoEditorProductoSubtitulo.Text = "1. Escriba el Nombre  •  2. Elija Categoría y Familia (o Autocompletar)  •  3. Presione 'Crear Producto Nuevo'";
            BtnGuardarProductoCatalogo.Content = "➕ Crear Producto Nuevo en Catálogo";
        }
        else
        {
            BdrModoEditorProducto.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
            BdrModoEditorProducto.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            TxtModoEditorProductoTitulo.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            TxtModoEditorProductoSubtitulo.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
            TxtModoEditorProductoTitulo.Text = $"✏️ EDITANDO PRODUCTO EXISTENTE: {prodEditando.CodigoInterno}";
            TxtModoEditorProductoSubtitulo.Text = $"Modificando '{prodEditando.Nombre}'. Para registrar un artículo distinto presione '+ Nuevo Producto'.";
            BtnGuardarProductoCatalogo.Content = $"💾 Guardar Cambios en {prodEditando.CodigoInterno}";
        }
    }

    private void BtnNuevoProducto_Click(object sender, RoutedEventArgs e)
    {
        LimpiarEditorProducto();
        SvEditorProducto?.ScrollToTop();
        TxtProdNombre?.Focus();
    }

    private void PegarFotoEnProductoActual()
    {
        string codigo = string.IsNullOrWhiteSpace(TxtProdCodigoInterno.Text) ? "PRD" : TxtProdCodigoInterno.Text;
        string? ruta = ImageStandardizerService.GuardarDesdePortapapeles(codigo);
        if (ruta == null)
        {
            System.Windows.MessageBox.Show(this,
                "El portapapeles no contiene una imagen en este momento.\nCopie cualquier imagen desde su navegador (Clic derecho -> Copiar imagen) y presione este botón o Ctrl+V.",
                "Portapapeles sin imagen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _productoEnEdicion.ImagenRuta = ruta;
        ImgPreviewProducto.Source = ImageStandardizerService.CargarImagenSegura(ruta);
    }

    private void BtnPegarImagenPortapapeles_Click(object sender, RoutedEventArgs e) => PegarFotoEnProductoActual();

    private void BtnSubirImagenArchivo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleccionar imagen de producto",
            Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
        };
        if (dlg.ShowDialog() == true)
        {
            string codigo = string.IsNullOrWhiteSpace(TxtProdCodigoInterno.Text) ? "PRD" : TxtProdCodigoInterno.Text;
            string? ruta = ImageStandardizerService.GuardarDesdeArchivo(dlg.FileName, codigo);
            if (ruta != null)
            {
                _productoEnEdicion.ImagenRuta = ruta;
                ImgPreviewProducto.Source = ImageStandardizerService.CargarImagenSegura(ruta);
            }
        }
    }

    private async void BtnGuardarProducto_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtProdNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Seleccione un producto del catálogo para editarlo o escriba el nombre del producto nuevo.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string codInterno = TxtProdCodigoInterno.Text.Trim();
        if (string.IsNullOrWhiteSpace(codInterno))
        {
            codInterno = DatabaseService.GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
            TxtProdCodigoInterno.Text = codInterno;
        }
        string codBarras = string.IsNullOrWhiteSpace(TxtProdCodigoBarras.Text) ? codInterno : TxtProdCodigoBarras.Text.Trim();

        int catId = (CboProdCategoria.SelectedItem as Categoria)?.Id ?? 0;
        if (catId <= 0)
        {
            var autoInfo = DatabaseService.ClasificarYExtraerEspecificacionesProducto(TxtProdNombre.Text.Trim(), TxtProdDescripcion.Text.Trim());
            catId = autoInfo.CategoriaId > 0 ? autoInfo.CategoriaId : 1;
        }

        _productoEnEdicion.CodigoInterno = codInterno;
        _productoEnEdicion.CodigoBarras = codBarras;
        _productoEnEdicion.Nombre = TxtProdNombre.Text.Trim();
        _productoEnEdicion.CategoriaId = catId;
        _productoEnEdicion.Marca = TxtProdMarca.Text.Trim();
        _productoEnEdicion.Familia = !string.IsNullOrWhiteSpace(CboProdFamilia?.Text)
            ? CboProdFamilia.Text.Trim().ToUpperInvariant()
            : DatabaseService.ExtraerFamiliaSugerida(_productoEnEdicion.Nombre);
        _productoEnEdicion.Modelo = TxtProdModelo.Text.Trim();
        _productoEnEdicion.Medidas = TxtProdMedidas.Text.Trim();
        _productoEnEdicion.CapacidadElectrica = TxtProdCapacidadElectrica.Text.Trim();
        _productoEnEdicion.Especificacion1 = TxtProdEspecificacion1.Text.Trim();
        _productoEnEdicion.Especificacion2 = TxtProdEspecificacion2.Text.Trim();
        _productoEnEdicion.Descripcion = TxtProdDescripcion.Text.Trim();
        _productoEnEdicion.UnidadCompra = string.IsNullOrWhiteSpace(TxtProdUnidadCompra.Text) ? "Pieza" : TxtProdUnidadCompra.Text.Trim();
        _productoEnEdicion.UnidadAlmacen = string.IsNullOrWhiteSpace(TxtProdUnidadAlmacen.Text) ? "Pieza" : TxtProdUnidadAlmacen.Text.Trim();
        _productoEnEdicion.FactorConversion = ParseDecimal(TxtProdFactor.Text, 1m);
        _productoEnEdicion.StockActual = ParseDecimal(TxtProdStock.Text, 0m);
        _productoEnEdicion.StockMinimo = ParseDecimal(TxtProdStockMin.Text, 5m);
        _productoEnEdicion.UltimoCostoCompra = ParseDecimal(TxtProdCosto.Text, 0m);
        _productoEnEdicion.Ubicacion = string.IsNullOrWhiteSpace(TxtProdUbicacion.Text) ? "A-01" : TxtProdUbicacion.Text.Trim();
        _productoEnEdicion.EsHerramienta = ChkProdEsHerramienta.IsChecked == true;

        await EjecutarConIndicadorCargaAsync("Guardando producto...", () =>
        {
            DatabaseService.GuardarProducto(_productoEnEdicion);
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            var actualizado = _cacheProductos.FirstOrDefault(x => x.Id == _productoEnEdicion.Id) ?? _productoEnEdicion;
            CargarProductoEnEditor(actualizado);
        });
    }

    private async void BtnEliminarProductoCatalogoCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Producto prod })
        {
            await EliminarProductoConConfirmacionAsync(prod);
        }
    }

    private async void BtnEliminarProducto_Click(object sender, RoutedEventArgs e)
    {
        if (_productoEnEdicion.Id == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un producto guardado para eliminarlo.", "Producto no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await EliminarProductoConConfirmacionAsync(_productoEnEdicion);
    }

    private async Task EliminarProductoConConfirmacionAsync(Producto prod)
    {
        if (!SolicitarAutorizacionAdmin($"Eliminar producto [{prod.CodigoInterno}] {prod.Nombre}"))
            return;

        if (System.Windows.MessageBox.Show(this, $"¿Desea eliminar el producto '[{prod.CodigoInterno}] {prod.Nombre}' del catálogo?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            await EjecutarConIndicadorCargaAsync("Eliminando producto del catálogo...", () =>
            {
                DatabaseService.EliminarProducto(prod.Id);
                _carritoTienda.RemoveAll(x => x.producto.Id == prod.Id);
                TxtStatusCarrito.Text = $"Carrito: {_carritoTienda.Sum(x => x.cantidad):0.##} artículos";
                if (_productoEnEdicion.Id == prod.Id)
                {
                    PonerEditorProductoEnReposo();
                }
                CargarTodoElSistema();
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Error al eliminar el producto:\n{ex.Message}", "Error al eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnEnviarCarritoASolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud solDestino && solDestino.Id > 0)
        {
            CambiarSeccion(2);
            var enHistorial = DgHistorialSolicitudes.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == solDestino.Id);
            if (enHistorial != null)
            {
                DgHistorialSolicitudes.SelectedItem = enHistorial;
                DgHistorialSolicitudes.ScrollIntoView(enHistorial);
                CargarSolicitudEnEditor(enHistorial);
            }
            return;
        }

        if (_carritoTienda.Count == 0)
        {
            CambiarSeccion(2);
            return;
        }

        if (_solicitudEditandoId > 0)
        {
            IniciarNuevaSolicitudVacia();
        }

        foreach (var (prod, cant) in _carritoTienda)
        {
            var existente = _partidasNuevaSolicitud.FirstOrDefault(x => x.ProductoId == prod.Id);
            if (existente != null)
                existente.CantidadSolicitada += cant;
            else
                _partidasNuevaSolicitud.Add(new SolicitudPartida
                {
                    ProductoId = prod.Id,
                    ProductoCodigo = prod.CodigoInterno,
                    ProductoNombre = prod.Nombre,
                    UnidadCompra = prod.UnidadCompra,
                    CantidadSolicitada = cant,
                    StockActualAlmacen = prod.StockActual,
                    NotasPartida = "Catálogo Visual"
                });
        }
        _carritoTienda.Clear();
        ActualizarIndicadorCarritoSuperior();
        DgPartidasNuevaSolicitud.Items.Refresh();
        CambiarSeccion(2);
    }

    private void BtnEnviarCarritoASalida_Click(object sender, RoutedEventArgs e)
    {
        if (_carritoTienda.Count == 0)
        {
            if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud sol && sol.Id > 0 && sol.Partidas.Count > 0)
            {
                var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
                foreach (var part in sol.Partidas)
                {
                    if (prodsDict.TryGetValue(part.ProductoId, out var prod) && !prod.EsHerramienta)
                    {
                        AgregarProductoAValeSalidaPos(prod, part.CantidadSolicitada);
                    }
                }
                CambiarSeccion(5);
                return;
            }

            System.Windows.MessageBox.Show(this, "Primero agregue productos al carrito haciendo clic en 'Agregar'.", "Carrito vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var (prod, cant) in _carritoTienda)
        {
            AgregarProductoAValeSalidaPos(prod, cant);
        }
        _carritoTienda.Clear();
        ActualizarIndicadorCarritoSuperior();
        CambiarSeccion(5);
    }

    private void BtnImprimirCatalogoCodigos_Click(object sender, RoutedEventArgs e)
    {
        var prods = DatabaseService.ObtenerProductos();
        var hers = DatabaseService.ObtenerHerramientas();
        BarcodePrintService.MostrarVistaPreviaCatalogoCodigosCarta(this, prods, hers);
    }

    // ==================== TAB 2: SOLICITUDES / CARRITOS DE INGENIERÍA ====================

    private void CboSolDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboSolDepartamento.SelectedItem is Departamento dep)
        {
            var encs = DatabaseService.ObtenerEncargados(dep.Id);
            CboSolEncargado.ItemsSource = encs;
            CboSolEncargado.SelectedIndex = -1;
            EstablecerPlaceholderCombo(CboSolEncargado, "Seleccione la persona solicitante...");
        }
        else if (CboSolEncargado != null)
        {
            CboSolEncargado.ItemsSource = null;
            CboSolEncargado.SelectedIndex = -1;
            EstablecerPlaceholderCombo(CboSolEncargado, "Seleccione primero un departamento...");
        }
    }

    private void FiltrarComboProductoSolicitud()
    {
        if (CboSolProductoAgregar == null) return;

        var baseProds = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        string q = TxtBuscarProductoSolicitud?.Text?.Trim() ?? string.Empty;
        string qNorm = NormalizarCodigoEscaneado(q);

        int prevId = (CboSolProductoAgregar.SelectedItem as Producto)?.Id ?? 0;

        List<Producto> filtrados;
        if (string.IsNullOrWhiteSpace(q))
        {
            filtrados = baseProds;
            if (TxtContadorFiltroProductoSol != null)
                TxtContadorFiltroProductoSol.Text = $"{baseProds.Count} artículos en catálogo";

            CboSolProductoAgregar.ItemsSource = filtrados;
            CboSolProductoAgregar.SelectedItem = prevId > 0 ? filtrados.FirstOrDefault(p => p.Id == prevId) : null;
            return;
        }
        else
        {
            var terminos = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filtrados = baseProds.Where(p =>
                (!string.IsNullOrEmpty(qNorm) && (
                    p.CodigoInterno.Contains(qNorm, StringComparison.OrdinalIgnoreCase) ||
                    p.CodigoBarras.Contains(qNorm, StringComparison.OrdinalIgnoreCase))) ||
                terminos.All(t =>
                    p.Nombre.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Familia.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.CodigoInterno.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.CodigoBarras.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.CategoriaNombre.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Marca.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Modelo.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Medidas.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.CapacidadElectrica.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Especificacion1.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Especificacion2.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Descripcion.Contains(t, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            if (TxtContadorFiltroProductoSol != null)
                TxtContadorFiltroProductoSol.Text = filtrados.Count > 0
                    ? $"{filtrados.Count} coincidencias (o clic en '➕ Concepto Abierto')"
                    : "0 en catálogo (clic en '➕ Concepto Abierto' o Enter)";
        }

        CboSolProductoAgregar.ItemsSource = filtrados;
        if (filtrados.Count > 0)
        {
            CboSolProductoAgregar.SelectedItem = filtrados.FirstOrDefault(p => p.Id == prevId) ?? filtrados[0];
        }
        else
        {
            CboSolProductoAgregar.SelectedIndex = -1;
        }
    }

    private void TxtBuscarProductoSolicitud_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        FiltrarComboProductoSolicitud();
    }

    private void TxtBuscarProductoSolicitud_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string raw = TxtBuscarProductoSolicitud.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(raw);

            // Si escaneó un folio de documento (SOL-, ENT-, SAL-, OC-)
            if (!string.IsNullOrWhiteSpace(code) && EsPrefijoDocumento(code))
            {
                TxtBuscarProductoSolicitud.Clear();
                ProcesarCodigoEscaneado(code);
                e.Handled = true;
                return;
            }

            decimal cant = ParseDecimal(TxtSolCantidadAgregar.Text, 1m);

            // Si coincide exactamente con un código de barras o código interno
            var prodExacto = DatabaseService.BuscarProductoPorCodigo(code) ?? DatabaseService.BuscarProductoPorCodigo(raw);
            if (prodExacto != null)
            {
                AgregarProductoAPartidasSolicitud(prodExacto, cant, "Búsqueda / Escáner");
                TxtBuscarProductoSolicitud.Clear();
                e.Handled = true;
                return;
            }

            // Si hay un producto seleccionado en el combo filtrado, agregarlo con Enter
            if (CboSolProductoAgregar.SelectedItem is Producto prodSel)
            {
                AgregarProductoAPartidasSolicitud(prodSel, cant, "Buscador de Solicitud");
                TxtBuscarProductoSolicitud.SelectAll();
                e.Handled = true;
                return;
            }

            // Si no existe en el catálogo pero el usuario escribió un concepto y presionó Enter, agregarlo como Concepto Abierto
            if (!string.IsNullOrWhiteSpace(raw))
            {
                AgregarConceptoLibreAPartidasSolicitud(raw, cant, "Concepto Abierto (Buscador)");
                TxtBuscarProductoSolicitud.Clear();
                e.Handled = true;
            }
        }
    }

    private void RefrescarSolicitudes(int? mantenerSolicitudId = null)
    {
        _sincronizandoSolicitudes = true;
        try
        {
            var sols = DatabaseService.ObtenerSolicitudes();

            int idHistSel = mantenerSolicitudId ?? (DgHistorialSolicitudes.SelectedItem as Solicitud)?.Id ?? _solicitudEditandoId;
            int idCompSel = (CboCompSolicitud.SelectedItem as Solicitud)?.Id ?? 0;
            int idDestSel = mantenerSolicitudId ?? (CboCarritoSolicitudDestino?.SelectedItem as Solicitud)?.Id ?? _solicitudEditandoId;

            DgHistorialSolicitudes.ItemsSource = sols;
            if (idHistSel > 0)
            {
                var matchHist = sols.FirstOrDefault(s => s.Id == idHistSel);
                if (matchHist != null)
                {
                    DgHistorialSolicitudes.SelectedItem = matchHist;
                }
            }

            CboCompSolicitud.ItemsSource = sols;
            if (idCompSel > 0)
            {
                var matchComp = sols.FirstOrDefault(s => s.Id == idCompSel);
                if (matchComp != null)
                {
                    CboCompSolicitud.SelectedItem = matchComp;
                    CboCompSolicitud_SelectionChanged(CboCompSolicitud, null!);
                }
                else
                {
                    CboCompSolicitud.SelectedIndex = -1;
                    CboCompSolicitud_SelectionChanged(CboCompSolicitud, null!);
                }
            }
            else
            {
                CboCompSolicitud.SelectedIndex = -1;
                CboCompSolicitud_SelectionChanged(CboCompSolicitud, null!);
            }

            if (CboCarritoSolicitudDestino != null)
            {
                var opcionesCarrito = new List<Solicitud>
                {
                    new() { Id = 0, Folio = "NUEVO", Estatus = "Pendiente" }
                };
                opcionesCarrito.AddRange(sols.Where(s => s.EsEditable));
                CboCarritoSolicitudDestino.ItemsSource = opcionesCarrito;
                CboCarritoSolicitudDestino.SelectedItem = opcionesCarrito.FirstOrDefault(s => s.Id == idDestSel) ?? opcionesCarrito[0];
            }

            if (_solicitudEditandoId > 0)
            {
                var solEdit = sols.FirstOrDefault(s => s.Id == _solicitudEditandoId);
                if (solEdit != null)
                {
                    SincronizarPartidasEditorDesdeSolicitud(solEdit);
                }
            }

            ActualizarIndicadorCarritoSuperior();
        }
        finally
        {
            _sincronizandoSolicitudes = false;
        }
    }

    private void SincronizarPartidasEditorDesdeSolicitud(Solicitud sol)
    {
        _partidasNuevaSolicitud.Clear();
        foreach (var p in sol.Partidas)
        {
            _partidasNuevaSolicitud.Add(new SolicitudPartida
            {
                Id = p.Id,
                SolicitudId = p.SolicitudId,
                ProductoId = p.ProductoId,
                ProductoCodigo = p.ProductoCodigo,
                ProductoNombre = p.ProductoNombre,
                DescripcionLibre = p.DescripcionLibre,
                Familia = p.Familia,
                UnidadCompra = p.UnidadCompra,
                CantidadSolicitada = p.CantidadSolicitada,
                StockActualAlmacen = p.StockActualAlmacen,
                NotasPartida = p.NotasPartida,
                Proveedor1Id = p.Proveedor1Id,
                PrecioSinIva1 = p.PrecioSinIva1,
                DiasEntrega1 = p.DiasEntrega1,
                FleteConIva1 = p.FleteConIva1,
                NotasCotizacion1 = p.NotasCotizacion1,
                Proveedor2Id = p.Proveedor2Id,
                PrecioSinIva2 = p.PrecioSinIva2,
                DiasEntrega2 = p.DiasEntrega2,
                FleteConIva2 = p.FleteConIva2,
                NotasCotizacion2 = p.NotasCotizacion2,
                Proveedor3Id = p.Proveedor3Id,
                PrecioSinIva3 = p.PrecioSinIva3,
                DiasEntrega3 = p.DiasEntrega3,
                FleteConIva3 = p.FleteConIva3,
                NotasCotizacion3 = p.NotasCotizacion3,
                ProveedorGanadorIndice = p.ProveedorGanadorIndice,
                OrdenCompraId = p.OrdenCompraId,
                OrdenCompraFolio = p.OrdenCompraFolio
            });
        }
        DgPartidasNuevaSolicitud.Items.Refresh();
        ActualizarEncabezadoEditorSolicitud(sol);
    }

    private void ActualizarEncabezadoEditorSolicitud(Solicitud? sol)
    {
        if (TxtTituloEditorSolicitud == null || TxtEstadoEditorSolicitud == null || BdrEstadoEditorSolicitud == null)
            return;

        if (sol == null || sol.Id == 0)
        {
            TxtTituloEditorSolicitud.Text = "Nueva Solicitud (Carrito)";
            TxtEstadoEditorSolicitud.Text = "NUEVO CARRITO";
            BdrEstadoEditorSolicitud.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 78, 216));
            BtnGuardarSolicitud.Content = "Crear / Guardar Carrito de Solicitud";
            BtnGuardarSolicitud.IsEnabled = true;
            BtnLimpiarPartidasSolicitud.IsEnabled = true;
        }
        else if (sol.EsEditable)
        {
            TxtTituloEditorSolicitud.Text = $"Editando Carrito: {sol.Folio}";
            TxtEstadoEditorSolicitud.Text = $"CARRITO ABIERTO ({sol.TotalSkus} SKUs)";
            BdrEstadoEditorSolicitud.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            BtnGuardarSolicitud.Content = $"Guardar Cambios en {sol.Folio} e Ir a Comparativa";
            BtnGuardarSolicitud.IsEnabled = true;
            BtnLimpiarPartidasSolicitud.IsEnabled = true;
        }
        else
        {
            TxtTituloEditorSolicitud.Text = $"Solicitud Cerrada: {sol.Folio}";
            TxtEstadoEditorSolicitud.Text = "OC GENERADA (SOLO LECTURA)";
            BdrEstadoEditorSolicitud.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            BtnGuardarSolicitud.Content = "Solicitud con Orden de Compra Generada";
            BtnGuardarSolicitud.IsEnabled = false;
            BtnLimpiarPartidasSolicitud.IsEnabled = false;
        }
    }

    private void IniciarNuevaSolicitudVacia()
    {
        _solicitudEditandoId = 0;
        _sincronizandoSolicitudes = true;
        try
        {
            DgHistorialSolicitudes.SelectedItem = null;
            if (CboCarritoSolicitudDestino?.ItemsSource is IEnumerable<Solicitud> opciones)
            {
                CboCarritoSolicitudDestino.SelectedItem = opciones.FirstOrDefault(s => s.Id == 0);
            }
        }
        finally
        {
            _sincronizandoSolicitudes = false;
        }

        _partidasNuevaSolicitud.Clear();
        if (CboSolDepartamento != null) CboSolDepartamento.SelectedIndex = -1;
        if (CboSolEncargado != null)
        {
            CboSolEncargado.ItemsSource = null;
            CboSolEncargado.SelectedIndex = -1;
            EstablecerPlaceholderCombo(CboSolEncargado, "Seleccione primero un departamento...");
        }
        if (CboSolProyecto != null) CboSolProyecto.SelectedIndex = -1;
        if (CboSolProductoAgregar != null) CboSolProductoAgregar.SelectedIndex = -1;
        TxtBuscarProductoSolicitud?.Clear();
        TxtSolCantidadAgregar?.Clear();
        TxtSolNotas.Clear();
        DgPartidasNuevaSolicitud.Items.Refresh();
        ActualizarEncabezadoEditorSolicitud(null);
        ActualizarIndicadorCarritoSuperior();
    }

    private void CargarSolicitudEnEditor(Solicitud sol)
    {
        _solicitudEditandoId = sol.Id;

        var dep = CboSolDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Id == sol.DepartamentoId);
        if (dep != null)
        {
            CboSolDepartamento.SelectedItem = dep;
            var encs = DatabaseService.ObtenerEncargados(dep.Id);
            CboSolEncargado.ItemsSource = encs;
            var enc = encs.FirstOrDefault(e => e.Id == sol.EncargadoId);
            if (enc != null) CboSolEncargado.SelectedItem = enc;
        }

        var proy = CboSolProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == sol.ProyectoId);
        if (proy != null) CboSolProyecto.SelectedItem = proy;

        TxtSolNotas.Text = sol.Notas;
        SincronizarPartidasEditorDesdeSolicitud(sol);

        if (sol.EsEditable && CboCarritoSolicitudDestino?.ItemsSource is IEnumerable<Solicitud> opciones)
        {
            _sincronizandoSolicitudes = true;
            try
            {
                var dest = opciones.FirstOrDefault(s => s.Id == sol.Id);
                if (dest != null) CboCarritoSolicitudDestino.SelectedItem = dest;
            }
            finally
            {
                _sincronizandoSolicitudes = false;
            }
            ActualizarIndicadorCarritoSuperior();
        }
    }

    private void DgHistorialSolicitudes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoSolicitudes) return;
        if (DgHistorialSolicitudes.SelectedItem is Solicitud sol)
        {
            CargarSolicitudEnEditor(sol);
        }
    }

    private void PersistirCambioSiSolicitudEnEdicion()
    {
        if (_solicitudEditandoId <= 0) return;

        var solActual = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
        if (solActual == null || !solActual.EsEditable) return;

        int depId = (CboSolDepartamento.SelectedItem as Departamento)?.Id ?? solActual.DepartamentoId;
        int encId = (CboSolEncargado.SelectedItem as Encargado)?.Id ?? solActual.EncargadoId;
        int proyId = (CboSolProyecto.SelectedItem as Proyecto)?.Id ?? solActual.ProyectoId;
        string origen = string.IsNullOrWhiteSpace(solActual.Origen) ? "Catálogo" : solActual.Origen;

        DatabaseService.ActualizarSolicitudExistente(
            _solicitudEditandoId,
            depId,
            encId,
            proyId,
            origen,
            TxtSolNotas.Text,
            _partidasNuevaSolicitud.ToList());

        RefrescarSolicitudes(_solicitudEditandoId);
        RefrescarDashboard();
    }

    private void AgregarProductoAPartidasSolicitud(Producto prod, decimal cant, string notas = "")
    {
        if (cant <= 0) cant = 1m;

        if (_solicitudEditandoId > 0)
        {
            var solActual = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
            if (solActual != null && !solActual.EsEditable)
            {
                System.Windows.MessageBox.Show(this, $"La solicitud {solActual.Folio} ya tiene Orden de Compra generada y no puede modificarse.\n\nHaga clic en '+ Nuevo Carrito' para crear una nueva solicitud.", "Solicitud cerrada", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var existente = _partidasNuevaSolicitud.FirstOrDefault(x => x.ProductoId == prod.Id);
        if (existente != null)
        {
            existente.CantidadSolicitada += cant;
            DgPartidasNuevaSolicitud.Items.Refresh();
        }
        else
        {
            _partidasNuevaSolicitud.Add(new SolicitudPartida
            {
                ProductoId = prod.Id,
                ProductoCodigo = prod.CodigoInterno,
                ProductoNombre = prod.Nombre,
                Familia = prod.Familia,
                UnidadCompra = prod.UnidadCompra,
                CantidadSolicitada = cant,
                StockActualAlmacen = prod.StockActual,
                NotasPartida = notas
            });
        }

        PersistirCambioSiSolicitudEnEdicion();
    }

    private void AgregarConceptoLibreAPartidasSolicitud(string concepto, decimal cant, string notas = "Concepto Abierto")
    {
        string desc = concepto.Trim();
        if (string.IsNullOrWhiteSpace(desc))
            return;

        if (cant <= 0) cant = 1m;
        if (!VerificarPermisoEdicionSolicitudActual()) return;

        var existente = _partidasNuevaSolicitud.FirstOrDefault(x =>
            x.ProductoId <= 0 &&
            string.Equals(x.ProductoNombre, desc, StringComparison.OrdinalIgnoreCase));

        if (existente != null)
        {
            existente.CantidadSolicitada += cant;
            DgPartidasNuevaSolicitud.Items.Refresh();
        }
        else
        {
            string famSug = DatabaseService.ExtraerFamiliaSugerida(desc);
            _partidasNuevaSolicitud.Add(new SolicitudPartida
            {
                ProductoId = 0,
                ProductoCodigo = "CONCEPTO",
                ProductoNombre = desc.ToUpperInvariant(),
                DescripcionLibre = desc.ToUpperInvariant(),
                Familia = famSug,
                UnidadCompra = "Pieza",
                CantidadSolicitada = cant,
                StockActualAlmacen = 0m,
                NotasPartida = notas
            });
        }

        PersistirCambioSiSolicitudEnEdicion();
    }

    private void BtnAgregarPartidaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        decimal cant = ParseDecimal(TxtSolCantidadAgregar.Text, 1m);
        if (CboSolProductoAgregar.SelectedItem is Producto prod)
        {
            AgregarProductoAPartidasSolicitud(prod, cant, "Manual");
            return;
        }

        string texto = TxtBuscarProductoSolicitud?.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(texto))
        {
            AgregarConceptoLibreAPartidasSolicitud(texto, cant, "Concepto Abierto");
            TxtBuscarProductoSolicitud?.Clear();
        }
    }

    private void BtnAgregarConceptoLibreSolicitud_Click(object sender, RoutedEventArgs e)
    {
        string texto = TxtBuscarProductoSolicitud?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(texto))
        {
            System.Windows.MessageBox.Show(this,
                "Escriba primero en el buscador el concepto genérico o artículo nuevo que desea solicitar (por ejemplo: 'Jabón', 'Gas R-32', 'Toalla microfibra') y luego presione '➕ Concepto Abierto'.\n\nAsí se agregará a la solicitud para cotizarlo con proveedores sin ensuciar el catálogo hasta concretar la compra.",
                "Escriba el Concepto Abierto",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TxtBuscarProductoSolicitud?.Focus();
            return;
        }

        decimal cant = ParseDecimal(TxtSolCantidadAgregar.Text, 1m);
        AgregarConceptoLibreAPartidasSolicitud(texto, cant, "Concepto Abierto (Se dará de alta al comprar)");
        TxtBuscarProductoSolicitud?.Clear();
    }

    private void BtnDisminuirPartidaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SolicitudPartida partida)
            return;

        if (!VerificarPermisoEdicionSolicitudActual()) return;
        if (partida.TieneOcGenerada)
        {
            System.Windows.MessageBox.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {partida.OrdenCompraFolio} y no puede modificarse.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (partida.CantidadSolicitada > 1)
        {
            partida.CantidadSolicitada -= 1;
            DgPartidasNuevaSolicitud.Items.Refresh();
        }
        else
        {
            _partidasNuevaSolicitud.Remove(partida);
        }

        PersistirCambioSiSolicitudEnEdicion();
    }

    private void BtnAumentarPartidaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SolicitudPartida partida)
            return;

        if (!VerificarPermisoEdicionSolicitudActual()) return;
        if (partida.TieneOcGenerada)
        {
            System.Windows.MessageBox.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {partida.OrdenCompraFolio} y no puede modificarse.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        partida.CantidadSolicitada += 1;
        DgPartidasNuevaSolicitud.Items.Refresh();
        PersistirCambioSiSolicitudEnEdicion();
    }

    private void BtnQuitarPartidaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SolicitudPartida partida)
            return;

        if (!VerificarPermisoEdicionSolicitudActual()) return;
        if (partida.TieneOcGenerada)
        {
            System.Windows.MessageBox.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {partida.OrdenCompraFolio} y no puede quitarse de la solicitud.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _partidasNuevaSolicitud.Remove(partida);
        DgPartidasNuevaSolicitud.Items.Refresh();
        PersistirCambioSiSolicitudEnEdicion();
    }

    private void DgPartidasNuevaSolicitud_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!VerificarPermisoEdicionSolicitudActual())
            {
                RefrescarSolicitudes(_solicitudEditandoId);
                return;
            }

            var invalidas = _partidasNuevaSolicitud.Where(p => p.CantidadSolicitada <= 0).ToList();
            foreach (var inv in invalidas)
            {
                _partidasNuevaSolicitud.Remove(inv);
            }
            DgPartidasNuevaSolicitud.Items.Refresh();
            PersistirCambioSiSolicitudEnEdicion();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool VerificarPermisoEdicionSolicitudActual()
    {
        if (_solicitudEditandoId <= 0) return true;
        var sol = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
        if (sol != null && !sol.EsEditable)
        {
            System.Windows.MessageBox.Show(this, $"La solicitud {sol.Folio} ya tiene Orden de Compra generada y no puede modificarse.", "Solo lectura", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void BtnNuevaSolicitudVacia_Click(object sender, RoutedEventArgs e)
    {
        IniciarNuevaSolicitudVacia();
    }

    private void BtnCrearCarritoEIrCatalogo_Click(object sender, RoutedEventArgs e)
    {
        if (CboSolDepartamento.SelectedItem is not Departamento dep ||
            CboSolEncargado.SelectedItem is not Encargado enc ||
            CboSolProyecto.SelectedItem is not Proyecto proy)
        {
            System.Windows.MessageBox.Show(this, "Seleccione Departamento, Solicitante y Proyecto para crear o vincular el Carrito de Solicitud.", "Datos requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Solicitud carritoActivo;
        if (_solicitudEditandoId > 0)
        {
            var solExistente = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
            if (solExistente != null && solExistente.EsEditable)
            {
                carritoActivo = DatabaseService.ActualizarSolicitudExistente(
                    _solicitudEditandoId,
                    dep.Id,
                    enc.Id,
                    proy.Id,
                    solExistente.Origen,
                    TxtSolNotas.Text,
                    _partidasNuevaSolicitud.ToList());
            }
            else
            {
                carritoActivo = DatabaseService.CrearSolicitud(dep.Id, enc.Id, proy.Id, "Catálogo", TxtSolNotas.Text, new List<SolicitudPartida>());
            }
        }
        else
        {
            carritoActivo = DatabaseService.CrearSolicitud(dep.Id, enc.Id, proy.Id, "Catálogo", TxtSolNotas.Text, _partidasNuevaSolicitud.ToList());
        }

        _solicitudEditandoId = carritoActivo.Id;
        RefrescarSolicitudes(carritoActivo.Id);
        RefrescarDashboard();
        CambiarSeccion(1);
    }

    private void BtnIrCatalogoConSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var sol = DgHistorialSolicitudes.SelectedItem as Solicitud
                  ?? DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);

        if (sol == null)
        {
            BtnCrearCarritoEIrCatalogo_Click(sender, e);
            return;
        }

        if (!sol.EsEditable)
        {
            System.Windows.MessageBox.Show(this, $"La solicitud {sol.Folio} ya tiene Orden de Compra generada y no admite más artículos.\n\nUse '+ Nuevo Carrito' para abrir una solicitud nueva.", "Solicitud cerrada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _solicitudEditandoId = sol.Id;
        RefrescarSolicitudes(sol.Id);
        CambiarSeccion(1);
    }

    private void BtnImprimirSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var sol = DgHistorialSolicitudes.SelectedItem as Solicitud
                  ?? DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);

        if (sol == null)
        {
            System.Windows.MessageBox.Show(this, "Seleccione o guarde primero una Solicitud para abrir su vista previa de impresión.", "Solicitud no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BarcodePrintService.MostrarVistaPreviaEImprimirSolicitud(this, sol);
    }

    private void BtnEliminarSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var sol = DgHistorialSolicitudes.SelectedItem as Solicitud
                  ?? DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);

        if (sol == null)
        {
            System.Windows.MessageBox.Show(this, "Seleccione una solicitud de la lista para eliminarla.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!sol.EsEditable)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar la solicitud {sol.Folio} porque ya tiene una Orden de Compra generada.", "Operación no permitida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar carrito/solicitud {sol.Folio}"))
            return;

        var resp = System.Windows.MessageBox.Show(this,
            $"¿Está seguro de eliminar el carrito/solicitud {sol.Folio} ({sol.ProyectoNombre}) con {sol.TotalSkus} partidas?",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp != System.Windows.MessageBoxResult.Yes)
            return;

        DatabaseService.EliminarSolicitud(sol.Id);
        IniciarNuevaSolicitudVacia();
        RefrescarSolicitudes();
        RefrescarDashboard();
    }

    private void BtnInterpretarWhatsapp_Click(object sender, RoutedEventArgs e)
    {
        if (!VerificarPermisoEdicionSolicitudActual()) return;

        string raw = TxtWhatsappInput.Text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            TxtWhatsappInput.Text = "4 tornillo inox\n2 cable calibre 12\n3 cinta aislar 3M";
            raw = TxtWhatsappInput.Text;
        }

        var productos = DatabaseService.ObtenerProductos();
        var lineas = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int agregados = 0;

        foreach (var linea in lineas)
        {
            string l = linea.Trim();
            if (string.IsNullOrWhiteSpace(l)) continue;

            decimal cant = 1m;
            var m = Regex.Match(l, @"^(\d+(\.\d+)?)\s+(.*)$");
            string textoBusqueda = l;
            if (m.Success)
            {
                cant = ParseDecimal(m.Groups[1].Value, 1m);
                textoBusqueda = m.Groups[3].Value.Trim();
            }

            var palabras = textoBusqueda.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2).ToList();

            var mejorProducto = productos
                .Select(p => new
                {
                    Producto = p,
                    Score = palabras.Count(w =>
                        p.Nombre.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.Descripcion.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.Marca.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.Modelo.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                        p.CodigoInterno.Contains(w, StringComparison.OrdinalIgnoreCase))
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (mejorProducto != null && mejorProducto.Score > 0)
            {
                var p = mejorProducto.Producto;
                var existente = _partidasNuevaSolicitud.FirstOrDefault(x => x.ProductoId == p.Id);
                if (existente != null)
                {
                    existente.CantidadSolicitada += cant;
                }
                else
                {
                    _partidasNuevaSolicitud.Add(new SolicitudPartida
                    {
                        ProductoId = p.Id,
                        ProductoCodigo = p.CodigoInterno,
                        ProductoNombre = p.Nombre,
                        Familia = p.Familia,
                        UnidadCompra = p.UnidadCompra,
                        CantidadSolicitada = cant,
                        StockActualAlmacen = p.StockActual,
                        NotasPartida = $"WhatsApp: {l}"
                    });
                }
                agregados++;
            }
            else if (!string.IsNullOrWhiteSpace(textoBusqueda))
            {
                // Si no coincide con ningún producto del catálogo, agregar como Concepto Abierto
                AgregarConceptoLibreAPartidasSolicitud(textoBusqueda, cant, $"WhatsApp (Concepto Abierto): {l}");
                agregados++;
            }
        }

        DgPartidasNuevaSolicitud.Items.Refresh();
        if (agregados > 0)
        {
            TxtWhatsappInput.Clear();
            PersistirCambioSiSolicitudEnEdicion();
        }
        else
        {
            System.Windows.MessageBox.Show(this, "No se encontraron coincidencias automáticas en el catálogo para ese texto. Puede buscarlos en la barra superior o darlos de alta en el Catálogo Visual.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExportarPlantillaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Guardar Plantilla de Solicitud para Ingeniería (Excel CSV)",
            FileName = "Plantilla_Solicitud_Ingenieria.csv",
            Filter = "Archivo CSV para Excel (*.csv)|*.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CodigoProducto,Cantidad,Notas");
            foreach (var p in DatabaseService.ObtenerProductos().Take(4))
            {
                sb.AppendLine($"{p.CodigoInterno},2,Para proyecto en curso ({p.Nombre})");
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            System.Windows.MessageBox.Show(this, $"Plantilla guardada en:\n{dlg.FileName}\n\nLos ingenieros pueden abrirla en Excel, capturar códigos/nombres y cantidades, y usted importarla con 1 clic.", "Plantilla Generada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnImportarPlantillaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (!VerificarPermisoEdicionSolicitudActual()) return;

        var dlg = new OpenFileDialog
        {
            Title = "Importar Plantilla de Solicitud (CSV)",
            Filter = "Archivo CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            var lineas = File.ReadAllLines(dlg.FileName);
            var productos = DatabaseService.ObtenerProductos();
            foreach (var l in lineas.Skip(1))
            {
                var cols = l.Split(',');
                if (cols.Length < 2) continue;
                string clave = cols[0].Trim();
                decimal cant = ParseDecimal(cols[1], 1m);
                string nota = cols.Length >= 3 ? cols[2].Trim() : "Importado de plantilla";

                var prod = productos.FirstOrDefault(p =>
                    p.CodigoInterno.Equals(clave, StringComparison.OrdinalIgnoreCase) ||
                    p.Nombre.Contains(clave, StringComparison.OrdinalIgnoreCase));
                if (prod != null)
                {
                    var existente = _partidasNuevaSolicitud.FirstOrDefault(x => x.ProductoId == prod.Id);
                    if (existente != null)
                    {
                        existente.CantidadSolicitada += cant;
                    }
                    else
                    {
                        _partidasNuevaSolicitud.Add(new SolicitudPartida
                        {
                            ProductoId = prod.Id,
                            ProductoCodigo = prod.CodigoInterno,
                            ProductoNombre = prod.Nombre,
                            Familia = prod.Familia,
                            UnidadCompra = prod.UnidadCompra,
                            CantidadSolicitada = cant,
                            StockActualAlmacen = prod.StockActual,
                            NotasPartida = nota
                        });
                    }
                }
                else if (!string.IsNullOrWhiteSpace(clave))
                {
                    AgregarConceptoLibreAPartidasSolicitud(clave, cant, nota);
                }
            }
            DgPartidasNuevaSolicitud.Items.Refresh();
            PersistirCambioSiSolicitudEnEdicion();
        }
    }

    private void BtnLimpiarPartidasSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (!VerificarPermisoEdicionSolicitudActual()) return;
        if (_partidasNuevaSolicitud.Count == 0) return;

        if (_solicitudEditandoId > 0)
        {
            var resp = System.Windows.MessageBox.Show(this,
                "¿Desea vaciar todos los artículos de este carrito de solicitud?",
                "Vaciar carrito",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (resp != System.Windows.MessageBoxResult.Yes) return;
        }

        _partidasNuevaSolicitud.Clear();
        DgPartidasNuevaSolicitud.Items.Refresh();
        PersistirCambioSiSolicitudEnEdicion();
    }

    private void BtnGuardarSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (CboSolDepartamento.SelectedItem is not Departamento dep ||
            CboSolEncargado.SelectedItem is not Encargado enc ||
            CboSolProyecto.SelectedItem is not Proyecto proy)
        {
            System.Windows.MessageBox.Show(this, "Seleccione Departamento, Encargado y Proyecto.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string origen = _partidasNuevaSolicitud.Any(p => (p.NotasPartida ?? "").StartsWith("WhatsApp")) ? "WhatsApp" : "Catálogo";
        Solicitud guardada;

        if (_solicitudEditandoId > 0)
        {
            if (!VerificarPermisoEdicionSolicitudActual()) return;
            guardada = DatabaseService.ActualizarSolicitudExistente(
                _solicitudEditandoId,
                dep.Id,
                enc.Id,
                proy.Id,
                origen,
                TxtSolNotas.Text,
                _partidasNuevaSolicitud.ToList());
        }
        else
        {
            guardada = DatabaseService.CrearSolicitud(
                dep.Id,
                enc.Id,
                proy.Id,
                origen,
                TxtSolNotas.Text,
                _partidasNuevaSolicitud.ToList());
            _solicitudEditandoId = guardada.Id;
        }

        RefrescarSolicitudes(guardada.Id);
        RefrescarDashboard();

        if (guardada.Partidas.Count == 0)
        {
            var irCat = System.Windows.MessageBox.Show(this,
                $"Se creó el carrito de solicitud abierto {guardada.Folio} para el proyecto '{guardada.ProyectoNombre}'.\n\n¿Desea ir ahora al Catálogo Visual para escoger y agregar artículos a este carrito?",
                "Carrito de Solicitud Creado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (irCat == System.Windows.MessageBoxResult.Yes)
            {
                CambiarSeccion(1);
            }
            return;
        }

        // Si ya tiene artículos, pasar directo a la Comparativa con la solicitud seleccionada
        CambiarSeccion(3);
        CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == guardada.Id);
    }

    private void BtnEnviarSolicitudAComparativa_Click(object sender, RoutedEventArgs e)
    {
        var sol = DgHistorialSolicitudes.SelectedItem as Solicitud
                  ?? DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
        if (sol == null)
        {
            System.Windows.MessageBox.Show(this, "Seleccione una solicitud de la lista.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (sol.Partidas.Count == 0)
        {
            System.Windows.MessageBox.Show(this, $"El carrito {sol.Folio} aún no tiene artículos agregados.\n\nAgregue artículos desde el buscador o desde el Catálogo Visual antes de abrir la Comparativa.", "Carrito sin artículos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CambiarSeccion(3);
        CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == sol.Id);
    }

    // ==================== TAB 3: COMPARATIVA DE COTIZACIONES POR PRODUCTO ====================

    private void CboCompSolicitud_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol)
        {
            if (TxtCompInfoSolicitud != null)
                TxtCompInfoSolicitud.Text = "Seleccione una Solicitud / Carrito en la lista desplegable superior para iniciar o editar su comparativa de proveedores.";
            _renglonesComparativa.Clear();
            CargarProductoEnTarjetasComparativa(null);
            RecalcularTotalesComparativa();
            return;
        }

        int prevPartidaId = (DgComparativa.SelectedItem as CotizacionRenglonComparativo)?.SolicitudPartidaId ?? 0;

        TxtCompInfoSolicitud.Text = $"Solicitud: {sol.Folio} | Depto: {sol.DepartamentoNombre} | Solicitante: {sol.EncargadoNombre} | Proyecto: {sol.ProyectoNombre} | Estatus: {sol.Estatus}";
        _renglonesComparativa.Clear();

        var productos = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var proveedores = DatabaseService.ObtenerProveedores();
        var provDict = proveedores.ToDictionary(p => p.Id);

        foreach (var part in sol.Partidas)
        {
            productos.TryGetValue(part.ProductoId, out var pObj);
            decimal ultimoCostoConIva = pObj?.UltimoCostoCompra ?? 0m;
            string familiaEfectiva = !string.IsNullOrWhiteSpace(part.Familia)
                ? part.Familia
                : (!string.IsNullOrWhiteSpace(pObj?.Familia) ? pObj.Familia : DatabaseService.ExtraerFamiliaSugerida(part.ProductoNombre));

            var (sugerencias, refHist) = DatabaseService.ObtenerSugerenciasCotizacionPorProducto(part.ProductoId, part.ProductoNombre, familiaEfectiva);

            int pv1 = part.Proveedor1Id;
            decimal pr1 = part.PrecioSinIva1;
            int d1 = part.DiasEntrega1 > 0 ? part.DiasEntrega1 : 2;
            string nc1 = part.NotasCotizacion1;

            int pv2 = part.Proveedor2Id;
            decimal pr2 = part.PrecioSinIva2;
            int d2 = part.DiasEntrega2 > 0 ? part.DiasEntrega2 : 3;
            string nc2 = part.NotasCotizacion2;

            int pv3 = part.Proveedor3Id;
            decimal pr3 = part.PrecioSinIva3;
            int d3 = part.DiasEntrega3 > 0 ? part.DiasEntrega3 : 1;
            string nc3 = part.NotasCotizacion3;

            bool sinProveedoresGuardados = pv1 == 0 && pv2 == 0 && pv3 == 0 && pr1 <= 0 && pr2 <= 0 && pr3 <= 0;
            if (sinProveedoresGuardados)
            {
                // Regla solicitada: Empezar por defecto SOLO con 1 opción.
                // Si el producto tiene histórico de compras, pre-llenar la Opción 1 con la mejor opción histórica;
                // de no tener histórico de compras, dejar la Opción 1 completamente en blanco.
                if (sugerencias.Count >= 1)
                {
                    pv1 = sugerencias[0].proveedorId;
                    pr1 = sugerencias[0].precioSinIva;
                    d1 = sugerencias[0].diasEntrega > 0 ? sugerencias[0].diasEntrega : 2;
                    nc1 = sugerencias[0].notaSugerida;
                }
                else
                {
                    pv1 = 0;
                    pr1 = 0m;
                    d1 = 2;
                    nc1 = string.Empty;
                }

                pv2 = 0; pr2 = 0m; d2 = 3; nc2 = string.Empty;
                pv3 = 0; pr3 = 0m; d3 = 1; nc3 = string.Empty;
            }

            int numOpcionesVisibles = 1;
            if (pv3 > 0 || pr3 > 0 || part.ProveedorGanadorIndice == 3)
                numOpcionesVisibles = 3;
            else if (pv2 > 0 || pr2 > 0 || part.ProveedorGanadorIndice == 2)
                numOpcionesVisibles = 2;

            var renglon = new CotizacionRenglonComparativo
            {
                SolicitudPartidaId = part.Id,
                ProductoId = part.ProductoId,
                ProductoCodigo = part.ProductoCodigo,
                ProductoNombre = part.ProductoNombre,
                DescripcionLibre = part.DescripcionLibre,
                Familia = familiaEfectiva,
                UnidadCompra = part.UnidadCompra,
                Cantidad = part.CantidadSolicitada,
                StockActualAlmacen = part.StockActualAlmacen,
                UltimoCostoCompraConIva = ultimoCostoConIva,
                ReferenciaHistorica = refHist,
                NumeroOpcionesVisibles = numOpcionesVisibles,

                Proveedor1Id = pv1,
                Proveedor1Nombre = provDict.TryGetValue(pv1, out var p1Obj) ? p1Obj.NombreMostrar : "Sin asignar",
                PrecioSinIva1 = pr1,
                DiasEntrega1 = d1,
                FleteConIva1 = part.FleteConIva1,
                NotasCotizacion1 = nc1,

                Proveedor2Id = pv2,
                Proveedor2Nombre = provDict.TryGetValue(pv2, out var p2Obj) ? p2Obj.NombreMostrar : "Sin asignar",
                PrecioSinIva2 = pr2,
                DiasEntrega2 = d2,
                FleteConIva2 = part.FleteConIva2,
                NotasCotizacion2 = nc2,

                Proveedor3Id = pv3,
                Proveedor3Nombre = provDict.TryGetValue(pv3, out var p3Obj) ? p3Obj.NombreMostrar : "Sin asignar",
                PrecioSinIva3 = pr3,
                DiasEntrega3 = d3,
                FleteConIva3 = part.FleteConIva3,
                NotasCotizacion3 = nc3,

                ProveedorGanadorIndice = part.ProveedorGanadorIndice,
                OrdenCompraId = part.OrdenCompraId,
                OrdenCompraFolio = part.OrdenCompraFolio
            };

            if (sinProveedoresGuardados && pv1 > 0 && renglon.SolicitudPartidaId > 0)
            {
                DatabaseService.GuardarCotizacionPartida(renglon);
            }

            _renglonesComparativa.Add(renglon);
        }

        if (_renglonesComparativa.Count > 0)
        {
            var aSeleccionar = _renglonesComparativa.FirstOrDefault(r => r.SolicitudPartidaId == prevPartidaId) ?? _renglonesComparativa[0];
            DgComparativa.SelectedItem = aSeleccionar;
            CargarProductoEnTarjetasComparativa(aSeleccionar);
        }
        else
        {
            CargarProductoEnTarjetasComparativa(null);
        }

        RecalcularTotalesComparativa();
    }

    private void DgComparativa_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa) return;
        CargarProductoEnTarjetasComparativa(DgComparativa.SelectedItem as CotizacionRenglonComparativo);
    }

    private void CargarProductoEnTarjetasComparativa(CotizacionRenglonComparativo? r)
    {
        if (TxtCompProdTitulo == null) return;

        _cargandoDetalleComparativa = true;
        try
        {
            if (r == null)
            {
                TxtCompProdTitulo.Text = "2. Seleccione un artículo de la lista izquierda";
                TxtCompProdSubtitulo.Text = "Seleccione primero una Solicitud arriba y luego un artículo de la tabla para cotizarlo.";
                TxtCompProdHistorial.Text = "";
                TxtCompProdCantidad.Text = "";
                if (CboCompReferenciasSimilares != null)
                {
                    CboCompReferenciasSimilares.ItemsSource = null;
                    CboCompReferenciasSimilares.SelectedIndex = -1;
                    EstablecerPlaceholderCombo(CboCompReferenciasSimilares, "Seleccione un artículo para consultar compras similares...");
                }
                if (CboCompProv1 != null) CboCompProv1.SelectedIndex = -1;
                if (CboCompProv2 != null) CboCompProv2.SelectedIndex = -1;
                if (CboCompProv3 != null) CboCompProv3.SelectedIndex = -1;
                return;
            }

            string famBadge = !string.IsNullOrWhiteSpace(r.Familia) ? $" | Familia: {r.Familia}" : string.Empty;
            TxtCompProdTitulo.Text = $"{r.ProductoCodigo} — {r.ProductoNombre}";
            TxtCompProdSubtitulo.Text = $"Unidad: {r.UnidadCompra}{famBadge} | Stock actual: {r.StockActualAlmacen:0.##} | Últ. Costo: {(r.UltimoCostoCompraConIva > 0 ? $"{r.UltimoCostoCompraConIva:C2} c/IVA" : "N/D")}";
            TxtCompProdHistorial.Text = r.ReferenciaHistorica;
            TxtCompProdCantidad.Text = r.Cantidad.ToString("0.##", CultureInfo.InvariantCulture);

            if (CboCompReferenciasSimilares != null)
            {
                var refsSimilares = DatabaseService.ObtenerReferenciasSimilaresParaComparativa(r.ProductoId, r.ProductoNombre, r.Familia);
                CboCompReferenciasSimilares.ItemsSource = refsSimilares;
                CboCompReferenciasSimilares.SelectedIndex = -1;
                EstablecerPlaceholderCombo(
                    CboCompReferenciasSimilares,
                    refsSimilares.Count > 0
                        ? $"🔎 {refsSimilares.Count} compra(s) similares encontradas — despliegue para elegir..."
                        : "Sin compras históricas similares registradas para esta familia/concepto");
            }

            var proveedores = CboCompProv1.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores();
            CboCompProv1.SelectedItem = r.Proveedor1Id > 0 ? proveedores.FirstOrDefault(p => p.Id == r.Proveedor1Id) : null;
            CboCompProv2.SelectedItem = r.Proveedor2Id > 0 ? proveedores.FirstOrDefault(p => p.Id == r.Proveedor2Id) : null;
            CboCompProv3.SelectedItem = r.Proveedor3Id > 0 ? proveedores.FirstOrDefault(p => p.Id == r.Proveedor3Id) : null;

            TxtCompPrecioSinIva1.Text = r.PrecioSinIva1 > 0 ? r.PrecioSinIva1.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompPrecioConIva1.Text = r.PrecioSinIva1 > 0 ? r.PrecioConIva1.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompFlete1.Text = r.FleteConIva1.ToString("0.00", CultureInfo.InvariantCulture);
            TxtCompDias1.Text = r.DiasEntrega1.ToString();
            TxtCompNotas1.Text = r.NotasCotizacion1;

            TxtCompPrecioSinIva2.Text = r.PrecioSinIva2 > 0 ? r.PrecioSinIva2.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompPrecioConIva2.Text = r.PrecioSinIva2 > 0 ? r.PrecioConIva2.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompFlete2.Text = r.FleteConIva2.ToString("0.00", CultureInfo.InvariantCulture);
            TxtCompDias2.Text = r.DiasEntrega2.ToString();
            TxtCompNotas2.Text = r.NotasCotizacion2;

            TxtCompPrecioSinIva3.Text = r.PrecioSinIva3 > 0 ? r.PrecioSinIva3.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompPrecioConIva3.Text = r.PrecioSinIva3 > 0 ? r.PrecioConIva3.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompFlete3.Text = r.FleteConIva3.ToString("0.00", CultureInfo.InvariantCulture);
            TxtCompDias3.Text = r.DiasEntrega3.ToString();
            TxtCompNotas3.Text = r.NotasCotizacion3;

            bool editable = !r.TieneOcGenerada;
            TxtCompProdCantidad.IsEnabled = editable;
            CboCompProv1.IsEnabled = editable;
            CboCompProv2.IsEnabled = editable;
            CboCompProv3.IsEnabled = editable;
            TxtCompPrecioSinIva1.IsEnabled = editable;
            TxtCompPrecioConIva1.IsEnabled = editable;
            TxtCompPrecioSinIva2.IsEnabled = editable;
            TxtCompPrecioConIva2.IsEnabled = editable;
            TxtCompPrecioSinIva3.IsEnabled = editable;
            TxtCompPrecioConIva3.IsEnabled = editable;
            TxtCompFlete1.IsEnabled = editable;
            TxtCompFlete2.IsEnabled = editable;
            TxtCompFlete3.IsEnabled = editable;
            TxtCompDias1.IsEnabled = editable;
            TxtCompDias2.IsEnabled = editable;
            TxtCompDias3.IsEnabled = editable;
            TxtCompNotas1.IsEnabled = editable;
            TxtCompNotas2.IsEnabled = editable;
            TxtCompNotas3.IsEnabled = editable;
            BtnElegirGanador1.IsEnabled = editable;
            BtnElegirGanador2.IsEnabled = editable;
            BtnElegirGanador3.IsEnabled = editable;

            ActualizarVisibilidadTarjetasComparativa(r);
            ActualizarResumenYAparienciaTarjetasProducto(r);
        }
        finally
        {
            _cargandoDetalleComparativa = false;
        }
    }

    private void ActualizarVisibilidadTarjetasComparativa(CotizacionRenglonComparativo r)
    {
        if (BdrCompCard2 == null || BdrCompCard3 == null || BtnAgregarOpcionProveedorComp == null)
            return;

        int n = Math.Clamp(r.NumeroOpcionesVisibles, 1, 3);
        BdrCompCard2.Visibility = n >= 2 ? Visibility.Visible : Visibility.Collapsed;
        BdrCompCard3.Visibility = n >= 3 ? Visibility.Visible : Visibility.Collapsed;

        bool editable = !r.TieneOcGenerada;
        BtnAgregarOpcionProveedorComp.IsEnabled = editable && n < 3;
        BtnAgregarOpcionProveedorComp.Content = n < 3
            ? $"➕ Añadir Opción {n + 1} de Proveedor"
            : "✔ 3 Opciones Activas";
    }

    private void BtnAgregarOpcionProveedorComp_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;

        if (r.NumeroOpcionesVisibles >= 3)
            return;

        r.NumeroOpcionesVisibles++;

        // Si existen más sugerencias históricas para este producto o su familia, podemos sugerir la siguiente opción histórica
        var (sugerencias, _) = DatabaseService.ObtenerSugerenciasCotizacionPorProducto(r.ProductoId, r.ProductoNombre, r.Familia);
        var provDict = (CboCompProv1.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores()).ToDictionary(p => p.Id);

        if (r.NumeroOpcionesVisibles == 2 && r.Proveedor2Id == 0 && sugerencias.Count >= 2)
        {
            var sug2 = sugerencias.FirstOrDefault(s => s.proveedorId != r.Proveedor1Id);
            if (sug2.proveedorId > 0)
            {
                r.Proveedor2Id = sug2.proveedorId;
                r.Proveedor2Nombre = provDict.TryGetValue(r.Proveedor2Id, out var p2) ? p2.NombreMostrar : "Sin asignar";
                r.PrecioSinIva2 = sug2.precioSinIva;
                r.DiasEntrega2 = sug2.diasEntrega > 0 ? sug2.diasEntrega : 3;
                r.NotasCotizacion2 = sug2.notaSugerida;
                DatabaseService.GuardarCotizacionPartida(r);
            }
        }
        else if (r.NumeroOpcionesVisibles == 3 && r.Proveedor3Id == 0 && sugerencias.Count >= 3)
        {
            var sug3 = sugerencias.FirstOrDefault(s => s.proveedorId != r.Proveedor1Id && s.proveedorId != r.Proveedor2Id);
            if (sug3.proveedorId > 0)
            {
                r.Proveedor3Id = sug3.proveedorId;
                r.Proveedor3Nombre = provDict.TryGetValue(r.Proveedor3Id, out var p3) ? p3.NombreMostrar : "Sin asignar";
                r.PrecioSinIva3 = sug3.precioSinIva;
                r.DiasEntrega3 = sug3.diasEntrega > 0 ? sug3.diasEntrega : 1;
                r.NotasCotizacion3 = sug3.notaSugerida;
                DatabaseService.GuardarCotizacionPartida(r);
            }
        }

        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
    }

    private void BtnAplicarReferenciaSimilarOp1_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;
        if (CboCompReferenciasSimilares?.SelectedItem is not ReferenciaSimilarHistorica refSim)
        {
            System.Windows.MessageBox.Show(this, "No hay referencias históricas de compras similares para este artículo.", "Sin referencias similares", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var provDict = (CboCompProv1.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores()).ToDictionary(p => p.Id);
        r.Proveedor1Id = refSim.ProveedorId;
        r.Proveedor1Nombre = provDict.TryGetValue(refSim.ProveedorId, out var p1) ? p1.NombreMostrar : refSim.ProveedorNombre;
        r.PrecioSinIva1 = refSim.PrecioSinIva;
        r.DiasEntrega1 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 2;
        r.NotasCotizacion1 = refSim.ReferenciaOrigen;

        DatabaseService.GuardarCotizacionPartida(r);
        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
        DgComparativa.Items.Refresh();
    }

    private void BtnAplicarReferenciaSimilarNuevaOp_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;
        if (CboCompReferenciasSimilares?.SelectedItem is not ReferenciaSimilarHistorica refSim)
        {
            System.Windows.MessageBox.Show(this, "No hay referencias históricas de compras similares para este artículo.", "Sin referencias similares", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var provDict = (CboCompProv1.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores()).ToDictionary(p => p.Id);
        string provNombre = provDict.TryGetValue(refSim.ProveedorId, out var pObj) ? pObj.NombreMostrar : refSim.ProveedorNombre;

        if (r.Proveedor1Id == 0 && r.PrecioSinIva1 <= 0)
        {
            r.Proveedor1Id = refSim.ProveedorId;
            r.Proveedor1Nombre = provNombre;
            r.PrecioSinIva1 = refSim.PrecioSinIva;
            r.DiasEntrega1 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 2;
            r.NotasCotizacion1 = refSim.ReferenciaOrigen;
        }
        else if (r.NumeroOpcionesVisibles < 2 || (r.Proveedor2Id == 0 && r.PrecioSinIva2 <= 0))
        {
            if (r.NumeroOpcionesVisibles < 2) r.NumeroOpcionesVisibles = 2;
            r.Proveedor2Id = refSim.ProveedorId;
            r.Proveedor2Nombre = provNombre;
            r.PrecioSinIva2 = refSim.PrecioSinIva;
            r.DiasEntrega2 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 3;
            r.NotasCotizacion2 = refSim.ReferenciaOrigen;
        }
        else
        {
            r.NumeroOpcionesVisibles = 3;
            r.Proveedor3Id = refSim.ProveedorId;
            r.Proveedor3Nombre = provNombre;
            r.PrecioSinIva3 = refSim.PrecioSinIva;
            r.DiasEntrega3 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 1;
            r.NotasCotizacion3 = refSim.ReferenciaOrigen;
        }

        DatabaseService.GuardarCotizacionPartida(r);
        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
        DgComparativa.Items.Refresh();
    }

    private void BtnQuitarOpcion2Comp_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;

        if (r.NumeroOpcionesVisibles == 3)
        {
            // Mover la opción 3 al lugar de la opción 2
            r.Proveedor2Id = r.Proveedor3Id;
            r.Proveedor2Nombre = r.Proveedor3Nombre;
            r.PrecioSinIva2 = r.PrecioSinIva3;
            r.FleteConIva2 = r.FleteConIva3;
            r.DiasEntrega2 = r.DiasEntrega3;
            r.NotasCotizacion2 = r.NotasCotizacion3;

            r.Proveedor3Id = 0;
            r.Proveedor3Nombre = "Sin asignar";
            r.PrecioSinIva3 = 0m;
            r.FleteConIva3 = 0m;
            r.DiasEntrega3 = 1;
            r.NotasCotizacion3 = string.Empty;

            if (r.ProveedorGanadorIndice == 2) r.ProveedorGanadorIndice = 0;
            else if (r.ProveedorGanadorIndice == 3) r.ProveedorGanadorIndice = 2;

            r.NumeroOpcionesVisibles = 2;
        }
        else
        {
            r.Proveedor2Id = 0;
            r.Proveedor2Nombre = "Sin asignar";
            r.PrecioSinIva2 = 0m;
            r.FleteConIva2 = 0m;
            r.DiasEntrega2 = 3;
            r.NotasCotizacion2 = string.Empty;
            if (r.ProveedorGanadorIndice == 2) r.ProveedorGanadorIndice = 0;
            r.NumeroOpcionesVisibles = 1;
        }

        DatabaseService.GuardarCotizacionPartida(r);
        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
    }

    private void BtnQuitarOpcion3Comp_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;

        r.Proveedor3Id = 0;
        r.Proveedor3Nombre = "Sin asignar";
        r.PrecioSinIva3 = 0m;
        r.FleteConIva3 = 0m;
        r.DiasEntrega3 = 1;
        r.NotasCotizacion3 = string.Empty;
        if (r.ProveedorGanadorIndice == 3) r.ProveedorGanadorIndice = 0;
        r.NumeroOpcionesVisibles = 2;

        DatabaseService.GuardarCotizacionPartida(r);
        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
    }

    private void ActualizarResumenYAparienciaTarjetasProducto(CotizacionRenglonComparativo r)
    {
        if (TxtResumenProv1 == null) return;

        decimal sub1 = Math.Round(r.Cantidad * r.PrecioSinIva1, 2);
        decimal sub2 = Math.Round(r.Cantidad * r.PrecioSinIva2, 2);
        decimal sub3 = Math.Round(r.Cantidad * r.PrecioSinIva3, 2);

        TxtResumenProv1.Text = $"Subtotal ({r.Cantidad:0.##} {r.UnidadCompra}): ${sub1:N2}\nPartida c/IVA: ${r.TotalPartidaConIva1:N2}\nTotal + Envío: ${r.TotalConFlete1:N2}";
        TxtResumenProv2.Text = $"Subtotal ({r.Cantidad:0.##} {r.UnidadCompra}): ${sub2:N2}\nPartida c/IVA: ${r.TotalPartidaConIva2:N2}\nTotal + Envío: ${r.TotalConFlete2:N2}";
        TxtResumenProv3.Text = $"Subtotal ({r.Cantidad:0.##} {r.UnidadCompra}): ${sub3:N2}\nPartida c/IVA: ${r.TotalPartidaConIva3:N2}\nTotal + Envío: ${r.TotalConFlete3:N2}";

        BdrBadgeGanador1.Visibility = r.ProveedorGanadorIndice == 1 ? Visibility.Visible : Visibility.Collapsed;
        BdrBadgeGanador2.Visibility = r.ProveedorGanadorIndice == 2 ? Visibility.Visible : Visibility.Collapsed;
        BdrBadgeGanador3.Visibility = r.ProveedorGanadorIndice == 3 ? Visibility.Visible : Visibility.Collapsed;

        BdrCompCard1.BorderThickness = new Thickness(r.ProveedorGanadorIndice == 1 ? 2.8 : 1.2);
        BdrCompCard2.BorderThickness = new Thickness(r.ProveedorGanadorIndice == 2 ? 2.8 : 1.2);
        BdrCompCard3.BorderThickness = new Thickness(r.ProveedorGanadorIndice == 3 ? 2.8 : 1.2);

        BtnElegirGanador1.Appearance = r.ProveedorGanadorIndice == 1 ? ControlAppearance.Primary : ControlAppearance.Secondary;
        BtnElegirGanador2.Appearance = r.ProveedorGanadorIndice == 2 ? ControlAppearance.Primary : ControlAppearance.Secondary;
        BtnElegirGanador3.Appearance = r.ProveedorGanadorIndice == 3 ? ControlAppearance.Primary : ControlAppearance.Secondary;

        BtnElegirGanador1.Content = r.ProveedorGanadorIndice == 1 ? "★ Opción 1 Elegida" : "✓ Elegir Opción 1";
        BtnElegirGanador2.Content = r.ProveedorGanadorIndice == 2 ? "★ Opción 2 Elegida" : "✓ Elegir Opción 2";
        BtnElegirGanador3.Content = r.ProveedorGanadorIndice == 3 ? "★ Opción 3 Elegida" : "✓ Elegir Opción 3";
    }

    private void SincronizarRenglonActualDesdeTarjetas()
    {
        if (_cargandoDetalleComparativa || DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;

        decimal cant = ParseDecimal(TxtCompProdCantidad.Text, r.Cantidad);
        if (cant > 0) r.Cantidad = cant;

        if (CboCompProv1.SelectedItem is Proveedor p1)
        {
            r.Proveedor1Id = p1.Id;
            r.Proveedor1Nombre = p1.NombreMostrar;
        }
        else
        {
            r.Proveedor1Id = 0;
            r.Proveedor1Nombre = "Sin asignar";
        }

        if (r.NumeroOpcionesVisibles >= 2 && CboCompProv2.SelectedItem is Proveedor p2)
        {
            r.Proveedor2Id = p2.Id;
            r.Proveedor2Nombre = p2.NombreMostrar;
        }
        else if (r.NumeroOpcionesVisibles < 2)
        {
            r.Proveedor2Id = 0;
            r.Proveedor2Nombre = "Sin asignar";
        }

        if (r.NumeroOpcionesVisibles >= 3 && CboCompProv3.SelectedItem is Proveedor p3)
        {
            r.Proveedor3Id = p3.Id;
            r.Proveedor3Nombre = p3.NombreMostrar;
        }
        else if (r.NumeroOpcionesVisibles < 3)
        {
            r.Proveedor3Id = 0;
            r.Proveedor3Nombre = "Sin asignar";
        }

        r.PrecioSinIva1 = ParseDecimal(TxtCompPrecioSinIva1.Text, 0m);
        r.PrecioSinIva2 = r.NumeroOpcionesVisibles >= 2 ? ParseDecimal(TxtCompPrecioSinIva2.Text, 0m) : 0m;
        r.PrecioSinIva3 = r.NumeroOpcionesVisibles >= 3 ? ParseDecimal(TxtCompPrecioSinIva3.Text, 0m) : 0m;

        r.FleteConIva1 = ParseDecimal(TxtCompFlete1.Text, 0m);
        r.FleteConIva2 = r.NumeroOpcionesVisibles >= 2 ? ParseDecimal(TxtCompFlete2.Text, 0m) : 0m;
        r.FleteConIva3 = r.NumeroOpcionesVisibles >= 3 ? ParseDecimal(TxtCompFlete3.Text, 0m) : 0m;

        r.DiasEntrega1 = Math.Max(1, (int)ParseDecimal(TxtCompDias1.Text, 2m));
        r.DiasEntrega2 = Math.Max(1, (int)ParseDecimal(TxtCompDias2.Text, 3m));
        r.DiasEntrega3 = Math.Max(1, (int)ParseDecimal(TxtCompDias3.Text, 1m));

        r.NotasCotizacion1 = TxtCompNotas1.Text ?? string.Empty;
        r.NotasCotizacion2 = r.NumeroOpcionesVisibles >= 2 ? (TxtCompNotas2.Text ?? string.Empty) : string.Empty;
        r.NotasCotizacion3 = r.NumeroOpcionesVisibles >= 3 ? (TxtCompNotas3.Text ?? string.Empty) : string.Empty;

        DatabaseService.GuardarCotizacionPartida(r);
        ActualizarResumenYAparienciaTarjetasProducto(r);
        RecalcularTotalesComparativa();
    }

    private void TxtCompProdCantidad_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa) return;
        SincronizarRenglonActualDesdeTarjetas();
    }

    private void CboCompProveedores_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa) return;
        SincronizarRenglonActualDesdeTarjetas();
    }

    private void TxtCompPrecioSinIva1_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _actualizandoPreciosIvaComp) return;
        _actualizandoPreciosIvaComp = true;
        try
        {
            decimal sinIva = ParseDecimal(TxtCompPrecioSinIva1.Text, 0m);
            TxtCompPrecioConIva1.Text = Math.Round(sinIva * 1.16m, 2).ToString("0.00", CultureInfo.InvariantCulture);
            SincronizarRenglonActualDesdeTarjetas();
        }
        finally { _actualizandoPreciosIvaComp = false; }
    }

    private void TxtCompPrecioConIva1_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _actualizandoPreciosIvaComp) return;
        _actualizandoPreciosIvaComp = true;
        try
        {
            decimal conIva = ParseDecimal(TxtCompPrecioConIva1.Text, 0m);
            TxtCompPrecioSinIva1.Text = Math.Round(conIva / 1.16m, 2).ToString("0.00", CultureInfo.InvariantCulture);
            SincronizarRenglonActualDesdeTarjetas();
        }
        finally { _actualizandoPreciosIvaComp = false; }
    }

    private void TxtCompPrecioSinIva2_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _actualizandoPreciosIvaComp) return;
        _actualizandoPreciosIvaComp = true;
        try
        {
            decimal sinIva = ParseDecimal(TxtCompPrecioSinIva2.Text, 0m);
            TxtCompPrecioConIva2.Text = Math.Round(sinIva * 1.16m, 2).ToString("0.00", CultureInfo.InvariantCulture);
            SincronizarRenglonActualDesdeTarjetas();
        }
        finally { _actualizandoPreciosIvaComp = false; }
    }

    private void TxtCompPrecioConIva2_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _actualizandoPreciosIvaComp) return;
        _actualizandoPreciosIvaComp = true;
        try
        {
            decimal conIva = ParseDecimal(TxtCompPrecioConIva2.Text, 0m);
            TxtCompPrecioSinIva2.Text = Math.Round(conIva / 1.16m, 2).ToString("0.00", CultureInfo.InvariantCulture);
            SincronizarRenglonActualDesdeTarjetas();
        }
        finally { _actualizandoPreciosIvaComp = false; }
    }

    private void TxtCompPrecioSinIva3_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _actualizandoPreciosIvaComp) return;
        _actualizandoPreciosIvaComp = true;
        try
        {
            decimal sinIva = ParseDecimal(TxtCompPrecioSinIva3.Text, 0m);
            TxtCompPrecioConIva3.Text = Math.Round(sinIva * 1.16m, 2).ToString("0.00", CultureInfo.InvariantCulture);
            SincronizarRenglonActualDesdeTarjetas();
        }
        finally { _actualizandoPreciosIvaComp = false; }
    }

    private void TxtCompPrecioConIva3_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _actualizandoPreciosIvaComp) return;
        _actualizandoPreciosIvaComp = true;
        try
        {
            decimal conIva = ParseDecimal(TxtCompPrecioConIva3.Text, 0m);
            TxtCompPrecioSinIva3.Text = Math.Round(conIva / 1.16m, 2).ToString("0.00", CultureInfo.InvariantCulture);
            SincronizarRenglonActualDesdeTarjetas();
        }
        finally { _actualizandoPreciosIvaComp = false; }
    }

    private void TxtCompExtras_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa) return;
        SincronizarRenglonActualDesdeTarjetas();
    }

    private void ElegirOpcionGanadoraProductoActual(int indiceOpcion)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r)
            return;

        if (r.TieneOcGenerada)
        {
            System.Windows.MessageBox.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {r.OrdenCompraFolio}.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SincronizarRenglonActualDesdeTarjetas();

        if (indiceOpcion >= 1 && indiceOpcion <= 3)
        {
            int provId = indiceOpcion switch { 1 => r.Proveedor1Id, 2 => r.Proveedor2Id, 3 => r.Proveedor3Id, _ => 0 };
            decimal precio = indiceOpcion switch { 1 => r.PrecioSinIva1, 2 => r.PrecioSinIva2, 3 => r.PrecioSinIva3, _ => 0m };

            if (provId <= 0)
            {
                System.Windows.MessageBox.Show(this, $"Seleccione primero un Proveedor en la Opción {indiceOpcion}.", "Proveedor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (precio <= 0)
            {
                System.Windows.MessageBox.Show(this, $"Capture primero un precio unitario mayor a $0.00 en la Opción {indiceOpcion}.", "Precio requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        r.ProveedorGanadorIndice = indiceOpcion;
        DatabaseService.GuardarCotizacionPartida(r);
        ActualizarResumenYAparienciaTarjetasProducto(r);
        RecalcularTotalesComparativa();
    }

    private void BtnElegirGanador1_Click(object sender, RoutedEventArgs e) => ElegirOpcionGanadoraProductoActual(1);
    private void BtnElegirGanador2_Click(object sender, RoutedEventArgs e) => ElegirOpcionGanadoraProductoActual(2);
    private void BtnElegirGanador3_Click(object sender, RoutedEventArgs e) => ElegirOpcionGanadoraProductoActual(3);
    private void BtnLimpiarGanadorProductoActual_Click(object sender, RoutedEventArgs e) => ElegirOpcionGanadoraProductoActual(0);

    private void BtnMejorPrecioProductoActual_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;

        SincronizarRenglonActualDesdeTarjetas();
        int mejorIdx = DeterminarIndiceMejorPrecio(r);
        if (mejorIdx == 0)
        {
            System.Windows.MessageBox.Show(this, "Capture al menos un proveedor con precio mayor a $0.00 en las opciones de este artículo.", "Sin precios capturados", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ElegirOpcionGanadoraProductoActual(mejorIdx);
    }

    private static int DeterminarIndiceMejorPrecio(CotizacionRenglonComparativo r)
    {
        var candidatos = new List<(int idx, decimal costoTotal)>();
        if (r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0) candidatos.Add((1, r.TotalConFlete1));
        if (r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0) candidatos.Add((2, r.TotalConFlete2));
        if (r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0) candidatos.Add((3, r.TotalConFlete3));

        return candidatos.Count > 0 ? candidatos.OrderBy(c => c.costoTotal).First().idx : 0;
    }

    private void DgComparativa_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DgComparativa.SelectedItem is CotizacionRenglonComparativo r && !r.TieneOcGenerada)
            {
                DatabaseService.GuardarCotizacionPartida(r);
                CargarProductoEnTarjetasComparativa(r);
            }
            RecalcularTotalesComparativa();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RecalcularTotalesComparativa()
    {
        if (DgPrevisualizacionOcs == null)
            return;

        DgComparativa.Items.Refresh();

        var decididosSinOc = _renglonesComparativa
            .Where(r => !r.TieneOcGenerada && r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorIndice <= 3 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .ToList();

        var resumenesOc = decididosSinOc
            .GroupBy(r => new { r.ProveedorGanadorId, r.ProveedorGanadorNombre })
            .Select(g =>
            {
                decimal subPartidas = g.Sum(x => Math.Round(x.Cantidad * x.PrecioUnitarioGanadorSinIva, 2));
                decimal fleteConIva = g.Sum(x => x.FleteGanadorConIva);
                decimal fleteSinIva = Math.Round(fleteConIva / 1.16m, 2);
                decimal subTotal = Math.Round(subPartidas + fleteSinIva, 2);
                decimal iva = Math.Round(subTotal * 0.16m, 2);
                decimal total = Math.Round(subTotal + iva, 2);

                return new ResumenOcPrevia
                {
                    ProveedorId = g.Key.ProveedorGanadorId,
                    ProveedorNombre = g.Key.ProveedorGanadorNombre,
                    CantidadPartidas = g.Count(),
                    ProductosResumen = string.Join(", ", g.Select(x => x.ProductoCodigo)),
                    DiasEntregaMax = g.Max(x => x.DiasEntregaGanador),
                    SubtotalSinIva = subTotal,
                    FleteConIva = fleteConIva,
                    IvaTotal = iva,
                    TotalConIva = total
                };
            })
            .OrderByDescending(x => x.TotalConIva)
            .ToList();

        DgPrevisualizacionOcs.ItemsSource = resumenesOc;

        int totalProductos = _renglonesComparativa.Count;
        int yaConOc = _renglonesComparativa.Count(r => r.TieneOcGenerada);
        int listosParaOc = decididosSinOc.Count;
        int pendientesElegir = totalProductos - yaConOc - listosParaOc;

        if (TxtCompAvanceDecision != null)
        {
            TxtCompAvanceDecision.Text = $"Avance: {listosParaOc} decididos listos para emitir OC | {pendientesElegir} pendientes de elegir | {yaConOc} ya con OC emitida";
        }

        decimal granTotalOcs = resumenesOc.Sum(x => x.TotalConIva);
        if (TxtResumenGeneralComparativa != null)
        {
            TxtResumenGeneralComparativa.Text = $"{resumenesOc.Count} OC(s) por emitir | Total: {granTotalOcs:C2}";
        }

        if (BtnGenerarOrdenesDesdeComparativa != null)
        {
            BtnGenerarOrdenesDesdeComparativa.IsEnabled = listosParaOc > 0;
            BtnGenerarOrdenesDesdeComparativa.Content = listosParaOc > 0
                ? $"Emitir {resumenesOc.Count} OC(s) ({listosParaOc} Productos Decididos)"
                : (yaConOc == totalProductos && totalProductos > 0 ? "Todas las OCs ya fueron emitidas" : "Elija Ganador en al menos 1 producto");
        }
    }

    private void BtnSeleccionarMejorPrecioPorPartida_Click(object sender, RoutedEventArgs e)
    {
        SincronizarRenglonActualDesdeTarjetas();

        int asignados = 0;
        foreach (var r in _renglonesComparativa)
        {
            if (r.TieneOcGenerada) continue;
            int mejorIdx = DeterminarIndiceMejorPrecio(r);
            if (mejorIdx > 0)
            {
                r.ProveedorGanadorIndice = mejorIdx;
                DatabaseService.GuardarCotizacionPartida(r);
                asignados++;
            }
        }

        if (DgComparativa.SelectedItem is CotizacionRenglonComparativo sel)
        {
            ActualizarResumenYAparienciaTarjetasProducto(sel);
        }
        RecalcularTotalesComparativa();

        if (asignados == 0)
        {
            System.Windows.MessageBox.Show(this, "Capture precios mayores a $0.00 en los productos pendientes para elegir automáticamente el mejor precio.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnGuardarBorradorComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol)
            return;

        SincronizarRenglonActualDesdeTarjetas();
        DatabaseService.GuardarBorradorComparativa(sol.Id, _renglonesComparativa.ToList());
        RecalcularTotalesComparativa();
        System.Windows.MessageBox.Show(this, $"Se guardó el borrador de la comparativa para la solicitud {sol.Folio}.\n\nPuede continuar cotizando más tarde o emitir Órdenes de Compra parciales cuando lo desee.", "Borrador Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnGenerarOrdenesDesdeComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
            return;

        SincronizarRenglonActualDesdeTarjetas();

        var decididosSinOc = _renglonesComparativa
            .Where(r => !r.TieneOcGenerada && r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorIndice <= 3 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .ToList();

        if (decididosSinOc.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Elija primero el proveedor ganador (con precio mayor a $0.00) en al menos un producto pendiente.", "Sin productos decididos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int pendientesRestantes = _renglonesComparativa.Count(r => !r.TieneOcGenerada) - decididosSinOc.Count;
        int numOcs = decididosSinOc.Select(r => r.ProveedorGanadorId).Distinct().Count();

        if (pendientesRestantes > 0)
        {
            var resp = System.Windows.MessageBox.Show(this,
                $"Se emitirán {numOcs} Orden(es) de Compra para los {decididosSinOc.Count} productos ya decididos.\n\nLos {pendientesRestantes} productos restantes quedarán pendientes en esta misma solicitud ({sol.Folio}) para que pueda cotizarlos y emitir su OC después.\n\n¿Desea emitir las Órdenes de Compra ahora?",
                "Emisión Parcial de Órdenes de Compra",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp != System.Windows.MessageBoxResult.Yes)
                return;
        }

        string folioComp = $"COT-{DateTime.Now:yyyy}-{sol.Id:D4}";
        var ordenesCreadas = DatabaseService.GenerarOrdenesDesdeComparativa(sol, folioComp, _renglonesComparativa.ToList());

        CargarTodoElSistema();
        CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == sol.Id);

        if (pendientesRestantes == 0)
        {
            CambiarSeccion(4);
        }

        foreach (var oc in ordenesCreadas)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, oc);
        }
    }

    // ==================== TAB 4: ÓRDENES DE COMPRA Y RECEPCIONES PARCIALES ====================

    private void RefrescarOrdenesYEntradas()
    {
        int prevOcId = (DgOrdenesCompra.SelectedItem as OrdenCompra)?.Id ?? 0;
        var ocs = DatabaseService.ObtenerOrdenesCompra();
        DgOrdenesCompra.ItemsSource = ocs;
        if (prevOcId > 0)
        {
            var match = ocs.FirstOrDefault(o => o.Id == prevOcId);
            if (match != null) DgOrdenesCompra.SelectedItem = match;
        }
        if (DgOrdenesCompra.SelectedIndex < 0 && ocs.Count > 0)
            DgOrdenesCompra.SelectedIndex = 0;

        RefrescarHistorialEntradasOc();
    }

    private void RefrescarHistorialEntradasOc()
    {
        if (DgEntradasHistorial == null) return;
        var entradas = DatabaseService.ObtenerEntradasAlmacen();
        if (ChkFiltrarEntradasPorOc?.IsChecked == true && DgOrdenesCompra.SelectedItem is OrdenCompra ocSel)
        {
            entradas = entradas.Where(e => e.OrdenCompraId == ocSel.Id || string.Equals(e.OrdenCompraFolio, ocSel.Folio, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        DgEntradasHistorial.ItemsSource = entradas;
    }

    private void ChkFiltrarEntradasPorOc_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RefrescarHistorialEntradasOc();
    }

    private void DgOrdenesCompra_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra oc)
        {
            TxtTituloRecepcionOc.Text = $"Recepción — {oc.Folio} ({oc.AvanceRecepcionTexto})";
            DgPartidasRecepcionOc.ItemsSource = oc.Partidas;
            RefrescarHistorialEntradasOc();
        }
    }

    private void BtnRecibirTodoPendienteOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc) return;
        foreach (var p in oc.Partidas)
        {
            p.CantidadRecibirAhora = Math.Max(0m, p.CantidadPendiente);
        }
        DgPartidasRecepcionOc.Items.Refresh();
    }

    private void BtnLimpiarCantidadesRecepcionOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc) return;
        foreach (var p in oc.Partidas)
        {
            p.CantidadRecibirAhora = 0m;
        }
        DgPartidasRecepcionOc.Items.Refresh();
    }

    private void BtnImprimirOcSeleccionada_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra oc)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, oc);
        }
    }

    private void BtnRegistrarRecepcionOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
            return;

        if (oc.Estatus == "Cancelada")
        {
            System.Windows.MessageBox.Show(this, "Esta Orden de Compra se encuentra Cancelada y no admite recepciones.", "OC Cancelada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var aRecibir = oc.Partidas
            .Where(p => p.CantidadRecibirAhora > 0)
            .Select(p => (p, p.CantidadRecibirAhora))
            .ToList();

        if (aRecibir.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Indique en la columna 'Recibir Hoy' al menos una cantidad mayor a 0 (puede recibir entregas parciales o usar el botón 'Recibir Todo lo Pendiente').", "Sin cantidades por recibir", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var conceptosAbiertosPorRecibir = aRecibir.Where(x => x.p.ProductoId <= 0).Select(x => x.p).ToList();
        if (conceptosAbiertosPorRecibir.Count > 0)
        {
            if (!ConfirmarNombresConceptosAbiertosEnRecepcion(oc, conceptosAbiertosPorRecibir))
                return;
        }

        string remision = TxtFolioRemisionRecepcionOc?.Text?.Trim() ?? string.Empty;
        string notas = TxtNotasRecepcionOc.Text?.Trim() ?? string.Empty;
        string notasCompletas = !string.IsNullOrWhiteSpace(remision)
            ? (string.IsNullOrWhiteSpace(notas) ? $"Remisión/Nota Proveedor: {remision}" : $"Remisión/Nota: {remision} | {notas}")
            : notas;

        var entrada = DatabaseService.RegistrarRecepcionOrdenCompra(oc, aRecibir, notasCompletas);
        TxtNotasRecepcionOc.Clear();
        TxtFolioRemisionRecepcionOc?.Clear();
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, entrada);
    }

    private bool ConfirmarNombresConceptosAbiertosEnRecepcion(OrdenCompra oc, List<OrdenCompraPartida> conceptosAbiertos)
    {
        var win = new Window
        {
            Title = $"Confirmar Alta en Catálogo de Conceptos Abiertos — {oc.Folio}",
            Width = 560,
            Height = Math.Min(540, 230 + conceptosAbiertos.Count * 86),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new DockPanel { Margin = new Thickness(18) };
        var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        topStack.Children.Add(new TextBlock
        {
            Text = "✨ Definir Nombre Específico para el Catálogo (PRD-xxxx)",
            FontSize = 15,
            FontWeight = FontWeights.Bold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = $"Esta Orden de Compra ({oc.ProveedorNombre}) incluye {conceptosAbiertos.Count} concepto(s) abierto(s). Puede confirmar o detallar el nombre/marca real comprado para que se registre correctamente en el Catálogo:",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(topStack, Dock.Top);
        root.Children.Add(topStack);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var btnCancelar = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnConfirmar = new System.Windows.Controls.Button { Content = "Confirmar y Crear en Catálogo", Padding = new Thickness(16, 6, 16, 6), FontWeight = FontWeights.Bold };
        btnPanel.Children.Add(btnCancelar);
        btnPanel.Children.Add(btnConfirmar);
        DockPanel.SetDock(btnPanel, Dock.Bottom);
        root.Children.Add(btnPanel);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var itemsPanel = new StackPanel();
        var editores = new List<(OrdenCompraPartida partida, System.Windows.Controls.TextBox txtNombre, System.Windows.Controls.TextBox txtUnidad)>();

        foreach (var part in conceptosAbiertos)
        {
            var card = new Border
            {
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
                BorderThickness = new Thickness(1)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"Concepto solicitado: {part.ProductoNombre} ({part.CantidadRecibirAhora:0.##} {part.UnidadCompra} a {part.PrecioUnitarioConIva:C2} c/IVA)",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var txtNom = new System.Windows.Controls.TextBox
            {
                Text = !string.IsNullOrWhiteSpace(part.DescripcionLibre) ? part.DescripcionLibre : part.ProductoNombre,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Nombre específico del artículo (ej. Jabón Líquido Salvo 900ml)"
            };
            var txtUni = new System.Windows.Controls.TextBox
            {
                Text = string.IsNullOrWhiteSpace(part.UnidadCompra) ? "Pieza" : part.UnidadCompra,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Unidad de compra/almacén"
            };
            Grid.SetColumn(txtNom, 0);
            Grid.SetColumn(txtUni, 1);
            rowGrid.Children.Add(txtNom);
            rowGrid.Children.Add(txtUni);
            sp.Children.Add(rowGrid);
            card.Child = sp;
            itemsPanel.Children.Add(card);
            editores.Add((part, txtNom, txtUni));
        }

        scroll.Content = itemsPanel;
        root.Children.Add(scroll);

        btnCancelar.Click += (_, _) => win.DialogResult = false;
        btnConfirmar.Click += (_, _) =>
        {
            foreach (var (partida, txtNombre, txtUnidad) in editores)
            {
                string nom = txtNombre.Text.Trim();
                if (!string.IsNullOrWhiteSpace(nom))
                {
                    partida.DescripcionLibre = nom;
                    partida.ProductoNombre = nom;
                }
                string uni = txtUnidad.Text.Trim();
                if (!string.IsNullOrWhiteSpace(uni))
                {
                    partida.UnidadCompra = uni;
                    partida.UnidadAlmacen = uni;
                }
            }
            win.DialogResult = true;
        };

        win.Content = root;
        return win.ShowDialog() == true;
    }

    private void BtnForzarEstatusOcAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            System.Windows.MessageBox.Show(this, "Seleccione primero una Orden de Compra de la lista.", "OC no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Forzar estatus o cierre de {oc.Folio} ({oc.ProveedorNombre})"))
            return;

        var win = new Window
        {
            Title = $"Forzar Estatus de Orden de Compra — {oc.Folio}",
            Width = 500,
            Height = 365,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = $"⚡ Cambiar / Forzar Estatus de {oc.Folio}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Proveedor: {oc.ProveedorNombre}  |  Estatus actual: {oc.Estatus}  |  Avance: {oc.AvanceRecepcionTexto}",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = "Seleccione la acción administrativa a aplicar:", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var cboModo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        cboModo.Items.Add("1. Marcar Recibida Completa e ingresar pendientes automáticamente al Stock");
        cboModo.Items.Add("2. Marcar Recibida Completa SIN afectar Stock de Almacén (Entregado directo en obra/servicio)");
        cboModo.Items.Add("3. Cerrar Parcialmente como Definitiva (El proveedor ya no surtirá el resto pendiente)");
        cboModo.Items.Add("4. Cancelar Orden de Compra");
        cboModo.SelectedIndex = 0;
        root.Children.Add(cboModo);

        root.Children.Add(new TextBlock { Text = "Motivo / Justificación (obligatorio para bitácora):", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var txtMotivo = new System.Windows.Controls.TextBox
        {
            Height = 64,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Text = "Autorizado por Administración: material entregado directamente en proyecto / cierre administrativo.",
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(txtMotivo);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnApply = new System.Windows.Controls.Button { Content = "Aplicar Cambio Administrativo", Padding = new Thickness(14, 6, 14, 6), FontWeight = FontWeights.Bold };

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnApply.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtMotivo.Text))
            {
                System.Windows.MessageBox.Show(win, "Escriba el motivo del cambio administrativo.", "Motivo requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int idx = cboModo.SelectedIndex;
            string modoAccion = idx switch
            {
                0 => "Recibida Completa (Ingresar pendientes al Stock)",
                1 => "Recibida Completa (Sin afectar Stock)",
                2 => "Cerrada Parcial Definitiva",
                3 => "Cancelar OC",
                _ => "Recibida Completa (Ingresar pendientes al Stock)"
            };

            DatabaseService.ForzarEstatusOrdenCompraAdmin(oc.Id, modoAccion, txtMotivo.Text.Trim());
            win.DialogResult = true;
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnApply);
        root.Children.Add(btnPanel);
        win.Content = root;

        if (win.ShowDialog() == true)
        {
            CargarTodoElSistema();
            System.Windows.MessageBox.Show(this, $"Se actualizó el estatus de la Orden de Compra {oc.Folio} y se registró en la Bitácora de Administrador.", "Estatus Actualizado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnLiquidarOcSinFactura_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            System.Windows.MessageBox.Show(this, "Seleccione primero una Orden de Compra de la lista.", "OC no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Liquidar OC {oc.Folio} con Nota/Ticket sin Factura XML"))
            return;

        var win = new Window
        {
            Title = $"Liquidar OC sin Factura XML — {oc.Folio}",
            Width = 460,
            Height = 310,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = $"🧾 Registrar Nota / Ticket de Compra para {oc.Folio}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Proveedor: {oc.ProveedorNombre}\nTotal de la OC: {oc.TotalConIva:C2}\nSe generará un comprobante interno vinculado a esta OC para el historial de compras y análisis.",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = "Folio de Nota / Ticket / Remisión:", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var txtFolioNota = new System.Windows.Controls.TextBox
        {
            Text = $"NOTA-{oc.Folio}",
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(txtFolioNota);

        root.Children.Add(new TextBlock { Text = "Observaciones:", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var txtNotas = new System.Windows.Controls.TextBox
        {
            Text = "Compra liquidada con nota de remisión / ticket (sin CFDI XML)",
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(txtNotas);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnOk = new System.Windows.Controls.Button { Content = "Registrar Nota sin Factura", Padding = new Thickness(14, 6, 14, 6), FontWeight = FontWeights.Bold };

        FacturaXml? comprobanteCreado = null;
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click += (_, _) =>
        {
            comprobanteCreado = DatabaseService.LiquidarOrdenCompraSinFactura(
                oc.Id,
                txtFolioNota.Text.Trim(),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                txtNotas.Text.Trim());
            win.DialogResult = true;
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);
        win.Content = root;

        if (win.ShowDialog() == true && comprobanteCreado != null)
        {
            CargarTodoElSistema();
            System.Windows.MessageBox.Show(this,
                $"Se registró la nota/comprobante '{comprobanteCreado.SerieFolio}' para la Orden de Compra {oc.Folio}.\n\nPuede consultarla en la pestaña '6. Facturas y Notas' y en las gráficas de '7. Análisis'.",
                "OC Liquidada sin Factura", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnImprimirEntradaSeleccionada_Click(object sender, RoutedEventArgs e)
    {
        if (DgEntradasHistorial.SelectedItem is EntradaAlmacen ent)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent);
        }
    }

    // ==================== TAB 5: ALMACÉN POS (SALIDAS Y PRÉSTAMO DE HERRAMIENTAS) ====================

    private void CboSalDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboSalDepartamento.SelectedItem is Departamento dep)
        {
            var encs = DatabaseService.ObtenerEncargados(dep.Id);
            string txtPrev = CboSalEncargado.Text;
            CboSalEncargado.ItemsSource = encs;
            CboSalEncargado.SelectedIndex = -1;
            CboSalEncargado.Text = txtPrev;
        }
        else if (CboSalEncargado != null)
        {
            string txtPrev = CboSalEncargado.Text;
            CboSalEncargado.ItemsSource = null;
            CboSalEncargado.SelectedIndex = -1;
            CboSalEncargado.Text = txtPrev;
        }
    }

    private void CboPreDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboPreDepartamento?.SelectedItem is Departamento dep && CboPrePersonaRecibe != null)
        {
            var encs = DatabaseService.ObtenerEncargados(dep.Id);
            string txtPrev = CboPrePersonaRecibe.Text;
            CboPrePersonaRecibe.ItemsSource = encs;
            CboPrePersonaRecibe.SelectedIndex = -1;
            CboPrePersonaRecibe.Text = txtPrev;
        }
        else if (CboPrePersonaRecibe != null)
        {
            string txtPrev = CboPrePersonaRecibe.Text;
            CboPrePersonaRecibe.ItemsSource = null;
            CboPrePersonaRecibe.SelectedIndex = -1;
            CboPrePersonaRecibe.Text = txtPrev;
        }
    }

    private void RefrescarAlmacenPosYPrestamos()
    {
        int herPrevId = (CboHerramientaDisponible.SelectedItem as HerramientaActivo)?.Id ?? 0;
        var herramientasDisp = DatabaseService.ObtenerHerramientas().Where(h => h.Estado == "Disponible").ToList();
        CboHerramientaDisponible.ItemsSource = herramientasDisp;
        CboHerramientaDisponible.SelectedItem = herPrevId > 0 ? herramientasDisp.FirstOrDefault(h => h.Id == herPrevId) : null;

        var prestamos = DatabaseService.ObtenerPrestamosHerramienta();
        var filas = new List<PrestamoVistaFila>();
        foreach (var pre in prestamos)
        {
            foreach (var part in pre.Partidas)
            {
                filas.Add(new PrestamoVistaFila
                {
                    PrestamoId = pre.Id,
                    PrestamoPartidaId = part.Id,
                    HerramientaId = part.HerramientaActivoId,
                    FolioVale = pre.Folio,
                    CodigoIndividual = part.CodigoIndividual,
                    HerramientaDescripcion = $"{part.ProductoNombre} ({part.MarcaModelo})",
                    DepartamentoNombre = pre.DepartamentoNombre,
                    EncargadoNombre = pre.PersonaRecibeEfectiva,
                    ProyectoNombre = pre.ProyectoNombre,
                    EstadoDevolucion = part.EstadoDevolucion,
                    PrestamoOriginal = pre
                });
            }
        }
        _todosPrestamosFilas = filas;

        var movs = new List<MovimientoAlmacenFila>();
        var entradas = DatabaseService.ObtenerEntradasAlmacen();
        foreach (var ent in entradas)
        {
            bool esDevolucionMat = !string.IsNullOrWhiteSpace(ent.TipoEntrada) &&
                                   ent.TipoEntrada.Contains("Devolución", StringComparison.OrdinalIgnoreCase);
            string detalle = ent.Partidas.Count > 0
                ? string.Join(", ", ent.Partidas.Select(p => $"+{p.CantidadIngresadaAlmacen:0.##} {p.UnidadAlmacen} {p.ProductoNombre}"))
                : ent.Notas;
            movs.Add(new MovimientoAlmacenFila
            {
                TipoMovimiento = esDevolucionMat ? "DEVOLUCIÓN" : "ENTRADA",
                Folio = ent.Folio,
                Fecha = ent.Fecha,
                ResponsableOProveedor = ent.ProveedorNombre,
                ProyectoNombre = string.IsNullOrWhiteSpace(ent.OrdenCompraFolio)
                    ? "Almacén General"
                    : (esDevolucionMat ? $"Vale: {ent.OrdenCompraFolio} ({ent.TipoEntrada})" : $"OC: {ent.OrdenCompraFolio}"),
                DetalleResumen = detalle,
                EntradaOriginal = ent
            });
        }

        var salidas = DatabaseService.ObtenerSalidasAlmacen();
        foreach (var sal in salidas)
        {
            string prefijoDev = sal.TotalUnidadesDevueltas > 0
                ? $"[{sal.EstatusDevolucionTexto}: {sal.TotalUnidadesDevueltas:0.##}/{sal.TotalUnidadesEntregadas:0.##} dev.] "
                : string.Empty;
            string detalle = sal.Partidas.Count > 0
                ? prefijoDev + string.Join(", ", sal.Partidas.Select(p =>
                    p.CantidadDevuelta > 0
                        ? $"{p.CantidadEntregada:0.##} {p.UnidadAlmacen} {p.ProductoNombre} (Dev: {p.CantidadDevuelta:0.##}, Neto: {p.CantidadNetaConsumida:0.##})"
                        : $"{p.CantidadEntregada:0.##} {p.UnidadAlmacen} {p.ProductoNombre}"))
                : sal.Notas;
            movs.Add(new MovimientoAlmacenFila
            {
                TipoMovimiento = sal.TotalUnidadesDevueltas > 0
                    ? (sal.TotalUnidadesDevueltas >= sal.TotalUnidadesEntregadas - 0.001m ? "SALIDA (DEV. 100%)" : "SALIDA (DEV. PARC.)")
                    : "SALIDA",
                Folio = sal.Folio,
                Fecha = sal.Fecha,
                ResponsableOProveedor = $"{sal.PersonaRecibeEfectiva} (Aprueba: {sal.DepartamentoNombre})",
                ProyectoNombre = sal.ProyectoNombre,
                DetalleResumen = detalle,
                SalidaOriginal = sal
            });
        }

        _todosMovimientosAlmacen = movs
            .OrderByDescending(m => m.Fecha)
            .ThenByDescending(m => m.Folio)
            .ToList();

        AplicarFiltroAlmacen();
    }

    private void AplicarFiltroAlmacen()
    {
        string raw = TxtBuscarAlmacen?.Text?.Trim() ?? string.Empty;
        string norm = NormalizarCodigoEscaneado(raw);

        var todosProds = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();

        if (string.IsNullOrWhiteSpace(raw))
        {
            int prodPrevId = (CboSalProductoPos.SelectedItem as Producto)?.Id ?? 0;
            var prodsNoHer = todosProds.Where(p => !p.EsHerramienta).ToList();
            CboSalProductoPos.ItemsSource = prodsNoHer;
            CboSalProductoPos.SelectedItem = prodPrevId > 0 ? prodsNoHer.FirstOrDefault(p => p.Id == prodPrevId) : null;

            DgPrestamosHerramientas.ItemsSource = _todosPrestamosFilas;
            DgMovimientosAlmacen.ItemsSource = _todosMovimientosAlmacen;
            return;
        }

        var prodsFiltrados = todosProds.Where(p =>
            p.Nombre.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.CodigoInterno.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.CodigoInterno.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            p.CodigoBarras.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.CodigoBarras.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            p.Marca.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.Modelo.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.Medidas.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.CapacidadElectrica.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.Especificacion1.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.Especificacion2.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.CategoriaNombre.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            p.Descripcion.Contains(raw, StringComparison.OrdinalIgnoreCase)).ToList();

        if (prodsFiltrados.Count > 0)
        {
            CboSalProductoPos.ItemsSource = prodsFiltrados;
            CboSalProductoPos.SelectedIndex = 0;
        }

        var prestamosFiltrados = _todosPrestamosFilas.Where(f =>
            f.FolioVale.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            f.FolioVale.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            CoincideFolioFlexible(f.FolioVale, norm) ||
            f.CodigoIndividual.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            f.CodigoIndividual.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            CoincideFolioFlexible(f.CodigoIndividual, norm) ||
            f.HerramientaDescripcion.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            f.EncargadoNombre.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            f.ProyectoNombre.Contains(raw, StringComparison.OrdinalIgnoreCase)).ToList();

        var movsFiltrados = _todosMovimientosAlmacen.Where(m =>
            m.Folio.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            m.Folio.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            CoincideFolioFlexible(m.Folio, norm) ||
            m.TipoMovimiento.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            m.ResponsableOProveedor.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            m.ProyectoNombre.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
            m.DetalleResumen.Contains(raw, StringComparison.OrdinalIgnoreCase)).ToList();

        DgPrestamosHerramientas.ItemsSource = prestamosFiltrados;
        if (prestamosFiltrados.Count == 1)
            DgPrestamosHerramientas.SelectedItem = prestamosFiltrados[0];

        DgMovimientosAlmacen.ItemsSource = movsFiltrados;
        if (movsFiltrados.Count == 1)
            DgMovimientosAlmacen.SelectedItem = movsFiltrados[0];
    }

    private void TxtBuscarAlmacen_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        AplicarFiltroAlmacen();
    }

    private void TxtBuscarAlmacen_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EjecutarAccionBuscadorAlmacen();
            e.Handled = true;
        }
    }

    private void BtnBuscarAlmacen_Click(object sender, RoutedEventArgs e)
    {
        EjecutarAccionBuscadorAlmacen();
    }

    private void BtnLimpiarBusquedaAlmacen_Click(object sender, RoutedEventArgs e)
    {
        TxtBuscarAlmacen.Clear();
        AplicarFiltroAlmacen();
        TxtBuscarAlmacen.Focus();
    }

    private void EjecutarAccionBuscadorAlmacen()
    {
        string raw = TxtBuscarAlmacen?.Text?.Trim() ?? string.Empty;
        string code = NormalizarCodigoEscaneado(raw);
        if (string.IsNullOrWhiteSpace(code))
            return;

        // 1. Si es código de Entrada, Salida, OC, Préstamo, Herramienta o Solicitud, o coincide con un folio existente
        if (EsPrefijoDocumento(code))
        {
            TxtBuscarAlmacen!.Clear();
            AplicarFiltroAlmacen();
            ProcesarCodigoEscaneado(code);
            return;
        }

        // 2. Si coincide exactamente con el código de barras o código interno de un producto, agregarlo al vale de salida POS
        var prodExacto = DatabaseService.BuscarProductoPorCodigo(code) ?? DatabaseService.BuscarProductoPorCodigo(raw);
        if (prodExacto != null)
        {
            TxtBuscarAlmacen!.Clear();
            AplicarFiltroAlmacen();
            CboSalProductoPos.SelectedItem = CboSalProductoPos.Items.Cast<Producto>().FirstOrDefault(x => x.Id == prodExacto.Id);
            decimal cant = ParseDecimal(TxtSalCantidadPos.Text, 1m);
            AgregarProductoAValeSalidaPos(prodExacto, cant);
            return;
        }

        // 3. Si al filtrar por texto queda exactamente 1 movimiento (Entrada o Salida) y ningún producto exacto, abrir su vista previa
        AplicarFiltroAlmacen();
        if (DgMovimientosAlmacen.Items.Count == 1 && DgMovimientosAlmacen.Items[0] is MovimientoAlmacenFila unicoMov &&
            (unicoMov.Folio.Contains(code, StringComparison.OrdinalIgnoreCase) || CoincideFolioFlexible(unicoMov.Folio, code)))
        {
            AbrirVistaPreviaMovimientoAlmacen(unicoMov);
            return;
        }

        // 4. Si al filtrar por texto coincide con 1 único producto en el combo POS, seleccionarlo listo para agregar
        if (CboSalProductoPos.Items.Count == 1 && CboSalProductoPos.Items[0] is Producto unicoProd)
        {
            CboSalProductoPos.SelectedItem = unicoProd;
        }
    }

    private void BtnImprimirMovimientoAlmacen_Click(object sender, RoutedEventArgs e)
    {
        if (DgMovimientosAlmacen.SelectedItem is MovimientoAlmacenFila fila)
        {
            AbrirVistaPreviaMovimientoAlmacen(fila);
        }
        else
        {
            System.Windows.MessageBox.Show(this,
                "Seleccione un movimiento (Entrada ENT-xxxx o Salida SAL-xxxx) de la tabla inferior o escanee su código de barras en el buscador.",
                "Seleccione un vale", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AbrirVistaPreviaMovimientoAlmacen(MovimientoAlmacenFila fila)
    {
        if (fila.EntradaOriginal != null)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, fila.EntradaOriginal);
        }
        else if (fila.SalidaOriginal != null)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, fila.SalidaOriginal);
        }
    }

    private void AgregarProductoAValeSalidaPos(Producto prod, decimal cantidadMenudeo)
    {
        var existente = _partidasSalidaPos.FirstOrDefault(x => x.ProductoId == prod.Id);
        if (existente != null)
        {
            existente.CantidadEntregada += cantidadMenudeo;
            DgPartidasSalidaPos.Items.Refresh();
        }
        else
        {
            _partidasSalidaPos.Add(new SalidaPartida
            {
                ProductoId = prod.Id,
                ProductoCodigo = prod.CodigoInterno,
                ProductoNombre = prod.Nombre,
                CantidadEntregada = cantidadMenudeo,
                UnidadAlmacen = prod.UnidadAlmacen,
                CostoUnitarioAlmacen = prod.CostoUnitarioAlmacen
            });
        }
    }

    private void BtnAgregarPartidaSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        if (CboSalProductoPos.SelectedItem is Producto prod)
        {
            decimal cant = ParseDecimal(TxtSalCantidadPos.Text, 1m);
            AgregarProductoAValeSalidaPos(prod, cant);
        }
    }

    private void BtnLimpiarSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        _partidasSalidaPos.Clear();
        if (CboSalDepartamento != null) CboSalDepartamento.SelectedIndex = -1;
        if (CboSalEncargado != null) { CboSalEncargado.SelectedIndex = -1; CboSalEncargado.Text = string.Empty; }
        if (CboSalProyecto != null) CboSalProyecto.SelectedIndex = -1;
        if (CboSalProductoPos != null) CboSalProductoPos.SelectedIndex = -1;
        TxtSalCantidadPos?.Clear();
        TxtSalNotas?.Clear();
    }

    private void BtnRegistrarSalidaAlmacen_Click(object sender, RoutedEventArgs e)
    {
        if (CboSalDepartamento.SelectedItem is not Departamento dep ||
            CboSalProyecto.SelectedItem is not Proyecto proy)
        {
            System.Windows.MessageBox.Show(this, "Seleccione obligatoriamente el Departamento que Aprueba la Salida y el Proyecto Destino.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string personaRecibe = (CboSalEncargado.SelectedItem as Encargado)?.Nombre
                               ?? CboSalEncargado.Text?.Trim()
                               ?? string.Empty;

        if (string.IsNullOrWhiteSpace(personaRecibe))
        {
            System.Windows.MessageBox.Show(this, "Escriba o seleccione el nombre de la Persona que Recibe el material.", "Falta persona que recibe", MessageBoxButton.OK, MessageBoxImage.Warning);
            CboSalEncargado.Focus();
            return;
        }

        if (_partidasSalidaPos.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Agregue o escanee al menos un material para el vale de salida.", "Vale vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var sinStockSuficiente = _partidasSalidaPos
            .Where(part => prodsDict.TryGetValue(part.ProductoId, out var p) && part.CantidadEntregada > p.StockActual)
            .Select(part =>
            {
                decimal stockDisp = prodsDict.TryGetValue(part.ProductoId, out var p) ? p.StockActual : 0m;
                return $"• [{part.ProductoCodigo}] {part.ProductoNombre}: solicita {part.CantidadEntregada:0.##} {part.UnidadAlmacen} (Stock actual: {stockDisp:0.##})";
            })
            .ToList();

        if (sinStockSuficiente.Count > 0)
        {
            var confirmarStock = System.Windows.MessageBox.Show(this,
                $"Los siguientes artículos superan la existencia registrada en almacén:\n\n{string.Join("\n", sinStockSuficiente)}\n\n¿Desea registrar la salida de todas formas (el stock quedará en 0)?",
                "Advertencia de Stock Insuficiente",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmarStock != System.Windows.MessageBoxResult.Yes)
                return;
        }

        int encId = (CboSalEncargado.SelectedItem as Encargado)?.Id
                    ?? DatabaseService.ObtenerEncargados(dep.Id).FirstOrDefault()?.Id
                    ?? 1;

        var salida = DatabaseService.RegistrarSalidaAlmacen(dep.Id, encId, proy.Id, TxtSalNotas.Text, _partidasSalidaPos.ToList(), personaRecibe);
        BtnLimpiarSalidaPos_Click(sender, e);
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, salida);
    }

    private void BtnPrestarHerramienta_Click(object sender, RoutedEventArgs e)
    {
        var dep = CboPreDepartamento?.SelectedItem as Departamento ?? CboSalDepartamento.SelectedItem as Departamento;
        var proy = CboPreProyecto?.SelectedItem as Proyecto ?? CboSalProyecto.SelectedItem as Proyecto;

        if (dep == null || proy == null)
        {
            System.Windows.MessageBox.Show(this, "Seleccione obligatoriamente el Departamento que Aprueba el Préstamo y el Proyecto Destino.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string personaRecibe = (CboPrePersonaRecibe?.SelectedItem as Encargado)?.Nombre
                               ?? CboPrePersonaRecibe?.Text?.Trim()
                               ?? (CboSalEncargado.SelectedItem as Encargado)?.Nombre
                               ?? CboSalEncargado.Text?.Trim()
                               ?? string.Empty;

        if (string.IsNullOrWhiteSpace(personaRecibe))
        {
            System.Windows.MessageBox.Show(this, "Escriba o seleccione el nombre de la Persona que Recibe la herramienta.", "Falta persona que recibe", MessageBoxButton.OK, MessageBoxImage.Warning);
            CboPrePersonaRecibe?.Focus();
            return;
        }

        if (CboHerramientaDisponible.SelectedItem is not HerramientaActivo her)
        {
            System.Windows.MessageBox.Show(this, "No hay una herramienta disponible seleccionada.", "Sin herramienta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int encId = (CboPrePersonaRecibe?.SelectedItem as Encargado)?.Id
                    ?? DatabaseService.ObtenerEncargados(dep.Id).FirstOrDefault()?.Id
                    ?? 1;

        string fechaCompromiso = (DpPreFechaCompromiso?.SelectedDate ?? DateTime.Today.AddDays(7)).ToString("yyyy-MM-dd");

        var prestamo = DatabaseService.RegistrarPrestamoHerramienta(dep.Id, encId, proy.Id, TxtSalNotas.Text, new List<HerramientaActivo> { her }, personaRecibe, fechaCompromiso);
        if (CboPreDepartamento != null) CboPreDepartamento.SelectedIndex = -1;
        if (CboPrePersonaRecibe != null) { CboPrePersonaRecibe.SelectedIndex = -1; CboPrePersonaRecibe.Text = string.Empty; }
        if (CboPreProyecto != null) CboPreProyecto.SelectedIndex = -1;
        if (CboHerramientaDisponible != null) CboHerramientaDisponible.SelectedIndex = -1;
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, prestamo);
    }

    private void BtnRegistrarDevolucionHerramienta_Click(object sender, RoutedEventArgs e)
    {
        if (DgPrestamosHerramientas.SelectedItem is not PrestamoVistaFila fila)
        {
            System.Windows.MessageBox.Show(this, "Seleccione en la tabla la herramienta en préstamo que desea devolver (o escanee el código PRE-xxxx / HER-xxxx).", "Seleccione un préstamo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string estado = (CboEstadoRetornoHerramienta.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bueno (Completo)";
        DatabaseService.DevolverHerramientaPartida(fila.PrestamoPartidaId, fila.HerramientaId, fila.PrestamoId, estado, TxtObsRetornoHerramienta.Text);
        TxtObsRetornoHerramienta.Clear();
        CargarTodoElSistema();

        var prestamoActualizado = DatabaseService.ObtenerPrestamosHerramienta().FirstOrDefault(p => p.Id == fila.PrestamoId);
        if (prestamoActualizado != null)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, prestamoActualizado);
        }
        else
        {
            System.Windows.MessageBox.Show(this,
                $"Devolución registrada para {fila.CodigoIndividual} con estado '{estado}'.",
                "Herramienta Devuelta", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnReimprimirValePrestamo_Click(object sender, RoutedEventArgs e)
    {
        if (DgPrestamosHerramientas.SelectedItem is PrestamoVistaFila fila)
        {
            var actualizado = DatabaseService.ObtenerPrestamosHerramienta().FirstOrDefault(p => p.Id == fila.PrestamoId) ?? fila.PrestamoOriginal;
            BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, actualizado);
        }
        else
        {
            System.Windows.MessageBox.Show(this,
                "Seleccione primero una herramienta o préstamo de la tabla para abrir e imprimir su Hoja de Préstamo / Devolución.",
                "Seleccione un préstamo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnDevolverMaterialSalida_Click(object sender, RoutedEventArgs e)
    {
        SalidaAlmacen? salidaInicial = null;
        if (DgMovimientosAlmacen.SelectedItem is MovimientoAlmacenFila filaMov)
        {
            if (filaMov.SalidaOriginal != null)
            {
                salidaInicial = DatabaseService.ObtenerSalidasAlmacen().FirstOrDefault(s => s.Id == filaMov.SalidaOriginal.Id);
            }
            else if (filaMov.EntradaOriginal != null &&
                     !string.IsNullOrWhiteSpace(filaMov.EntradaOriginal.OrdenCompraFolio) &&
                     filaMov.EntradaOriginal.OrdenCompraFolio.StartsWith("SAL-", StringComparison.OrdinalIgnoreCase))
            {
                salidaInicial = DatabaseService.ObtenerSalidasAlmacen()
                    .FirstOrDefault(s => s.Folio.Equals(filaMov.EntradaOriginal.OrdenCompraFolio, StringComparison.OrdinalIgnoreCase));
            }
        }

        MostrarModalDevolucionMaterialSalida(salidaInicial);
    }

    private void MostrarModalDevolucionMaterialSalida(SalidaAlmacen? salidaPreseleccionada)
    {
        var todasSalidas = DatabaseService.ObtenerSalidasAlmacen();
        if (todasSalidas.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Aún no existen Vales de Salida de Material (SAL-xxxx) registrados en el sistema.",
                "Sin salidas registradas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = "Devolución / Reingreso de Material a Almacén (Completa o Parcial)",
            Width = 860,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Encabezado e selector de Vale de Salida
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        topPanel.Children.Add(new TextBlock
        {
            Text = "Registrar Devolución de Material Sobrante a Almacén",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });
        topPanel.Children.Add(new TextBlock
        {
            Text = "Seleccione el Vale de Salida (SAL-xxxx), indique la cantidad devuelta por partida (parcial o completa) y genere el Comprobante Imprimible de Devolución sin precios.",
            FontSize = 11.5,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var selGrid = new Grid();
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        selGrid.Children.Add(new TextBlock
        {
            Text = "Vale de Salida (SAL-xxxx):",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        var cboSalidas = new ComboBox
        {
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = todasSalidas
        };
        Grid.SetColumn(cboSalidas, 1);
        selGrid.Children.Add(cboSalidas);

        var btnDevolverTodo = new System.Windows.Controls.Button
        {
            Content = "🔄 Devolver Todo (Completo)",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnDevolverTodo, 2);
        selGrid.Children.Add(btnDevolverTodo);

        var btnLimpiarCant = new System.Windows.Controls.Button
        {
            Content = "🧹 Poner en 0",
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnLimpiarCant, 3);
        selGrid.Children.Add(btnLimpiarCant);

        topPanel.Children.Add(selGrid);
        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        // Resumen del vale seleccionado
        var infoBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var txtInfoSalida = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Text = "Seleccione un vale de salida..."
        };
        infoBorder.Child = txtInfoSalida;
        Grid.SetRow(infoBorder, 1);
        root.Children.Add(infoBorder);

        // Tabla editable de partidas a devolver
        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("ProductoCodigo"), IsReadOnly = true, Width = 90 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Material / Artículo", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadAlmacen"), IsReadOnly = true, Width = 75 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Entregado", Binding = new System.Windows.Data.Binding("CantidadEntregada") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 90 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Ya Devuelto", Binding = new System.Windows.Data.Binding("CantidadDevuelta") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 95 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Pendiente Máx.", Binding = new System.Windows.Data.Binding("CantidadPendienteDevolver") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 105 });
        dg.Columns.Add(new DataGridTextColumn { Header = "✏️ Devolver Hoy", Binding = new System.Windows.Data.Binding("CantidadDevolverAhora") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, IsReadOnly = false, Width = 115 });

        Grid.SetRow(dg, 2);
        root.Children.Add(dg);

        // Observaciones
        var obsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        obsStack.Children.Add(new TextBlock
        {
            Text = "Motivo / Observaciones de la Devolución:",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkSlateGray,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var txtObs = new System.Windows.Controls.TextBox
        {
            Padding = new Thickness(8, 6, 8, 6),
            Text = "Sobrante de material en obra / proyecto reingresado en buen estado a almacén."
        };
        obsStack.Children.Add(txtObs);
        Grid.SetRow(obsStack, 3);
        root.Children.Add(obsStack);

        // Botones inferiores
        var bottomPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancelar = new System.Windows.Controls.Button
        {
            Content = "Cancelar",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        var btnConfirmar = new System.Windows.Controls.Button
        {
            Content = "✅ Registrar Devolución e Imprimir Comprobante",
            Padding = new Thickness(16, 7, 16, 7),
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        bottomPanel.Children.Add(btnCancelar);
        bottomPanel.Children.Add(btnConfirmar);
        Grid.SetRow(bottomPanel, 4);
        root.Children.Add(bottomPanel);

        void CargarPartidasDeSalida(SalidaAlmacen? sal)
        {
            if (sal == null)
            {
                txtInfoSalida.Text = "Seleccione un vale de salida...";
                dg.ItemsSource = null;
                return;
            }

            foreach (var p in sal.Partidas)
            {
                p.CantidadDevolverAhora = p.CantidadPendienteDevolver;
            }
            txtInfoSalida.Text = $"Folio: {sal.Folio}  |  Recibió: {sal.PersonaRecibeEfectiva} ({sal.DepartamentoNombre})  |  Proyecto: {sal.ProyectoNombre}  |  Estado actual: {sal.EstatusDevolucionTexto}";
            dg.ItemsSource = null;
            dg.ItemsSource = sal.Partidas;
        }

        cboSalidas.SelectionChanged += (_, _) =>
        {
            CargarPartidasDeSalida(cboSalidas.SelectedItem as SalidaAlmacen);
        };

        btnDevolverTodo.Click += (_, _) =>
        {
            if (cboSalidas.SelectedItem is SalidaAlmacen sal)
            {
                foreach (var p in sal.Partidas)
                    p.CantidadDevolverAhora = p.CantidadPendienteDevolver;
                dg.Items.Refresh();
            }
        };

        btnLimpiarCant.Click += (_, _) =>
        {
            if (cboSalidas.SelectedItem is SalidaAlmacen sal)
            {
                foreach (var p in sal.Partidas)
                    p.CantidadDevolverAhora = 0m;
                dg.Items.Refresh();
            }
        };

        btnCancelar.Click += (_, _) => win.Close();

        btnConfirmar.Click += (_, _) =>
        {
            dg.CommitEdit(DataGridEditingUnit.Cell, true);
            dg.CommitEdit(DataGridEditingUnit.Row, true);

            if (cboSalidas.SelectedItem is not SalidaAlmacen salSel)
            {
                System.Windows.MessageBox.Show(win, "Seleccione un Vale de Salida (SAL-xxxx).", "Falta vale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var devoluciones = salSel.Partidas
                .Where(p => p.CantidadDevolverAhora > 0)
                .Select(p => (p.Id, p.ProductoId, Math.Min(p.CantidadDevolverAhora, p.CantidadPendienteDevolver)))
                .Where(x => x.Item3 > 0)
                .ToList();

            if (devoluciones.Count == 0)
            {
                System.Windows.MessageBox.Show(win,
                    "Indique una cantidad a devolver mayor a 0 en al menos una partida (o verifique que el vale aún tenga unidades pendientes por devolver).",
                    "Sin unidades a devolver", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var (salActualizada, entDevolucion) = DatabaseService.RegistrarDevolucionMaterialSalida(salSel.Id, devoluciones, txtObs.Text);
                CargarTodoElSistema();
                win.Close();
                BarcodePrintService.MostrarVistaPreviaEImprimirDevolucionMaterial(this, salActualizada, entDevolucion);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(win, ex.Message, "Error al registrar devolución", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        var inicial = salidaPreseleccionada != null
            ? todasSalidas.FirstOrDefault(s => s.Id == salidaPreseleccionada.Id)
            : todasSalidas.FirstOrDefault(s => s.TotalUnidadesDevueltas < s.TotalUnidadesEntregadas) ?? todasSalidas.FirstOrDefault();

        cboSalidas.SelectedItem = inicial;
        CargarPartidasDeSalida(inicial);

        win.Content = root;
        win.ShowDialog();
    }

    private void BtnNuevaHerramientaIndividual_Click(object sender, RoutedEventArgs e)
    {
        var prodHer = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.EsHerramienta) ?? DatabaseService.ObtenerProductos().First();
        string nuevoCodigo = DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual");
        DatabaseService.GuardarHerramientaActivo(new HerramientaActivo
        {
            ProductoId = prodHer.Id,
            CodigoIndividual = nuevoCodigo,
            NumeroSerie = $"SN-{DateTime.Now:MMddHHmm}",
            MarcaModelo = prodHer.Nombre,
            Estado = "Disponible",
            Ubicacion = "Gabinete H-1",
            NotasEstado = "Registrada lista para préstamo"
        });
        CargarTodoElSistema();
        System.Windows.MessageBox.Show(this, $"Se dio de alta la herramienta individual {nuevoCodigo} asociada a '{prodHer.Nombre}'.", "Alta de Herramienta", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==================== TAB 6: FACTURAS XML (CFDI 4.0) ====================

    private void RefrescarFacturasXml()
    {
        _actualizandoCombosXml = true;
        try
        {
            var proyectos = DatabaseService.ObtenerProyectos();
            int? proyIdPrevio = (CboXmlProyecto.SelectedItem as Proyecto)?.Id;
            CboXmlProyecto.ItemsSource = proyectos;

            var ocs = new List<OrdenCompra>
            {
                new() { Id = 0, Folio = "➕ Crear nueva OC automáticamente" }
            };
            ocs.AddRange(DatabaseService.ObtenerOrdenesCompra());
            CboXmlOrdenCompra.ItemsSource = ocs;

            int? facIdSeleccionada = (DgFacturasRegistradas.SelectedItem as FacturaXml)?.Id;
            var facturas = DatabaseService.ObtenerFacturasXml();
            DgFacturasRegistradas.ItemsSource = facturas;
            if (facIdSeleccionada.HasValue && _facturaXmlEnPantalla == null)
            {
                var reSel = facturas.FirstOrDefault(f => f.Id == facIdSeleccionada.Value);
                if (reSel != null)
                {
                    DgFacturasRegistradas.SelectedItem = reSel;
                    DgXmlConceptosActual.ItemsSource = reSel.Conceptos;
                    CboXmlProyecto.SelectedItem = proyIdPrevio.HasValue
                        ? proyectos.FirstOrDefault(p => p.Id == proyIdPrevio.Value)
                        : null;
                }
            }
            else if (_facturaXmlEnPantalla == null)
            {
                CboXmlProyecto.SelectedIndex = -1;
                CboXmlOrdenCompra.SelectedIndex = -1;
                CboXmlVincularProducto.SelectedIndex = -1;
            }
        }
        finally
        {
            _actualizandoCombosXml = false;
        }
    }

    private void BtnSubirArchivoXml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleccionar Factura Electrónica XML (CFDI 4.0 / 3.3)",
            Filter = "Factura XML (*.xml)|*.xml"
        };
        if (dlg.ShowDialog() == true)
        {
            CargarArchivoXmlEnPantalla(dlg.FileName);
        }
    }

    private void BtnCargarXmlDemo_Click(object sender, RoutedEventArgs e)
    {
        string demoPath = XmlCfdiService.GenerarFacturaXmlDemoEnCarpeta();
        CargarArchivoXmlEnPantalla(demoPath);
    }

    private async void CargarArchivoXmlEnPantalla(string rutaXml)
    {
        try
        {
            await EjecutarConIndicadorCargaAsync("Analizando factura XML y verificando productos...", () =>
            {
                _facturaXmlEnPantalla = XmlCfdiService.LeerArchivoCfdiXml(rutaXml);

                _actualizandoCombosXml = true;
                try
                {
                    DgFacturasRegistradas.SelectedItem = null;

                    // Seleccionar proyecto Stock General por defecto al cargar nueva factura
                    if (CboXmlProyecto.ItemsSource is IEnumerable<Proyecto> proys)
                    {
                        var stock = proys.FirstOrDefault(p => p.EsStockGeneral) ?? proys.FirstOrDefault();
                        if (stock != null)
                            CboXmlProyecto.SelectedItem = stock;
                    }

                    // Buscar si ya existe una OC emitida sin factura de ese mismo proveedor; si no, seleccionar "Crear nueva OC automáticamente"
                    if (CboXmlOrdenCompra.ItemsSource is IEnumerable<OrdenCompra> listaOcs)
                    {
                        var idsConFactura = DatabaseService.ObtenerFacturasXml()
                            .Where(f => f.OrdenCompraId.HasValue)
                            .Select(f => f.OrdenCompraId!.Value)
                            .ToHashSet();

                        var ocMismoProv = listaOcs.FirstOrDefault(o =>
                            o.Id > 0 &&
                            o.ProveedorRfc.Equals(_facturaXmlEnPantalla.EmisorRfc, StringComparison.OrdinalIgnoreCase) &&
                            o.Estatus == "Emitida" &&
                            !idsConFactura.Contains(o.Id));

                        CboXmlOrdenCompra.SelectedItem = ocMismoProv ?? listaOcs.FirstOrDefault(o => o.Id == 0);
                    }
                }
                finally
                {
                    _actualizandoCombosXml = false;
                }

                ActualizarResumenFacturaXml();
                DgXmlConceptosActual.ItemsSource = _facturaXmlEnPantalla.Conceptos;
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Error al leer el archivo XML:\n{ex.Message}", "Error XML", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ActualizarResumenFacturaXml()
    {
        if (_facturaXmlEnPantalla == null) return;
        int registrados = _facturaXmlEnPantalla.Conceptos.Count(c => c.ProductoIdVinculado.HasValue);
        int nuevos = _facturaXmlEnPantalla.Conceptos.Count - registrados;
        TxtXmlResumenActual.Text = $"UUID: {_facturaXmlEnPantalla.Uuid} | Emisor: {_facturaXmlEnPantalla.EmisorNombre} | Total: ${_facturaXmlEnPantalla.Total:N2} ({registrados} registrados, {nuevos} por crear)";
    }

    private void CboXmlProyecto_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_actualizandoCombosXml) return;
        if (_facturaXmlEnPantalla == null && DgFacturasRegistradas.SelectedItem is FacturaXml facReg && CboXmlProyecto.SelectedItem is Proyecto proy)
        {
            DatabaseService.ActualizarProyectoYOrdenFacturaRegistrada(facReg.Id, proy.Id, facReg.OrdenCompraId);
            CargarTodoElSistema();
        }
    }

    private void CboXmlOrdenCompra_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_actualizandoCombosXml) return;
        if (_facturaXmlEnPantalla == null && DgFacturasRegistradas.SelectedItem is FacturaXml facReg && CboXmlOrdenCompra.SelectedItem is OrdenCompra ocSel)
        {
            int proyId = (CboXmlProyecto.SelectedItem as Proyecto)?.Id ?? facReg.ProyectoId ?? DatabaseService.ObtenerProyectoStockGeneralId();
            int? nuevaOcId = ocSel.Id > 0 ? ocSel.Id : null;
            if (nuevaOcId != facReg.OrdenCompraId)
            {
                DatabaseService.ActualizarProyectoYOrdenFacturaRegistrada(facReg.Id, proyId, nuevaOcId);
                CargarTodoElSistema();
            }
        }
    }

    private void BtnVincularConceptoXmlConProducto_Click(object sender, RoutedEventArgs e)
    {
        if (DgXmlConceptosActual.SelectedItem is not FacturaConcepto concepto ||
            CboXmlVincularProducto.SelectedItem is not Producto prod)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un concepto en la tabla y el producto de su catálogo con el que desea relacionarlo.", "Vincular concepto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_facturaXmlEnPantalla != null)
        {
            concepto.ProductoIdVinculado = prod.Id;
            concepto.ProductoVinculadoNombre = $"✔ Registrado: [{prod.CodigoInterno}] {prod.Nombre}";
            DatabaseService.GuardarMapaProductoProveedor(_facturaXmlEnPantalla.EmisorRfc, concepto.NoIdentificacion, concepto.Descripcion, prod.Id, concepto.ValorUnitario);
            DgXmlConceptosActual.Items.Refresh();
            ActualizarResumenFacturaXml();
            return;
        }

        if (DgFacturasRegistradas.SelectedItem is FacturaXml facRegistrada)
        {
            DatabaseService.ActualizarConceptoFacturaRegistrada(facRegistrada.Id, concepto.Id, prod.Id);
            CargarTodoElSistema();
            TxtXmlResumenActual.Text = $"Concepto '{concepto.Descripcion}' vinculado a [{prod.CodigoInterno}] {prod.Nombre} y actualizado en Análisis.";
            return;
        }

        System.Windows.MessageBox.Show(this, "Cargue un archivo XML o seleccione una factura registrada en la tabla inferior.", "Vincular concepto", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnMarcarConceptoComoNuevo_Click(object sender, RoutedEventArgs e)
    {
        if (DgXmlConceptosActual.SelectedItem is not FacturaConcepto concepto)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un concepto en la tabla para marcarlo como producto nuevo.", "Seleccione concepto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_facturaXmlEnPantalla != null)
        {
            concepto.ProductoIdVinculado = null;
            concepto.ProductoVinculadoNombre = "➕ No registrado (se creará al guardar)";
            DgXmlConceptosActual.Items.Refresh();
            ActualizarResumenFacturaXml();
            return;
        }

        if (DgFacturasRegistradas.SelectedItem is FacturaXml)
        {
            concepto.ProductoIdVinculado = null;
            concepto.ProductoVinculadoNombre = "➕ Listo para crear como nuevo (haga clic en 'Crear Nuevos en Catálogo')";
            DgXmlConceptosActual.Items.Refresh();
            return;
        }
    }

    private async void BtnCrearProductoDesdeConceptoXml_Click(object sender, RoutedEventArgs e)
    {
        var facturaActiva = _facturaXmlEnPantalla ?? (DgFacturasRegistradas.SelectedItem as FacturaXml);
        if (facturaActiva == null)
        {
            System.Windows.MessageBox.Show(this, "Primero cargue una factura XML o seleccione una factura registrada.", "Sin factura", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var conceptosACrear = new List<FacturaConcepto>();
        if (DgXmlConceptosActual.SelectedItem is FacturaConcepto seleccionado && (!seleccionado.ProductoIdVinculado.HasValue || seleccionado.ProductoIdVinculado <= 0))
        {
            conceptosACrear.Add(seleccionado);
        }
        else
        {
            conceptosACrear.AddRange(facturaActiva.Conceptos.Where(c => !c.ProductoIdVinculado.HasValue || c.ProductoIdVinculado <= 0));
        }

        if (conceptosACrear.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Todos los conceptos ya están vinculados a productos registrados en el catálogo.\nSi desea crear uno nuevo a partir de un concepto ya vinculado, selecciónelo y haga clic en 'Marcar como Nuevo'.", "Productos ya registrados", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var creados = new List<Producto>();
        await EjecutarConIndicadorCargaAsync("Creando productos nuevos en el catálogo...", () =>
        {
            foreach (var c in conceptosACrear)
            {
                var prod = DatabaseService.CrearProductoDesdeConceptoXml(facturaActiva.EmisorRfc, facturaActiva.SerieFolio, c);
                creados.Add(prod);
                if (_facturaXmlEnPantalla == null && facturaActiva.Id > 0 && c.Id > 0)
                {
                    DatabaseService.ActualizarConceptoFacturaRegistrada(facturaActiva.Id, c.Id, prod.Id);
                }
            }

            if (_facturaXmlEnPantalla == null)
            {
                CargarTodoElSistema();
            }
            else
            {
                CargarCombosBase();
                RefrescarCatalogoVisual();
                DgXmlConceptosActual.Items.Refresh();
                ActualizarResumenFacturaXml();
            }
        });

        string listaNombres = string.Join("\n", creados.Select(p => $"• [{p.CodigoInterno}] {p.Nombre} ({p.UltimoCostoCompra:C2})"));
        System.Windows.MessageBox.Show(this,
            $"Se dieron de alta {creados.Count} producto(s) nuevos en el Catálogo:\n\n{listaNombres}",
            "Productos Creados en Catálogo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DgFacturasRegistradas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgFacturasRegistradas.SelectedItem is FacturaXml fac && _facturaXmlEnPantalla == null)
        {
            _actualizandoCombosXml = true;
            try
            {
                if (CboXmlProyecto.ItemsSource is IEnumerable<Proyecto> proys && fac.ProyectoId.HasValue)
                {
                    var matchProy = proys.FirstOrDefault(p => p.Id == fac.ProyectoId.Value);
                    if (matchProy != null)
                        CboXmlProyecto.SelectedItem = matchProy;
                }

                if (CboXmlOrdenCompra.ItemsSource is IEnumerable<OrdenCompra> ocs)
                {
                    var matchOc = fac.OrdenCompraId.HasValue
                        ? ocs.FirstOrDefault(o => o.Id == fac.OrdenCompraId.Value)
                        : ocs.FirstOrDefault(o => o.Id == 0);
                    if (matchOc != null)
                        CboXmlOrdenCompra.SelectedItem = matchOc;
                }
            }
            finally
            {
                _actualizandoCombosXml = false;
            }

            DgXmlConceptosActual.ItemsSource = fac.Conceptos;
            TxtXmlResumenActual.Text = $"Factura Registrada — Folio: {fac.SerieFolio} | OC: {fac.OrdenCompraFolio} | Emisor: {fac.EmisorNombre} | Total: ${fac.Total:N2}";
        }
    }

    private async void BtnEliminarFacturaXml_Click(object sender, RoutedEventArgs e)
    {
        if (DgFacturasRegistradas.SelectedItem is FacturaXml fac)
        {
            if (!SolicitarAutorizacionAdmin($"Eliminar comprobante/factura {fac.SerieFolio} ({fac.EmisorNombre})"))
                return;

            var resp = System.Windows.MessageBox.Show(this,
                $"¿Desea eliminar el comprobante {fac.SerieFolio} (UUID/Folio: {fac.Uuid}) del proveedor '{fac.EmisorNombre}' por {fac.Total:C2}?",
                "Confirmar eliminación de factura",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp != System.Windows.MessageBoxResult.Yes)
                return;

            await EjecutarConIndicadorCargaAsync("Eliminando factura y actualizando análisis...", () =>
            {
                DatabaseService.EliminarFacturaXml(fac.Id);
                if (_facturaXmlEnPantalla == null)
                {
                    DgXmlConceptosActual.ItemsSource = null;
                    TxtXmlResumenActual.Text = "Comprobante eliminado correctamente.";
                }
                CargarTodoElSistema();
            });
            return;
        }

        if (_facturaXmlEnPantalla != null)
        {
            var resp = System.Windows.MessageBox.Show(this,
                "¿Desea descartar y limpiar la factura XML que está cargada actualmente en pantalla?",
                "Limpiar factura en pantalla",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp == System.Windows.MessageBoxResult.Yes)
            {
                _facturaXmlEnPantalla = null;
                DgXmlConceptosActual.ItemsSource = null;
                TxtXmlResumenActual.Text = "Seleccione un archivo XML para cargar sus conceptos.";
            }
            return;
        }

        System.Windows.MessageBox.Show(this,
            "Seleccione una factura en la tabla 'Facturas y Notas Registradas' para eliminarla.",
            "Factura no seleccionada",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void BtnGuardarFacturaXml_Click(object sender, RoutedEventArgs e)
    {
        if (_facturaXmlEnPantalla == null)
        {
            System.Windows.MessageBox.Show(this, "Primero seleccione un archivo .xml o cargue el XML Demo.", "Sin factura cargada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DatabaseService.ObtenerFacturasXml().Any(f => f.Uuid.Equals(_facturaXmlEnPantalla.Uuid, StringComparison.OrdinalIgnoreCase)))
        {
            System.Windows.MessageBox.Show(this, $"La factura con UUID {_facturaXmlEnPantalla.Uuid} ya se encuentra registrada en el sistema.", "Factura duplicada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (CboXmlProyecto.SelectedItem is Proyecto proySel)
        {
            _facturaXmlEnPantalla.ProyectoId = proySel.Id;
        }

        if (CboXmlOrdenCompra.SelectedItem is OrdenCompra oc && oc.Id > 0)
        {
            _facturaXmlEnPantalla.OrdenCompraId = oc.Id;
            _facturaXmlEnPantalla.OrdenCompraFolio = oc.Folio;
        }
        else
        {
            _facturaXmlEnPantalla.OrdenCompraId = null;
            _facturaXmlEnPantalla.OrdenCompraFolio = string.Empty;
        }

        FacturaXml facturaGuardada = null!;
        int nuevosCreados = 0;

        await EjecutarConIndicadorCargaAsync("Guardando factura XML, orden de compra y productos...", () =>
        {
            int productosAntes = DatabaseService.ObtenerProductos().Count;
            facturaGuardada = DatabaseService.GuardarFacturaXml(_facturaXmlEnPantalla);
            int productosDespues = DatabaseService.ObtenerProductos().Count;
            nuevosCreados = Math.Max(0, productosDespues - productosAntes);

            _facturaXmlEnPantalla = null;
            DgXmlConceptosActual.ItemsSource = facturaGuardada.Conceptos;
            TxtXmlResumenActual.Text = nuevosCreados > 0
                ? $"Factura registrada exitosamente (OC: {facturaGuardada.OrdenCompraFolio}). Se crearon {nuevosCreados} producto(s) nuevos en el catálogo."
                : $"Factura registrada exitosamente (OC: {facturaGuardada.OrdenCompraFolio}). Costos y análisis actualizados.";
            CargarTodoElSistema();
        });

        if (nuevosCreados > 0)
        {
            System.Windows.MessageBox.Show(this,
                $"Factura {facturaGuardada.SerieFolio} registrada y asociada a la Orden de Compra {facturaGuardada.OrdenCompraFolio}.\n\nSe detectaron {nuevosCreados} concepto(s) nuevos y se crearon automáticamente en el Catálogo.",
                "Factura, OC y Nuevos Productos Registrados", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // --- 6.2 COMPRA DIRECTA SIN FACTURA (NOTA / TICKET / REMISIÓN) ---

    private void FiltrarComboProductoSinFactura()
    {
        if (CboSinFacProducto == null) return;
        var baseProds = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        string q = TxtBuscarProdSinFac?.Text?.Trim() ?? string.Empty;
        int prevId = (CboSinFacProducto.SelectedItem as Producto)?.Id ?? 0;

        var filtrados = string.IsNullOrWhiteSpace(q)
            ? baseProds
            : baseProds.Where(p =>
                p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoInterno.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoBarras.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Marca.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Modelo.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        CboSinFacProducto.ItemsSource = filtrados;
        if (!string.IsNullOrWhiteSpace(q) && filtrados.Count > 0)
        {
            CboSinFacProducto.SelectedItem = filtrados.FirstOrDefault(p => p.Id == prevId) ?? filtrados[0];
        }
        else if (prevId > 0)
        {
            CboSinFacProducto.SelectedItem = filtrados.FirstOrDefault(p => p.Id == prevId);
        }
        else
        {
            CboSinFacProducto.SelectedIndex = -1;
        }
    }

    private void TxtBuscarProdSinFac_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        FiltrarComboProductoSinFactura();
    }

    private void CboSinFacProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (CboSinFacProducto.SelectedItem is Producto prod && TxtSinFacPrecioUnitario != null)
        {
            decimal precioSugerido = prod.UltimoCostoCompra > 0
                ? (ChkSinFacMasIva?.IsChecked == true ? Math.Round(prod.UltimoCostoCompra / 1.16m, 2) : prod.UltimoCostoCompra)
                : 0m;
            TxtSinFacPrecioUnitario.Text = precioSugerido.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    private void BtnAgregarPartidaSinFactura_Click(object sender, RoutedEventArgs e)
    {
        if (CboSinFacProducto.SelectedItem is not Producto prod)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un producto del catálogo para agregarlo a la nota.", "Producto requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        decimal cant = ParseDecimal(TxtSinFacCantidad.Text, 1m);
        decimal precio = ParseDecimal(TxtSinFacPrecioUnitario.Text, 0m);
        if (cant <= 0 || precio <= 0)
        {
            System.Windows.MessageBox.Show(this, "Capture una cantidad y un precio unitario mayores a 0.", "Datos requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existente = _partidasCompraSinFactura.FirstOrDefault(x => x.ProductoId == prod.Id);
        if (existente != null)
        {
            existente.Cantidad += cant;
            existente.PrecioUnitario = precio;
        }
        else
        {
            _partidasCompraSinFactura.Add(new PartidaCompraSinFactura
            {
                ProductoId = prod.Id,
                ProductoCodigo = prod.CodigoInterno,
                ProductoNombre = prod.Nombre,
                UnidadCompra = prod.UnidadCompra,
                Cantidad = cant,
                PrecioUnitario = precio
            });
        }

        DgPartidasCompraSinFactura.Items.Refresh();
        RecalcularTotalCompraSinFactura();
    }

    private void BtnQuitarPartidaSinFactura_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PartidaCompraSinFactura item })
        {
            _partidasCompraSinFactura.Remove(item);
            DgPartidasCompraSinFactura.Items.Refresh();
            RecalcularTotalCompraSinFactura();
        }
    }

    private void ChkSinFacMasIva_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RecalcularTotalCompraSinFactura();
    }

    private void RecalcularTotalCompraSinFactura()
    {
        if (TxtSinFacResumenTotal == null) return;
        decimal sub = _partidasCompraSinFactura.Sum(p => p.Importe);
        bool masIva = ChkSinFacMasIva?.IsChecked == true;
        decimal iva = masIva ? Math.Round(sub * 0.16m, 2) : 0m;
        decimal total = Math.Round(sub + iva, 2);
        TxtSinFacResumenTotal.Text = masIva
            ? $"Partidas: {_partidasCompraSinFactura.Count} | Subtotal: {sub:C2} + IVA: {iva:C2} = Total: {total:C2}"
            : $"Partidas: {_partidasCompraSinFactura.Count} | Total Neto: {total:C2}";
    }

    private void BtnLimpiarCompraSinFactura_Click(object sender, RoutedEventArgs e)
    {
        _partidasCompraSinFactura.Clear();
        if (CboSinFacProveedor != null)
        {
            CboSinFacProveedor.SelectedIndex = -1;
            CboSinFacProveedor.Text = string.Empty;
        }
        if (CboSinFacProyecto != null) CboSinFacProyecto.SelectedIndex = -1;
        TxtSinFacFolioNota?.Clear();
        TxtSinFacNotas?.Clear();
        TxtBuscarProdSinFac?.Clear();
        if (CboSinFacProducto != null) CboSinFacProducto.SelectedIndex = -1;
        TxtSinFacCantidad?.Clear();
        TxtSinFacPrecioUnitario?.Clear();
        DgPartidasCompraSinFactura?.Items.Refresh();
        RecalcularTotalCompraSinFactura();
    }

    private void BtnRegistrarCompraSinFactura_Click(object sender, RoutedEventArgs e)
    {
        var provSel = CboSinFacProveedor.SelectedItem as Proveedor;
        string provTexto = CboSinFacProveedor.Text?.Trim() ?? string.Empty;
        if (provSel == null && string.IsNullOrWhiteSpace(provTexto))
        {
            System.Windows.MessageBox.Show(this, "Seleccione o escriba el nombre del Proveedor.", "Proveedor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CboSinFacProyecto.SelectedItem is not Proyecto proy)
        {
            System.Windows.MessageBox.Show(this, "Seleccione el Proyecto destino.", "Proyecto requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_partidasCompraSinFactura.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Agregue al menos un artículo a la nota de compra.", "Sin partidas", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string nombreProvMostrar = provSel?.NombreMostrar ?? provTexto;
        if (!SolicitarAutorizacionAdmin($"Registrar compra sin factura XML con '{nombreProvMostrar}'"))
            return;

        string folioNota = TxtSinFacFolioNota.Text?.Trim() ?? string.Empty;
        string notas = TxtSinFacNotas.Text?.Trim() ?? string.Empty;
        bool masIva = ChkSinFacMasIva.IsChecked == true;
        bool ingresarAlmacen = ChkSinFacIngresarAlmacen.IsChecked == true;

        var (comprobante, folioOc, folioEntrada) = DatabaseService.RegistrarCompraDirectaSinFactura(
            provSel?.Id ?? 0,
            provTexto,
            proy.Id,
            folioNota,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            masIva,
            ingresarAlmacen,
            "Almacén / Compras",
            notas,
            _partidasCompraSinFactura.ToList());

        BtnLimpiarCompraSinFactura_Click(sender, e);
        CargarTodoElSistema();

        if (!string.IsNullOrWhiteSpace(folioEntrada))
        {
            var ent = DatabaseService.ObtenerEntradasAlmacen().FirstOrDefault(x => x.Folio == folioEntrada);
            if (ent != null)
            {
                BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent);
                return;
            }
        }

        var ocCreada = DatabaseService.ObtenerOrdenesCompra().FirstOrDefault(x => x.Folio == folioOc);
        if (ocCreada != null)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, ocCreada);
        }
    }

    // ==================== TAB 7: ANÁLISIS Y GRÁFICAS DE PRODUCTO ====================

    private class FilaAnalisisProducto
    {
        public int ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public string Frecuencia { get; set; } = string.Empty;
        public string CantidadTotal { get; set; } = string.Empty;
        public decimal ImporteTotal { get; set; }
    }

    private void FiltrarComboProductoAnalisis()
    {
        if (CboProductoAnalisis == null) return;
        var baseProds = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        string q = TxtBuscarProductoAnalisis?.Text?.Trim() ?? string.Empty;
        int prevId = (CboProductoAnalisis.SelectedItem as Producto)?.Id ?? 0;

        var filtrados = string.IsNullOrWhiteSpace(q)
            ? baseProds
            : baseProds.Where(p =>
                p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoInterno.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Marca.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CategoriaNombre.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _sincronizandoProductoAnalisis = true;
        try
        {
            CboProductoAnalisis.ItemsSource = filtrados;
            if (!string.IsNullOrWhiteSpace(q) && filtrados.Count > 0)
            {
                CboProductoAnalisis.SelectedItem = filtrados.FirstOrDefault(p => p.Id == prevId) ?? filtrados[0];
            }
            else if (prevId > 0)
            {
                CboProductoAnalisis.SelectedItem = filtrados.FirstOrDefault(p => p.Id == prevId);
            }
            else
            {
                CboProductoAnalisis.SelectedIndex = -1;
            }
        }
        finally
        {
            _sincronizandoProductoAnalisis = false;
        }
    }

    private void TxtBuscarProductoAnalisis_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        FiltrarComboProductoAnalisis();
        RenderizarGraficasProductoAnalisis();
    }

    private void CboProductoAnalisis_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoProductoAnalisis) return;
        RenderizarGraficasProductoAnalisis();
    }

    private void CboMetricaBarrasAnalisis_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RenderizarGraficasProductoAnalisis();
    }

    private void DgAnalisisProductos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoProductoAnalisis) return;
        if (DgAnalisisProductos.SelectedItem is FilaAnalisisProducto fila && CboProductoAnalisis != null)
        {
            _sincronizandoProductoAnalisis = true;
            try
            {
                if (CboProductoAnalisis.ItemsSource is IEnumerable<Producto> items)
                {
                    var match = items.FirstOrDefault(p => p.Id == fila.ProductoId);
                    if (match != null)
                    {
                        CboProductoAnalisis.SelectedItem = match;
                    }
                    else
                    {
                        TxtBuscarProductoAnalisis?.Clear();
                        FiltrarComboProductoAnalisis();
                        if (CboProductoAnalisis.ItemsSource is IEnumerable<Producto> todos)
                            CboProductoAnalisis.SelectedItem = todos.FirstOrDefault(p => p.Id == fila.ProductoId);
                    }
                }
            }
            finally
            {
                _sincronizandoProductoAnalisis = false;
            }
            RenderizarGraficasProductoAnalisis();
        }
    }

    private void CanvasGraficasProducto_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RenderizarGraficasProductoAnalisis();
    }

    private void RefrescarAnalisis()
    {
        var proveedores = DatabaseService.ObtenerProveedores();
        var ocs = DatabaseService.ObtenerOrdenesCompra().Where(o => o.Estatus != "Cancelada").ToList();
        var salidas = DatabaseService.ObtenerSalidasAlmacen();
        var proyectos = DatabaseService.ObtenerProyectos();

        // 1. Análisis por Proveedor
        var provStats = proveedores.Select(p =>
        {
            var ocsProv = ocs.Where(o => o.ProveedorId == p.Id).ToList();
            return new
            {
                p.Rfc,
                Proveedor = p.NombreMostrar,
                NumOrdenes = ocsProv.Count,
                DiasPromedio = $"{p.DiasEntregaPromedio} días",
                MontoTotal = ocsProv.Sum(o => o.TotalConIva)
            };
        }).OrderByDescending(x => x.MontoTotal).ToList();
        DgAnalisisProveedores.ItemsSource = provStats;

        // 2. Frecuencia de Compra por Producto
        var todasPartidasOc = ocs.SelectMany(o => o.Partidas).ToList();
        var prodStats = todasPartidasOc
            .GroupBy(p => p.ProductoId)
            .Select(g =>
            {
                var primero = g.First();
                int numOcs = g.Select(x => x.OrdenCompraId).Distinct().Count();
                return new FilaAnalisisProducto
                {
                    ProductoId = primero.ProductoId,
                    Codigo = primero.ProductoCodigo,
                    Producto = primero.ProductoNombre,
                    Frecuencia = $"{numOcs} OC{(numOcs == 1 ? "" : "s")}",
                    CantidadTotal = $"{g.Sum(x => x.CantidadPedida):0.##} {primero.UnidadCompra}",
                    ImporteTotal = g.Sum(x => x.ImporteConIva)
                };
            })
            .OrderByDescending(x => x.ImporteTotal)
            .ToList();
        DgAnalisisProductos.ItemsSource = prodStats;

        // 3. Gasto por Proyecto y Departamento
        var proyStats = proyectos.Select(pr =>
        {
            var ocsProy = ocs.Where(o => o.ProyectoId == pr.Id).ToList();
            var salProy = salidas.Where(s => s.ProyectoId == pr.Id).ToList();
            string depto = ocsProy.FirstOrDefault()?.DepartamentoNombre
                ?? salProy.FirstOrDefault()?.DepartamentoNombre
                ?? pr.ClienteArea;
            return new
            {
                Proyecto = $"{pr.Codigo} - {pr.Nombre}",
                Departamento = depto,
                TotalCompras = ocsProy.Sum(o => o.TotalConIva),
                TotalSalidas = salProy.Sum(s => s.CostoTotalEstimado)
            };
        }).OrderByDescending(x => x.TotalCompras).ToList();
        DgAnalisisProyectos.ItemsSource = proyStats;

        RenderizarGraficasProductoAnalisis();
    }

    private void RenderizarGraficasProductoAnalisis()
    {
        if (CanvasDispersionProducto == null || CanvasBarrasProducto == null)
            return;

        CanvasDispersionProducto.Children.Clear();
        CanvasBarrasProducto.Children.Clear();

        if (CboProductoAnalisis?.SelectedItem is not Producto prod)
        {
            if (TxtResumenGraficaProducto != null)
                TxtResumenGraficaProducto.Text = "Seleccione un producto en el desplegable o en la tabla superior para visualizar sus gráficas de dispersión y barras.";
            DibujarMensajeSinDatosCanvas(CanvasDispersionProducto, "Seleccione un producto arriba o en la tabla superior para graficar");
            DibujarMensajeSinDatosCanvas(CanvasBarrasProducto, "Seleccione un producto arriba o en la tabla superior para graficar");
            return;
        }

        var puntos = DatabaseService.ObtenerHistorialComprasProductoParaAnalisis(prod.Id);
        if (puntos.Count == 0)
        {
            if (TxtResumenGraficaProducto != null)
                TxtResumenGraficaProducto.Text = $"[{prod.CodigoInterno}] {prod.Nombre} — Sin compras registradas aún. Último costo catálogo: {prod.UltimoCostoCompra:C2}";

            DibujarMensajeSinDatosCanvas(CanvasDispersionProducto, "Sin historial de compras para graficar dispersión");
            DibujarMensajeSinDatosCanvas(CanvasBarrasProducto, "Sin historial de compras para graficar barras");
            return;
        }

        decimal minPrecio = puntos.Min(x => x.PrecioUnitarioSinIva);
        decimal maxPrecio = puntos.Max(x => x.PrecioUnitarioSinIva);
        decimal promPrecio = Math.Round(puntos.Average(x => x.PrecioUnitarioSinIva), 2);
        decimal totalGastado = puntos.Sum(x => x.ImporteConIva);
        decimal totalUnidades = puntos.Sum(x => x.Cantidad);

        if (TxtResumenGraficaProducto != null)
        {
            TxtResumenGraficaProducto.Text =
                $"[{prod.CodigoInterno}] {prod.Nombre} — {puntos.Count} compra(s) | " +
                $"Mín s/IVA: {minPrecio:C2} | Prom s/IVA: {promPrecio:C2} | Máx s/IVA: {maxPrecio:C2} | " +
                $"Volumen Total: {totalUnidades:0.##} {prod.UnidadCompra} ({totalGastado:C2} c/IVA)";
        }

        DibujarGraficaDispersion(CanvasDispersionProducto, puntos, promPrecio);
        DibujarGraficaBarras(CanvasBarrasProducto, puntos, prod.UnidadCompra);
    }

    private static void DibujarMensajeSinDatosCanvas(Canvas canvas, string mensaje)
    {
        double w = canvas.ActualWidth > 40 ? canvas.ActualWidth : 420;
        double h = canvas.ActualHeight > 40 ? canvas.ActualHeight : 190;
        var tb = new TextBlock
        {
            Text = mensaje,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175))
        };
        Canvas.SetLeft(tb, Math.Max(16, (w - 260) / 2));
        Canvas.SetTop(tb, Math.Max(16, (h - 20) / 2));
        canvas.Children.Add(tb);
    }

    private void DibujarGraficaDispersion(Canvas canvas, List<PuntoHistorialCompraProducto> puntos, decimal promPrecio)
    {
        double w = canvas.ActualWidth > 60 ? canvas.ActualWidth : 460;
        double h = canvas.ActualHeight > 60 ? canvas.ActualHeight : 195;

        double padLeft = 58, padRight = 22, padTop = 18, padBottom = 34;
        double plotW = Math.Max(40, w - padLeft - padRight);
        double plotH = Math.Max(40, h - padTop - padBottom);

        // Ejes
        var ejeY = new Line { X1 = padLeft, Y1 = padTop, X2 = padLeft, Y2 = padTop + plotH, Stroke = new SolidColorBrush(Color.FromRgb(209, 213, 219)), StrokeThickness = 1.2 };
        var ejeX = new Line { X1 = padLeft, Y1 = padTop + plotH, X2 = padLeft + plotW, Y2 = padTop + plotH, Stroke = new SolidColorBrush(Color.FromRgb(209, 213, 219)), StrokeThickness = 1.2 };
        canvas.Children.Add(ejeY);
        canvas.Children.Add(ejeX);

        double minY = (double)puntos.Min(p => p.PrecioUnitarioSinIva);
        double maxY = (double)puntos.Max(p => p.PrecioUnitarioSinIva);
        if (Math.Abs(maxY - minY) < 0.01)
        {
            minY *= 0.85;
            maxY *= 1.15;
            if (maxY <= 0) maxY = 10;
        }
        else
        {
            double extra = (maxY - minY) * 0.15;
            minY = Math.Max(0, minY - extra);
            maxY += extra;
        }

        // Líneas guía horizontales
        for (int i = 0; i <= 3; i++)
        {
            double frac = i / 3.0;
            double y = padTop + plotH - (frac * plotH);
            double val = minY + (frac * (maxY - minY));

            var gridLine = new Line
            {
                X1 = padLeft,
                Y1 = y,
                X2 = padLeft + plotW,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
                StrokeThickness = 1
            };
            canvas.Children.Add(gridLine);

            var lblY = new TextBlock
            {
                Text = $"${val:N0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            Canvas.SetLeft(lblY, 4);
            Canvas.SetTop(lblY, y - 7);
            canvas.Children.Add(lblY);
        }

        // Línea punteada de precio promedio
        double yProm = padTop + plotH - (((double)promPrecio - minY) / (maxY - minY) * plotH);
        if (yProm >= padTop && yProm <= padTop + plotH)
        {
            var promLine = new Line
            {
                X1 = padLeft,
                Y1 = yProm,
                X2 = padLeft + plotW,
                Y2 = yProm,
                Stroke = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            canvas.Children.Add(promLine);
        }

        var paleta = new[]
        {
            Color.FromRgb(37, 99, 235),
            Color.FromRgb(5, 150, 105),
            Color.FromRgb(217, 119, 6),
            Color.FromRgb(124, 58, 237),
            Color.FromRgb(220, 38, 38),
            Color.FromRgb(13, 148, 136)
        };
        var provs = puntos.Select(p => p.ProveedorNombre).Distinct().ToList();
        var colorPorProv = new Dictionary<string, Color>();
        for (int i = 0; i < provs.Count; i++)
        {
            colorPorProv[provs[i]] = paleta[i % paleta.Length];
        }

        // Línea conectora de tendencia y puntos de dispersión
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(95, 59, 130, 246)),
            StrokeThickness = 1.6
        };

        int n = puntos.Count;
        for (int i = 0; i < n; i++)
        {
            var pt = puntos[i];
            double x = n == 1
                ? padLeft + (plotW / 2.0)
                : padLeft + (i / (double)(n - 1) * plotW);
            double y = padTop + plotH - (((double)pt.PrecioUnitarioSinIva - minY) / (maxY - minY) * plotH);
            polyline.Points.Add(new Point(x, y));
        }
        if (n > 1) canvas.Children.Add(polyline);

        for (int i = 0; i < n; i++)
        {
            var pt = puntos[i];
            double x = n == 1
                ? padLeft + (plotW / 2.0)
                : padLeft + (i / (double)(n - 1) * plotW);
            double y = padTop + plotH - (((double)pt.PrecioUnitarioSinIva - minY) / (maxY - minY) * plotH);

            var col = colorPorProv[pt.ProveedorNombre];
            var dot = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = new SolidColorBrush(col),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Cursor = Cursors.Hand,
                ToolTip = $"{pt.FolioDocumento} ({pt.Fecha:yyyy-MM-dd})\nProveedor: {pt.ProveedorNombre}\nPrecio Unit. s/IVA: {pt.PrecioUnitarioSinIva:C2} ({pt.PrecioUnitarioConIva:C2} c/IVA)\nCantidad: {pt.Cantidad:0.##}"
            };
            Canvas.SetLeft(dot, x - 5.5);
            Canvas.SetTop(dot, y - 5.5);
            canvas.Children.Add(dot);

            if (n <= 6 || i == 0 || i == n - 1 || i == n / 2)
            {
                var lblX = new TextBlock
                {
                    Text = pt.Fecha.ToString("dd/MMM", CultureInfo.GetCultureInfo("es-MX")),
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
                };
                Canvas.SetLeft(lblX, Math.Clamp(x - 18, padLeft - 8, padLeft + plotW - 32));
                Canvas.SetTop(lblX, padTop + plotH + 6);
                canvas.Children.Add(lblX);
            }
        }
    }

    private void DibujarGraficaBarras(Canvas canvas, List<PuntoHistorialCompraProducto> puntos, string unidad)
    {
        double w = canvas.ActualWidth > 60 ? canvas.ActualWidth : 460;
        double h = canvas.ActualHeight > 60 ? canvas.ActualHeight : 195;

        double padLeft = 18, padRight = 18, padTop = 22, padBottom = 38;
        double plotW = Math.Max(40, w - padLeft - padRight);
        double plotH = Math.Max(40, h - padTop - padBottom);

        int modo = CboMetricaBarrasAnalisis?.SelectedIndex ?? 0;

        List<(string etiqueta, decimal valor, string textoValor, string tooltip)> barras;
        if (modo == 1)
        {
            barras = puntos
                .GroupBy(p => p.ProveedorNombre)
                .Select(g =>
                {
                    decimal cant = g.Sum(x => x.Cantidad);
                    return (g.Key, cant, $"{cant:0.##} {unidad}", $"Proveedor: {g.Key}\nCantidad Total: {cant:0.##} {unidad}\nCompras: {g.Count()}");
                })
                .OrderByDescending(x => x.cant)
                .Take(6)
                .ToList();
        }
        else if (modo == 2)
        {
            barras = puntos
                .GroupBy(p => p.Fecha.ToString("yyyy-MM"))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    decimal imp = g.Sum(x => x.ImporteConIva);
                    return (g.Key, imp, $"{imp:C0}", $"Periodo: {g.Key}\nGasto c/IVA: {imp:C2}\nCantidad: {g.Sum(x => x.Cantidad):0.##} {unidad}");
                })
                .TakeLast(6)
                .ToList();
        }
        else
        {
            barras = puntos
                .GroupBy(p => p.ProveedorNombre)
                .Select(g =>
                {
                    decimal imp = g.Sum(x => x.ImporteConIva);
                    return (g.Key, imp, $"{imp:C0}", $"Proveedor: {g.Key}\nImporte Total c/IVA: {imp:C2}\nPrecio Prom. s/IVA: {g.Average(x => x.PrecioUnitarioSinIva):C2}");
                })
                .OrderByDescending(x => x.imp)
                .Take(6)
                .ToList();
        }

        var ejeX = new Line
        {
            X1 = padLeft,
            Y1 = padTop + plotH,
            X2 = padLeft + plotW,
            Y2 = padTop + plotH,
            Stroke = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            StrokeThickness = 1.2
        };
        canvas.Children.Add(ejeX);

        if (barras.Count == 0) return;

        double maxVal = (double)barras.Max(b => b.valor);
        if (maxVal <= 0) maxVal = 1;

        var colores = new[]
        {
            Color.FromRgb(37, 99, 235),
            Color.FromRgb(5, 150, 105),
            Color.FromRgb(217, 119, 6),
            Color.FromRgb(124, 58, 237),
            Color.FromRgb(13, 148, 136),
            Color.FromRgb(220, 38, 38)
        };

        double slotW = plotW / barras.Count;
        double barW = Math.Min(54, Math.Max(18, slotW * 0.56));

        for (int i = 0; i < barras.Count; i++)
        {
            var b = barras[i];
            double barH = Math.Max(4, ((double)b.valor / maxVal) * (plotH - 14));
            double x = padLeft + (i * slotW) + ((slotW - barW) / 2.0);
            double y = padTop + plotH - barH;

            var rect = new Border
            {
                Width = barW,
                Height = barH,
                Background = new SolidColorBrush(colores[i % colores.Length]),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                ToolTip = b.tooltip,
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);

            var lblVal = new TextBlock
            {
                Text = b.textoValor,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
            };
            Canvas.SetLeft(lblVal, x + Math.Max(-8, (barW - 44) / 2.0));
            Canvas.SetTop(lblVal, Math.Max(2, y - 16));
            canvas.Children.Add(lblVal);

            string etqCorta = b.etiqueta.Length > 13 ? b.etiqueta[..12] + "…" : b.etiqueta;
            var lblEtq = new TextBlock
            {
                Text = etqCorta,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
                ToolTip = b.etiqueta
            };
            Canvas.SetLeft(lblEtq, x + Math.Max(-12, (barW - 56) / 2.0));
            Canvas.SetTop(lblEtq, padTop + plotH + 6);
            canvas.Children.Add(lblEtq);
        }
    }

    // =====================================================================
    // TAB 8: DIRECTORIOS (PROVEEDORES, PROYECTOS, PERSONAL, DEPTOS, HERRAMIENTAS, CATEGORÍAS)
    // =====================================================================
    private void RefrescarDirectorios()
    {
        DgDirProveedores.ItemsSource = DatabaseService.ObtenerProveedores();
        DgDirProyectos.ItemsSource = DatabaseService.ObtenerProyectos();
        DgDirPersonal.ItemsSource = DatabaseService.ObtenerEncargados();
        DgDirDepartamentos.ItemsSource = DatabaseService.ObtenerDepartamentos();
        DgDirHerramientas.ItemsSource = DatabaseService.ObtenerHerramientas();
        DgDirCategorias.ItemsSource = DatabaseService.ObtenerCategorias();
    }

    private void PonerFormulariosDirectoriosEnBlanco()
    {
        BtnNuevoProveedor_Click(this, new RoutedEventArgs());
        BtnNuevoProyecto_Click(this, new RoutedEventArgs());
        BtnNuevoEncargado_Click(this, new RoutedEventArgs());
        BtnNuevoDepartamento_Click(this, new RoutedEventArgs());
        BtnNuevaHerramientaDir_Click(this, new RoutedEventArgs());
        BtnNuevaCategoriaDir_Click(this, new RoutedEventArgs());
    }

    // --- 8.1 PROVEEDORES ---
    private void DgDirProveedores_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgDirProveedores.SelectedItem is not Proveedor p) return;

        _proveedorEditandoId = p.Id;
        TxtProvRfc.Text = p.Rfc;
        TxtProvComercial.Text = p.NombreComercial;
        TxtProvRazon.Text = p.RazonSocial;
        TxtProvContacto.Text = p.Contacto;
        TxtProvTelefono.Text = p.Telefono;
        TxtProvCorreo.Text = p.Correo;
        TxtProvCondiciones.Text = p.CondicionesPago;
        TxtProvDias.Text = p.DiasEntregaPromedio.ToString();
        TxtProvNotas.Text = p.Notas;

        var ocs = DatabaseService.ObtenerOrdenesCompra().Where(o => o.ProveedorId == p.Id).ToList();
        var cfdis = DatabaseService.ObtenerFacturasXml().Where(f => string.Equals(f.EmisorRfc, p.Rfc, StringComparison.OrdinalIgnoreCase)).ToList();

        var desglose = new List<object>();
        foreach (var oc in ocs)
        {
            desglose.Add(new
            {
                Tipo = "Orden Compra",
                Folio = oc.Folio,
                Fecha = oc.Fecha,
                Detalle = $"{oc.ProyectoNombre} ({oc.Partidas.Count} partidas)",
                Estatus = oc.Estatus,
                Monto = oc.TotalConIva
            });
        }
        foreach (var cfdi in cfdis)
        {
            desglose.Add(new
            {
                Tipo = "Factura CFDI",
                Folio = string.IsNullOrWhiteSpace(cfdi.SerieFolio) ? cfdi.Uuid[..Math.Min(8, cfdi.Uuid.Length)] : cfdi.SerieFolio,
                Fecha = cfdi.FechaTimbrado,
                Detalle = string.IsNullOrWhiteSpace(cfdi.OrdenCompraFolio) ? "Sin OC vinculada" : $"OC: {cfdi.OrdenCompraFolio}",
                Estatus = "Timbrada",
                Monto = cfdi.Total
            });
        }

        TxtDesgloseProveedorTitulo.Text = $"Desglose de {p.NombreComercial} — {ocs.Count} OC ({ocs.Sum(x => x.TotalConIva):C2}) | {cfdis.Count} Facturas CFDI";
        DgDesgloseProveedor.ItemsSource = desglose;
    }

    private void BtnNuevoProveedor_Click(object sender, RoutedEventArgs e)
    {
        _proveedorEditandoId = 0;
        DgDirProveedores.SelectedItem = null;
        TxtProvRfc.Clear();
        TxtProvComercial.Clear();
        TxtProvRazon.Clear();
        TxtProvContacto.Clear();
        TxtProvTelefono.Clear();
        TxtProvCorreo.Clear();
        TxtProvCondiciones.Clear();
        TxtProvDias.Clear();
        TxtProvNotas.Clear();
        TxtDesgloseProveedorTitulo.Text = "Seleccione un proveedor para consultar sus órdenes de compra y facturas";
        DgDesgloseProveedor.ItemsSource = null;
    }

    private void BtnGuardarProveedor_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtProvRfc.Text) || string.IsNullOrWhiteSpace(TxtProvRazon.Text))
        {
            System.Windows.MessageBox.Show(this, "Complete al menos el RFC y la Razón Social del proveedor.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DatabaseService.GuardarProveedor(new Proveedor
        {
            Id = _proveedorEditandoId,
            Rfc = TxtProvRfc.Text.Trim(),
            RazonSocial = TxtProvRazon.Text.Trim(),
            NombreComercial = string.IsNullOrWhiteSpace(TxtProvComercial.Text) ? TxtProvRazon.Text.Trim() : TxtProvComercial.Text.Trim(),
            Contacto = TxtProvContacto.Text.Trim(),
            Telefono = TxtProvTelefono.Text.Trim(),
            Correo = TxtProvCorreo.Text.Trim(),
            CondicionesPago = string.IsNullOrWhiteSpace(TxtProvCondiciones.Text) ? "Contado" : TxtProvCondiciones.Text.Trim(),
            DiasEntregaPromedio = (int)ParseDecimal(TxtProvDias.Text, 3m),
            Notas = TxtProvNotas.Text.Trim()
        });

        BtnNuevoProveedor_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarProveedor_Click(object sender, RoutedEventArgs e)
    {
        if (_proveedorEditandoId == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un proveedor de la tabla para eliminarlo.", "Proveedor no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar proveedor '{TxtProvComercial.Text}'"))
            return;

        if (System.Windows.MessageBox.Show(this, "¿Desea eliminar este proveedor del directorio?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarProveedor(_proveedorEditandoId);
            BtnNuevoProveedor_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar este proveedor porque tiene documentos asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- 8.2 PROYECTOS Y CLIENTES / ÁREAS ---
    private void DgDirProyectos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgDirProyectos.SelectedItem is not Proyecto pr) return;

        _proyectoEditandoId = pr.Id;
        TxtProyCodigo.Text = pr.Codigo;
        TxtProyNombre.Text = pr.Nombre;
        TxtProyArea.Text = pr.ClienteArea;
        foreach (ComboBoxItem item in CboProyEstatus.Items)
        {
            if (string.Equals(item.Content?.ToString(), pr.Estatus, StringComparison.OrdinalIgnoreCase))
            {
                CboProyEstatus.SelectedItem = item;
                break;
            }
        }

        var sols = DatabaseService.ObtenerSolicitudes().Where(s => s.ProyectoId == pr.Id).ToList();
        var ocs = DatabaseService.ObtenerOrdenesCompra().Where(o => o.ProyectoId == pr.Id).ToList();
        var salidas = DatabaseService.ObtenerSalidasAlmacen().Where(s => s.ProyectoId == pr.Id).ToList();

        var desglose = new List<object>();
        foreach (var s in sols)
        {
            desglose.Add(new
            {
                Tipo = "Solicitud",
                Folio = s.Folio,
                Fecha = s.Fecha,
                Detalle = $"{s.DepartamentoNombre} - {s.EncargadoNombre} ({s.Partidas.Count} partidas)",
                Estatus = s.Estatus,
                Monto = 0m
            });
        }
        foreach (var oc in ocs)
        {
            desglose.Add(new
            {
                Tipo = "Orden Compra",
                Folio = oc.Folio,
                Fecha = oc.Fecha,
                Detalle = $"Proveedor: {oc.ProveedorNombre}",
                Estatus = oc.Estatus,
                Monto = oc.TotalConIva
            });
        }
        foreach (var sal in salidas)
        {
            desglose.Add(new
            {
                Tipo = "Salida Almacén",
                Folio = sal.Folio,
                Fecha = sal.Fecha,
                Detalle = $"Recibe: {sal.EncargadoNombre} ({sal.Partidas.Count} artículos)",
                Estatus = "Entregada",
                Monto = sal.CostoTotalEstimado
            });
        }

        TxtDesgloseProyectoTitulo.Text = $"Desglose de {pr.Codigo} ({pr.ClienteArea}) — Compras OC: {ocs.Sum(x => x.TotalConIva):C2} | Salidas Almacén: {salidas.Sum(x => x.CostoTotalEstimado):C2}";
        DgDesgloseProyecto.ItemsSource = desglose;
    }

    private void BtnNuevoProyecto_Click(object sender, RoutedEventArgs e)
    {
        _proyectoEditandoId = 0;
        DgDirProyectos.SelectedItem = null;
        TxtProyCodigo.Clear();
        TxtProyNombre.Clear();
        TxtProyArea.Clear();
        CboProyEstatus.SelectedIndex = -1;
        TxtDesgloseProyectoTitulo.Text = "Seleccione un proyecto para consultar sus solicitudes, órdenes de compra y salidas de almacén";
        DgDesgloseProyecto.ItemsSource = null;
    }

    private void BtnGuardarProyecto_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtProyCodigo.Text) || string.IsNullOrWhiteSpace(TxtProyNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Escriba el código y nombre del proyecto.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string estatus = (CboProyEstatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Activo";
        DatabaseService.GuardarProyecto(TxtProyCodigo.Text, TxtProyNombre.Text, TxtProyArea.Text, estatus, _proyectoEditandoId);
        BtnNuevoProyecto_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarProyecto_Click(object sender, RoutedEventArgs e)
    {
        if (_proyectoEditandoId == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un proyecto de la tabla para eliminarlo.", "Proyecto no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar proyecto '{TxtProyCodigo.Text}'"))
            return;

        if (System.Windows.MessageBox.Show(this, "¿Desea eliminar este proyecto?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarProyecto(_proyectoEditandoId);
            BtnNuevoProyecto_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar este proyecto porque tiene documentos asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- 8.3 PERSONAL Y DEPARTAMENTOS ---
    private void DgDirPersonal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgDirPersonal.SelectedItem is not Encargado enc) return;

        _encargadoEditandoId = enc.Id;
        TxtAltaEncargadoNombre.Text = enc.Nombre;
        TxtAltaEncargadoPuesto.Text = enc.Puesto;
        TxtAltaEncargadoTelefono.Text = enc.Telefono;
        if (CboAltaEncargadoDepto.ItemsSource is IEnumerable<Departamento> deptos)
            CboAltaEncargadoDepto.SelectedItem = deptos.FirstOrDefault(d => d.Id == enc.DepartamentoId);

        var sols = DatabaseService.ObtenerSolicitudes().Where(s => s.EncargadoId == enc.Id).ToList();
        var salidas = DatabaseService.ObtenerSalidasAlmacen().Where(s => s.EncargadoId == enc.Id).ToList();
        var prestamos = DatabaseService.ObtenerPrestamosHerramienta().Where(p => p.EncargadoId == enc.Id).ToList();

        var desglose = new List<object>();
        foreach (var s in sols)
        {
            desglose.Add(new
            {
                Tipo = "Solicitud",
                Folio = s.Folio,
                Fecha = s.Fecha,
                Detalle = $"Proyecto: {s.ProyectoNombre} ({s.Partidas.Count} partidas)",
                Estatus = s.Estatus
            });
        }
        foreach (var sal in salidas)
        {
            desglose.Add(new
            {
                Tipo = "Salida Almacén",
                Folio = sal.Folio,
                Fecha = sal.Fecha,
                Detalle = $"Proyecto: {sal.ProyectoNombre} ({sal.CostoTotalEstimado:C2})",
                Estatus = "Entregada"
            });
        }
        foreach (var pre in prestamos)
        {
            desglose.Add(new
            {
                Tipo = "Préstamo Herr.",
                Folio = pre.Folio,
                Fecha = pre.FechaSalida,
                Detalle = $"{pre.Partidas.Count} herramientas ({pre.ProyectoNombre})",
                Estatus = pre.Estatus
            });
        }

        TxtDesglosePersonalTitulo.Text = $"Desglose de {enc.Nombre} ({enc.Puesto}) — {sols.Count} Solicitudes | {salidas.Count} Salidas | {prestamos.Count} Préstamos";
        DgDesglosePersonal.ItemsSource = desglose;
    }

    private void BtnNuevoEncargado_Click(object sender, RoutedEventArgs e)
    {
        _encargadoEditandoId = 0;
        DgDirPersonal.SelectedItem = null;
        if (CboAltaEncargadoDepto != null) CboAltaEncargadoDepto.SelectedIndex = -1;
        TxtAltaEncargadoNombre.Clear();
        TxtAltaEncargadoPuesto.Clear();
        TxtAltaEncargadoTelefono.Clear();
        TxtDesglosePersonalTitulo.Text = "Seleccione una persona para ver sus solicitudes, salidas de almacén y préstamos de herramienta";
        DgDesglosePersonal.ItemsSource = null;
    }

    private void BtnGuardarEncargado_Click(object sender, RoutedEventArgs e)
    {
        if (CboAltaEncargadoDepto.SelectedItem is not Departamento dep || string.IsNullOrWhiteSpace(TxtAltaEncargadoNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Seleccione un departamento y escriba el nombre completo.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DatabaseService.GuardarEncargado(dep.Id, TxtAltaEncargadoNombre.Text, TxtAltaEncargadoPuesto.Text, TxtAltaEncargadoTelefono.Text, _encargadoEditandoId);
        BtnNuevoEncargado_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarEncargado_Click(object sender, RoutedEventArgs e)
    {
        if (_encargadoEditandoId == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un registro de personal para eliminarlo.", "Personal no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar personal '{TxtAltaEncargadoNombre.Text}'"))
            return;

        if (System.Windows.MessageBox.Show(this, "¿Desea eliminar a esta persona del directorio?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarEncargado(_encargadoEditandoId);
            BtnNuevoEncargado_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar este registro de personal porque tiene documentos asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DgDirDepartamentos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgDirDepartamentos.SelectedItem is not Departamento d) return;
        _departamentoEditandoId = d.Id;
        TxtDeptoCodigo.Text = d.Codigo;
        TxtDeptoNombre.Text = d.Nombre;
    }

    private void BtnNuevoDepartamento_Click(object sender, RoutedEventArgs e)
    {
        _departamentoEditandoId = 0;
        DgDirDepartamentos.SelectedItem = null;
        TxtDeptoCodigo.Clear();
        TxtDeptoNombre.Clear();
    }

    private void BtnGuardarDepartamento_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDeptoCodigo.Text) || string.IsNullOrWhiteSpace(TxtDeptoNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Escriba el código y nombre del departamento.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DatabaseService.GuardarDepartamento(TxtDeptoCodigo.Text, TxtDeptoNombre.Text, _departamentoEditandoId);
        BtnNuevoDepartamento_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarDepartamento_Click(object sender, RoutedEventArgs e)
    {
        if (_departamentoEditandoId == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un departamento para eliminarlo.", "Departamento no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar departamento '{TxtDeptoNombre.Text}'"))
            return;

        if (System.Windows.MessageBox.Show(this, "¿Desea eliminar este departamento?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarDepartamento(_departamentoEditandoId);
            BtnNuevoDepartamento_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar este departamento porque tiene registros asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- 8.4 HERRAMIENTAS Y CATEGORÍAS ---
    private void DgDirHerramientas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgDirHerramientas.SelectedItem is not HerramientaActivo h) return;

        _herramientaEditandoId = h.Id;
        if (CboDirHerProducto.ItemsSource is IEnumerable<Producto> prods)
            CboDirHerProducto.SelectedItem = prods.FirstOrDefault(p => p.Id == h.ProductoId);
        TxtDirHerCodigo.Text = h.CodigoIndividual;
        TxtDirHerSerie.Text = h.NumeroSerie;
        TxtDirHerMarca.Text = h.MarcaModelo;
        TxtDirHerUbicacion.Text = h.Ubicacion;
        TxtDirHerNotas.Text = h.NotasEstado;

        foreach (ComboBoxItem item in CboDirHerEstado.Items)
        {
            if (string.Equals(item.Content?.ToString(), h.Estado, StringComparison.OrdinalIgnoreCase))
            {
                CboDirHerEstado.SelectedItem = item;
                break;
            }
        }
    }

    private void BtnNuevaHerramientaDir_Click(object sender, RoutedEventArgs e)
    {
        _herramientaEditandoId = 0;
        DgDirHerramientas.SelectedItem = null;
        if (CboDirHerProducto != null) CboDirHerProducto.SelectedIndex = -1;
        TxtDirHerCodigo.Clear();
        TxtDirHerSerie.Clear();
        TxtDirHerMarca.Clear();
        CboDirHerEstado.SelectedIndex = -1;
        TxtDirHerUbicacion.Clear();
        TxtDirHerNotas.Clear();
    }

    private void BtnGuardarHerramientaDir_Click(object sender, RoutedEventArgs e)
    {
        if (CboDirHerProducto.SelectedItem is not Producto prod)
        {
            System.Windows.MessageBox.Show(this, "Seleccione el producto base para la herramienta.", "Falta producto", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string codigo = string.IsNullOrWhiteSpace(TxtDirHerCodigo.Text)
            ? DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual")
            : TxtDirHerCodigo.Text.Trim();
        string estado = (CboDirHerEstado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Disponible";

        DatabaseService.GuardarHerramientaActivo(new HerramientaActivo
        {
            Id = _herramientaEditandoId,
            ProductoId = prod.Id,
            CodigoIndividual = codigo,
            NumeroSerie = string.IsNullOrWhiteSpace(TxtDirHerSerie.Text) ? "S/N" : TxtDirHerSerie.Text.Trim(),
            MarcaModelo = TxtDirHerMarca.Text.Trim(),
            Estado = estado,
            Ubicacion = string.IsNullOrWhiteSpace(TxtDirHerUbicacion.Text) ? "Almacén Herramientas" : TxtDirHerUbicacion.Text.Trim(),
            NotasEstado = TxtDirHerNotas.Text.Trim()
        });

        BtnNuevaHerramientaDir_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarHerramientaDir_Click(object sender, RoutedEventArgs e)
    {
        if (_herramientaEditandoId == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione una herramienta de la tabla para eliminarla.", "Herramienta no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar herramienta individual '{TxtDirHerCodigo.Text}'"))
            return;

        if (System.Windows.MessageBox.Show(this, "¿Desea eliminar esta herramienta individual?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarHerramientaActivo(_herramientaEditandoId);
            BtnNuevaHerramientaDir_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar esta herramienta porque tiene préstamos registrados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DgDirCategorias_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgDirCategorias.SelectedItem is not Categoria c) return;
        _categoriaEditandoId = c.Id;
        TxtDirCatNombre.Text = c.Nombre;
        TxtDirCatCampo1.Text = c.EtiquetaCampo1;
        TxtDirCatCampo2.Text = c.EtiquetaCampo2;
        TxtDirCatCampo3.Text = c.EtiquetaCampo3;
    }

    private void BtnNuevaCategoriaDir_Click(object sender, RoutedEventArgs e)
    {
        _categoriaEditandoId = 0;
        DgDirCategorias.SelectedItem = null;
        TxtDirCatNombre.Clear();
        TxtDirCatCampo1.Clear();
        TxtDirCatCampo2.Clear();
        TxtDirCatCampo3.Clear();
    }

    private void BtnGuardarCategoriaDir_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDirCatNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Escriba el nombre de la categoría.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string c1 = string.IsNullOrWhiteSpace(TxtDirCatCampo1.Text) ? "Capacidad Eléctrica / Tipo" : TxtDirCatCampo1.Text.Trim();
        string c2 = string.IsNullOrWhiteSpace(TxtDirCatCampo2.Text) ? "Especificación / Material" : TxtDirCatCampo2.Text.Trim();
        string c3 = string.IsNullOrWhiteSpace(TxtDirCatCampo3.Text) ? "Detalle Adicional / Color" : TxtDirCatCampo3.Text.Trim();

        DatabaseService.GuardarCategoria(
            TxtDirCatNombre.Text,
            _categoriaEditandoId,
            c1,
            c2,
            c3);
        BtnNuevaCategoriaDir_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarCategoriaDir_Click(object sender, RoutedEventArgs e)
    {
        if (_categoriaEditandoId == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione una categoría para eliminarla.", "Categoría no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar categoría '{TxtDirCatNombre.Text}'"))
            return;

        if (System.Windows.MessageBox.Show(this, "¿Desea eliminar esta categoría?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarCategoria(_categoriaEditandoId);
            BtnNuevaCategoriaDir_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar esta categoría porque tiene productos vinculados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRespaldarDb_Click(object sender, RoutedEventArgs e)
    {
        string ruta = DatabaseService.CrearRespaldoBaseDatos();
        System.Windows.MessageBox.Show(this, $"Respaldo local creado exitosamente en:\n{ruta}", "Respaldo SQLite", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static decimal ParseDecimal(string? txt, decimal defecto)
    {
        if (string.IsNullOrWhiteSpace(txt)) return defecto;
        string clean = txt.Replace("$", "").Replace(",", "").Trim();
        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return val;
        return defecto;
    }
}
