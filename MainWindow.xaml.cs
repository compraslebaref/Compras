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
    private readonly ObservableCollection<HerramientaActivo> _herramientasPorPrestar = new();
    private readonly ObservableCollection<PartidaCompraSinFactura> _partidasCompraSinFactura = new();
    private List<PrestamoVistaFila> _todosPrestamosFilas = new();
    private List<MovimientoAlmacenFila> _todosMovimientosAlmacen = new();
    private FacturaXml? _facturaXmlEnPantalla;
    private bool _actualizandoCombosXml = false;
    private bool _modoAltaNuevoProducto = false;
    private readonly Dictionary<ComboBox, string> _placeholdersCombos = new();
    private readonly Dictionary<ComboBox, TextBlock> _textBlocksPlaceholderCombos = new();
    private readonly DispatcherTimer _timerAutoEscaneoPistola;
    private DateTime _ultimoPulsoPistolaUtc = DateTime.MinValue;
    private int _rachaPulsosRapidosPistola = 0;
    private string _origenAutoEscaneo = string.Empty;
    private bool _temaOscuroActivo = false;
    private bool _sidebarColapsado = false;

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
        public string FechaPromesa { get; set; } = string.Empty;
        public string EstadoDevolucion { get; set; } = string.Empty;
        public string GrupoValeResumen { get; set; } = string.Empty;
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

    /// <summary>
    /// Aplica tema y color de acento del sistema; si Windows (o una política de seguridad)
    /// no permite leer el acento, usa el acento predeterminado del tema.
    /// </summary>
    private static void AplicarTemaNativo(ApplicationTheme tema)
    {
        try
        {
            ApplicationThemeManager.Apply(tema, WindowBackdropType.Mica, true);
        }
        catch (Exception)
        {
            ApplicationThemeManager.Apply(tema, WindowBackdropType.Mica, false);
        }
    }

    private static Style? CeldaRecortable() => Application.Current.TryFindResource("CeldaRecortableStyle") as Style;

    private static Brush Tk(string key) => Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    public MainWindow()
    {
        InitializeComponent();

        // Integración nativa con Windows: tema (claro/oscuro) y color de acento del sistema
        var temaSistema = ApplicationTheme.Light;
        try
        {
            if (ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark)
                temaSistema = ApplicationTheme.Dark;
        }
        catch (Exception)
        {
            // Sin acceso al registro: se mantiene el tema claro.
        }
        _temaOscuroActivo = temaSistema == ApplicationTheme.Dark;
        AplicarTemaNativo(temaSistema);
        if (BtnAlternarTema != null)
            BtnAlternarTema.Content = _temaOscuroActivo ? "Claro" : "Oscuro";

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

        _timerAutoEscaneoPistola = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _timerAutoEscaneoPistola.Tick += (_, _) =>
        {
            _timerAutoEscaneoPistola.Stop();
            EjecutarAutoEscaneoPistolaSinEnter();
        };

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewTextInput += MainWindow_PreviewTextInput;
        RegistrarSuscripcionesMediator();
        Loaded += async (_, _) =>
        {
            await EjecutarConIndicadorCargaAsync("Cargando sistema y catálogos...", () =>
            {
                ConfigurarPlaceholdersInicialesCombos();
                CargarTodoElSistema();
                PonerEditorProductoEnReposo();
                IniciarNuevaSolicitudVacia();
                PonerFormulariosDirectoriosEnBlanco();
                InicializarRolYCheckpoints();
            });
            TxtOmniScanner.Focus();
        };
    }

    private void RegistrarSuscripcionesMediator()
    {
        var med = AppMediator.Instance;

        med.Suscribir<NavegarModuloMessage>(msg =>
        {
            CambiarSeccion((int)msg.Modulo);
            if (!string.IsNullOrWhiteSpace(msg.FiltroBusqueda))
            {
                if (msg.Modulo == ModuloSistema.Catalogo && TxtBuscarCatalogo != null)
                {
                    TxtBuscarCatalogo.Text = msg.FiltroBusqueda;
                    RefrescarCatalogoVisual();
                }
                else if (msg.Modulo == ModuloSistema.AlmacenPos && TxtBuscarAlmacen != null)
                {
                    TxtBuscarAlmacen.Text = msg.FiltroBusqueda;
                }
            }
        });

        med.Suscribir<AbrirMapaAnaquelesMessage>(msg =>
        {
            MostrarSelectorYMapaAnaqueles(msg.CoordenadaInicial, msg.AlSeleccionarCoordenada, this);
        });

        med.Suscribir<ProcesarScannerMessage>(msg =>
        {
            ProcesarCodigoEscaneado(msg.CodigoRaw);
        });

        med.Suscribir<DatosActualizadosMessage>(msg =>
        {
            CargarTodoElSistema(msg.ConservarScrollCatalogo);
        });

        med.Suscribir<CarritoActualizadoMessage>(msg =>
        {
            if (TxtStatusCarrito != null)
            {
                TxtStatusCarrito.Text = $"Carrito: {msg.TotalSkus} SKUs ({msg.TotalUnidades:0.##} u.)";
            }
        });

        med.Suscribir<AlternarTemaVisualMessage>(_ =>
        {
            AlternarTemaVisualInterno();
        });
    }

    private void ConfigurarPlaceholdersInicialesCombos()
    {
        // Tab 1: Catálogo
        EstablecerPlaceholderCombo(CboProdCategoria, "Seleccione la categoría del artículo...");
        EstablecerPlaceholderCombo(CboProdFamilia, "Seleccione o escriba familia (ej. JABON, MICROFIBRA)...");
        EstablecerPlaceholderCombo(TxtProdUbicacion, "Seleccione o escriba ubicación (ej. A-01, DETRAS-A, GABINETE)...");

        // Tab 2: Solicitudes
        EstablecerPlaceholderCombo(CboSolDepartamento, "Seleccione departamento solicitante...");
        EstablecerPlaceholderCombo(CboSolEncargado, "Seleccione primero un departamento...");
        EstablecerPlaceholderCombo(CboSolProyecto, "Opcional: Proyecto destino (en blanco = Consumo Interno)...");
        EstablecerPlaceholderCombo(CboSolProductoAgregar, "Busque arriba o despliegue para elegir artículo del catálogo...");

        // Tab 3: Comparativa
        EstablecerPlaceholderCombo(CboCompSolicitud, "Seleccione una Solicitud de Compra para cotizar...");
        EstablecerPlaceholderCombo(CboCompReferenciasSimilares, "Seleccione una compra previa similar si desea aplicarla...");
        EstablecerPlaceholderCombo(CboCompProv1, "Seleccione proveedor para Opción 1...");
        EstablecerPlaceholderCombo(CboCompProv2, "Seleccione proveedor para Opción 2...");
        EstablecerPlaceholderCombo(CboCompProv3, "Seleccione proveedor para Opción 3...");

        // Tab 5: Almacén POS y Préstamos
        EstablecerPlaceholderCombo(CboSalDepartamento, "Seleccione depto. que aprueba...");
        EstablecerPlaceholderCombo(CboSalEncargado, "Seleccione o escriba quién recibe...");
        EstablecerPlaceholderCombo(CboSalProyecto, "Opcional: Proyecto destino (en blanco = Consumo Interno)...");
        EstablecerPlaceholderCombo(CboSalProductoPos, "Escanee código o seleccione material de almacén...");
        EstablecerPlaceholderCombo(CboPreDepartamento, "Seleccione depto. que aprueba...");
        EstablecerPlaceholderCombo(CboPrePersonaRecibe, "Seleccione o escriba quién recibe...");
        EstablecerPlaceholderCombo(CboPreProyecto, "Opcional: Proyecto destino (en blanco = Uso Interno)...");
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
        EstablecerPlaceholderCombo(TxtDirHerUbicacion, "Seleccione o escriba ubicación (ej. A-04, DETRAS-A, GABINETE)...");
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
                Foreground = Tk("TextFillColorSecondaryBrush"),
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
            if (MainTabControl.SelectedIndex == 5 && TxtSalBuscarProductoPos != null)
            {
                TxtSalBuscarProductoPos.Focus();
                TxtSalBuscarProductoPos.SelectAll();
            }
            else
            {
                TxtOmniScanner.Focus();
                TxtOmniScanner.SelectAll();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            CambiarSeccion(1);
            TxtBuscarCatalogo?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F4)
        {
            MostrarSelectorYMapaAnaqueles(null, null, this);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            CargarTodoElSistema();
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

    private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
            return;

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            return;

        // Si el foco ya está en un TextBox (o en el TextBox interno de un ComboBox editable) o PasswordBox, dejar que escriba normal
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
            Keyboard.FocusedElement is System.Windows.Controls.PasswordBox)
        {
            return;
        }

        // Si ningún campo de texto tiene el foco (ej. al disparar con la pistola lectora de código de barras o empezar a teclear directo en la pestaña):
        if (MainTabControl.SelectedIndex == 5 && TxtSalBuscarProductoPos != null)
        {
            TxtSalBuscarProductoPos.Focus();
            TxtSalBuscarProductoPos.Text += e.Text;
            TxtSalBuscarProductoPos.CaretIndex = TxtSalBuscarProductoPos.Text.Length;
            e.Handled = true;
        }
        else if (MainTabControl.SelectedIndex == 2 && TxtBuscarProductoSolicitud != null)
        {
            TxtBuscarProductoSolicitud.Focus();
            TxtBuscarProductoSolicitud.Text += e.Text;
            TxtBuscarProductoSolicitud.CaretIndex = TxtBuscarProductoSolicitud.Text.Length;
            e.Handled = true;
        }
        else if (MainTabControl.SelectedIndex == 1 && TxtBuscarCatalogo != null)
        {
            TxtBuscarCatalogo.Focus();
            TxtBuscarCatalogo.Text += e.Text;
            TxtBuscarCatalogo.CaretIndex = TxtBuscarCatalogo.Text.Length;
            e.Handled = true;
        }
        else if (TxtOmniScanner != null)
        {
            TxtOmniScanner.Focus();
            TxtOmniScanner.Text += e.Text;
            TxtOmniScanner.CaretIndex = TxtOmniScanner.Text.Length;
            e.Handled = true;
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
            if (TxtProdUnidadCompra != null)
            {
                string ucActual = TxtProdUnidadCompra.Text;
                TxtProdUnidadCompra.ItemsSource = DatabaseService.UnidadesMedidaEstandar;
                TxtProdUnidadCompra.Text = ucActual;
            }
            if (TxtProdUnidadAlmacen != null)
            {
                string uaActual = TxtProdUnidadAlmacen.Text;
                TxtProdUnidadAlmacen.ItemsSource = DatabaseService.UnidadesMedidaEstandar;
                TxtProdUnidadAlmacen.Text = uaActual;
            }
            var ubicacionesCatalogo = ObtenerCatalogoUbicacionesDesplegable();
            if (CboFiltroUbicacion != null)
            {
                string ubiFiltroPrevia = CboFiltroUbicacion.SelectedItem as string ?? "Todas las ubicaciones";
                var opcionesFiltroUbi = ObtenerOpcionesFiltroUbicacionCatalogo(productos);
                CboFiltroUbicacion.ItemsSource = opcionesFiltroUbi;
                CboFiltroUbicacion.SelectedItem = opcionesFiltroUbi.FirstOrDefault(u => u.Equals(ubiFiltroPrevia, StringComparison.OrdinalIgnoreCase)) ?? opcionesFiltroUbi[0];
            }
            if (TxtProdUbicacion != null)
            {
                string ubiActual = TxtProdUbicacion.Text;
                TxtProdUbicacion.ItemsSource = ubicacionesCatalogo;
                TxtProdUbicacion.SelectedIndex = -1;
                TxtProdUbicacion.Text = ubiActual;
            }
            if (TxtDirHerUbicacion != null)
            {
                string ubiHerActual = TxtDirHerUbicacion.Text;
                TxtDirHerUbicacion.ItemsSource = ubicacionesCatalogo;
                TxtDirHerUbicacion.SelectedIndex = -1;
                TxtDirHerUbicacion.Text = ubiHerActual;
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
            CboSalDepartamento.ItemsSource = deptos;
            CboSalDepartamento.SelectedItem = salDepPrev > 0 ? deptos.FirstOrDefault(d => d.Id == salDepPrev) : null;
            CboSalProyecto.ItemsSource = proyectos;
            CboSalProyecto.SelectedItem = salProyPrev > 0 ? proyectos.FirstOrDefault(p => p.Id == salProyPrev) : null;
            FiltrarComboProductoSalidaPos();

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
            var prodsHer = productos
                .OrderByDescending(p => p.EsHerramienta)
                .ThenByDescending(p => p.CategoriaId == 4)
                .ThenBy(p => p.Nombre)
                .ToList();
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

        if (index == 5)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtSalBuscarProductoPos?.Focus();
            }), DispatcherPriority.Input);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int idx))
        {
            AppMediator.Instance.Publicar(new NavegarModuloMessage((ModuloSistema)idx));
        }
    }

    private void BtnIrRecepciones_Click(object sender, RoutedEventArgs e) =>
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.OrdenesCompra));

    private void BtnIrAlmacenPos_Click(object sender, RoutedEventArgs e) =>
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.AlmacenPos));

    // --- NAVEGACIÓN DESDE TARJETAS KPI INTERACTIVAS DEL DASHBOARD (VÍA MEDIATOR) ---
    private void KpiCardSolicitudes_Click(object sender, MouseButtonEventArgs e) =>
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Solicitudes));

    private void KpiCardOrdenes_Click(object sender, MouseButtonEventArgs e) =>
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.OrdenesCompra));

    private void KpiCardAlertas_Click(object sender, MouseButtonEventArgs e)
    {
        if (DgDashboardAlertas?.Items.Count > 0)
        {
            DgDashboardAlertas.Focus();
            DgDashboardAlertas.SelectedIndex = 0;
            DgDashboardAlertas.ScrollIntoView(DgDashboardAlertas.SelectedItem);
        }
        else
        {
            AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Catalogo));
        }
    }

    private void KpiCardHerramientas_Click(object sender, MouseButtonEventArgs e) =>
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.AlmacenPos));

    private void KpiCardAnalisis_Click(object sender, MouseButtonEventArgs e) =>
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Analisis));

    // --- ALTERNAR TEMA CLARO / OSCURO Y COLAPSAR SIDEBAR ---
    private void BtnAlternarTema_Click(object sender, RoutedEventArgs e) =>
        AppMediator.Instance.Publicar(new AlternarTemaVisualMessage());

    private void AlternarTemaVisualInterno()
    {
        _temaOscuroActivo = !_temaOscuroActivo;
        var nuevoTema = _temaOscuroActivo ? ApplicationTheme.Dark : ApplicationTheme.Light;
        AplicarTemaNativo(nuevoTema);

        if (BtnAlternarTema != null)
        {
            BtnAlternarTema.Content = _temaOscuroActivo ? "Claro" : "Oscuro";
        }
    }

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (!_toggleSidebarAutomatico) _sidebarAutoColapsado = false; _sidebarColapsado = !_sidebarColapsado;
        if (ColSidebarNavegacion != null)
        {
            ColSidebarNavegacion.Width = new GridLength(_sidebarColapsado ? 68 : 232);
        }

        Visibility visTexto = _sidebarColapsado ? Visibility.Collapsed : Visibility.Visible;
        if (PnlMarcaSidebar != null) PnlMarcaSidebar.Visibility = visTexto;
        if (PnlFooterSidebar != null) PnlFooterSidebar.Visibility = visTexto;
        if (LblSecGeneral != null) LblSecGeneral.Visibility = visTexto;
        if (LblSecCompras != null) LblSecCompras.Visibility = visTexto;
        if (LblSecAlmacen != null) LblSecAlmacen.Visibility = visTexto;
        if (BtnToggleSidebar != null) BtnToggleSidebar.Content = _sidebarColapsado ? "⟫" : "⟪";

        var navItems = new (Wpf.Ui.Controls.Button? btn, string label)[]
        {
            (NavDashboard, "Inicio"),
            (NavCatalogo, "Catálogo"),
            (NavSolicitudes, "Solicitudes"),
            (NavComparativa, "Comparativa"),
            (NavOrdenes, "Órdenes de Compra"),
            (NavAlmacenPos, "Almacén"),
            (NavFacturasXml, "Facturas"),
            (NavAnalisis, "Análisis"),
            (NavDirectorios, "Directorios")
        };

        foreach (var (btn, label) in navItems)
        {
            if (btn == null) continue;
            btn.Content = _sidebarColapsado ? null : label;
            btn.HorizontalContentAlignment = _sidebarColapsado ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }

        RefrescarDashboard();
    }

    private void BtnPerfilAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (_sesionAdminActiva)
        {
            var resp = DialogoFluent.Show(this,
                "La sesión de Perfil Administrador se encuentra ACTIVA (desbloqueada).\n\n¿Desea bloquear nuevamente la sesión de Administrador para que vuelva a solicitar el PIN en acciones críticas?",
                "Perfil Administrador Activo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp == System.Windows.MessageBoxResult.Yes)
            {
                _sesionAdminActiva = false;
                if (BtnPerfilAdmin != null)
                {
                    BtnPerfilAdmin.Content = "Perfil: Administrador";
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
                BtnPerfilAdmin.Content = "Admin Activo";
                BtnPerfilAdmin.Appearance = ControlAppearance.Primary;
            }
        }
    }

    private void BtnCambiarPinAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (!SolicitarAutorizacionAdmin("Autorizar cambio de PIN de Administrador", false))
            return;

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Cambiar PIN de Administrador",
            Width = 390,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Definir Nuevo PIN de Administrador",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Ingrese la nueva clave o PIN (mínimo 4 caracteres):",
            FontSize = 12,
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
                DialogoFluent.Show(win, "El PIN debe tener al menos 4 caracteres.", "PIN inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "El PIN de Administrador se actualizó correctamente.", "PIN Actualizado", MessageBoxButton.OK, MessageBoxImage.Information);
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
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Autorización de Administrador Requerida",
            Width = 420,
            Height = 275,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Clave de Administrador",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("AccentTextFillColorPrimaryBrush"),
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
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
                DialogoFluent.Show(win, "PIN de Administrador incorrecto.", "Acceso denegado", MessageBoxButton.OK, MessageBoxImage.Error);
                pwdBox.Clear();
                pwdBox.Focus();
                return;
            }

            if (chkMantener.IsChecked == true)
            {
                _sesionAdminActiva = true;
                if (BtnPerfilAdmin != null)
                {
                    BtnPerfilAdmin.Content = "Admin Activo";
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

    private void RegistrarPulsoScannerPistola(string origen, string? textoActual)
    {
        var ahora = DateTime.UtcNow;
        double ms = (ahora - _ultimoPulsoPistolaUtc).TotalMilliseconds;
        _ultimoPulsoPistolaUtc = ahora;

        if (ms <= 55)
            _rachaPulsosRapidosPistola++;
        else
            _rachaPulsosRapidosPistola = 1;

        _origenAutoEscaneo = origen;
        _timerAutoEscaneoPistola.Stop();

        string txt = textoActual?.Trim() ?? string.Empty;
        if (txt.Length >= 4 && _rachaPulsosRapidosPistola >= 3)
        {
            _timerAutoEscaneoPistola.Start();
        }
    }

    private void EjecutarAutoEscaneoPistolaSinEnter()
    {
        if (_rachaPulsosRapidosPistola < 3)
            return;

        _rachaPulsosRapidosPistola = 0;

        if (_origenAutoEscaneo == "SalidaPos" && TxtSalBuscarProductoPos != null)
        {
            string raw = TxtSalBuscarProductoPos.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(raw);
            if (!string.IsNullOrWhiteSpace(code) &&
                (EsPrefijoDocumento(code) || DatabaseService.BuscarProductoPorCodigo(code) != null || DatabaseService.BuscarProductoPorCodigo(raw) != null))
            {
                if (ProcesarBusquedaOEscaneoSalidaPos(raw))
                {
                    TxtSalBuscarProductoPos.Clear();
                    FiltrarComboProductoSalidaPos();
                    TxtSalBuscarProductoPos.Focus();
                }
            }
        }
        else if (_origenAutoEscaneo == "AlmacenTop" && TxtBuscarAlmacen != null)
        {
            string raw = TxtBuscarAlmacen.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(raw);
            if (!string.IsNullOrWhiteSpace(code) &&
                (EsPrefijoDocumento(code) || DatabaseService.BuscarProductoPorCodigo(code) != null || DatabaseService.BuscarProductoPorCodigo(raw) != null))
            {
                EjecutarAccionBuscadorAlmacen();
            }
        }
        else if (_origenAutoEscaneo == "Omni" && TxtOmniScanner != null)
        {
            string raw = TxtOmniScanner.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(raw);
            if (!string.IsNullOrWhiteSpace(code) &&
                (EsPrefijoDocumento(code) || DatabaseService.BuscarProductoPorCodigo(code) != null || DatabaseService.BuscarProductoPorCodigo(raw) != null))
            {
                ProcesarCodigoEscaneado(raw);
            }
        }
    }

    private void TxtOmniScanner_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RegistrarPulsoScannerPistola("Omni", TxtOmniScanner.Text);
    }

    private void TxtOmniScanner_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || (e.Key == Key.Tab && _rachaPulsosRapidosPistola >= 3))
        {
            _timerAutoEscaneoPistola.Stop();
            _rachaPulsosRapidosPistola = 0;
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

        // 1. ¿Es el folio de una Orden de Compra (OC-xxxx)? -> Abrir modal de recepción parcial/total de esa OC
        if (code.StartsWith("OC-"))
        {
            var ocs = DatabaseService.ObtenerOrdenesCompra();
            var oc = ocs.FirstOrDefault(o => CoincideFolioFlexible(o.Folio, code));
            if (oc != null)
            {
                CambiarSeccion(4);
                DgOrdenesCompra.SelectedItem = DgOrdenesCompra.Items.Cast<OrdenCompra>().FirstOrDefault(x => x.Id == oc.Id);
                MostrarModalRecepcionOrdenCompraAgrupada(oc);
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
                string? herEspecifica = code.StartsWith("HER-", StringComparison.OrdinalIgnoreCase) ? match.CodigoIndividual : null;
                MostrarModalDevolucionPrestamoHerramienta(match.PrestamoOriginal, herEspecifica);
                return;
            }

            // Si es una herramienta disponible, agregarla a la lista de préstamo actual
            var disp = CboHerramientaDisponible.Items.Cast<HerramientaActivo>().FirstOrDefault(h => CoincideFolioFlexible(h.CodigoIndividual, code));
            if (disp != null)
            {
                AgregarHerramientaAListaPrestamo(disp);
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
                if (EsModoAlmacen || MainTabControl.SelectedIndex == 5 || string.Equals(sol.Estatus, "Autorizada para Surtir Stock", StringComparison.OrdinalIgnoreCase)) { CargarSolicitudEnSalidaPos(sol); CambiarSeccion(5); return; }
                CambiarSeccion(3);
                CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(x => x.Id == sol.Id);
                return;
            }
        }

        // 4. ¿Es un Vale de Entrada (ENT-xxxx) o Vale de Salida (SAL-xxxx)? -> Abrir el modal de siguiente acción
        if (code.StartsWith("ENT-"))
        {
            var ent = DatabaseService.ObtenerEntradasAlmacen().FirstOrDefault(x => CoincideFolioFlexible(x.Folio, code));
            if (ent != null)
            {
                if (MainTabControl.SelectedIndex == 5)
                {
                    RefrescarAlmacenPosYPrestamos();
                    var filaMov = DgMovimientosAlmacen.Items.Cast<MovimientoAlmacenFila>().FirstOrDefault(m => m.Folio == ent.Folio);
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
                MostrarModalAccionEntradaAlmacen(ent);
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
                var filaMov = DgMovimientosAlmacen.Items.Cast<MovimientoAlmacenFila>().FirstOrDefault(m => m.Folio == sal.Folio);
                if (filaMov != null)
                {
                    DgMovimientosAlmacen.SelectedItem = filaMov;
                    DgMovimientosAlmacen.ScrollIntoView(filaMov);
                }
                MostrarModalDevolucionMaterialSalida(sal);
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
                decimal cant = ParseDecimal(TxtSalCantidadPos?.Text, 1m);
                CboSalProductoPos.SelectedItem = CboSalProductoPos.Items.Cast<Producto>().FirstOrDefault(x => x.Id == prod.Id);
                AgregarProductoAValeSalidaPos(prod, cant);
                TxtUltimoEscaneo.Text = $"Agregado a Salida: [{prod.CodigoInterno}] {prod.Nombre} (x{cant:0.##})";
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

        DialogoFluent.Show(this,
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

        var alertasStock = prods
            .Where(p => p.StockMinimo > p.StockActual)
            .OrderBy(p => p.StockActual <= 0 ? 0 : (p.StockActual <= p.StockMinimo * 0.5m ? 1 : 2))
            .ThenByDescending(p => p.FaltanteStock)
            .ThenBy(p => p.StockActual)
            .ToList();

        int cantSolsPendientes = sols.Count(s => s.Estatus == "Pendiente");
        int cantOcsAbiertas = ocs.Count(o => o.Estatus != "Recibida Completa" && o.Estatus != "Cancelada" && o.Estatus != "Cerrada Parcial Definitiva" && o.Estatus != "Cerrada Parcial (Admin)");
        int cantAlertas = alertasStock.Count;
        int cantPrestamos = her.Count(h => h.Estado == "En Préstamo");

        KpiSolicitudes.Text = cantSolsPendientes.ToString();
        KpiOcsAbiertas.Text = cantOcsAbiertas.ToString();
        KpiStockBajo.Text = cantAlertas.ToString();
        KpiHerramientasPrestadas.Text = cantPrestamos.ToString();
        KpiGastoTotal.Text = ocs.Where(o => o.Estatus != "Cancelada").Sum(o => o.TotalConIva).ToString("C2", CultureInfo.GetCultureInfo("es-MX"));

        ActualizarBadgesSidebar(cantAlertas, cantSolsPendientes, cantOcsAbiertas, cantPrestamos);

        DgDashboardOcs.ItemsSource = ocs.Where(o => o.Estatus != "Recibida Completa" && o.Estatus != "Cancelada" && o.Estatus != "Cerrada Parcial Definitiva" && o.Estatus != "Cerrada Parcial (Admin)").ToList();
        DgDashboardAlertas.ItemsSource = alertasStock;

        RefrescarCalendarioYAgendaDashboard();
    }

    private void ActualizarBadgesSidebar(int alertas, int solicitudes, int ocs, int prestamos)
    {
        bool mostrar = !_sidebarColapsado;

        if (BdgSidebarAlertas != null && TxtBadgeSidebarAlertas != null)
        {
            TxtBadgeSidebarAlertas.Text = alertas.ToString();
            BdgSidebarAlertas.Visibility = (mostrar && alertas > 0) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (BdgSidebarSolicitudes != null && TxtBadgeSidebarSolicitudes != null)
        {
            TxtBadgeSidebarSolicitudes.Text = solicitudes.ToString();
            BdgSidebarSolicitudes.Visibility = (mostrar && solicitudes > 0) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (BdgSidebarOcs != null && TxtBadgeSidebarOcs != null)
        {
            TxtBadgeSidebarOcs.Text = ocs.ToString();
            BdgSidebarOcs.Visibility = (mostrar && ocs > 0) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (BdgSidebarPrestamos != null && TxtBadgeSidebarPrestamos != null)
        {
            TxtBadgeSidebarPrestamos.Text = prestamos.ToString();
            BdgSidebarPrestamos.Visibility = (mostrar && prestamos > 0) ? Visibility.Visible : Visibility.Collapsed;
        }
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
            DialogoFluent.Show(this, "Escriba el título o descripción del recordatorio o evento.", "Falta descripción", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, "Los eventos automáticos de Órdenes de Compra o Préstamos se actualizan automáticamente al recibir la OC o devolver la herramienta.", "Evento automático", MessageBoxButton.OK, MessageBoxImage.Information);
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
        string ubiFiltro = CboFiltroUbicacion?.SelectedItem as string ?? "Todas las ubicaciones";

        IEnumerable<Producto> query = _cacheProductos;
        if (catId > 0)
        {
            query = query.Where(p => p.CategoriaId == catId);
        }
        if (!string.IsNullOrWhiteSpace(famFiltro) && !famFiltro.Equals("Todas las familias", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => string.Equals(p.Familia, famFiltro, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(ubiFiltro) && !ubiFiltro.Equals("Todas las ubicaciones", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => CoincideFiltroUbicacionProducto(p, ubiFiltro));
        }

        if (!string.IsNullOrWhiteSpace(filtroRaw))
        {
            query = query.Where(p =>
                p.Nombre.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Familia.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Ubicacion.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
                p.Ubicacion.Contains(filtroNorm, StringComparison.OrdinalIgnoreCase) ||
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

    private void CboFiltroUbicacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _actualizandoCombosBase) return;
        RefrescarCatalogoVisual();
    }

    private static List<string> ObtenerOpcionesFiltroUbicacionCatalogo(IEnumerable<Producto>? productos = null)
    {
        var lista = new List<string>
        {
            "Todas las ubicaciones",
            "🏢 Zona: Almacén General (Sin anaquel específico)",
            "⚠️ Sin ubicación asignada",
            "🧱 Zona: DETRAS-A (Detrás de A / Pared)",
            "🏗️ Zona: PISO (Piso / Exterior)",
            "🚪 Zona: GABINETE (Todos los 5 niveles)",
            "📦 Zona: Anaquel A (A-01 a A-09)",
            "📦 Zona: Anaquel B (B-01 a B-09)",
            "📦 Zona: Anaquel C (C-01 a C-09)",
            "📦 Zona: Anaquel D (D-01 a D-09)",
            "📦 Zona: Anaquel E (E-01 a E-09)",
            "📦 Zona: Anaquel F (F-01 a F-09)"
        };

        // Agregar los 5 niveles individuales del Gabinete y las 54 posiciones de Anaqueles A–F
        foreach (var item in ObtenerCatalogoUbicacionesDesplegable())
        {
            // Almacén General, DETRAS-A y PISO ya están como opciones de zona única arriba
            if (item.StartsWith("Almacén General", StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith("DETRAS-A", StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith("PISO", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            lista.Add(item);
        }

        // Incluir cualquier otra ubicación personalizada que exista en los productos de la base de datos
        if (productos != null)
        {
            var extras = productos
                .Select(p => p.Ubicacion?.Trim() ?? string.Empty)
                .Where(u => !string.IsNullOrWhiteSpace(u) && string.IsNullOrEmpty(ParsearCoordenadaAlmacen(u).Estante))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(u => u);

            foreach (var extra in extras)
            {
                lista.Add(extra);
            }
        }

        return lista;
    }

    private static bool CoincideFiltroUbicacionProducto(Producto p, string filtroUbi)
    {
        if (string.IsNullOrWhiteSpace(filtroUbi) || filtroUbi.Equals("Todas las ubicaciones", StringComparison.OrdinalIgnoreCase))
            return true;

        if (filtroUbi.StartsWith("⚠️", StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(p.Ubicacion);

        if (string.IsNullOrWhiteSpace(p.Ubicacion))
            return false;

        var (estProd, cuadProd, _) = ParsearCoordenadaAlmacen(p.Ubicacion);

        // Filtros por Zona completa (Almacén General, DETRAS-A, PISO, GABINETE completo o Anaquel A..F completo)
        if (filtroUbi.Contains("Zona: Almacén General", StringComparison.OrdinalIgnoreCase))
            return string.Equals(estProd, "ALMACEN-GENERAL", StringComparison.OrdinalIgnoreCase);

        if (filtroUbi.Contains("Zona: DETRAS-A", StringComparison.OrdinalIgnoreCase))
            return string.Equals(estProd, "DETRAS-A", StringComparison.OrdinalIgnoreCase);

        if (filtroUbi.Contains("Zona: PISO", StringComparison.OrdinalIgnoreCase))
            return string.Equals(estProd, "PISO", StringComparison.OrdinalIgnoreCase);

        if (filtroUbi.Contains("Zona: GABINETE", StringComparison.OrdinalIgnoreCase))
            return string.Equals(estProd, "GABINETE", StringComparison.OrdinalIgnoreCase);

        string[] anaqueles = { "A", "B", "C", "D", "E", "F" };
        foreach (var est in anaqueles)
        {
            if (filtroUbi.Contains($"Zona: Anaquel {est} ", StringComparison.OrdinalIgnoreCase))
                return string.Equals(estProd, est, StringComparison.OrdinalIgnoreCase);
        }

        // Filtro por nivel específico (ej. GABINETE-03 o A-04)
        string codFiltro = ExtraerCodigoUbicacionLimpio(filtroUbi);
        var (estFiltro, cuadFiltro, _) = ParsearCoordenadaAlmacen(codFiltro);
        if (!string.IsNullOrEmpty(estFiltro))
        {
            return string.Equals(estProd, estFiltro, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(cuadProd, cuadFiltro, StringComparison.OrdinalIgnoreCase);
        }

        return p.Ubicacion.Contains(codFiltro, StringComparison.OrdinalIgnoreCase);
    }

    private void BtnGestionarFamilias_Click(object sender, RoutedEventArgs e)
    {
        var familias = DatabaseService.ObtenerFamiliasProductos();
        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Gestión Manual de Familias / Agrupadores de Productos",
            Width = 540,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Crear, Renombrar o Unificar Familias de Productos",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Las Familias agrupan productos equivalentes de distintas marcas o presentaciones (ej. 'JABON', 'MICROFIBRA', 'GAS REFRIGERANTE') para comparar sus precios históricos juntos.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        // Sección 1: Crear / Asignar nueva familia al producto actual en edición
        var bdrNueva = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
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
            Background = Tk("ControlFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
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
                DialogoFluent.Show(win, "Escriba el nombre de la familia.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
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
                DialogoFluent.Show(win, $"Se guardó la familia '{fam}' y se asignó al producto {_productoEnEdicion.CodigoInterno}.", "Familia Guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DialogoFluent.Show(win, $"Se creó y guardó la familia '{fam}' en el catálogo de familias.", "Familia Creada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            win.Close();
        };

        btnAplicarRenombre.Click += (_, _) =>
        {
            string origen = (cboFamOrigen.SelectedItem as string)?.Trim() ?? string.Empty;
            string destino = txtFamDestino.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            {
                DialogoFluent.Show(win, "Seleccione la familia actual y escriba el nuevo nombre.", "Datos requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int afectados = DatabaseService.RenombrarFamiliaMasivo(origen, destino);
            if (CboProdFamilia != null && string.Equals(CboProdFamilia.Text, origen, StringComparison.OrdinalIgnoreCase))
            {
                CboProdFamilia.Text = destino;
            }
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            DialogoFluent.Show(win, $"Se actualizaron {afectados} productos de la familia '{origen}' a '{destino}'.", "Familia Actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, "Escriba primero el nombre del producto para detectar su categoría y especificaciones técnicas.", "Nombre vacío", MessageBoxButton.OK, MessageBoxImage.Information);
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

        if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud sol)
        {
            if (sol.Id > 0 && _solicitudEditandoId != sol.Id)
            {
                _sincronizandoSolicitudes = true;
                try
                {
                    var enHist = DgHistorialSolicitudes?.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == sol.Id);
                    if (enHist != null && DgHistorialSolicitudes != null)
                        DgHistorialSolicitudes.SelectedItem = enHist;
                }
                finally
                {
                    _sincronizandoSolicitudes = false;
                }
                CargarSolicitudEnEditor(sol);
            }
            else if (sol.Id == 0 && _solicitudEditandoId > 0)
            {
                IniciarNuevaSolicitudVacia();
            }
        }

        ActualizarIndicadorCarritoSuperior();
    }

    private void ActualizarIndicadorCarritoSuperior()
    {
        if (TxtStatusCarrito == null) return;

        if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud sol && sol.Id > 0)
        {
            TxtStatusCarrito.Text = $"{sol.Folio}: {sol.TotalArticulos:0.##} art. ({sol.TotalSkus} SKUs)";
            if (BtnIrSolicitudDesdeCatalogo != null)
                BtnIrSolicitudDesdeCatalogo.Content = $"Ir a {sol.Folio} ({sol.TotalSkus} art.)";
        }
        else
        {
            decimal totalItems = _partidasNuevaSolicitud.Sum(x => x.CantidadSolicitada);
            int totalSkus = _partidasNuevaSolicitud.Count;
            TxtStatusCarrito.Text = $"Solicitud activa: {totalItems:0.##} art. ({totalSkus} SKUs)";
            if (BtnIrSolicitudDesdeCatalogo != null)
                BtnIrSolicitudDesdeCatalogo.Content = totalSkus > 0
                    ? $"📋 Ir a la Solicitud ({totalSkus} art.)"
                    : "📋 Ir a la Solicitud";
        }
    }

    private void AgregarAlCarritoTienda(Producto p, decimal cantidad)
    {
        if (CboCarritoSolicitudDestino?.SelectedItem is Solicitud solDestino && solDestino.Id > 0 && solDestino.EsEditable)
        {
            try
            {
                var solActualizada = DatabaseService.AgregarOIncrementarPartidaEnSolicitud(solDestino.Id, p.Id, cantidad, "Catálogo Visual");
                _solicitudEditandoId = solActualizada.Id;
                RefrescarSolicitudes(solActualizada.Id);
                RefrescarDashboard();
                ActualizarIndicadorCarritoSuperior();
                return;
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(this, ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (_solicitudEditandoId > 0)
        {
            IniciarNuevaSolicitudVacia();
        }

        AgregarProductoAPartidasSolicitud(p, cantidad, "Catálogo Visual");
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
            CargarProductoEnEditor(p, abrirEdicion: true);
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

    private void CargarProductoEnEditor(Producto p, bool abrirEdicion = false)
    {
        MostrarPanelEditorCatalogo(true);
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
        ActualizarEtiquetasUnidadesYConversionProducto();
        ActualizarSeleccionCatalogo();
        ActualizarBannerModoEditorProducto(p);
        ActualizarFichaProducto(p);
        SeleccionarPestanaPanelProducto(abrirEdicion);
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
        if (TxtProdUbicacion != null)
        {
            TxtProdUbicacion.SelectedIndex = -1;
            TxtProdUbicacion.Text = string.Empty;
        }
        ChkProdEsHerramienta.IsChecked = false;
        ImgPreviewProducto.Source = null;
        ImgBarcodeProductoEditor.Source = null;
        ActualizarEtiquetasFichaPorCategoria(null);
        ActualizarEtiquetasUnidadesYConversionProducto();
        ActualizarSeleccionCatalogo();
        ActualizarBannerModoEditorProducto(null);
        ActualizarFichaProducto(null);
        SeleccionarPestanaPanelProducto(false);
    }

    private void LimpiarEditorProducto()
    {
        MostrarPanelEditorCatalogo(true);
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
        if (TxtProdUbicacion != null)
        {
            TxtProdUbicacion.SelectedIndex = -1;
            TxtProdUbicacion.Text = string.Empty;
        }
        ChkProdEsHerramienta.IsChecked = false;
        ImgPreviewProducto.Source = null;
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(sig);
        ActualizarEtiquetasFichaPorCategoria(null);
        ActualizarEtiquetasUnidadesYConversionProducto();
        ActualizarSeleccionCatalogo();
        ActualizarBannerModoEditorProducto(null);
        ActualizarFichaProducto(null);
        SeleccionarPestanaPanelProducto(true);
    }

    private void UnidadOFactorProducto_Changed(object sender, EventArgs e)
    {
        if (!IsLoaded) return;
        ActualizarEtiquetasUnidadesYConversionProducto();
    }

    private void TxtProdFactorOCosto_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActualizarEtiquetasUnidadesYConversionProducto();
    }

    private void ActualizarEtiquetasUnidadesYConversionProducto()
    {
        if (LblProdExistenciaUnidad == null || LblProdStockMinUnidad == null || LblProdCostoUnidad == null || TxtResumenConversionProducto == null)
            return;

        string uCompraRaw = (TxtProdUnidadCompra?.SelectedItem as string) ?? TxtProdUnidadCompra?.Text ?? string.Empty;
        string uEntregaRaw = (TxtProdUnidadAlmacen?.SelectedItem as string) ?? TxtProdUnidadAlmacen?.Text ?? string.Empty;

        string uCompra = string.IsNullOrWhiteSpace(uCompraRaw) ? "Unid. Compra" : DatabaseService.NormalizarUnidadMedida(uCompraRaw);
        string uEntrega = string.IsNullOrWhiteSpace(uEntregaRaw) ? "Unid. Entrega" : DatabaseService.NormalizarUnidadMedida(uEntregaRaw);

        decimal factor = Math.Max(1m, ParseDecimal(TxtProdFactor?.Text, 1m));
        decimal costoCompra = ParseDecimal(TxtProdCosto?.Text, 0m);
        decimal costoEntrega = factor > 0 ? Math.Round(costoCompra / factor, 2) : costoCompra;

        LblProdExistenciaUnidad.Text = $"Existencia (en {uEntrega})";
        LblProdStockMinUnidad.Text = $"Stock Mín. ({uEntrega})";
        LblProdCostoUnidad.Text = $"Costo x {uCompra} (c/IVA)";

        if (factor > 1m || !string.Equals(uCompra, uEntrega, StringComparison.OrdinalIgnoreCase))
        {
            string costoTxt = costoCompra > 0m ? $" | Costo x {uEntrega}: {costoEntrega:C2}" : string.Empty;
            TxtResumenConversionProducto.Text = $"1 {uCompra} (Compra) = {factor:0.##} {uEntrega} (Entrega en almacén). Existencia expresada en {uEntrega}{costoTxt}";
        }
        else
        {
            TxtResumenConversionProducto.Text = $"Relación 1 a 1: Compras por {uCompra} y la Existencia se controla en {uEntrega}.";
        }
    }

    private void ActualizarBannerModoEditorProducto(Producto? prodEditando)
    {
        if (BdrModoEditorProducto == null || TxtModoEditorProductoTitulo == null || TxtModoEditorProductoSubtitulo == null || BtnGuardarProductoCatalogo == null)
            return;

        if ((prodEditando == null || prodEditando.Id == 0) && !_modoAltaNuevoProducto)
        {
            BdrModoEditorProducto.Background = Tk("ControlFillColorSecondaryBrush");
            BdrModoEditorProducto.BorderBrush = Tk("ControlStrokeColorDefaultBrush");
            TxtModoEditorProductoTitulo.Foreground = Tk("TextFillColorPrimaryBrush");
            TxtModoEditorProductoSubtitulo.Foreground = Tk("TextFillColorSecondaryBrush");
            TxtModoEditorProductoTitulo.Text = "ℹ️ SIN PRODUCTO SELECCIONADO";
            TxtModoEditorProductoSubtitulo.Text = "Seleccione una tarjeta del catálogo a la izquierda para editarla, o presione '+ Nuevo Producto' para iniciar un alta nueva.";
            BtnGuardarProductoCatalogo.Content = "Crear Producto Nuevo en Catálogo";
        }
        else if (prodEditando == null || prodEditando.Id == 0)
        {
            string codigoNuevo = TxtProdCodigoInterno?.Text ?? "PRD-NUEVO";
            BdrModoEditorProducto.Background = Tk("SubtleFillColorSecondaryBrush");
            BdrModoEditorProducto.BorderBrush = Tk("AccentFillColorDefaultBrush");
            TxtModoEditorProductoTitulo.Foreground = Tk("AccentFillColorDefaultBrush");
            TxtModoEditorProductoSubtitulo.Foreground = Tk("AccentTextFillColorPrimaryBrush");
            TxtModoEditorProductoTitulo.Text = $"MODO: ALTA DE NUEVO PRODUCTO ({codigoNuevo})";
            TxtModoEditorProductoSubtitulo.Text = "1. Escriba el Nombre  •  2. Elija Categoría y Familia (o Autocompletar)  •  3. Presione 'Crear Producto Nuevo'";
            BtnGuardarProductoCatalogo.Content = "Crear Producto Nuevo en Catálogo";
        }
        else
        {
            BdrModoEditorProducto.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
            BdrModoEditorProducto.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            TxtModoEditorProductoTitulo.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            TxtModoEditorProductoSubtitulo.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
            TxtModoEditorProductoTitulo.Text = $"EDITANDO PRODUCTO EXISTENTE: {prodEditando.CodigoInterno}";
            TxtModoEditorProductoSubtitulo.Text = $"Modificando '{prodEditando.Nombre}'. Para registrar un artículo distinto presione '+ Nuevo Producto'.";
            BtnGuardarProductoCatalogo.Content = $"Guardar Cambios en {prodEditando.CodigoInterno}";
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
            DialogoFluent.Show(this,
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
            DialogoFluent.Show(this, "Seleccione un producto del catálogo para editarlo o escriba el nombre del producto nuevo.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        bool eraHerramientaAntes = _productoEnEdicion.Id > 0 &&
                                   DatabaseService.ObtenerHerramientas().Any(h => h.ProductoId == _productoEnEdicion.Id);

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
        _productoEnEdicion.UnidadCompra = DatabaseService.NormalizarUnidadMedida(TxtProdUnidadCompra.Text);
        _productoEnEdicion.UnidadAlmacen = DatabaseService.NormalizarUnidadMedida(TxtProdUnidadAlmacen.Text);
        _productoEnEdicion.FactorConversion = ParseDecimal(TxtProdFactor.Text, 1m);
        _productoEnEdicion.StockActual = ParseDecimal(TxtProdStock.Text, 0m);
        _productoEnEdicion.StockMinimo = ParseDecimal(TxtProdStockMin.Text, 5m);
        _productoEnEdicion.UltimoCostoCompra = ParseDecimal(TxtProdCosto.Text, 0m);
        string ubiLimpiaProd = ExtraerCodigoUbicacionLimpio(TxtProdUbicacion.Text);
        _productoEnEdicion.Ubicacion = string.IsNullOrWhiteSpace(ubiLimpiaProd) ? "A-01" : ubiLimpiaProd;
        _productoEnEdicion.EsHerramienta = ChkProdEsHerramienta.IsChecked == true;

        Producto? prodGuardado = null;
        await EjecutarConIndicadorCargaAsync("Guardando producto...", () =>
        {
            int idGuardado = DatabaseService.GuardarProducto(_productoEnEdicion);
            _productoEnEdicion.Id = idGuardado;
            DatabaseService.SincronizarHerramientasCatalogoConDirectorio();
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            prodGuardado = _cacheProductos.FirstOrDefault(x => x.Id == idGuardado) ?? _productoEnEdicion;
            CargarProductoEnEditor(prodGuardado);
        });

        if (prodGuardado != null && prodGuardado.EsHerramienta && !eraHerramientaAntes)
        {
            int cantSugerida = Math.Max(0, (int)Math.Round(prodGuardado.StockActual));
            MostrarModalRegistroHerramienta(prodGuardado, cantSugerida);
        }
    }

    private void ChkProdEsHerramienta_Click(object sender, RoutedEventArgs e)
    {
        if (ChkProdEsHerramienta.IsChecked != true)
            return;

        if (_productoEnEdicion.Id > 0)
        {
            _productoEnEdicion.EsHerramienta = true;
            DatabaseService.GuardarProducto(_productoEnEdicion);
            DatabaseService.SincronizarHerramientasCatalogoConDirectorio();
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            var actualizado = _cacheProductos.FirstOrDefault(x => x.Id == _productoEnEdicion.Id) ?? _productoEnEdicion;
            int cantSug = Math.Max(0, (int)Math.Round(actualizado.StockActual));
            MostrarModalRegistroHerramienta(actualizado, cantSug);
        }
        else if (!string.IsNullOrWhiteSpace(TxtProdNombre.Text))
        {
            BtnGuardarProducto_Click(sender, e);
        }
    }

    private void BtnConfigurarHerramientaCatalogo_Click(object sender, RoutedEventArgs e)
    {
        if (_productoEnEdicion.Id > 0)
        {
            if (!_productoEnEdicion.EsHerramienta)
            {
                ChkProdEsHerramienta.IsChecked = true;
                _productoEnEdicion.EsHerramienta = true;
                DatabaseService.GuardarProducto(_productoEnEdicion);
            }
            DatabaseService.SincronizarHerramientasCatalogoConDirectorio();
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            var actualizado = _cacheProductos.FirstOrDefault(x => x.Id == _productoEnEdicion.Id) ?? _productoEnEdicion;
            int cantSug = Math.Max(0, (int)Math.Round(actualizado.StockActual));
            MostrarModalRegistroHerramienta(actualizado, cantSug);
            return;
        }

        if (!string.IsNullOrWhiteSpace(TxtProdNombre.Text))
        {
            ChkProdEsHerramienta.IsChecked = true;
            BtnGuardarProducto_Click(sender, e);
            return;
        }

        MostrarModalRegistroHerramienta(null, 0);
    }

    private void MostrarModalRegistroHerramienta(Producto? prodPreseleccionado, int cantidadSugerida = 0)
    {
        var todosProductos = DatabaseService.ObtenerProductos()
            .OrderByDescending(p => p.EsHerramienta)
            .ThenByDescending(p => p.CategoriaId == 4)
            .ThenBy(p => p.Nombre)
            .ToList();

        if (todosProductos.Count == 0)
        {
            DialogoFluent.Show(this, "No hay productos en el catálogo.", "Catálogo vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Registro de Información de Herramienta en Almacén (Directorio HER-xxxx)",
            Width = 860,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Título
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Selector Producto
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tabla HER existentes
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Formulario edición/alta
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Botones inferiores

        // 1. Encabezado
        var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        headerStack.Children.Add(new TextBlock
        {
            Text = "Ficha de Herramienta para Resguardo y Préstamo en Almacén",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("TextFillColorPrimaryBrush")
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Capture o actualice el Código Individual (HER-xxxx), Número de Serie, Marca/Modelo, Ubicación en Gabinete y Estado. Esta información se sincroniza automáticamente con el Directorio de Herramientas y con el módulo de Préstamos.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetRow(headerStack, 0);
        root.Children.Add(headerStack);

        // 2. Selector de Producto del Catálogo
        var prodBorder = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var prodGrid = new Grid();
        prodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        prodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        prodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        prodGrid.Children.Add(new TextBlock
        {
            Text = "Producto de Catálogo:",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        var cboProd = new ComboBox
        {
            ItemsSource = todosProductos,
            IsEditable = false,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(cboProd, 1);
        prodGrid.Children.Add(cboProd);

        var lblResumenStock = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(3, 105, 161)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lblResumenStock, 2);
        prodGrid.Children.Add(lblResumenStock);

        prodBorder.Child = prodGrid;
        Grid.SetRow(prodBorder, 1);
        root.Children.Add(prodBorder);

        // 3. Tabla de unidades individuales HER-xxxx registradas para este producto
        var tableGroup = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var topTableBar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(topTableBar, Dock.Top);

        var btnLimpiarFormHer = new System.Windows.Controls.Button
        {
            Content = "+ Preparar Nueva Pieza (HER-xxxx)",
            Padding = new Thickness(10, 4, 10, 4),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(btnLimpiarFormHer, Dock.Right);
        topTableBar.Children.Add(btnLimpiarFormHer);

        var lblTablaTitulo = new TextBlock
        {
            Text = "Unidades físicas (HER-xxxx) registradas en el Directorio para este artículo (seleccione una fila para editarla):",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        topTableBar.Children.Add(lblTablaTitulo);
        tableGroup.Children.Add(topTableBar);

        var dgHer = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            Background = Tk("ControlFillColorDefaultBrush")
        };
        dgHer.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Código HER", Binding = new System.Windows.Data.Binding("CodigoIndividual"), Width = 100 });
        dgHer.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "N° de Serie", Binding = new System.Windows.Data.Binding("NumeroSerie"), Width = 130 });
        dgHer.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Marca / Modelo", Binding = new System.Windows.Data.Binding("MarcaModelo"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dgHer.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Ubicación Almacén", Binding = new System.Windows.Data.Binding("Ubicacion"), Width = 145 });
        dgHer.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Estado", Binding = new System.Windows.Data.Binding("Estado"), Width = 110 });
        dgHer.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Notas / Accesorios", Binding = new System.Windows.Data.Binding("NotasEstado"), Width = 160 });
        tableGroup.Children.Add(dgHer);

        Grid.SetRow(tableGroup, 2);
        root.Children.Add(tableGroup);

        // 4. Formulario de captura / edición de la herramienta
        var formBorder = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("AccentFillColorDefaultBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var formStack = new StackPanel();
        var lblModoForm = new TextBlock
        {
            Text = "Capturar Datos de la Herramienta",
            FontWeight = FontWeights.Bold,
            FontSize = 12.5,
            Foreground = Tk("AccentFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        formStack.Children.Add(lblModoForm);

        var row1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        var spCod = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spCod.Children.Add(new TextBlock { Text = "Código Individual (HER-xxxx)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtCodigo = new System.Windows.Controls.TextBox { FontFamily = new FontFamily("Consolas"), Padding = new Thickness(6, 4, 6, 4) };
        spCod.Children.Add(txtCodigo);
        Grid.SetColumn(spCod, 0);
        row1.Children.Add(spCod);

        var spSerie = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spSerie.Children.Add(new TextBlock { Text = "Número de Serie (o S/N)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtSerie = new System.Windows.Controls.TextBox { Padding = new Thickness(6, 4, 6, 4) };
        spSerie.Children.Add(txtSerie);
        Grid.SetColumn(spSerie, 1);
        row1.Children.Add(spSerie);

        var spMarca = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spMarca.Children.Add(new TextBlock { Text = "Marca y Modelo", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtMarca = new System.Windows.Controls.TextBox { Padding = new Thickness(6, 4, 6, 4) };
        spMarca.Children.Add(txtMarca);
        Grid.SetColumn(spMarca, 2);
        row1.Children.Add(spMarca);

        var spCant = new StackPanel();
        spCant.Children.Add(new TextBlock { Text = "Cant. Piezas", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtCantNuevas = new System.Windows.Controls.TextBox { Text = Math.Max(0, cantidadSugerida).ToString(), Padding = new Thickness(6, 4, 6, 4), ToolTip = "Cantidad de fichas HER individuales a generar según la existencia real en almacén (0 si aún no hay stock)" };
        spCant.Children.Add(txtCantNuevas);
        Grid.SetColumn(spCant, 3);
        row1.Children.Add(spCant);

        formStack.Children.Add(row1);

        var row2 = new Grid();
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });

        var spUbi = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spUbi.Children.Add(new TextBlock { Text = "Ubicación en Almacén (Anaquel / Zona)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var gridUbi = new Grid();
        gridUbi.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridUbi.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtUbi = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            ItemsSource = ObtenerCatalogoUbicacionesDesplegable(),
            Padding = new Thickness(6, 4, 6, 4)
        };
        txtUbi.SelectionChanged += CboUbicacionAlmacen_SelectionChanged;
        Grid.SetColumn(txtUbi, 0);
        gridUbi.Children.Add(txtUbi);
        var btnMapaUbi = new System.Windows.Controls.Button
        {
            Content = "Mapa",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Abrir selector gráfico 3x3 de anaquel y cuadrante (A–F, Detrás de A, Gabinete y Piso)"
        };
        btnMapaUbi.Click += (_, _) => MostrarSelectorYMapaAnaqueles(ExtraerCodigoUbicacionLimpio(txtUbi.Text), nuevaCoord => txtUbi.Text = nuevaCoord, win);
        Grid.SetColumn(btnMapaUbi, 1);
        gridUbi.Children.Add(btnMapaUbi);
        spUbi.Children.Add(gridUbi);
        Grid.SetColumn(spUbi, 0);
        row2.Children.Add(spUbi);

        var spEstado = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spEstado.Children.Add(new TextBlock { Text = "Estado Inicial / Actual", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var cboEstado = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        foreach (var est in new[] { "Disponible", "En Préstamo", "En Mantenimiento", "Dañada", "Extraviada / Baja" })
            cboEstado.Items.Add(est);
        cboEstado.SelectedIndex = 0;
        spEstado.Children.Add(cboEstado);
        Grid.SetColumn(spEstado, 1);
        row2.Children.Add(spEstado);

        var spNotas = new StackPanel();
        spNotas.Children.Add(new TextBlock { Text = "Notas de Resguardo / Accesorios Incluidos (Estuche, cargador, etc.)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtNotas = new System.Windows.Controls.TextBox { Padding = new Thickness(6, 4, 6, 4) };
        spNotas.Children.Add(txtNotas);
        Grid.SetColumn(spNotas, 2);
        row2.Children.Add(spNotas);

        formStack.Children.Add(row2);
        formBorder.Child = formStack;
        Grid.SetRow(formBorder, 3);
        root.Children.Add(formBorder);

        // 5. Barra inferior de botones
        var bottomBar = new DockPanel();
        var btnIrDirectorio = new System.Windows.Controls.Button
        {
            Content = "Ver en Directorio de Herramientas",
            Padding = new Thickness(12, 7, 12, 7),
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(btnIrDirectorio, Dock.Left);
        bottomBar.Children.Add(btnIrDirectorio);

        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = "Cerrar",
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        var btnGuardarHer = new System.Windows.Controls.Button
        {
            Content = "Guardar Información en Directorio de Herramientas",
            Padding = new Thickness(16, 7, 16, 7),
            FontWeight = FontWeights.Bold,
            Background = Tk("AccentFillColorDefaultBrush"),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand
        };
        rightBtns.Children.Add(btnCerrar);
        rightBtns.Children.Add(btnGuardarHer);
        bottomBar.Children.Add(rightBtns);

        Grid.SetRow(bottomBar, 4);
        root.Children.Add(bottomBar);

        int idHerEditandoModal = 0;

        void PrepararFormularioParaProducto(Producto? pSel, HerramientaActivo? herEditar = null, bool forzarNuevaPiezaManual = false)
        {
            if (pSel == null) return;

            if (herEditar != null)
            {
                idHerEditandoModal = herEditar.Id;
                lblModoForm.Text = $"Editando Ficha Existente: {herEditar.CodigoIndividual}";
                txtCodigo.Text = herEditar.CodigoIndividual;
                txtCodigo.IsEnabled = true;
                txtSerie.Text = herEditar.NumeroSerie;
                txtMarca.Text = herEditar.MarcaModelo;
                txtUbi.Text = herEditar.Ubicacion;
                cboEstado.SelectedItem = herEditar.Estado;
                if (cboEstado.SelectedIndex < 0) cboEstado.SelectedIndex = 0;
                txtNotas.Text = herEditar.NotasEstado;
                txtCantNuevas.Text = "1";
                txtCantNuevas.IsEnabled = false;
                btnGuardarHer.Content = $"Guardar Cambios en {herEditar.CodigoIndividual}";
            }
            else
            {
                idHerEditandoModal = 0;
                int activasActuales = DatabaseService.ObtenerHerramientas()
                    .Count(h => h.ProductoId == pSel.Id && !string.Equals(h.Estado, "Extraviada / Baja", StringComparison.OrdinalIgnoreCase));
                int stockRedondeado = Math.Max(0, (int)Math.Round(pSel.StockActual));
                int faltantesPorStock = Math.Max(0, stockRedondeado - activasActuales);
                int cantDefault = forzarNuevaPiezaManual ? (stockRedondeado > activasActuales ? 1 : 0) : faltantesPorStock;

                txtSerie.Text = !string.IsNullOrWhiteSpace(pSel.CodigoBarras) && !pSel.CodigoBarras.StartsWith("PRD-")
                    ? pSel.CodigoBarras
                    : "S/N";
                string marcaMod = $"{pSel.Marca} {pSel.Modelo}".Trim();
                txtMarca.Text = !string.IsNullOrWhiteSpace(marcaMod) ? marcaMod : pSel.Nombre;
                txtUbi.Text = !string.IsNullOrWhiteSpace(pSel.Ubicacion)
                    ? pSel.Ubicacion
                    : "A-04";
                cboEstado.SelectedIndex = 0;
                txtNotas.Text = pSel.Descripcion;
                txtCantNuevas.Text = cantDefault.ToString();
                txtCantNuevas.IsEnabled = cantDefault > 0;

                if (cantDefault > 0)
                {
                    string sigHer = DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual");
                    lblModoForm.Text = $"Registrar Nueva Ficha Individual ({sigHer})";
                    txtCodigo.Text = sigHer;
                    txtCodigo.IsEnabled = true;
                    btnGuardarHer.Content = "Guardar y Registrar en Directorio de Herramientas";
                }
                else
                {
                    lblModoForm.Text = "Configurar Herramienta en Catálogo (Existencia actual: 0 — Las fichas HER se crearán al recibir compra)";
                    txtCodigo.Text = "(Al recibir stock)";
                    txtCodigo.IsEnabled = false;
                    btnGuardarHer.Content = "Guardar Configuración de Herramienta (Sin crear existencia)";
                }
            }
        }

        void RefrescarTablaHerramientasProducto()
        {
            if (cboProd.SelectedItem is not Producto pSel) return;
            var prodFresco = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == pSel.Id) ?? pSel;
            pSel.StockActual = prodFresco.StockActual;
            var lista = DatabaseService.ObtenerHerramientas().Where(h => h.ProductoId == pSel.Id).ToList();
            dgHer.ItemsSource = lista;
            lblResumenStock.Text = $"Existencia: {pSel.StockActual:0.##} {pSel.UnidadAlmacen} | Fichas HER: {lista.Count}";

            if (lista.Count > 0)
            {
                var recienSincronizada = lista.LastOrDefault(h => string.Equals(h.NotasEstado, "Sincronizada desde Catálogo de Herramientas", StringComparison.OrdinalIgnoreCase))
                                         ?? lista[0];
                dgHer.SelectedItem = recienSincronizada;
                PrepararFormularioParaProducto(pSel, recienSincronizada);
            }
            else
            {
                PrepararFormularioParaProducto(pSel, null);
            }
        }

        cboProd.SelectionChanged += (_, _) =>
        {
            RefrescarTablaHerramientasProducto();
        };

        dgHer.SelectionChanged += (_, _) =>
        {
            if (cboProd.SelectedItem is Producto pSel && dgHer.SelectedItem is HerramientaActivo hSel)
            {
                PrepararFormularioParaProducto(pSel, hSel);
            }
        };

        btnLimpiarFormHer.Click += (_, _) =>
        {
            dgHer.SelectedItem = null;
            if (cboProd.SelectedItem is Producto pSel)
            {
                PrepararFormularioParaProducto(pSel, null, forzarNuevaPiezaManual: true);
                txtSerie.Focus();
                txtSerie.SelectAll();
            }
        };

        btnGuardarHer.Click += (_, _) =>
        {
            if (cboProd.SelectedItem is not Producto pSel)
            {
                DialogoFluent.Show(win, "Seleccione el producto del catálogo.", "Falta producto", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string serie = string.IsNullOrWhiteSpace(txtSerie.Text) ? "S/N" : txtSerie.Text.Trim();
            string marca = string.IsNullOrWhiteSpace(txtMarca.Text) ? pSel.Nombre : txtMarca.Text.Trim();
            string ubiLimpiaModal = ExtraerCodigoUbicacionLimpio(txtUbi.Text);
            string ubi = string.IsNullOrWhiteSpace(ubiLimpiaModal) ? "GABINETE-01" : ubiLimpiaModal;
            string est = cboEstado.SelectedItem?.ToString() ?? "Disponible";
            string notas = txtNotas.Text.Trim();

            if (idHerEditandoModal > 0)
            {
                string cod = string.IsNullOrWhiteSpace(txtCodigo.Text) || txtCodigo.Text.StartsWith("(")
                    ? DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual")
                    : txtCodigo.Text.Trim();

                DatabaseService.GuardarHerramientaActivo(new HerramientaActivo
                {
                    Id = idHerEditandoModal,
                    ProductoId = pSel.Id,
                    CodigoIndividual = cod,
                    NumeroSerie = serie,
                    MarcaModelo = marca,
                    Estado = est,
                    Ubicacion = ubi,
                    NotasEstado = notas
                });
            }
            else
            {
                int cant = Math.Max(0, (int)ParseDecimal(txtCantNuevas.Text, 0m));
                for (int i = 0; i < cant; i++)
                {
                    string cod = (i == 0 && !string.IsNullOrWhiteSpace(txtCodigo.Text) && !txtCodigo.Text.StartsWith("("))
                        ? txtCodigo.Text.Trim()
                        : DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual");

                    string serieItem = (cant > 1 && serie != "S/N") ? $"{serie}-{i + 1}" : serie;

                    DatabaseService.GuardarHerramientaActivo(new HerramientaActivo
                    {
                        Id = 0,
                        ProductoId = pSel.Id,
                        CodigoIndividual = cod,
                        NumeroSerie = serieItem,
                        MarcaModelo = marca,
                        Estado = est,
                        Ubicacion = ubi,
                        NotasEstado = notas
                    });
                }
            }

            // Sincronizar ubicación y bandera EsHerramienta en el producto del catálogo SIN alterar StockActual
            var prodActual = DatabaseService.ObtenerProductos().FirstOrDefault(x => x.Id == pSel.Id);
            if (prodActual != null)
            {
                prodActual.EsHerramienta = true;
                if (!string.IsNullOrWhiteSpace(ubi))
                    prodActual.Ubicacion = ubi;
                DatabaseService.GuardarProducto(prodActual);
            }

            CargarTodoElSistema(conservarPosicionCatalogo: true);
            RefrescarTablaHerramientasProducto();
            DialogoFluent.Show(win,
                "La información de la herramienta quedó guardada sin alterar la existencia de almacén.",
                "Herramienta Guardada", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        btnIrDirectorio.Click += (_, _) =>
        {
            win.Close();
            CambiarSeccion(8);
        };

        btnCerrar.Click += (_, _) => win.Close();

        var inicial = prodPreseleccionado != null
            ? todosProductos.FirstOrDefault(p => p.Id == prodPreseleccionado.Id)
            : todosProductos.FirstOrDefault(p => p.EsHerramienta) ?? todosProductos.FirstOrDefault();

        cboProd.SelectedItem = inicial;
        RefrescarTablaHerramientasProducto();

        win.Content = root;
        win.ShowDialog();

        CargarTodoElSistema(conservarPosicionCatalogo: true);
        if (_productoEnEdicion.Id > 0)
        {
            var recargado = _cacheProductos.FirstOrDefault(p => p.Id == _productoEnEdicion.Id);
            if (recargado != null)
                CargarProductoEnEditor(recargado);
        }
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
            DialogoFluent.Show(this, "Seleccione un producto guardado para eliminarlo.", "Producto no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await EliminarProductoConConfirmacionAsync(_productoEnEdicion);
    }

    private async Task EliminarProductoConConfirmacionAsync(Producto prod)
    {
        if (!SolicitarAutorizacionAdmin($"Eliminar producto [{prod.CodigoInterno}] {prod.Nombre}"))
            return;

        if (DialogoFluent.Show(this, $"¿Desea eliminar el producto '[{prod.CodigoInterno}] {prod.Nombre}' del catálogo?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
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
            DialogoFluent.Show(this, $"Error al eliminar el producto:\n{ex.Message}", "Error al eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        DgPartidasNuevaSolicitud.Items.Refresh();
        ActualizarIndicadorCarritoSuperior();
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
                    if (prodsDict.TryGetValue(part.ProductoId, out var prod))
                    {
                        AgregarProductoAValeSalidaPos(prod, part.CantidadSolicitada);
                    }
                }
                CambiarSeccion(5);
                return;
            }

            DialogoFluent.Show(this, "Primero agregue productos al carrito haciendo clic en 'Agregar'.", "Carrito vacío", MessageBoxButton.OK, MessageBoxImage.Information);
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
            )
            .OrderByDescending(p => CalcularPrioridadBusquedaProducto(p, q, qNorm))
            .ThenBy(p => p.CodigoInterno)
            .ToList();

            if (TxtContadorFiltroProductoSol != null)
                TxtContadorFiltroProductoSol.Text = filtrados.Count > 0
                    ? $"{filtrados.Count} coincidencias (o clic en '➕ Concepto Abierto')"
                    : "0 en catálogo (clic en '➕ Concepto Abierto' o Enter)";
        }

        CboSolProductoAgregar.ItemsSource = filtrados;
        if (filtrados.Count > 0)
        {
            CboSolProductoAgregar.SelectedItem = filtrados[0];
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
                OrdenCompraFolio = p.OrdenCompraFolio,
                IncluirEnCotizacion = p.IncluirEnCotizacion,
                DiasVigenciaCotizacion = p.DiasVigenciaCotizacion > 0 ? p.DiasVigenciaCotizacion : 15,
                FechaCotizacion = p.FechaCotizacion
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
            TxtTituloEditorSolicitud.Text = "Nueva Solicitud de Compra";
            TxtEstadoEditorSolicitud.Text = "EN CAPTURA";
            BdrEstadoEditorSolicitud.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 78, 216));
            BtnGuardarSolicitud.Content = "Guardar Solicitud de Compra";
            BtnGuardarSolicitud.IsEnabled = true;
            BtnLimpiarPartidasSolicitud.IsEnabled = true;
        }
        else if (sol.EsEditable)
        {
            TxtTituloEditorSolicitud.Text = $"Editando Solicitud: {sol.Folio}";
            TxtEstadoEditorSolicitud.Text = $"ABIERTA ({sol.TotalSkus} SKUs)";
            BdrEstadoEditorSolicitud.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            BtnGuardarSolicitud.Content = $"Guardar Cambios en {sol.Folio} e Ir a Comparativa";
            BtnGuardarSolicitud.IsEnabled = true;
            BtnLimpiarPartidasSolicitud.IsEnabled = true;
        }
        else
        {
            TxtTituloEditorSolicitud.Text = $"Solicitud Bloqueada: {sol.Folio}";
            TxtEstadoEditorSolicitud.Text = $"{sol.Estatus.ToUpperInvariant()} (SOLO LECTURA)";
            BdrEstadoEditorSolicitud.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            BtnGuardarSolicitud.Content = $"Bloqueada ({sol.Estatus}) — Use '+ Crear Anexo'";
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
        if (CboSolTipoSolicitud != null) CboSolTipoSolicitud.SelectedIndex = 0;
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

        if (CboSolProyecto != null)
        {
            var proy = sol.ProyectoId > 0 ? CboSolProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == sol.ProyectoId) : null;
            CboSolProyecto.SelectedItem = proy;
            if (proy == null) CboSolProyecto.SelectedIndex = -1;
        }

        if (CboSolTipoSolicitud != null)
        {
            CboSolTipoSolicitud.SelectedIndex = sol.EsSoloCotizacion ? 1 : 0;
        }

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
        // La apertura del detalle se hace explícitamente (clic en fila, Enter o menú "Abrir solicitud"),
        // para que cambios de selección automáticos no alternen la pantalla.
    }

    private void PersistirCambioSiSolicitudEnEdicion()
    {
        ActualizarIndicadorCarritoSuperior();
        if (_solicitudEditandoId <= 0) return;

        var solActual = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
        if (solActual == null || !solActual.EsEditable) return;

        int depId = (CboSolDepartamento.SelectedItem as Departamento)?.Id ?? solActual.DepartamentoId;
        int encId = (CboSolEncargado.SelectedItem as Encargado)?.Id ?? solActual.EncargadoId;
        int proyId = (CboSolProyecto.SelectedItem as Proyecto)?.Id ?? 0;
        string origen = string.IsNullOrWhiteSpace(solActual.Origen) ? "Catálogo" : solActual.Origen;
        string tipoSol = (CboSolTipoSolicitud?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? solActual.TipoSolicitud;

        DatabaseService.ActualizarSolicitudExistente(
            _solicitudEditandoId,
            depId,
            encId,
            proyId,
            origen,
            TxtSolNotas.Text,
            _partidasNuevaSolicitud.ToList(),
            tipoSol);

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
                DialogoFluent.Show(this, $"La solicitud {solActual.Folio} ya tiene Orden de Compra generada y no puede modificarse.\n\nHaga clic en '+ Nueva Solicitud' para crear una nueva solicitud.", "Solicitud cerrada", MessageBoxButton.OK, MessageBoxImage.Information);
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
        ActualizarIndicadorCarritoSuperior();
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
        ActualizarIndicadorCarritoSuperior();
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
            DialogoFluent.Show(this,
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
            DialogoFluent.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {partida.OrdenCompraFolio} y no puede modificarse.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {partida.OrdenCompraFolio} y no puede modificarse.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {partida.OrdenCompraFolio} y no puede quitarse de la solicitud.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, $"La solicitud {sol.Folio} se encuentra en etapa '{sol.Estatus}' y está bloqueada contra edición.\n\nUse el botón '+ Crear Anexo' arriba a la derecha para agregar artículos adicionales como un anexo vinculado ({sol.Folio}-A1).", "Solicitud bloqueada por etapa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void BtnNuevaSolicitudVacia_Click(object sender, RoutedEventArgs e)
    {
        IniciarNuevaSolicitudVacia();
        MostrarDetalleSolicitud(true);
    }

    /// <summary>Alterna entre la lista de solicitudes y el detalle de la solicitud abierta.</summary>
    private void MostrarDetalleSolicitud(bool detalle)
    {
        if (PnlSolicitudesLista == null || PnlSolicitudDetalle == null) return;
        PnlSolicitudesLista.Visibility = detalle ? Visibility.Collapsed : Visibility.Visible;
        PnlSolicitudDetalle.Visibility = detalle ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AbrirSolicitudDeLista(Solicitud? sol)
    {
        if (sol == null) return;
        CargarSolicitudEnEditor(sol);
        MostrarDetalleSolicitud(true);
    }

    private void DgHistorialSolicitudes_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Solo se abre al hacer clic sobre una fila (no en encabezados ni espacios vacíos)
        var origen = e.OriginalSource as DependencyObject;
        while (origen != null && origen is not DataGridRow && origen is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            origen = System.Windows.Media.VisualTreeHelper.GetParent(origen);

        if (origen is DataGridRow fila && fila.Item is Solicitud sol)
            AbrirSolicitudDeLista(sol);
    }

    private void DgHistorialSolicitudes_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DgHistorialSolicitudes.SelectedItem is Solicitud sol)
        {
            AbrirSolicitudDeLista(sol);
            e.Handled = true;
        }
    }

    private void BtnAbrirSolicitudSeleccionada_Click(object sender, RoutedEventArgs e) =>
        AbrirSolicitudDeLista(DgHistorialSolicitudes.SelectedItem as Solicitud);

    private void BtnVolverListaSolicitudes_Click(object sender, RoutedEventArgs e)
    {
        MostrarDetalleSolicitud(false);
        RefrescarSolicitudes();
    }

    private void BtnCrearCarritoEIrCatalogo_Click(object sender, RoutedEventArgs e)
    {
        if (CboSolDepartamento.SelectedItem is not Departamento dep ||
            CboSolEncargado.SelectedItem is not Encargado enc)
        {
            // Si aún no eligen Departamento/Encargado, llevarlos directo al Catálogo con [Nueva Solicitud en Captura]
            if (_solicitudEditandoId <= 0 && CboCarritoSolicitudDestino?.ItemsSource is IEnumerable<Solicitud> opciones)
            {
                _sincronizandoSolicitudes = true;
                try
                {
                    CboCarritoSolicitudDestino.SelectedItem = opciones.FirstOrDefault(s => s.Id == 0);
                }
                finally
                {
                    _sincronizandoSolicitudes = false;
                }
                ActualizarIndicadorCarritoSuperior();
            }
            CambiarSeccion(1);
            return;
        }

        int proyId = (CboSolProyecto.SelectedItem as Proyecto)?.Id ?? 0;
        string tipoSol = (CboSolTipoSolicitud?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Requerimiento de Compra";

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
                    proyId,
                    solExistente.Origen,
                    TxtSolNotas.Text,
                    _partidasNuevaSolicitud.ToList(),
                    tipoSol);
            }
            else
            {
                carritoActivo = DatabaseService.CrearSolicitud(dep.Id, enc.Id, proyId, "Catálogo", TxtSolNotas.Text, new List<SolicitudPartida>(), tipoSol);
            }
        }
        else
        {
            carritoActivo = DatabaseService.CrearSolicitud(dep.Id, enc.Id, proyId, "Catálogo", TxtSolNotas.Text, _partidasNuevaSolicitud.ToList(), tipoSol);
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
            DialogoFluent.Show(this, $"La solicitud {sol.Folio} ya tiene Orden de Compra generada y no admite más artículos.\n\nUse '+ Nueva Solicitud' para abrir una solicitud nueva.", "Solicitud cerrada", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, "Seleccione o guarde primero una Solicitud para abrir su vista previa de impresión.", "Solicitud no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BarcodePrintService.MostrarVistaPreviaEImprimirSolicitud(this, sol);
    }

    private void BtnDescargarSolicitudArchivo_Click(object sender, RoutedEventArgs e)
    {
        var sol = DgHistorialSolicitudes.SelectedItem as Solicitud
                  ?? DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);

        if (sol == null)
        {
            DialogoFluent.Show(this, "Seleccione o guarde primero una Solicitud para descargar su archivo (PDF, PNG o Excel CSV).", "Solicitud no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BarcodePrintService.MostrarVistaPreviaEImprimirSolicitud(this, sol, soloDescargar: true);
    }

    private void BtnEliminarSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var sol = DgHistorialSolicitudes.SelectedItem as Solicitud
                  ?? DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);

        if (sol == null)
        {
            DialogoFluent.Show(this, "Seleccione una solicitud de la lista para eliminarla.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!sol.EsEditable)
        {
            DialogoFluent.Show(this, $"No se puede eliminar la solicitud {sol.Folio} porque ya tiene una Orden de Compra generada.", "Operación no permitida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar solicitud {sol.Folio}"))
            return;

        var resp = DialogoFluent.Show(this,
            $"¿Está seguro de eliminar la solicitud {sol.Folio} ({sol.ProyectoNombre}) con {sol.TotalSkus} partidas?",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp != System.Windows.MessageBoxResult.Yes)
            return;

        DatabaseService.EliminarSolicitud(sol.Id);
        IniciarNuevaSolicitudVacia();
        MostrarDetalleSolicitud(false);
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
            DialogoFluent.Show(this, "No se encontraron coincidencias automáticas en el catálogo para ese texto. Puede buscarlos en la barra superior o darlos de alta en el Catálogo Visual.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExportarPlantillaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Guardar Plantilla Estándar de Solicitud de Material (Excel / Portal Web CSV)",
            FileName = "Plantilla_Estandar_Solicitud_Material.csv",
            Filter = "Archivo CSV para Excel (*.csv)|*.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FORMATO ESTANDAR DE SOLICITUD DE MATERIAL E INSUMOS (WEB / EXCEL),,,,,,");
            sb.AppendLine("Folio Web (Opcional),WEB-2026-0001,,,,,");
            sb.AppendLine("Departamento / Area,HVAC,,,,,");
            sb.AppendLine("Nombre Solicitante,Ing. Carlos Mendoza,,,,,");
            sb.AppendLine("Persona que Recibe,Lázaro Burgos,,,,,");
            sb.AppendLine("Proyecto / Obra Destino,HVAC-PAM,,,,,");
            sb.AppendLine($"Fecha Requerida,{DateTime.Today.AddDays(3):yyyy-MM-dd},,,,,");
            sb.AppendLine("Nivel de Urgencia,Normal,Opciones: Normal | Urgente,,,,");
            sb.AppendLine("Lugar de Entrega,Recoger en Almacén,Opciones: Recoger en Almacén | Entrega en Obra | Envío Foráneo,,,,");
            sb.AppendLine("Observaciones Generales,Material para instalación programada de la semana,,,,,");
            sb.AppendLine(",,,,,,");
            sb.AppendLine("DescripcionArticulo,EspecificacionMedida,MarcaModeloRef,Cantidad,UnidadMedida,PrecioRefSinIva,NotasUso");
            sb.AppendLine("\"Tubo de cobre rígido\",\"Tipo L de 1/2 pulg x 6.10 m\",\"IUSA o Nacobre\",4,Tramo,480.00,\"Línea de succión equipo principal\"");
            sb.AppendLine("\"Cable de uso rudo\",\"Calibre 12 AWG 3 hilos 600V\",\"Condumex o Indiana\",1,Rollo,2150.00,\"Alimentación condensadora\"");
            sb.AppendLine("\"Guante de nitrilo anticorte\",\"Talla Grande (L) Nivel 5\",\"Pretul o Truper\",6,Par,85.00,\"EPP para cuadrilla de montaje\"");

            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            DialogoFluent.Show(this,
                $"Plantilla estándar guardada en:\n{dlg.FileName}\n\n" +
                "• Incluye el Encabezado obligatorio (Folio Web, Departamento, Solicitante, Quién recibe, Proyecto, Fecha requerida, Urgencia y Lugar de entrega).\n" +
                "• Incluye las 7 columnas por artículo (Descripción, Especificación/Medida, Marca/Modelo Ref, Cantidad, Unidad, Precio Ref. sin IVA y Notas de uso).\n" +
                "• Al importarla aquí, todas las partidas entran como Conceptos Libres listos para cotizar o vincular al catálogo.",
                "Plantilla Estándar Generada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static List<string> DividirLineaCsvEstandar(string linea, char delimitador)
    {
        var resultado = new List<string>();
        if (linea == null) return resultado;

        var actual = new StringBuilder();
        bool enComillas = false;
        for (int i = 0; i < linea.Length; i++)
        {
            char c = linea[i];
            if (c == '"')
            {
                if (enComillas && i + 1 < linea.Length && linea[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else
                {
                    enComillas = !enComillas;
                }
            }
            else if (c == delimitador && !enComillas)
            {
                resultado.Add(actual.ToString().Trim());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }
        resultado.Add(actual.ToString().Trim());
        return resultado;
    }

    private void BtnImportarPlantillaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (!VerificarPermisoEdicionSolicitudActual()) return;

        var dlg = new OpenFileDialog
        {
            Title = "Importar Solicitud desde Archivo Estándar (Web / Excel CSV)",
            Filter = "Archivo CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        string[] lineas;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            string contenido = Encoding.UTF8.GetString(bytes);
            if (contenido.Contains('\uFFFD'))
            {
                contenido = Encoding.Latin1.GetString(bytes);
            }
            lineas = contenido.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se pudo leer el archivo:\n{ex.Message}", "Error de lectura", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Detectar delimitador (coma o punto y coma si Excel regional lo guardó con ';')
        char delim = ',';
        var lineaCabecera = lineas.FirstOrDefault(l =>
            l.Contains("DescripcionArticulo", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("CodigoProducto", StringComparison.OrdinalIgnoreCase));
        if (lineaCabecera != null && lineaCabecera.Count(c => c == ';') > lineaCabecera.Count(c => c == ','))
        {
            delim = ';';
        }
        else if (lineaCabecera == null && lineas.Take(10).Sum(l => l.Count(c => c == ';')) > lineas.Take(10).Sum(l => l.Count(c => c == ',')))
        {
            delim = ';';
        }

        string folioWeb = string.Empty;
        string deptoTxt = string.Empty;
        string solicitanteTxt = string.Empty;
        string recibeTxt = string.Empty;
        string proyectoTxt = string.Empty;
        string fechaReqTxt = string.Empty;
        string urgenciaTxt = string.Empty;
        string lugarEntregaTxt = string.Empty;
        string obsGeneralesTxt = string.Empty;

        bool enTablaPartidas = false;
        bool esFormatoLegacy3Cols = false;
        int partidasImportadas = 0;

        foreach (var rawLine in lineas)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var cols = DividirLineaCsvEstandar(rawLine, delim);
            if (cols.Count == 0 || cols.All(string.IsNullOrWhiteSpace)) continue;

            string c0 = cols[0].Trim();
            if (string.IsNullOrWhiteSpace(c0)) continue;

            // Detectar inicio de la tabla de partidas
            if (c0.Equals("DescripcionArticulo", StringComparison.OrdinalIgnoreCase) ||
                c0.Equals("Descripción", StringComparison.OrdinalIgnoreCase) ||
                c0.Equals("Descripcion", StringComparison.OrdinalIgnoreCase) ||
                c0.Equals("Articulo", StringComparison.OrdinalIgnoreCase) ||
                c0.Equals("Artículo", StringComparison.OrdinalIgnoreCase))
            {
                enTablaPartidas = true;
                esFormatoLegacy3Cols = false;
                continue;
            }
            if (c0.Equals("CodigoProducto", StringComparison.OrdinalIgnoreCase))
            {
                enTablaPartidas = true;
                esFormatoLegacy3Cols = true;
                continue;
            }

            if (!enTablaPartidas)
            {
                string val = cols.Count > 1 ? cols[1].Trim() : string.Empty;
                if (c0.StartsWith("FORMATO ESTANDAR", StringComparison.OrdinalIgnoreCase) ||
                    c0.StartsWith("SOLICITUD DE MATERIAL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (c0.Contains("Folio Web", StringComparison.OrdinalIgnoreCase) || c0.Equals("Folio", StringComparison.OrdinalIgnoreCase))
                    folioWeb = val;
                else if (c0.Contains("Departamento", StringComparison.OrdinalIgnoreCase) || c0.Contains("Area", StringComparison.OrdinalIgnoreCase) || c0.Contains("Área", StringComparison.OrdinalIgnoreCase))
                    deptoTxt = val;
                else if (c0.Contains("Solicitante", StringComparison.OrdinalIgnoreCase) || c0.Contains("Quien pide", StringComparison.OrdinalIgnoreCase))
                    solicitanteTxt = val;
                else if (c0.Contains("Recibe", StringComparison.OrdinalIgnoreCase))
                    recibeTxt = val;
                else if (c0.Contains("Proyecto", StringComparison.OrdinalIgnoreCase) || c0.Contains("Obra", StringComparison.OrdinalIgnoreCase))
                    proyectoTxt = val;
                else if (c0.Contains("Fecha Requerida", StringComparison.OrdinalIgnoreCase) || c0.Contains("Fecha Compromiso", StringComparison.OrdinalIgnoreCase))
                    fechaReqTxt = val;
                else if (c0.Contains("Urgencia", StringComparison.OrdinalIgnoreCase) || c0.Contains("Prioridad", StringComparison.OrdinalIgnoreCase))
                    urgenciaTxt = val;
                else if (c0.Contains("Lugar de Entrega", StringComparison.OrdinalIgnoreCase) || c0.Contains("Entrega", StringComparison.OrdinalIgnoreCase))
                    lugarEntregaTxt = val;
                else if (c0.Contains("Observaciones", StringComparison.OrdinalIgnoreCase) || c0.Contains("Notas", StringComparison.OrdinalIgnoreCase))
                    obsGeneralesTxt = val;
                continue;
            }

            // Procesar renglón de partida (entran todas como Concepto Libre según el flujo definido)
            if (esFormatoLegacy3Cols)
            {
                string descLegacy = c0;
                decimal cantLegacy = cols.Count > 1 ? ParseDecimal(cols[1], 1m) : 1m;
                string notaLegacy = cols.Count > 2 && !string.IsNullOrWhiteSpace(cols[2]) ? cols[2].Trim() : "Importado de plantilla";
                AgregarPartidaConceptoLibreEstructurada(descLegacy, string.Empty, string.Empty, cantLegacy, "Pieza", 0m, notaLegacy);
                partidasImportadas++;
            }
            else
            {
                string descArt = c0;
                string especMedida = cols.Count > 1 ? cols[1].Trim() : string.Empty;
                string marcaRef = cols.Count > 2 ? cols[2].Trim() : string.Empty;
                decimal cant = cols.Count > 3 ? ParseDecimal(cols[3], 1m) : 1m;
                string unidadRaw = cols.Count > 4 && !string.IsNullOrWhiteSpace(cols[4]) ? cols[4].Trim() : "Pieza";
                decimal precioRef = cols.Count > 5 ? ParseDecimal(cols[5].Replace("$", "").Trim(), 0m) : 0m;
                string notasUso = cols.Count > 6 ? cols[6].Trim() : string.Empty;

                if (cant <= 0m) cant = 1m;
                AgregarPartidaConceptoLibreEstructurada(descArt, especMedida, marcaRef, cant, unidadRaw, precioRef, notasUso);
                partidasImportadas++;
            }
        }

        // Aplicar metadatos del encabezado a los controles de la Solicitud
        AplicarEncabezadoSolicitudImportada(
            folioWeb,
            deptoTxt,
            solicitanteTxt,
            recibeTxt,
            proyectoTxt,
            fechaReqTxt,
            urgenciaTxt,
            lugarEntregaTxt,
            obsGeneralesTxt);

        DgPartidasNuevaSolicitud.Items.Refresh();
        PersistirCambioSiSolicitudEnEdicion();

        DialogoFluent.Show(this,
            $"Se importaron {partidasImportadas} partida(s) como Conceptos Libres listos para cotizar o vincular al catálogo.\n\n" +
            $"• Folio Web: {(string.IsNullOrWhiteSpace(folioWeb) ? "(Sin folio web)" : folioWeb)}\n" +
            $"• Departamento: {(string.IsNullOrWhiteSpace(deptoTxt) ? "(Seleccione en pantalla)" : deptoTxt)}\n" +
            $"• Solicitante: {(string.IsNullOrWhiteSpace(solicitanteTxt) ? "(Seleccione en pantalla)" : solicitanteTxt)}\n" +
            $"• Proyecto: {(string.IsNullOrWhiteSpace(proyectoTxt) ? "Consumo Interno / General" : proyectoTxt)}\n" +
            $"• Fecha Requerida: {(string.IsNullOrWhiteSpace(fechaReqTxt) ? "No especificada" : fechaReqTxt)} ({(string.IsNullOrWhiteSpace(urgenciaTxt) ? "Normal" : urgenciaTxt)})",
            "Solicitud Importada Correctamente", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AgregarPartidaConceptoLibreEstructurada(
        string descripcionBase,
        string especificacionMedida,
        string marcaModeloRef,
        decimal cantidad,
        string unidadRaw,
        decimal precioRefSinIva,
        string notasUso)
    {
        string desc = (descripcionBase ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(desc)) return;

        var partesDesc = new List<string> { desc };
        if (!string.IsNullOrWhiteSpace(especificacionMedida))
            partesDesc.Add(especificacionMedida.Trim());
        if (!string.IsNullOrWhiteSpace(marcaModeloRef))
            partesDesc.Add($"(Ref: {marcaModeloRef.Trim()})");

        string descCompleta = string.Join(" - ", partesDesc).ToUpperInvariant();
        string unidadNorm = DatabaseService.NormalizarUnidadMedida(unidadRaw);

        var partesNota = new List<string>();
        if (!string.IsNullOrWhiteSpace(notasUso))
            partesNota.Add(notasUso.Trim());
        if (precioRefSinIva > 0m)
            partesNota.Add($"Precio Ref. S/IVA: ${precioRefSinIva:N2}");

        string notaFinal = partesNota.Count > 0 ? string.Join(" | ", partesNota) : "Importado de Solicitud Web/Excel";

        var existente = _partidasNuevaSolicitud.FirstOrDefault(x =>
            x.ProductoId <= 0 &&
            string.Equals(x.DescripcionLibre, descCompleta, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.UnidadCompra, unidadNorm, StringComparison.OrdinalIgnoreCase));

        if (existente != null)
        {
            existente.CantidadSolicitada += cantidad;
            if (precioRefSinIva > 0m && existente.PrecioSinIva1 <= 0m)
            {
                existente.PrecioSinIva1 = precioRefSinIva;
                existente.NotasCotizacion1 = "Precio ref. solicitante";
            }
        }
        else
        {
            string famSug = DatabaseService.ExtraerFamiliaSugerida(desc);
            _partidasNuevaSolicitud.Add(new SolicitudPartida
            {
                ProductoId = 0,
                ProductoCodigo = "CONCEPTO",
                ProductoNombre = descCompleta,
                DescripcionLibre = descCompleta,
                Familia = famSug,
                UnidadCompra = unidadNorm,
                CantidadSolicitada = cantidad,
                StockActualAlmacen = 0m,
                NotasPartida = notaFinal,
                PrecioSinIva1 = precioRefSinIva > 0m ? precioRefSinIva : 0m,
                NotasCotizacion1 = precioRefSinIva > 0m ? "Precio ref. solicitante" : string.Empty
            });
        }
    }

    private void AplicarEncabezadoSolicitudImportada(
        string folioWeb,
        string deptoTxt,
        string solicitanteTxt,
        string recibeTxt,
        string proyectoTxt,
        string fechaReqTxt,
        string urgenciaTxt,
        string lugarEntregaTxt,
        string obsGeneralesTxt)
    {
        // 1. Departamento
        Departamento? deptoSel = null;
        if (!string.IsNullOrWhiteSpace(deptoTxt))
        {
            var deptos = DatabaseService.ObtenerDepartamentos();
            deptoSel = deptos.FirstOrDefault(d =>
                d.Nombre.Equals(deptoTxt, StringComparison.OrdinalIgnoreCase) ||
                d.Codigo.Equals(deptoTxt, StringComparison.OrdinalIgnoreCase) ||
                d.Nombre.Contains(deptoTxt, StringComparison.OrdinalIgnoreCase) ||
                deptoTxt.Contains(d.Nombre, StringComparison.OrdinalIgnoreCase));

            if (deptoSel == null)
            {
                string codNuevo = deptoTxt.Length >= 3
                    ? new string(deptoTxt.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant()
                    : "DEP-WEB";
                DatabaseService.GuardarDepartamento(codNuevo, deptoTxt.Trim());
                CargarCombosBase();
                deptoSel = DatabaseService.ObtenerDepartamentos().FirstOrDefault(d => d.Nombre.Equals(deptoTxt.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (deptoSel != null && CboSolDepartamento != null)
            {
                CboSolDepartamento.SelectedItem = CboSolDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Id == deptoSel.Id);
            }
        }
        else
        {
            deptoSel = CboSolDepartamento?.SelectedItem as Departamento;
        }

        // 2. Solicitante / Encargado
        string nombreEncargadoBuscar = !string.IsNullOrWhiteSpace(solicitanteTxt) ? solicitanteTxt : recibeTxt;
        if (deptoSel != null && !string.IsNullOrWhiteSpace(nombreEncargadoBuscar))
        {
            var encs = DatabaseService.ObtenerEncargados(deptoSel.Id);
            var encSel = encs.FirstOrDefault(e =>
                e.Nombre.Equals(nombreEncargadoBuscar, StringComparison.OrdinalIgnoreCase) ||
                e.Nombre.Contains(nombreEncargadoBuscar, StringComparison.OrdinalIgnoreCase) ||
                nombreEncargadoBuscar.Contains(e.Nombre, StringComparison.OrdinalIgnoreCase));

            if (encSel == null)
            {
                DatabaseService.GuardarEncargado(deptoSel.Id, nombreEncargadoBuscar.Trim(), "Solicitante Web/Excel", "");
                encs = DatabaseService.ObtenerEncargados(deptoSel.Id);
                encSel = encs.FirstOrDefault(e => e.Nombre.Equals(nombreEncargadoBuscar.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (CboSolEncargado != null)
            {
                CboSolEncargado.ItemsSource = encs;
                if (encSel != null)
                    CboSolEncargado.SelectedItem = encs.FirstOrDefault(x => x.Id == encSel.Id);
            }
        }

        // 3. Proyecto / Obra Destino
        if (!string.IsNullOrWhiteSpace(proyectoTxt) && CboSolProyecto != null)
        {
            if (proyectoTxt.Contains("Interno", StringComparison.OrdinalIgnoreCase) ||
                proyectoTxt.Contains("Sin proyecto", StringComparison.OrdinalIgnoreCase))
            {
                CboSolProyecto.SelectedIndex = -1;
            }
            else
            {
                var proys = CboSolProyecto.Items.Cast<Proyecto>().ToList();
                var proySel = proys.FirstOrDefault(p =>
                    p.Codigo.Equals(proyectoTxt, StringComparison.OrdinalIgnoreCase) ||
                    p.Nombre.Equals(proyectoTxt, StringComparison.OrdinalIgnoreCase) ||
                    p.Codigo.Contains(proyectoTxt, StringComparison.OrdinalIgnoreCase) ||
                    p.Nombre.Contains(proyectoTxt, StringComparison.OrdinalIgnoreCase) ||
                    proyectoTxt.Contains(p.Codigo, StringComparison.OrdinalIgnoreCase));
                if (proySel != null)
                {
                    CboSolProyecto.SelectedItem = proySel;
                }
            }
        }

        // 4. Construir resumen estructurado en Notas de la Solicitud
        var metadatosNotas = new List<string>();
        if (!string.IsNullOrWhiteSpace(folioWeb))
            metadatosNotas.Add($"[Folio Web: {folioWeb}]");
        if (!string.IsNullOrWhiteSpace(fechaReqTxt))
            metadatosNotas.Add($"[Fecha Requerida: {fechaReqTxt}]");
        if (!string.IsNullOrWhiteSpace(urgenciaTxt))
            metadatosNotas.Add($"[Urgencia: {urgenciaTxt}]");
        if (!string.IsNullOrWhiteSpace(lugarEntregaTxt))
            metadatosNotas.Add($"[Entrega: {lugarEntregaTxt}]");
        if (!string.IsNullOrWhiteSpace(recibeTxt) && !string.Equals(recibeTxt, solicitanteTxt, StringComparison.OrdinalIgnoreCase))
            metadatosNotas.Add($"[Recibe: {recibeTxt}]");
        if (!string.IsNullOrWhiteSpace(proyectoTxt) && CboSolProyecto?.SelectedItem == null && !proyectoTxt.Contains("Interno", StringComparison.OrdinalIgnoreCase))
            metadatosNotas.Add($"[Obra indicada: {proyectoTxt}]");
        if (!string.IsNullOrWhiteSpace(obsGeneralesTxt))
            metadatosNotas.Add(obsGeneralesTxt);

        if (metadatosNotas.Count > 0 && TxtSolNotas != null)
        {
            TxtSolNotas.Text = string.Join(" | ", metadatosNotas);
        }
    }

    private void BtnLimpiarPartidasSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (!VerificarPermisoEdicionSolicitudActual()) return;
        if (_partidasNuevaSolicitud.Count == 0) return;

        if (_solicitudEditandoId > 0)
        {
            var resp = DialogoFluent.Show(this,
                "¿Desea vaciar todos los artículos de esta solicitud de compra?",
                "Vaciar solicitud",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (resp != System.Windows.MessageBoxResult.Yes) return;
        }

        _partidasNuevaSolicitud.Clear();
        DgPartidasNuevaSolicitud.Items.Refresh();
        PersistirCambioSiSolicitudEnEdicion();
    }

    private void BtnExportarExistenciasCatalogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar Reporte de Existencias de Almacén (Excel CSV)",
            FileName = $"Existencias_Almacen_{DateTime.Now:yyyyMMdd}.csv",
            Filter = "Archivo CSV para Excel (*.csv)|*.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CodigoInterno,CodigoBarras,Familia,Categoria,Articulo,Marca,Modelo,UnidadAlmacen,StockActual,StockMinimo,Ubicacion");
            foreach (var p in DatabaseService.ObtenerProductos().OrderBy(x => x.CategoriaNombre).ThenBy(x => x.Nombre))
            {
                string nom = p.Nombre.Replace(",", " ");
                string cat = p.CategoriaNombre.Replace(",", " ");
                string fam = p.Familia.Replace(",", " ");
                sb.AppendLine($"{p.CodigoInterno},{p.CodigoBarras},{fam},{cat},{nom},{p.Marca},{p.Modelo},{p.UnidadAlmacen},{p.StockActual:0.##},{p.StockMinimo:0.##},{p.Ubicacion}");
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            DialogoFluent.Show(this,
                $"Reporte de existencias exportado en:\n{dlg.FileName}\n\nPuede compartirlo con las Gerencias de Área para consulta de stock disponible.",
                "Existencias Exportadas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnGenerarSalidaStockSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (_partidasNuevaSolicitud.Count == 0)
        {
            DialogoFluent.Show(this, "Agregue artículos a la solicitud para verificar cuáles cuentan con existencia disponible en almacén.", "Sin partidas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var conStock = new List<(Producto prod, decimal cantSurtiendo, SolicitudPartida part)>();

        foreach (var part in _partidasNuevaSolicitud)
        {
            if (part.ProductoId > 0 && prodsDict.TryGetValue(part.ProductoId, out var prod) && prod.StockActual > 0)
            {
                decimal cantSurtir = Math.Min(part.CantidadSolicitada, prod.StockActual);
                if (cantSurtir > 0)
                    conStock.Add((prod, cantSurtir, part));
            }
        }

        if (conStock.Count == 0)
        {
            DialogoFluent.Show(this, "Ninguno de los artículos de esta solicitud tiene existencia disponible actualmente en Almacén.", "Sin stock disponible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _partidasSalidaPos.Clear();
        foreach (var item in conStock)
        {
            AgregarProductoAValeSalidaPos(item.prod, item.cantSurtiendo);
        }

        if (CboSolDepartamento.SelectedItem is Departamento dep && CboSalDepartamento != null)
        {
            CboSalDepartamento.SelectedItem = CboSalDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Id == dep.Id);
        }
        if (CboSolEncargado.SelectedItem is Encargado enc && CboSalEncargado != null)
        {
            CboSalEncargado.Text = enc.Nombre;
        }
        if (CboSolProyecto.SelectedItem is Proyecto proy && CboSalProyecto != null)
        {
            CboSalProyecto.SelectedItem = CboSalProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == proy.Id);
        }

        var preguntarDesmarcar = DialogoFluent.Show(this,
            $"Se cargaron {conStock.Count} artículo(s) con existencia disponible en la pestaña '5. Almacén -> Salida de Material'.\n\n¿Desea desmarcar la casilla 'Cotizar' en los artículos que ya quedan cubiertos al 100% con el stock de almacén? (Si elige 'No', permanecerán marcados por si desea cotizarlos de todas formas para actualizar precios).",
            "Artículos Cargados en Salida de Almacén",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (preguntarDesmarcar == System.Windows.MessageBoxResult.Yes)
        {
            foreach (var item in conStock)
            {
                if (item.cantSurtiendo >= item.part.CantidadSolicitada)
                    item.part.IncluirEnCotizacion = false;
            }
            DgPartidasNuevaSolicitud.Items.Refresh();
            PersistirCambioSiSolicitudEnEdicion();
        }

        CambiarSeccion(5);
    }

    private void BtnGuardarSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (CboSolDepartamento.SelectedItem is not Departamento dep ||
            CboSolEncargado.SelectedItem is not Encargado enc)
        {
            DialogoFluent.Show(this, "Seleccione Departamento y Solicitante (el Proyecto Destino es opcional).", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int proyId = (CboSolProyecto.SelectedItem as Proyecto)?.Id ?? 0;
        string tipoSol = (CboSolTipoSolicitud?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Requerimiento de Compra";
        string origen = _partidasNuevaSolicitud.Any(p => (p.NotasPartida ?? "").StartsWith("WhatsApp")) ? "WhatsApp" : "Catálogo";
        Solicitud guardada;

        if (_solicitudEditandoId > 0)
        {
            if (!VerificarPermisoEdicionSolicitudActual()) return;
            guardada = DatabaseService.ActualizarSolicitudExistente(
                _solicitudEditandoId,
                dep.Id,
                enc.Id,
                proyId,
                origen,
                TxtSolNotas.Text,
                _partidasNuevaSolicitud.ToList(),
                tipoSol);
        }
        else
        {
            guardada = DatabaseService.CrearSolicitud(
                dep.Id,
                enc.Id,
                proyId,
                origen,
                TxtSolNotas.Text,
                _partidasNuevaSolicitud.ToList(),
                tipoSol);
            _solicitudEditandoId = guardada.Id;
        }

        RefrescarSolicitudes(guardada.Id);
        RefrescarDashboard();

        if (guardada.Partidas.Count == 0)
        {
            var irCat = DialogoFluent.Show(this,
                $"Se creó el carrito de solicitud abierto {guardada.Folio} ({guardada.ProyectoMostrar}).\n\n¿Desea ir ahora al Catálogo Visual para escoger y agregar artículos a este carrito?",
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
            DialogoFluent.Show(this, "Seleccione una solicitud de la lista.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (sol.Partidas.Count == 0)
        {
            DialogoFluent.Show(this, $"El carrito {sol.Folio} aún no tiene artículos agregados.\n\nAgregue artículos desde el buscador o desde el Catálogo Visual antes de abrir la Comparativa.", "Carrito sin artículos", MessageBoxButton.OK, MessageBoxImage.Information);
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

        TxtCompInfoSolicitud.Text = $"Solicitud: {sol.Folio} | Tipo: {sol.TipoSolicitud} | Depto: {sol.DepartamentoNombre} | Solicitante: {sol.EncargadoNombre} | Destino: {sol.ProyectoMostrar} | Estatus: {sol.Estatus}";
        if (BtnConvertirPresupuestoEnCompra != null)
        {
            BtnConvertirPresupuestoEnCompra.Content = sol.EsSoloCotizacion
                ? "Finalizar Cotización / Convertir a Compra"
                : "Convertir a Solo Cotización";
        }
        _renglonesComparativa.Clear();

        var productos = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var proveedores = DatabaseService.ObtenerProveedores();
        var provDict = proveedores.ToDictionary(p => p.Id);

        var partidasParaCotizar = sol.Partidas.Any(p => p.IncluirEnCotizacion || p.TieneOcGenerada)
            ? sol.Partidas.Where(p => p.IncluirEnCotizacion || p.TieneOcGenerada).ToList()
            : sol.Partidas;

        foreach (var part in partidasParaCotizar)
        {
            productos.TryGetValue(part.ProductoId, out var pObj);
            decimal ultimoCostoConIva = pObj?.UltimoCostoCompra ?? 0m;
            string familiaEfectiva = !string.IsNullOrWhiteSpace(part.Familia)
                ? part.Familia
                : (!string.IsNullOrWhiteSpace(pObj?.Familia) ? pObj.Familia : DatabaseService.ExtraerFamiliaSugerida(part.ProductoNombre));

            var (sugerencias, refHist) = DatabaseService.ObtenerSugerenciasCotizacionPorProducto(part.ProductoId, part.ProductoNombre, familiaEfectiva);
            var provsExtra = part.Id > 0 ? DatabaseService.ObtenerProveedoresExtraPartida(part.Id) : new List<CotizacionProveedorExtra>();

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

            bool sinProveedoresGuardados = pv1 == 0 && pv2 == 0 && pv3 == 0 && pr1 <= 0 && pr2 <= 0 && pr3 <= 0 && provsExtra.Count == 0;
            if (sinProveedoresGuardados)
            {
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
            if (pv3 > 0 || pr3 > 0 || part.ProveedorGanadorIndice >= 3 || provsExtra.Count > 0)
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
                DiasVigenciaCotizacion = part.DiasVigenciaCotizacion > 0 ? part.DiasVigenciaCotizacion : 15,
                FechaCotizacion = part.FechaCotizacion,
                ProveedoresExtra = provsExtra,

                Proveedor1Id = pv1,
                Proveedor1Nombre = provDict.TryGetValue(pv1, out var p1Obj) ? p1Obj.NombreMostrar : "Sin asignar",
                PrecioSinIva1 = pr1,
                DiasEntrega1 = d1,
                FleteConIva1 = part.FleteConIva1,
                NotasCotizacion1 = nc1,
                DiasVigencia1 = part.DiasVigencia1 > 0 ? part.DiasVigencia1 : (part.DiasVigenciaCotizacion > 0 ? part.DiasVigenciaCotizacion : 15),
                FechaCotizacion1 = !string.IsNullOrWhiteSpace(part.FechaCotizacion1) ? part.FechaCotizacion1 : part.FechaCotizacion,

                Proveedor2Id = pv2,
                Proveedor2Nombre = provDict.TryGetValue(pv2, out var p2Obj) ? p2Obj.NombreMostrar : "Sin asignar",
                PrecioSinIva2 = pr2,
                DiasEntrega2 = d2,
                FleteConIva2 = part.FleteConIva2,
                NotasCotizacion2 = nc2,
                DiasVigencia2 = part.DiasVigencia2 > 0 ? part.DiasVigencia2 : (part.DiasVigenciaCotizacion > 0 ? part.DiasVigenciaCotizacion : 15),
                FechaCotizacion2 = !string.IsNullOrWhiteSpace(part.FechaCotizacion2) ? part.FechaCotizacion2 : part.FechaCotizacion,

                Proveedor3Id = pv3,
                Proveedor3Nombre = provDict.TryGetValue(pv3, out var p3Obj) ? p3Obj.NombreMostrar : "Sin asignar",
                PrecioSinIva3 = pr3,
                DiasEntrega3 = d3,
                FleteConIva3 = part.FleteConIva3,
                NotasCotizacion3 = nc3,
                DiasVigencia3 = part.DiasVigencia3 > 0 ? part.DiasVigencia3 : (part.DiasVigenciaCotizacion > 0 ? part.DiasVigenciaCotizacion : 15),
                FechaCotizacion3 = !string.IsNullOrWhiteSpace(part.FechaCotizacion3) ? part.FechaCotizacion3 : part.FechaCotizacion,

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
                if (UgTarjetasComparativa != null) UgTarjetasComparativa.Columns = 1;
                if (BdrCompCard2 != null) BdrCompCard2.Visibility = Visibility.Collapsed;
                if (BdrCompCard3 != null) BdrCompCard3.Visibility = Visibility.Collapsed;
                if (BdrProveedoresExtraComp != null) BdrProveedoresExtraComp.Visibility = Visibility.Collapsed;
                if (BtnAgregarOpcionProveedorComp != null)
                {
                    BtnAgregarOpcionProveedorComp.Visibility = Visibility.Visible;
                    BtnAgregarOpcionProveedorComp.IsEnabled = true;
                    BtnAgregarOpcionProveedorComp.Content = "+ Añadir Proveedor Concursante";
                }
                if (CboCompReferenciasSimilares != null)
                {
                    CboCompReferenciasSimilares.ItemsSource = null;
                    CboCompReferenciasSimilares.SelectedIndex = -1;
                    EstablecerPlaceholderCombo(CboCompReferenciasSimilares, "Seleccione un artículo para consultar compras similares...");
                }
                if (CboCompProv1 != null) CboCompProv1.SelectedIndex = -1;
                if (BtnVincularLineaAbiertaComp != null) BtnVincularLineaAbiertaComp.Visibility = Visibility.Collapsed;
                if (TxtTituloCardComp1 != null) TxtTituloCardComp1.Text = "Proveedor 1 (Principal / Histórico)";
                if (TxtTituloCardComp2 != null) TxtTituloCardComp2.Text = "Proveedor 2";
                if (TxtTituloCardComp3 != null) TxtTituloCardComp3.Text = "Proveedor 3";
                if (CboCompProv2 != null) CboCompProv2.SelectedIndex = -1;
                if (CboCompProv3 != null) CboCompProv3.SelectedIndex = -1;
                return;
            }

            string famBadge = !string.IsNullOrWhiteSpace(r.Familia) ? $" | Familia: {r.Familia}" : string.Empty;
            TxtCompProdTitulo.Text = $"{r.ProductoCodigo} — {r.ProductoNombre}";
            TxtCompProdSubtitulo.Text = $"Unidad: {r.UnidadCompra}{famBadge} | Stock actual: {r.StockActualAlmacen:0.##} | Últ. Costo: {(r.UltimoCostoCompraConIva > 0 ? $"{r.UltimoCostoCompraConIva:C2} c/IVA" : "N/D")} | Vigencia: {r.VigenciaEstadoTexto}";
            TxtCompProdHistorial.Text = r.ReferenciaHistorica;
            TxtCompProdCantidad.Text = r.Cantidad.ToString("0.##", CultureInfo.InvariantCulture);
            if (BtnVincularLineaAbiertaComp != null)
                BtnVincularLineaAbiertaComp.Visibility = r.ProductoId <= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompDiasVigencia != null)
                TxtCompDiasVigencia.Text = (r.DiasVigenciaCotizacion > 0 ? r.DiasVigenciaCotizacion : 15).ToString();

            if (CboCompReferenciasSimilares != null)
            {
                var refsSimilares = DatabaseService.ObtenerReferenciasSimilaresParaComparativa(r.ProductoId, r.ProductoNombre, r.Familia);
                CboCompReferenciasSimilares.ItemsSource = refsSimilares;
                CboCompReferenciasSimilares.SelectedIndex = -1;
                EstablecerPlaceholderCombo(
                    CboCompReferenciasSimilares,
                    refsSimilares.Count > 0
                        ? $"{refsSimilares.Count} compra(s) / cotizaciones previas encontradas — despliegue para elegir..."
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
            if (TxtCompVigencia1 != null) TxtCompVigencia1.Text = (r.DiasVigencia1 > 0 ? r.DiasVigencia1 : 15).ToString();
            TxtCompNotas1.Text = r.NotasCotizacion1;

            TxtCompPrecioSinIva2.Text = r.PrecioSinIva2 > 0 ? r.PrecioSinIva2.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompPrecioConIva2.Text = r.PrecioSinIva2 > 0 ? r.PrecioConIva2.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompFlete2.Text = r.FleteConIva2.ToString("0.00", CultureInfo.InvariantCulture);
            TxtCompDias2.Text = r.DiasEntrega2.ToString();
            if (TxtCompVigencia2 != null) TxtCompVigencia2.Text = (r.DiasVigencia2 > 0 ? r.DiasVigencia2 : 15).ToString();
            TxtCompNotas2.Text = r.NotasCotizacion2;

            TxtCompPrecioSinIva3.Text = r.PrecioSinIva3 > 0 ? r.PrecioSinIva3.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompPrecioConIva3.Text = r.PrecioSinIva3 > 0 ? r.PrecioConIva3.ToString("0.00", CultureInfo.InvariantCulture) : "0.00";
            TxtCompFlete3.Text = r.FleteConIva3.ToString("0.00", CultureInfo.InvariantCulture);
            TxtCompDias3.Text = r.DiasEntrega3.ToString();
            if (TxtCompVigencia3 != null) TxtCompVigencia3.Text = (r.DiasVigencia3 > 0 ? r.DiasVigencia3 : 15).ToString();
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
            if (TxtCompVigencia1 != null) TxtCompVigencia1.IsEnabled = editable;
            if (TxtCompVigencia2 != null) TxtCompVigencia2.IsEnabled = editable;
            if (TxtCompVigencia3 != null) TxtCompVigencia3.IsEnabled = editable;
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
        if (UgTarjetasComparativa != null)
            UgTarjetasComparativa.Columns = n;
        BdrCompCard2.Visibility = n >= 2 ? Visibility.Visible : Visibility.Collapsed;
        BdrCompCard3.Visibility = n >= 3 ? Visibility.Visible : Visibility.Collapsed;

        if (BdrProveedoresExtraComp != null && DgProveedoresExtraComp != null)
        {
            DgProveedoresExtraComp.ItemsSource = null;
            DgProveedoresExtraComp.ItemsSource = r.ProveedoresExtra;
            BdrProveedoresExtraComp.Visibility = r.ProveedoresExtra.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        bool editable = !r.TieneOcGenerada;
        BtnAgregarOpcionProveedorComp.Visibility = Visibility.Visible;
        BtnAgregarOpcionProveedorComp.IsEnabled = editable;
        int sigOp = n < 3 ? n + 1 : 4 + r.ProveedoresExtra.Count;
        BtnAgregarOpcionProveedorComp.Content = $"+ Añadir Opción {sigOp} de Proveedor";
    }

    private void BtnAgregarOpcionProveedorComp_Click(object sender, RoutedEventArgs e)
    {
        var r = DgComparativa.SelectedItem as CotizacionRenglonComparativo;
        if (r == null && _renglonesComparativa.Count > 0)
        {
            DgComparativa.SelectedIndex = 0;
            r = DgComparativa.SelectedItem as CotizacionRenglonComparativo;
        }

        if (r == null)
        {
            DialogoFluent.Show(this, "Seleccione primero una Solicitud arriba y un artículo de la lista izquierda para añadir proveedores concursantes.", "Seleccione un artículo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (r.TieneOcGenerada)
        {
            DialogoFluent.Show(this, $"Este artículo ya tiene emitida la Orden de Compra {r.OrdenCompraFolio}.", "Artículo cerrado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (r.NumeroOpcionesVisibles >= 3)
        {
            MostrarModalAgregarProveedorExtraComp(r);
            return;
        }

        r.NumeroOpcionesVisibles++;

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

    private void MostrarModalAgregarProveedorExtraComp(CotizacionRenglonComparativo r)
    {
        if (r.SolicitudPartidaId <= 0)
        {
            DialogoFluent.Show(this, "Guarde primero la solicitud para habilitar proveedores adicionales (Opción 4+).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int numOpcion = 4 + r.ProveedoresExtra.Count;
        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = $"Añadir Opción {numOpcion} de Proveedor — {r.ProductoNombre}",
            Width = 460,
            Height = 365,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = $"Opción {numOpcion} de Proveedor Concursante",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        root.Children.Add(new TextBlock { Text = "Proveedor:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboProv = new ComboBox
        {
            ItemsSource = DatabaseService.ObtenerProveedores(),
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(cboProv);

        var gridPrecios = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        gridPrecios.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridPrecios.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridPrecios.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var spPrecio = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spPrecio.Children.Add(new TextBlock { Text = "Precio Unit. s/IVA ($):", FontSize = 11 });
        var txtPrecio = new System.Windows.Controls.TextBox { Text = "0.00", Padding = new Thickness(6, 4, 6, 4) };
        spPrecio.Children.Add(txtPrecio);
        Grid.SetColumn(spPrecio, 0);
        gridPrecios.Children.Add(spPrecio);

        var spFlete = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spFlete.Children.Add(new TextBlock { Text = "Flete / Envío c/IVA ($):", FontSize = 11 });
        var txtFlete = new System.Windows.Controls.TextBox { Text = "0.00", Padding = new Thickness(6, 4, 6, 4) };
        spFlete.Children.Add(txtFlete);
        Grid.SetColumn(spFlete, 1);
        gridPrecios.Children.Add(spFlete);

        var spDias = new StackPanel();
        spDias.Children.Add(new TextBlock { Text = "Entrega (días):", FontSize = 11 });
        var txtDias = new System.Windows.Controls.TextBox { Text = "2", Padding = new Thickness(6, 4, 6, 4) };
        spDias.Children.Add(txtDias);
        Grid.SetColumn(spDias, 2);
        gridPrecios.Children.Add(spDias);
        root.Children.Add(gridPrecios);

        root.Children.Add(new TextBlock { Text = "Notas / Marca cotizada:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtNotas = new System.Windows.Controls.TextBox { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(txtNotas);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnOk = new System.Windows.Controls.Button { Content = "Agregar Proveedor a Comparativa", Padding = new Thickness(14, 6, 14, 6), FontWeight = FontWeights.Bold };

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click += (_, _) =>
        {
            if (cboProv.SelectedItem is not Proveedor provSel)
            {
                DialogoFluent.Show(win, "Seleccione un proveedor de la lista.", "Proveedor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            decimal precio = ParseDecimal(txtPrecio.Text, 0m);
            if (precio <= 0)
            {
                DialogoFluent.Show(win, "Capture un precio unitario sin IVA mayor a 0.", "Precio requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var extra = new CotizacionProveedorExtra
            {
                SolicitudPartidaId = r.SolicitudPartidaId,
                NumeroOpcion = numOpcion,
                ProveedorId = provSel.Id,
                ProveedorNombre = provSel.NombreMostrar,
                PrecioSinIva = precio,
                FleteConIva = ParseDecimal(txtFlete.Text, 0m),
                DiasEntrega = Math.Max(1, (int)ParseDecimal(txtDias.Text, 2m)),
                NotasCotizacion = txtNotas.Text.Trim(),
                DiasVigencia = r.DiasVigenciaCotizacion > 0 ? r.DiasVigenciaCotizacion : 15,
                FechaCotizacion = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            DatabaseService.GuardarProveedorExtraPartida(extra);
            r.ProveedoresExtra = DatabaseService.ObtenerProveedoresExtraPartida(r.SolicitudPartidaId);
            DatabaseService.GuardarCotizacionPartida(r);
            win.DialogResult = true;
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);
        win.Content = root;

        if (win.ShowDialog() == true)
        {
            CargarProductoEnTarjetasComparativa(r);
            RecalcularTotalesComparativa();
        }
    }

    private void BtnElegirProveedorExtraGanador_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CotizacionProveedorExtra extra } &&
            DgComparativa.SelectedItem is CotizacionRenglonComparativo r && !r.TieneOcGenerada)
        {
            ElegirOpcionGanadoraProductoActual(extra.NumeroOpcion);
        }
    }

    private void BtnEliminarProveedorExtra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CotizacionProveedorExtra extra } &&
            DgComparativa.SelectedItem is CotizacionRenglonComparativo r && !r.TieneOcGenerada)
        {
            DatabaseService.EliminarProveedorExtraPartida(extra.Id);
            r.ProveedoresExtra = DatabaseService.ObtenerProveedoresExtraPartida(r.SolicitudPartidaId);
            if (r.ProveedorGanadorIndice == extra.NumeroOpcion)
                r.ProveedorGanadorIndice = 0;
            DatabaseService.GuardarCotizacionPartida(r);
            CargarProductoEnTarjetasComparativa(r);
            RecalcularTotalesComparativa();
        }
    }

    private void BtnAplicarReferenciaSimilarOp1_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;
        if (CboCompReferenciasSimilares?.SelectedItem is not ReferenciaSimilarHistorica refSim)
        {
            DialogoFluent.Show(this, "No hay referencias históricas de compras similares para este artículo.", "Sin referencias similares", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var provDict = (CboCompProv1.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores()).ToDictionary(p => p.Id);
        r.Proveedor1Id = refSim.ProveedorId;
        r.Proveedor1Nombre = provDict.TryGetValue(refSim.ProveedorId, out var p1) ? p1.NombreMostrar : refSim.ProveedorNombre;
        r.PrecioSinIva1 = refSim.PrecioSinIva;
        r.DiasEntrega1 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 2;
        r.NotasCotizacion1 = refSim.ReferenciaOrigen;
        r.DiasVigencia1 = refSim.DiasVigencia > 0 ? refSim.DiasVigencia : 15;
        r.FechaCotizacion1 = !string.IsNullOrWhiteSpace(refSim.FechaCotizacion) ? refSim.FechaCotizacion : DateTime.Today.ToString("yyyy-MM-dd");

        DatabaseService.GuardarCotizacionPartida(r);
        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
        ActualizarEncabezadosColumnasTablaComparativa();
        DgComparativa.Items.Refresh();
    }

    private void BtnAplicarReferenciaSimilarNuevaOp_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa.SelectedItem is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
            return;
        if (CboCompReferenciasSimilares?.SelectedItem is not ReferenciaSimilarHistorica refSim)
        {
            DialogoFluent.Show(this, "No hay referencias históricas de compras similares para este artículo.", "Sin referencias similares", MessageBoxButton.OK, MessageBoxImage.Information);
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
            r.DiasVigencia1 = refSim.DiasVigencia > 0 ? refSim.DiasVigencia : 15;
            r.FechaCotizacion1 = !string.IsNullOrWhiteSpace(refSim.FechaCotizacion) ? refSim.FechaCotizacion : DateTime.Today.ToString("yyyy-MM-dd");
        r.DiasVigencia1 = refSim.DiasVigencia > 0 ? refSim.DiasVigencia : 15;
        r.FechaCotizacion1 = !string.IsNullOrWhiteSpace(refSim.FechaCotizacion) ? refSim.FechaCotizacion : DateTime.Today.ToString("yyyy-MM-dd");
        }
        else if (r.NumeroOpcionesVisibles < 2 || (r.Proveedor2Id == 0 && r.PrecioSinIva2 <= 0))
        {
            if (r.NumeroOpcionesVisibles < 2) r.NumeroOpcionesVisibles = 2;
            r.Proveedor2Id = refSim.ProveedorId;
            r.Proveedor2Nombre = provNombre;
            r.PrecioSinIva2 = refSim.PrecioSinIva;
            r.DiasEntrega2 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 3;
            r.NotasCotizacion2 = refSim.ReferenciaOrigen;
            r.DiasVigencia2 = refSim.DiasVigencia > 0 ? refSim.DiasVigencia : 15;
            r.FechaCotizacion2 = !string.IsNullOrWhiteSpace(refSim.FechaCotizacion) ? refSim.FechaCotizacion : DateTime.Today.ToString("yyyy-MM-dd");
        }
        else
        {
            r.NumeroOpcionesVisibles = 3;
            r.Proveedor3Id = refSim.ProveedorId;
            r.Proveedor3Nombre = provNombre;
            r.PrecioSinIva3 = refSim.PrecioSinIva;
            r.DiasEntrega3 = refSim.DiasEntrega > 0 ? refSim.DiasEntrega : 1;
            r.NotasCotizacion3 = refSim.ReferenciaOrigen;
            r.DiasVigencia3 = refSim.DiasVigencia > 0 ? refSim.DiasVigencia : 15;
            r.FechaCotizacion3 = !string.IsNullOrWhiteSpace(refSim.FechaCotizacion) ? refSim.FechaCotizacion : DateTime.Today.ToString("yyyy-MM-dd");
        }

        DatabaseService.GuardarCotizacionPartida(r);
        CargarProductoEnTarjetasComparativa(r);
        RecalcularTotalesComparativa();
        ActualizarEncabezadosColumnasTablaComparativa();
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

        string nom1 = r.Proveedor1Id > 0 && !string.IsNullOrWhiteSpace(r.Proveedor1Nombre) && r.Proveedor1Nombre != "Sin asignar"
            ? Proveedor.ExtraerNombreComercialCorto(r.Proveedor1Nombre)
            : "Proveedor 1";
        string nom2 = r.Proveedor2Id > 0 && !string.IsNullOrWhiteSpace(r.Proveedor2Nombre) && r.Proveedor2Nombre != "Sin asignar"
            ? Proveedor.ExtraerNombreComercialCorto(r.Proveedor2Nombre)
            : "Proveedor 2";
        string nom3 = r.Proveedor3Id > 0 && !string.IsNullOrWhiteSpace(r.Proveedor3Nombre) && r.Proveedor3Nombre != "Sin asignar"
            ? Proveedor.ExtraerNombreComercialCorto(r.Proveedor3Nombre)
            : "Proveedor 3";

        if (TxtTituloCardComp1 != null)
            TxtTituloCardComp1.Text = r.Proveedor1Id > 0 ? $"🏢 {nom1}" : "Proveedor 1 (Sin asignar)";
        if (TxtTituloCardComp2 != null)
            TxtTituloCardComp2.Text = r.Proveedor2Id > 0 ? $"🏢 {nom2}" : "Proveedor 2 (Sin asignar)";
        if (TxtTituloCardComp3 != null)
            TxtTituloCardComp3.Text = r.Proveedor3Id > 0 ? $"🏢 {nom3}" : "Proveedor 3 (Sin asignar)";

        BtnElegirGanador1.Content = r.ProveedorGanadorIndice == 1 ? $"★ {nom1} (Elegido)" : $"✓ Elegir {nom1}";
        BtnElegirGanador2.Content = r.ProveedorGanadorIndice == 2 ? $"★ {nom2} (Elegido)" : $"✓ Elegir {nom2}";
        BtnElegirGanador3.Content = r.ProveedorGanadorIndice == 3 ? $"★ {nom3} (Elegido)" : $"✓ Elegir {nom3}";
        if (TxtEstadoVigenciaCard1 != null)
            TxtEstadoVigenciaCard1.Text = r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0 ? r.EstadoVigencia1 : "Sin cotizar";
        if (TxtEstadoVigenciaCard2 != null)
            TxtEstadoVigenciaCard2.Text = r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0 ? r.EstadoVigencia2 : "Sin cotizar";
        if (TxtEstadoVigenciaCard3 != null)
            TxtEstadoVigenciaCard3.Text = r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0 ? r.EstadoVigencia3 : "Sin cotizar";
        if (TxtCompProdSubtitulo != null)
        {
            string famBadge = !string.IsNullOrWhiteSpace(r.Familia) ? $" | Familia: {r.Familia}" : string.Empty;
            TxtCompProdSubtitulo.Text = $"Unidad: {r.UnidadCompra}{famBadge} | Stock actual: {r.StockActualAlmacen:0.##} | Últ. Costo: {(r.UltimoCostoCompraConIva > 0 ? $"{r.UltimoCostoCompraConIva:C2} c/IVA" : "N/D")} | Vigencia: {r.VigenciaEstadoTexto}";
        }
        ActualizarEncabezadosColumnasTablaComparativa();
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
        if (TxtCompVigencia1 != null) r.DiasVigencia1 = Math.Max(1, (int)ParseDecimal(TxtCompVigencia1.Text, 15m));
        if (TxtCompVigencia2 != null) r.DiasVigencia2 = Math.Max(1, (int)ParseDecimal(TxtCompVigencia2.Text, 15m));
        if (TxtCompVigencia3 != null) r.DiasVigencia3 = Math.Max(1, (int)ParseDecimal(TxtCompVigencia3.Text, 15m));
        string hoyStr = DateTime.Today.ToString("yyyy-MM-dd");
        if (r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0 && string.IsNullOrWhiteSpace(r.FechaCotizacion1)) r.FechaCotizacion1 = hoyStr;
        if (r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0 && string.IsNullOrWhiteSpace(r.FechaCotizacion2)) r.FechaCotizacion2 = hoyStr;
        if (r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0 && string.IsNullOrWhiteSpace(r.FechaCotizacion3)) r.FechaCotizacion3 = hoyStr;

        r.NotasCotizacion1 = TxtCompNotas1.Text ?? string.Empty;
        r.NotasCotizacion2 = r.NumeroOpcionesVisibles >= 2 ? (TxtCompNotas2.Text ?? string.Empty) : string.Empty;
        r.NotasCotizacion3 = r.NumeroOpcionesVisibles >= 3 ? (TxtCompNotas3.Text ?? string.Empty) : string.Empty;

        if (TxtCompDiasVigencia != null)
            r.DiasVigenciaCotizacion = Math.Max(1, (int)ParseDecimal(TxtCompDiasVigencia.Text, 15m));
        if ((r.PrecioSinIva1 > 0 || r.PrecioSinIva2 > 0 || r.PrecioSinIva3 > 0 || r.ProveedoresExtra.Any(p => p.PrecioSinIva > 0)) && string.IsNullOrWhiteSpace(r.FechaCotizacion))
            r.FechaCotizacion = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

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
            DialogoFluent.Show(this, $"Este artículo ya fue emitido en la Orden de Compra {r.OrdenCompraFolio}.", "Artículo con OC emitida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SincronizarRenglonActualDesdeTarjetas();

        if (indiceOpcion >= 1 && indiceOpcion <= 3)
        {
            int provId = indiceOpcion switch { 1 => r.Proveedor1Id, 2 => r.Proveedor2Id, 3 => r.Proveedor3Id, _ => 0 };
            decimal precio = indiceOpcion switch { 1 => r.PrecioSinIva1, 2 => r.PrecioSinIva2, 3 => r.PrecioSinIva3, _ => 0m };

            if (provId <= 0)
            {
                DialogoFluent.Show(this, $"Seleccione primero un Proveedor en la Opción {indiceOpcion}.", "Proveedor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (precio <= 0)
            {
                DialogoFluent.Show(this, $"Capture primero un precio unitario mayor a $0.00 en la Opción {indiceOpcion}.", "Precio requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Capture al menos un proveedor con precio mayor a $0.00 en las opciones de este artículo.", "Sin precios capturados", MessageBoxButton.OK, MessageBoxImage.Information);
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
        foreach (var ex in r.ProveedoresExtra)
        {
            if (ex.ProveedorId > 0 && ex.PrecioSinIva > 0)
            {
                decimal totEx = Math.Round(r.Cantidad * ex.PrecioConIva + ex.FleteConIva, 2);
                candidatos.Add((ex.NumeroOpcion, totEx));
            }
        }

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

        ActualizarEncabezadosColumnasTablaComparativa();
        DgComparativa.Items.Refresh();

        var decididosSinOc = _renglonesComparativa
            .Where(r => !r.TieneOcGenerada && r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
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
            DialogoFluent.Show(this, "Capture precios mayores a $0.00 en los productos pendientes para elegir automáticamente el mejor precio.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnGuardarBorradorComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol)
            return;

        SincronizarRenglonActualDesdeTarjetas();
        string estAntSol = sol.Estatus;
        DatabaseService.GuardarBorradorComparativa(sol.Id, _renglonesComparativa.ToList(), sol.Folio);
        if (string.Equals(estAntSol, "Pendiente", StringComparison.OrdinalIgnoreCase))
            RegistrarTransicionCheckpoint("Etapa 2: Comparativa", "CP-1B", sol.Folio, estAntSol, "En Comparativa", "Solicitante, Compras", $"Compras inició la cotización de {sol.Folio} ({sol.ProyectoMostrar}). La solicitud original queda bloqueada contra edición (use '+ Crear Anexo' si requiere más partidas).");
        RecalcularTotalesComparativa();
        DialogoFluent.Show(this, $"Se guardó el borrador de la comparativa para la solicitud {sol.Folio}.\n\nPuede continuar cotizando más tarde o emitir Órdenes de Compra parciales cuando lo desee.", "Borrador Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnCopiarListaParaCotizar_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione primero una solicitud con artículos para copiar la lista de cotización.", "Sin solicitud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"SOLICITUD DE COTIZACIÓN — Folio: {sol.Folio}");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine("Favor de cotizar los siguientes materiales indicando precio unitario, tiempo de entrega y vigencia:");
        sb.AppendLine("------------------------------------------------------------");
        int idx = 1;
        foreach (var r in _renglonesComparativa)
        {
            sb.AppendLine($"{idx}. ({r.Cantidad:0.##} {r.UnidadCompra}) {r.ProductoNombre}");
            idx++;
        }
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine("Atentamente: Gerencia de Compras");

        Clipboard.SetText(sb.ToString());
        DialogoFluent.Show(this,
            "Se copió al portapapeles la lista limpia de materiales (sin precios internos) lista para pegar y enviar por WhatsApp o correo a los proveedores concursantes.",
            "Lista Copiada para Proveedores", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnImprimirComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione primero una solicitud con artículos para imprimir su Cuadro Comparativo.", "Sin solicitud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SincronizarRenglonActualDesdeTarjetas();
        string estAntSol = sol.Estatus;
        DatabaseService.GuardarBorradorComparativa(sol.Id, _renglonesComparativa.ToList(), sol.Folio);
        if (string.Equals(estAntSol, "Pendiente", StringComparison.OrdinalIgnoreCase))
            RegistrarTransicionCheckpoint("Etapa 2: Comparativa", "CP-1B", sol.Folio, estAntSol, "En Comparativa", "Solicitante, Compras", $"Compras inició la cotización de {sol.Folio} ({sol.ProyectoMostrar}). La solicitud original queda bloqueada contra edición (use '+ Crear Anexo' si requiere más partidas).");
        BarcodePrintService.MostrarVistaPreviaEImprimirComparativa(this, sol, _renglonesComparativa.ToList());
    }

    private void BtnDescargarComparativaArchivo_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione primero una solicitud con artículos para descargar su Cuadro Comparativo (PDF, PNG o Excel CSV).", "Sin solicitud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SincronizarRenglonActualDesdeTarjetas();
        string estAntSol = sol.Estatus;
        DatabaseService.GuardarBorradorComparativa(sol.Id, _renglonesComparativa.ToList(), sol.Folio);
        if (string.Equals(estAntSol, "Pendiente", StringComparison.OrdinalIgnoreCase))
            RegistrarTransicionCheckpoint("Etapa 2: Comparativa", "CP-1B", sol.Folio, estAntSol, "En Comparativa", "Solicitante, Compras", $"Compras inició la cotización de {sol.Folio} ({sol.ProyectoMostrar}). La solicitud original queda bloqueada contra edición (use '+ Crear Anexo' si requiere más partidas).");
        BarcodePrintService.MostrarVistaPreviaEImprimirComparativa(this, sol, _renglonesComparativa.ToList(), soloDescargar: true);
    }

    private void BtnConvertirPresupuestoEnCompra_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol)
            return;

        SincronizarRenglonActualDesdeTarjetas();
        string estAntSol = sol.Estatus;
        DatabaseService.GuardarBorradorComparativa(sol.Id, _renglonesComparativa.ToList(), sol.Folio);
        if (string.Equals(estAntSol, "Pendiente", StringComparison.OrdinalIgnoreCase))
            RegistrarTransicionCheckpoint("Etapa 2: Comparativa", "CP-1B", sol.Folio, estAntSol, "En Comparativa", "Solicitante, Compras", $"Compras inició la cotización de {sol.Folio} ({sol.ProyectoMostrar}). La solicitud original queda bloqueada contra edición (use '+ Crear Anexo' si requiere más partidas).");

        if (sol.EsSoloCotizacion)
        {
            var resp = DialogoFluent.Show(this,
                $"La solicitud {sol.Folio} está registrada como 'Solo Cotización / Presupuesto'.\n\n• Si presiona 'Sí': Se convertirá en 'Requerimiento de Compra' para que pueda emitir Órdenes de Compra.\n• Si presiona 'No': Se marcará como 'Cotizada (Presupuesto)' conservando todos los precios en el Banco Histórico sin generar compra.",
                "Finalizar Cotización o Convertir a Compra",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (resp == System.Windows.MessageBoxResult.Yes)
            {
                DatabaseService.ActualizarTipoYEstatusSolicitud(sol.Id, "Requerimiento de Compra", "En Comparativa");
                RefrescarSolicitudes(sol.Id);
                DialogoFluent.Show(this, $"La solicitud {sol.Folio} se convirtió a 'Requerimiento de Compra'. Ya puede emitir las Órdenes de Compra correspondientes.", "Convertida a Compra", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (resp == System.Windows.MessageBoxResult.No)
            {
                DatabaseService.ActualizarTipoYEstatusSolicitud(sol.Id, "Solo Cotización / Presupuesto", "Cotizada (Presupuesto)");
                RefrescarSolicitudes(sol.Id);
                DialogoFluent.Show(this, $"La solicitud {sol.Folio} quedó registrada como 'Cotizada (Presupuesto)' y sus precios se guardaron en el Banco Histórico con su vigencia.", "Presupuesto Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        else
        {
            var resp = DialogoFluent.Show(this,
                $"¿Desea cambiar la solicitud {sol.Folio} a modalidad 'Solo Cotización / Presupuesto' (investigación de precios sin emisión de compra)?",
                "Cambiar Tipo de Solicitud",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp == System.Windows.MessageBoxResult.Yes)
            {
                DatabaseService.ActualizarTipoYEstatusSolicitud(sol.Id, "Solo Cotización / Presupuesto", "Cotizada (Presupuesto)");
                RefrescarSolicitudes(sol.Id);
            }
        }
    }

    private void BtnGenerarOrdenesDesdeComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
            return;

        SincronizarRenglonActualDesdeTarjetas();

        if (sol.EsSoloCotizacion)
        {
            var confirmarConversion = DialogoFluent.Show(this,
                $"Esta solicitud ({sol.Folio}) fue creada como 'Solo Cotización / Presupuesto'.\n\n¿Desea convertirla en 'Requerimiento de Compra' y emitir las Órdenes de Compra ahora?",
                "Solicitud de Solo Cotización",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmarConversion != System.Windows.MessageBoxResult.Yes)
                return;

            DatabaseService.ActualizarTipoYEstatusSolicitud(sol.Id, "Requerimiento de Compra", "En Comparativa");
        }

        var decididosSinOc = _renglonesComparativa
            .Where(r => !r.TieneOcGenerada && r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .ToList();

        if (decididosSinOc.Count == 0)
        {
            DialogoFluent.Show(this, "Elija primero el proveedor ganador (con precio mayor a $0.00) en al menos un producto pendiente.", "Sin productos decididos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int pendientesRestantes = _renglonesComparativa.Count(r => !r.TieneOcGenerada) - decididosSinOc.Count;
        int numOcs = decididosSinOc.Select(r => r.ProveedorGanadorId).Distinct().Count();

        if (pendientesRestantes > 0)
        {
            var resp = DialogoFluent.Show(this,
                $"Se emitirán {numOcs} Orden(es) de Compra para los {decididosSinOc.Count} productos ya decididos.\n\nLos {pendientesRestantes} productos restantes quedarán pendientes en esta misma solicitud ({sol.Folio}) para que pueda cotizarlos y emitir su OC después.\n\n¿Desea emitir las Órdenes de Compra ahora?",
                "Emisión Parcial de Órdenes de Compra",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resp != System.Windows.MessageBoxResult.Yes)
                return;
        }

        string folioComp = $"COT-{DateTime.Now:yyyy}-{sol.Id:D4}";
        var ordenesCreadas = DatabaseService.GenerarOrdenesDesdeComparativa(sol, folioComp, _renglonesComparativa.ToList());
        foreach (var ocGen in ordenesCreadas)
            RegistrarTransicionCheckpoint("Etapa 3: Emisión OC", "CP-2B", ocGen.Folio, "En Comparativa", ocGen.Estatus, "Dirección, Solicitante, Compras", $"Compras adjudicó proveedor ({ocGen.ProveedorNombre}) y emitió {ocGen.Folio} desde {sol.Folio}. Pendiente Vo.Bo. Dirección.");

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

            if (CboOcEstatusOperativo != null)
            {
                foreach (ComboBoxItem item in CboOcEstatusOperativo.Items)
                {
                    if (string.Equals(item.Content?.ToString(), oc.Estatus, StringComparison.OrdinalIgnoreCase))
                    {
                        CboOcEstatusOperativo.SelectedItem = item;
                        break;
                    }
                }
            }

            if (CboOcCondicionPago != null)
            {
                bool encontrado = false;
                foreach (ComboBoxItem item in CboOcCondicionPago.Items)
                {
                    if (string.Equals(item.Content?.ToString(), oc.CondicionPago, StringComparison.OrdinalIgnoreCase))
                    {
                        CboOcCondicionPago.SelectedItem = item;
                        encontrado = true;
                        break;
                    }
                }
                if (!encontrado) CboOcCondicionPago.SelectedIndex = 0;
            }

            if (DpOcFechaProgramadaPago != null)
            {
                if (!string.IsNullOrWhiteSpace(oc.FechaProgramadaPago) && DateTime.TryParse(oc.FechaProgramadaPago, out var fProg))
                    DpOcFechaProgramadaPago.SelectedDate = fProg;
                else
                    DpOcFechaProgramadaPago.SelectedDate = null;
            }

            RefrescarHistorialEntradasOc();
        }
    }

    private void BtnActualizarFlujoOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra de la tabla.", "OC no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string nuevoEstatus = (CboOcEstatusOperativo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? oc.Estatus;
        string condicionPago = (CboOcCondicionPago?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? oc.CondicionPago;
        string fechaProg = DpOcFechaProgramadaPago?.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty;

        string estatusPrevOc = oc.Estatus;
        DatabaseService.ActualizarEstatusYCondicionOrdenCompra(oc.Id, nuevoEstatus, condicionPago, fechaProg);
        RegistrarTransicionCheckpoint("Etapa 3: Orden de Compra", nuevoEstatus.Contains("Lista para Almacén") || nuevoEstatus == "Emitida" ? "CP-4" : "CP-3", oc.Folio, estatusPrevOc, nuevoEstatus, "Almacén, Finanzas", $"Cambio de estatus operativo de {oc.Folio} ({oc.ProveedorNombre}) a '{nuevoEstatus}' ({condicionPago}).");
        CargarTodoElSistema();
        DialogoFluent.Show(this,
            $"Se actualizó el flujo de la Orden de Compra {oc.Folio}:\n• Estatus: {nuevoEstatus}\n• Condición: {condicionPago}\n• Fecha programada/vencimiento: {(string.IsNullOrWhiteSpace(fechaProg) ? "No especificada" : fechaProg)}\n\nLos recordatorios de seguimiento se actualizaron en el Calendario del Dashboard.",
            "Flujo de OC Actualizado", MessageBoxButton.OK, MessageBoxImage.Information);
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
        else
        {
            DialogoFluent.Show(this, "Seleccione una Orden de Compra de la lista para imprimirla.", "Sin OC seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnDescargarOcSeleccionada_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra oc)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, oc, soloDescargar: true);
        }
        else
        {
            DialogoFluent.Show(this, "Seleccione una Orden de Compra de la lista para descargar su archivo (PDF, PNG o Excel CSV).", "Sin OC seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnRegistrarRecepcionOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
            return;

        if (oc.Estatus == "Cancelada")
        {
            DialogoFluent.Show(this, "Esta Orden de Compra se encuentra Cancelada y no admite recepciones.", "OC Cancelada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var aRecibir = oc.Partidas
            .Where(p => p.CantidadRecibirAhora > 0)
            .Select(p => (p, p.CantidadRecibirAhora))
            .ToList();

        if (aRecibir.Count == 0)
        {
            DialogoFluent.Show(this, "Indique en la columna 'Recibir Hoy' al menos una cantidad mayor a 0 (puede recibir entregas parciales o usar el botón 'Recibir Todo lo Pendiente').", "Sin cantidades por recibir", MessageBoxButton.OK, MessageBoxImage.Information);
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

        bool reservarProyecto = ChkReservarStockProyectoRecepcion?.IsChecked == true;
        string estatusPrevRecep = oc.Estatus;
        bool sinAutorizacionOPagoPrevio = estatusPrevRecep.Contains("Pendiente Vo.Bo.", StringComparison.OrdinalIgnoreCase) || estatusPrevRecep.Contains("Pendiente Pago", StringComparison.OrdinalIgnoreCase);
        var entrada = DatabaseService.RegistrarRecepcionOrdenCompra(oc, aRecibir, notasCompletas, reservarProyecto);
        var ocPost = DatabaseService.ObtenerOrdenesCompra().FirstOrDefault(x => x.Id == oc.Id);
        string nuevoEstRecep = ocPost?.Estatus ?? "Recibida";
        if (sinAutorizacionOPagoPrevio)
            RegistrarTransicionCheckpoint("Etapa 4: Recepción Anticipada", "CP-5B", $"{entrada.Folio} ({oc.Folio})", estatusPrevRecep, nuevoEstRecep, "Compras, Finanzas", $"ALERTA: Almacén recibió mercancía ({entrada.Folio}) de {oc.Folio} que aún figuraba como '{estatusPrevRecep}'. Regularizar Vo.Bo./Pago y subir XML.");
        else
            RegistrarTransicionCheckpoint("Etapa 4: Recepción Almacén", "CP-5A", $"{entrada.Folio} ({oc.Folio})", estatusPrevRecep, nuevoEstRecep, "Compras, Solicitante", $"Almacén registró entrada {entrada.Folio} de {oc.Folio} ({oc.ProveedorNombre}). Material disponible en bolsa libre para entrega.");
        TxtNotasRecepcionOc.Clear();
        TxtFolioRemisionRecepcionOc?.Clear();

        // Verificar si se recibieron herramientas para abrir el modal de registro de activo individual (HER-xxxx)
        var prodsDict = DatabaseService.ObtenerProductos().ToDictionary(p => p.Id);
        var partidasHerramienta = entrada.Partidas
            .Where(ep => prodsDict.TryGetValue(ep.ProductoId, out var pr) && (pr.EsHerramienta || pr.CategoriaId == 4))
            .ToList();
        if (partidasHerramienta.Count > 0)
        {
            foreach (var ep in partidasHerramienta)
            {
                if (prodsDict.TryGetValue(ep.ProductoId, out var prodHer))
                {
                    int cantHer = Math.Max(1, (int)Math.Round(ep.CantidadIngresadaAlmacen));
                    MostrarModalRegistroHerramienta(prodHer, cantHer);
                }
            }
        }

        CargarTodoElSistema();
        BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, entrada);

        // Si el material no se reservó en stock y la OC tiene departamento/proyecto, ofrecer pre-cargarlo en Salida de Material
        if (!reservarProyecto && entrada.Partidas.Count > 0)
        {
            var respSal = DialogoFluent.Show(this,
                $"¿Desea cargar los artículos recién recibidos de {oc.Folio} en la pestaña '5. Almacén -> Salida de Material' para emitir su Vale de Salida (SAL-xxxx) hacia el área solicitante ({oc.DepartamentoNombre})?",
                "Emitir Salida de Material Recibido",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (respSal == System.Windows.MessageBoxResult.Yes)
            {
                CargarPartidasDeEntradaEnSalidaPos(entrada, oc);
                CambiarSeccion(5);
            }
        }
    }

    private void CargarPartidasDeEntradaEnSalidaPos(EntradaAlmacen entrada, OrdenCompra? ocOrigen)
    {
        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        _partidasSalidaPos.Clear();
        foreach (var ep in entrada.Partidas)
        {
            if (prodsDict.TryGetValue(ep.ProductoId, out var prod))
            {
                decimal cantMenudeo = ep.CantidadIngresadaAlmacen > 0 ? ep.CantidadIngresadaAlmacen : ep.CantidadCompraRecibida;
                AgregarProductoAValeSalidaPos(prod, cantMenudeo);
            }
        }

        if (ocOrigen != null)
        {
            if (CboSalDepartamento != null && !string.IsNullOrWhiteSpace(ocOrigen.DepartamentoNombre))
            {
                var dep = CboSalDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Nombre.Equals(ocOrigen.DepartamentoNombre, StringComparison.OrdinalIgnoreCase));
                if (dep != null) CboSalDepartamento.SelectedItem = dep;
            }
            if (CboSalProyecto != null && ocOrigen.ProyectoId > 0)
            {
                var proy = CboSalProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == ocOrigen.ProyectoId);
                if (proy != null) CboSalProyecto.SelectedItem = proy;
            }
            if (TxtSalNotas != null)
            {
                TxtSalNotas.Text = $"Entrega de material recibido de {ocOrigen.Folio} ({entrada.Folio})";
            }
        }
    }

    private bool ConfirmarNombresConceptosAbiertosEnRecepcion(OrdenCompra oc, List<OrdenCompraPartida> conceptosAbiertos)
    {
        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = $"Confirmar Alta en Catálogo de Conceptos Abiertos — {oc.Folio}",
            Width = 560,
            Height = Math.Min(540, 230 + conceptosAbiertos.Count * 86),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new DockPanel { Margin = new Thickness(18) };
        var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        topStack.Children.Add(new TextBlock
        {
            Text = "Definir Nombre Específico para el Catálogo (PRD-xxxx)",
            FontSize = 15,
            FontWeight = FontWeights.Bold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = $"Esta Orden de Compra ({oc.ProveedorNombre}) incluye {conceptosAbiertos.Count} concepto(s) abierto(s). Puede confirmar o detallar el nombre/marca real comprado para que se registre correctamente en el Catálogo:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
        var editores = new List<(OrdenCompraPartida partida, System.Windows.Controls.TextBox txtNombre, ComboBox cboUnidad)>();

        foreach (var part in conceptosAbiertos)
        {
            var card = new Border
            {
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(6),
                Background = Tk("ControlFillColorSecondaryBrush"),
                BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
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
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

            var txtNom = new System.Windows.Controls.TextBox
            {
                Text = !string.IsNullOrWhiteSpace(part.DescripcionLibre) ? part.DescripcionLibre : part.ProductoNombre,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Nombre específico del artículo (ej. Jabón Líquido Salvo 900ml)"
            };
            var cboUni = new ComboBox
            {
                ItemsSource = DatabaseService.UnidadesMedidaEstandar,
                IsEditable = true,
                Text = DatabaseService.NormalizarUnidadMedida(part.UnidadCompra),
                ToolTip = "Unidad de compra/almacén estandarizada"
            };
            Grid.SetColumn(txtNom, 0);
            Grid.SetColumn(cboUni, 1);
            rowGrid.Children.Add(txtNom);
            rowGrid.Children.Add(cboUni);
            sp.Children.Add(rowGrid);
            card.Child = sp;
            itemsPanel.Children.Add(card);
            editores.Add((part, txtNom, cboUni));
        }

        scroll.Content = itemsPanel;
        root.Children.Add(scroll);

        btnCancelar.Click += (_, _) => win.DialogResult = false;
        btnConfirmar.Click += (_, _) =>
        {
            foreach (var (partida, txtNombre, cboUnidad) in editores)
            {
                string nom = txtNombre.Text.Trim();
                if (!string.IsNullOrWhiteSpace(nom))
                {
                    partida.DescripcionLibre = nom;
                    partida.ProductoNombre = nom;
                }
                string uni = DatabaseService.NormalizarUnidadMedida(cboUnidad.Text);
                partida.UnidadCompra = uni;
                partida.UnidadAlmacen = uni;
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
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra de la lista.", "OC no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Forzar estatus o cierre de {oc.Folio} ({oc.ProveedorNombre})"))
            return;

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = $"Forzar Estatus de Orden de Compra — {oc.Folio}",
            Width = 500,
            Height = 365,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = $"Cambiar / Forzar Estatus de {oc.Folio}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Proveedor: {oc.ProveedorNombre}  |  Estatus actual: {oc.Estatus}  |  Avance: {oc.AvanceRecepcionTexto}",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
                DialogoFluent.Show(win, "Escriba el motivo del cambio administrativo.", "Motivo requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, $"Se actualizó el estatus de la Orden de Compra {oc.Folio} y se registró en la Bitácora de Administrador.", "Estatus Actualizado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnLiquidarOcSinFactura_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra de la lista.", "OC no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Liquidar OC {oc.Folio} con Nota/Ticket sin Factura XML"))
            return;

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = $"Liquidar OC sin Factura XML — {oc.Folio}",
            Width = 460,
            Height = 310,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = $"Registrar Nota / Ticket de Compra para {oc.Folio}",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Proveedor: {oc.ProveedorNombre}\nTotal de la OC: {oc.TotalConIva:C2}\nSe generará un comprobante interno vinculado a esta OC para el historial de compras y análisis.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
            DialogoFluent.Show(this,
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
        else
        {
            DialogoFluent.Show(this, "Seleccione una recepción (ENT-xxxx) del historial para imprimirla.", "Sin recepción seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnDescargarEntradaSeleccionada_Click(object sender, RoutedEventArgs e)
    {
        if (DgEntradasHistorial.SelectedItem is EntradaAlmacen ent)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent, soloDescargar: true);
        }
        else
        {
            DialogoFluent.Show(this, "Seleccione una recepción (ENT-xxxx) del historial para descargar su archivo (PDF, PNG o Excel CSV).", "Sin recepción seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
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
            int totalHer = pre.Partidas.Count;
            int pendHer = pre.Partidas.Count(x => string.Equals(x.EstadoDevolucion, "Pendiente", StringComparison.OrdinalIgnoreCase));
            string estadoGrupo = pendHer > 0
                ? $"⏳ {pendHer} pendiente(s) de {totalHer}"
                : $"✔ Devuelto ({totalHer}/{totalHer})";
            string resumenGrupo = $"📄 {pre.Folio} — Recibió: {pre.PersonaRecibeEfectiva} ({pre.DepartamentoNombre}) | Proy: {pre.ProyectoNombre} [{estadoGrupo}]";

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
                    FechaPromesa = pre.FechaCompromiso,
                    EstadoDevolucion = part.EstadoDevolucion,
                    GrupoValeResumen = resumenGrupo,
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

    private static int CalcularPrioridadBusquedaProducto(Producto p, string q, string qNorm)
    {
        if (string.IsNullOrWhiteSpace(q)) return 0;

        if (string.Equals(p.CodigoInterno, q, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.CodigoInterno, qNorm, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.CodigoBarras, q, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.CodigoBarras, qNorm, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        // Si el usuario escribe solo el número (ej. "441" o "0441") o "PRD-441", priorizar PRD-0441 en primer lugar
        var partesQ = qNorm.Split('-', StringSplitOptions.RemoveEmptyEntries);
        int numBuscado = -1;
        bool esNumeroPrd =
            (partesQ.Length == 2 && partesQ[0].Equals("PRD", StringComparison.OrdinalIgnoreCase) && int.TryParse(partesQ[1], out numBuscado)) ||
            (partesQ.Length == 1 && q.Length <= 4 && int.TryParse(q, out numBuscado) && numBuscado > 0);

        if (esNumeroPrd && numBuscado > 0)
        {
            var partesP = NormalizarCodigoEscaneado(p.CodigoInterno).Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (partesP.Length == 2 &&
                partesP[0].Equals("PRD", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(partesP[1], out int numP) &&
                numP == numBuscado)
            {
                return 95;
            }
        }

        if (p.CodigoInterno.Contains(qNorm, StringComparison.OrdinalIgnoreCase))
            return 80;

        if (p.Nombre.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 70;

        if (p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 60;

        return 10;
    }

    private void FiltrarComboProductoSalidaPos()
    {
        if (CboSalProductoPos == null) return;

        var todosProds = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        var baseProds = todosProds.ToList();

        string q = TxtSalBuscarProductoPos?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q))
            q = TxtBuscarAlmacen?.Text?.Trim() ?? string.Empty;

        string qNorm = NormalizarCodigoEscaneado(q);
        int prevId = (CboSalProductoPos.SelectedItem as Producto)?.Id ?? 0;

        if (string.IsNullOrWhiteSpace(q))
        {
            if (TxtContadorFiltroProductoSal != null)
                TxtContadorFiltroProductoSal.Text = $"{baseProds.Count} artículos disponibles";

            CboSalProductoPos.ItemsSource = baseProds;
            CboSalProductoPos.SelectedItem = prevId > 0 ? baseProds.FirstOrDefault(p => p.Id == prevId) : null;
            if (prevId <= 0)
                CboSalProductoPos.Text = string.Empty;
            return;
        }

        var terminos = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtrados = baseProds.Where(p =>
            (!string.IsNullOrEmpty(qNorm) && (
                p.CodigoInterno.Contains(qNorm, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoBarras.Contains(qNorm, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoEfectivo.Contains(qNorm, StringComparison.OrdinalIgnoreCase))) ||
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
        )
        .OrderByDescending(p => CalcularPrioridadBusquedaProducto(p, q, qNorm))
        .ThenBy(p => p.CodigoInterno)
        .ToList();

        if (TxtContadorFiltroProductoSal != null)
            TxtContadorFiltroProductoSal.Text = $"{filtrados.Count} coincidencias (Enter para agregar)";

        CboSalProductoPos.ItemsSource = filtrados;
        if (filtrados.Count > 0)
        {
            CboSalProductoPos.SelectedItem = filtrados[0];
        }
        else
        {
            CboSalProductoPos.SelectedIndex = -1;
            CboSalProductoPos.Text = string.Empty;
        }
    }

    private void TxtSalBuscarProductoPos_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        FiltrarComboProductoSalidaPos();
        RegistrarPulsoScannerPistola("SalidaPos", TxtSalBuscarProductoPos?.Text);
    }

    private void TxtSalBuscarProductoPos_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && CboSalProductoPos.Items.Count > 0)
        {
            int idx = CboSalProductoPos.SelectedIndex;
            if (idx < CboSalProductoPos.Items.Count - 1)
                CboSalProductoPos.SelectedIndex = idx + 1;
            else
                CboSalProductoPos.SelectedIndex = 0;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && CboSalProductoPos.Items.Count > 0)
        {
            int idx = CboSalProductoPos.SelectedIndex;
            if (idx > 0)
                CboSalProductoPos.SelectedIndex = idx - 1;
            else
                CboSalProductoPos.SelectedIndex = CboSalProductoPos.Items.Count - 1;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || (e.Key == Key.Tab && _rachaPulsosRapidosPistola >= 3))
        {
            _timerAutoEscaneoPistola.Stop();
            _rachaPulsosRapidosPistola = 0;
            string raw = TxtSalBuscarProductoPos?.Text?.Trim() ?? string.Empty;
            if (ProcesarBusquedaOEscaneoSalidaPos(raw))
            {
                TxtSalBuscarProductoPos?.Clear();
                FiltrarComboProductoSalidaPos();
                TxtSalBuscarProductoPos?.Focus();
            }
            e.Handled = true;
        }
    }

    private void CboSalEncargado_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string txt = CboSalEncargado.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(txt);

            // Si por error el cursor estaba en 'Persona que Recibe' cuando se disparó con la pistola de código de barras
            if (!string.IsNullOrWhiteSpace(code) &&
                (EsPrefijoDocumento(code) || DatabaseService.BuscarProductoPorCodigo(code) != null || DatabaseService.BuscarProductoPorCodigo(txt) != null))
            {
                CboSalEncargado.SelectedIndex = -1;
                CboSalEncargado.Text = string.Empty;
                ProcesarBusquedaOEscaneoSalidaPos(txt);
                TxtSalBuscarProductoPos?.Focus();
                e.Handled = true;
                return;
            }

            TxtSalBuscarProductoPos?.Focus();
            e.Handled = true;
        }
    }

    private void CboSalProyecto_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _actualizandoCombosBase) return;
        if (CboSalProyecto.SelectedItem != null)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtSalBuscarProductoPos?.Focus();
            }), DispatcherPriority.Input);
        }
    }

    private void CboSalProductoPos_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            decimal cant = ParseDecimal(TxtSalCantidadPos?.Text, 1m);
            if (CboSalProductoPos.SelectedItem is Producto prodSel)
            {
                AgregarProductoAValeSalidaPos(prodSel, cant);
                TxtSalBuscarProductoPos?.Clear();
                CboSalProductoPos.SelectedIndex = -1;
                CboSalProductoPos.Text = string.Empty;
                FiltrarComboProductoSalidaPos();
                TxtSalBuscarProductoPos?.Focus();
                e.Handled = true;
                return;
            }

            string raw = CboSalProductoPos.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(raw) && ProcesarBusquedaOEscaneoSalidaPos(raw))
            {
                TxtSalBuscarProductoPos?.Clear();
                CboSalProductoPos.SelectedIndex = -1;
                CboSalProductoPos.Text = string.Empty;
                FiltrarComboProductoSalidaPos();
                TxtSalBuscarProductoPos?.Focus();
            }
            e.Handled = true;
        }
    }

    private void TxtSalCantidadPos_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string rawCant = TxtSalCantidadPos?.Text?.Trim() ?? string.Empty;
            string code = NormalizarCodigoEscaneado(rawCant);

            // Si el usuario tenía el foco en Cantidad y disparó con la pistola lectora de código de barras
            if (!string.IsNullOrWhiteSpace(code) &&
                !decimal.TryParse(rawCant, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                (EsPrefijoDocumento(code) || DatabaseService.BuscarProductoPorCodigo(code) != null || DatabaseService.BuscarProductoPorCodigo(rawCant) != null))
            {
                TxtSalCantidadPos!.Text = "1";
                ProcesarBusquedaOEscaneoSalidaPos(rawCant);
                TxtSalBuscarProductoPos?.Focus();
                e.Handled = true;
                return;
            }

            BtnAgregarPartidaSalidaPos_Click(sender, e);
            e.Handled = true;
        }
    }

    private bool ProcesarBusquedaOEscaneoSalidaPos(string? rawInput)
    {
        string raw = rawInput?.Trim() ?? string.Empty;
        string code = NormalizarCodigoEscaneado(raw);
        if (string.IsNullOrWhiteSpace(code))
            return false;

        // 1. Si escaneó un folio de documento o herramienta (ENT-, SAL-, OC-, PRE-, HER-, SOL-)
        if (EsPrefijoDocumento(code))
        {
            ProcesarCodigoEscaneado(code);
            return true;
        }

        decimal cant = ParseDecimal(TxtSalCantidadPos?.Text, 1m);

        // 2. Si coincide con el código de barras o código interno de un producto
        var prodExacto = DatabaseService.BuscarProductoPorCodigo(code) ?? DatabaseService.BuscarProductoPorCodigo(raw);
        if (prodExacto != null)
        {
            AgregarProductoAValeSalidaPos(prodExacto, cant);
            TxtUltimoEscaneo.Text = $"Agregado a Salida: [{prodExacto.CodigoInterno}] {prodExacto.Nombre} (x{cant:0.##})";
            return true;
        }

        // 3. Si hay un producto filtrado/seleccionado en CboSalProductoPos
        if (CboSalProductoPos.SelectedItem is Producto prodSel)
        {
            AgregarProductoAValeSalidaPos(prodSel, cant);
            TxtUltimoEscaneo.Text = $"Agregado a Salida: [{prodSel.CodigoInterno}] {prodSel.Nombre} (x{cant:0.##})";
            return true;
        }

        // 4. Buscar por coincidencia parcial en nombre/código/marca
        var todosProds = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        var matchParcial = todosProds
            .Where(p =>
                p.Nombre.Contains(raw, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoInterno.Contains(code, StringComparison.OrdinalIgnoreCase) ||
                p.CodigoBarras.Contains(code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => CalcularPrioridadBusquedaProducto(p, raw, code))
            .FirstOrDefault();

        if (matchParcial != null)
        {
            AgregarProductoAValeSalidaPos(matchParcial, cant);
            TxtUltimoEscaneo.Text = $"Agregado a Salida: [{matchParcial.CodigoInterno}] {matchParcial.Nombre} (x{cant:0.##})";
            return true;
        }

        DialogoFluent.Show(this,
            $"No se encontró ningún material en almacén que coincida con '{raw}'.\n\nVerifique el código de barras o escriba parte del nombre del artículo.",
            "Artículo no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void AsignarItemsPrestamosConAgrupacion(List<PrestamoVistaFila> lista)
    {
        var cvs = new System.Windows.Data.CollectionViewSource { Source = lista };
        cvs.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(PrestamoVistaFila.GrupoValeResumen)));
        DgPrestamosHerramientas.ItemsSource = cvs.View;
    }

    private void AplicarFiltroAlmacen()
    {
        string raw = TxtBuscarAlmacen?.Text?.Trim() ?? string.Empty;
        string norm = NormalizarCodigoEscaneado(raw);

        FiltrarComboProductoSalidaPos();

        if (string.IsNullOrWhiteSpace(raw))
        {
            AsignarItemsPrestamosConAgrupacion(_todosPrestamosFilas);
            DgMovimientosAlmacen.ItemsSource = _todosMovimientosAlmacen;
            return;
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

        AsignarItemsPrestamosConAgrupacion(prestamosFiltrados);
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
        RegistrarPulsoScannerPistola("AlmacenTop", TxtBuscarAlmacen?.Text);
    }

    private void TxtBuscarAlmacen_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || (e.Key == Key.Tab && _rachaPulsosRapidosPistola >= 3))
        {
            _timerAutoEscaneoPistola.Stop();
            _rachaPulsosRapidosPistola = 0;
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
        TxtSalBuscarProductoPos?.Focus();
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
            decimal cant = ParseDecimal(TxtSalCantidadPos.Text, 1m);
            AgregarProductoAValeSalidaPos(prodExacto, cant);
            TxtUltimoEscaneo.Text = $"Agregado a Salida: [{prodExacto.CodigoInterno}] {prodExacto.Nombre} (x{cant:0.##})";
            return;
        }

        // 3. Si al filtrar por texto queda exactamente 1 movimiento (Entrada o Salida) y ningún producto seleccionado, abrir su vista previa
        AplicarFiltroAlmacen();
        if (DgMovimientosAlmacen.Items.Count == 1 && DgMovimientosAlmacen.Items[0] is MovimientoAlmacenFila unicoMov &&
            (unicoMov.Folio.Contains(code, StringComparison.OrdinalIgnoreCase) || CoincideFolioFlexible(unicoMov.Folio, code)))
        {
            AbrirVistaPreviaMovimientoAlmacen(unicoMov);
            return;
        }

        // 4. Si hay un producto seleccionado en el combo POS tras filtrar por texto, agregarlo directo al vale de salida
        if (CboSalProductoPos.SelectedItem is Producto prodFiltrado)
        {
            decimal cant = ParseDecimal(TxtSalCantidadPos.Text, 1m);
            AgregarProductoAValeSalidaPos(prodFiltrado, cant);
            TxtBuscarAlmacen!.Clear();
            AplicarFiltroAlmacen();
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
            DialogoFluent.Show(this,
                "Seleccione un movimiento (Entrada ENT-xxxx o Salida SAL-xxxx) de la tabla inferior o escanee su código de barras en el buscador.",
                "Seleccione un vale", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnDescargarMovimientoAlmacen_Click(object sender, RoutedEventArgs e)
    {
        if (DgMovimientosAlmacen.SelectedItem is MovimientoAlmacenFila fila)
        {
            AbrirVistaPreviaMovimientoAlmacen(fila, soloDescargar: true);
        }
        else
        {
            DialogoFluent.Show(this,
                "Seleccione un movimiento (Entrada ENT-xxxx o Salida SAL-xxxx) de la tabla inferior para descargar su archivo (PDF, PNG o Excel CSV).",
                "Seleccione un vale", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AbrirVistaPreviaMovimientoAlmacen(MovimientoAlmacenFila fila, bool soloDescargar = false)
    {
        if (fila.EntradaOriginal != null)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, fila.EntradaOriginal, soloDescargar);
        }
        else if (fila.SalidaOriginal != null)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, fila.SalidaOriginal, soloDescargar);
        }
    }

    private void AgregarProductoAValeSalidaPos(Producto prod, decimal cantidadMenudeo)
    {
        if (cantidadMenudeo <= 0) cantidadMenudeo = 1m;
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
            DgPartidasSalidaPos.Items.Refresh();
        }
    }

    private void BtnDisminuirPartidaSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SalidaPartida partida)
            return;

        if (partida.CantidadEntregada > 1)
        {
            partida.CantidadEntregada -= 1;
            DgPartidasSalidaPos.Items.Refresh();
        }
        else
        {
            _partidasSalidaPos.Remove(partida);
        }
    }

    private void BtnAumentarPartidaSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SalidaPartida partida)
            return;

        partida.CantidadEntregada += 1;
        DgPartidasSalidaPos.Items.Refresh();
    }

    private void BtnQuitarPartidaSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SalidaPartida partida)
            return;

        _partidasSalidaPos.Remove(partida);
    }

    private void BtnAgregarPartidaSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        decimal cant = ParseDecimal(TxtSalCantidadPos.Text, 1m);
        if (CboSalProductoPos.SelectedItem is Producto prod)
        {
            AgregarProductoAValeSalidaPos(prod, cant);
            TxtSalBuscarProductoPos?.Clear();
            CboSalProductoPos.SelectedIndex = -1;
            CboSalProductoPos.Text = string.Empty;
            FiltrarComboProductoSalidaPos();
            TxtSalBuscarProductoPos?.Focus();
            return;
        }

        string texto = !string.IsNullOrWhiteSpace(TxtSalBuscarProductoPos?.Text)
            ? TxtSalBuscarProductoPos!.Text.Trim()
            : (CboSalProductoPos.Text?.Trim() ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(texto))
        {
            if (ProcesarBusquedaOEscaneoSalidaPos(texto))
            {
                TxtSalBuscarProductoPos?.Clear();
                CboSalProductoPos.SelectedIndex = -1;
                CboSalProductoPos.Text = string.Empty;
                FiltrarComboProductoSalidaPos();
                TxtSalBuscarProductoPos?.Focus();
            }
        }
    }

    private void BtnLimpiarSalidaPos_Click(object sender, RoutedEventArgs e)
    {
        _partidasSalidaPos.Clear();
        if (CboSalDepartamento != null) CboSalDepartamento.SelectedIndex = -1;
        if (CboSalEncargado != null) { CboSalEncargado.SelectedIndex = -1; CboSalEncargado.Text = string.Empty; }
        if (CboSalProyecto != null) CboSalProyecto.SelectedIndex = -1;
        TxtSalBuscarProductoPos?.Clear();
        if (CboSalProductoPos != null) { CboSalProductoPos.SelectedIndex = -1; CboSalProductoPos.Text = string.Empty; }
        FiltrarComboProductoSalidaPos();
        TxtSalCantidadPos?.Clear();
        TxtSalNotas?.Clear();
    }

    private void BtnCargarMaterialOcRecibidaEnSalida_Click(object sender, RoutedEventArgs e)
    {
        var entradas = DatabaseService.ObtenerEntradasAlmacen();
        if (entradas.Count == 0)
        {
            DialogoFluent.Show(this, "Aún no existen recepciones de Orden de Compra (ENT-xxxx) registradas en almacén.", "Sin recepciones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Cargar Material de OC Recibida en Vale de Salida",
            Width = 520,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Seleccionar Recepción de Almacén (ENT-xxxx / OC-xxxx)",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Seleccione el Vale de Entrada cuyos materiales desea entregar al área solicitante o cuerpo técnico:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var cboEnt = new ComboBox
        {
            ItemsSource = entradas,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(cboEnt);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnOk = new System.Windows.Controls.Button { Content = "Cargar Artículos en Salida", Padding = new Thickness(14, 6, 14, 6), FontWeight = FontWeights.Bold };

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click += (_, _) =>
        {
            if (cboEnt.SelectedItem is EntradaAlmacen entSel)
            {
                var oc = entSel.OrdenCompraId > 0
                    ? DatabaseService.ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == entSel.OrdenCompraId)
                    : null;
                CargarPartidasDeEntradaEnSalidaPos(entSel, oc);
                win.DialogResult = true;
            }
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);
        win.Content = root;
        win.ShowDialog();
    }

    private void BtnImprimirCartaResponsivaEpp_Click(object sender, RoutedEventArgs e)
    {
        SalidaAlmacen? sal = null;
        if (DgMovimientosAlmacen.SelectedItem is MovimientoAlmacenFila fila && fila.SalidaOriginal != null)
        {
            sal = DatabaseService.ObtenerSalidasAlmacen().FirstOrDefault(s => s.Id == fila.SalidaOriginal.Id) ?? fila.SalidaOriginal;
        }
        else
        {
            sal = DatabaseService.ObtenerSalidasAlmacen().FirstOrDefault();
        }

        if (sal == null)
        {
            DialogoFluent.Show(this,
                "Registre primero un Vale de Salida de Material (SAL-xxxx) o selecciónelo en la tabla 'Vales de Entrada y Salida' para generar e imprimir su Carta de Aceptación de Responsabilidad de EPP / Equipo.",
                "Seleccione una Salida de Material", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BarcodePrintService.MostrarVistaPreviaEImprimirCartaResponsabilidadEpp(this, sal);
    }

    private static bool EsArticuloEppOResponsiva(Producto? p, string nombreFallback)
    {
        if (p != null && (p.CategoriaId == 8 || p.CategoriaNombre.Contains("EPP", StringComparison.OrdinalIgnoreCase) || p.CategoriaNombre.Contains("Seguridad", StringComparison.OrdinalIgnoreCase)))
            return true;

        string nom = (p?.Nombre ?? nombreFallback ?? string.Empty).ToUpperInvariant();
        string[] keywords = { "CASCO", "BOTA", "CALZADO", "CHALECO", "GUANTE", "LENTE", "GAFA", "ARNES", "ARNÉS", "FAJA", "CARETA", "TAPON", "AUDITIVO", "UNIFORME", "IMPERMEABLE", "RESPIRADOR", "MASCARILLA", "EPP" };
        return keywords.Any(k => nom.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private void BtnRegistrarSalidaAlmacen_Click(object sender, RoutedEventArgs e)
    {
        if (CboSalDepartamento.SelectedItem is not Departamento dep)
        {
            DialogoFluent.Show(this, "Seleccione obligatoriamente el Departamento que Aprueba la Salida (el Proyecto Destino es opcional).", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int proyId = (CboSalProyecto.SelectedItem as Proyecto)?.Id ?? DatabaseService.ObtenerProyectoStockGeneralId();

        string personaRecibe = (CboSalEncargado.SelectedItem as Encargado)?.Nombre
                               ?? CboSalEncargado.Text?.Trim()
                               ?? string.Empty;

        if (string.IsNullOrWhiteSpace(personaRecibe))
        {
            DialogoFluent.Show(this, "Escriba o seleccione el nombre de la Persona que Recibe el material.", "Falta persona que recibe", MessageBoxButton.OK, MessageBoxImage.Warning);
            CboSalEncargado.Focus();
            return;
        }

        if (_partidasSalidaPos.Count == 0)
        {
            DialogoFluent.Show(this, "Agregue o escanee al menos un material para el vale de salida.", "Vale vacío", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var confirmarStock = DialogoFluent.Show(this,
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

        string notasSalPrev = TxtSalNotas.Text ?? string.Empty;
        var salida = DatabaseService.RegistrarSalidaAlmacen(dep.Id, encId, proyId, notasSalPrev, _partidasSalidaPos.ToList(), personaRecibe);
        RegistrarTransicionCheckpoint("Etapa 5: Salida de Material", "CP-8", salida.Folio, "Disponible en Almacén", "Entregado", "Compras, Solicitante", $"Almacén entregó {salida.Partidas.Count} artículo(s) en el vale {salida.Folio} a '{salida.PersonaRecibeEfectiva}' ({salida.ProyectoNombre}).");
        var solVinculada = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Folio) && notasSalPrev.Contains(s.Folio, StringComparison.OrdinalIgnoreCase));
        if (solVinculada != null && string.Equals(solVinculada.Estatus, "Autorizada para Surtir Stock", StringComparison.OrdinalIgnoreCase))
            DatabaseService.ActualizarEstatusSolicitud(solVinculada.Id, "Atendida");
        bool incluyeEpp = salida.Partidas.Any(sp =>
        {
            prodsDict.TryGetValue(sp.ProductoId, out var prodObj);
            return EsArticuloEppOResponsiva(prodObj, sp.ProductoNombre);
        });

        BtnLimpiarSalidaPos_Click(sender, e);
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, salida);

        if (incluyeEpp)
        {
            var respEpp = DialogoFluent.Show(this,
                $"Se detectó Equipo de Protección Personal (EPP) o equipo de asignación en el vale {salida.Folio} entregado a '{salida.PersonaRecibeEfectiva}'.\n\n¿Desea abrir e imprimir la Carta de Aceptación de Responsabilidad de EPP con firmas del Técnico y de la Gerencia correspondiente ({salida.DepartamentoNombre})?",
                "Imprimir Carta Responsiva de EPP",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (respEpp == System.Windows.MessageBoxResult.Yes)
            {
                BarcodePrintService.MostrarVistaPreviaEImprimirCartaResponsabilidadEpp(this, salida);
            }
        }
    }

    private void CboHerramientaDisponible_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnAgregarHerramientaAPrestamo_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnAgregarHerramientaAPrestamo_Click(object sender, RoutedEventArgs e)
    {
        if (CboHerramientaDisponible.SelectedItem is HerramientaActivo herSel)
        {
            AgregarHerramientaAListaPrestamo(herSel);
            return;
        }

        string txt = CboHerramientaDisponible.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(txt) && CboHerramientaDisponible.ItemsSource is IEnumerable<HerramientaActivo> disponibles)
        {
            var match = disponibles.FirstOrDefault(h =>
                h.CodigoIndividual.Equals(txt, StringComparison.OrdinalIgnoreCase) ||
                CoincideFolioFlexible(h.CodigoIndividual, txt) ||
                h.ProductoNombre.Contains(txt, StringComparison.OrdinalIgnoreCase) ||
                h.ToString().Contains(txt, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                AgregarHerramientaAListaPrestamo(match);
                return;
            }
        }

        DialogoFluent.Show(this,
            "Seleccione una herramienta disponible del listado para agregarla al vale de préstamo.",
            "Seleccione herramienta", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AgregarHerramientaAListaPrestamo(HerramientaActivo her)
    {
        if (_herramientasPorPrestar.Any(x => x.Id == her.Id))
        {
            CboHerramientaDisponible.SelectedIndex = -1;
            CboHerramientaDisponible.Text = string.Empty;
            return;
        }

        _herramientasPorPrestar.Add(her);
        CboHerramientaDisponible.SelectedIndex = -1;
        CboHerramientaDisponible.Text = string.Empty;
        ActualizarVistaListaPrestamoHerramientas();
    }

    private void BtnQuitarHerramientaDePrestamo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HerramientaActivo her })
        {
            var item = _herramientasPorPrestar.FirstOrDefault(x => x.Id == her.Id);
            if (item != null)
                _herramientasPorPrestar.Remove(item);
            ActualizarVistaListaPrestamoHerramientas();
        }
    }

    private void BtnLimpiarListaPrestamo_Click(object sender, RoutedEventArgs e)
    {
        _herramientasPorPrestar.Clear();
        ActualizarVistaListaPrestamoHerramientas();
    }

    private void ActualizarVistaListaPrestamoHerramientas()
    {
        if (BdrListaHerramientasPorPrestar == null || IcHerramientasPorPrestar == null || TxtResumenListaPrestamoHer == null)
            return;

        if (IcHerramientasPorPrestar.ItemsSource == null)
            IcHerramientasPorPrestar.ItemsSource = _herramientasPorPrestar;

        int n = _herramientasPorPrestar.Count;
        BdrListaHerramientasPorPrestar.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtResumenListaPrestamoHer.Text = n == 1
            ? "Lista para prestar en un solo vale (1 herramienta seleccionada):"
            : $"Lista para prestar en un solo vale ({n} herramientas seleccionadas):";

        if (BtnRegistrarPrestamoHer != null)
        {
            BtnRegistrarPrestamoHer.Content = n > 0
                ? $"Registrar Préstamo ({n})"
                : "Registrar Préstamo";
        }
    }

    private void BtnPrestarHerramienta_Click(object sender, RoutedEventArgs e)
    {
        var dep = CboPreDepartamento?.SelectedItem as Departamento ?? CboSalDepartamento.SelectedItem as Departamento;
        var proy = CboPreProyecto?.SelectedItem as Proyecto ?? CboSalProyecto.SelectedItem as Proyecto;

        if (dep == null)
        {
            DialogoFluent.Show(this, "Seleccione obligatoriamente el Departamento que Aprueba el Préstamo (el Proyecto Destino es opcional).", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int proyId = proy?.Id ?? DatabaseService.ObtenerProyectoStockGeneralId();

        string personaRecibe = (CboPrePersonaRecibe?.SelectedItem as Encargado)?.Nombre
                               ?? CboPrePersonaRecibe?.Text?.Trim()
                               ?? (CboSalEncargado.SelectedItem as Encargado)?.Nombre
                               ?? CboSalEncargado.Text?.Trim()
                               ?? string.Empty;

        if (string.IsNullOrWhiteSpace(personaRecibe))
        {
            DialogoFluent.Show(this, "Escriba o seleccione el nombre de la Persona que Recibe la herramienta.", "Falta persona que recibe", MessageBoxButton.OK, MessageBoxImage.Warning);
            CboPrePersonaRecibe?.Focus();
            return;
        }

        var listaParaPrestar = _herramientasPorPrestar.ToList();
        if (CboHerramientaDisponible.SelectedItem is HerramientaActivo herCombo &&
            !listaParaPrestar.Any(x => x.Id == herCombo.Id))
        {
            listaParaPrestar.Add(herCombo);
        }

        if (listaParaPrestar.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione una o más herramientas disponibles (usando '+ Agregar a Lista' o el selector) para registrar el préstamo.", "Sin herramientas seleccionadas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int encId = (CboPrePersonaRecibe?.SelectedItem as Encargado)?.Id
                    ?? DatabaseService.ObtenerEncargados(dep.Id).FirstOrDefault()?.Id
                    ?? 1;

        string fechaCompromiso = (DpPreFechaCompromiso?.SelectedDate ?? DateTime.Today.AddDays(7)).ToString("yyyy-MM-dd");

        var prestamo = DatabaseService.RegistrarPrestamoHerramienta(dep.Id, encId, proyId, string.Empty, listaParaPrestar, personaRecibe, fechaCompromiso);
        _herramientasPorPrestar.Clear();
        ActualizarVistaListaPrestamoHerramientas();
        if (CboPreDepartamento != null) CboPreDepartamento.SelectedIndex = -1;
        if (CboPrePersonaRecibe != null) { CboPrePersonaRecibe.SelectedIndex = -1; CboPrePersonaRecibe.Text = string.Empty; }
        if (CboPreProyecto != null) CboPreProyecto.SelectedIndex = -1;
        if (CboHerramientaDisponible != null) { CboHerramientaDisponible.SelectedIndex = -1; CboHerramientaDisponible.Text = string.Empty; }
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, prestamo);
    }

    private void BtnRegistrarDevolucionHerramienta_Click(object sender, RoutedEventArgs e)
    {
        if (DgPrestamosHerramientas.SelectedItem is PrestamoVistaFila fila)
        {
            MostrarModalDevolucionPrestamoHerramienta(fila.PrestamoOriginal, null);
            return;
        }

        var primerActivo = _todosPrestamosFilas.FirstOrDefault(f => f.EstadoDevolucion == "Pendiente")?.PrestamoOriginal
                           ?? _todosPrestamosFilas.FirstOrDefault()?.PrestamoOriginal;
        MostrarModalDevolucionPrestamoHerramienta(primerActivo, null);
    }

    private void DgPrestamosHerramientas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgPrestamosHerramientas.SelectedItem is PrestamoVistaFila fila)
        {
            MostrarModalDevolucionPrestamoHerramienta(fila.PrestamoOriginal, null);
        }
    }

    private void HeaderGrupoPrestamo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string grupoNombre })
        {
            var fila = _todosPrestamosFilas.FirstOrDefault(f => string.Equals(f.GrupoValeResumen, grupoNombre, StringComparison.Ordinal));
            if (fila != null)
            {
                MostrarModalDevolucionPrestamoHerramienta(fila.PrestamoOriginal, null);
                e.Handled = true;
            }
        }
    }

    private void BtnAbrirDevolucionGrupoPrestamo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string grupoNombre })
        {
            var fila = _todosPrestamosFilas.FirstOrDefault(f => string.Equals(f.GrupoValeResumen, grupoNombre, StringComparison.Ordinal));
            if (fila != null)
            {
                MostrarModalDevolucionPrestamoHerramienta(fila.PrestamoOriginal, null);
                e.Handled = true;
            }
        }
    }

    private void DgOrdenesCompra_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra oc)
        {
            MostrarModalRecepcionOrdenCompraAgrupada(oc);
        }
    }

    /// <summary>Devuelve el elemento de la fila sobre la que se hizo clic (ignora encabezados y espacios vacíos).</summary>
    private static T? FilaClicada<T>(MouseButtonEventArgs e) where T : class
    {
        var origen = e.OriginalSource as DependencyObject;
        while (origen != null && origen is not DataGridRow && origen is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            origen = System.Windows.Media.VisualTreeHelper.GetParent(origen);

        return (origen as DataGridRow)?.Item as T;
    }

    /// <summary>Alterna entre la lista de órdenes de compra y el detalle de la orden seleccionada.</summary>
    private void MostrarDetalleOrdenCompra(bool detalle)
    {
        if (PnlOrdenesLista == null || PnlOrdenDetalle == null) return;
        PnlOrdenesLista.Visibility = detalle ? Visibility.Collapsed : Visibility.Visible;
        PnlOrdenDetalle.Visibility = detalle ? Visibility.Visible : Visibility.Collapsed;
        if (detalle && ChkFiltrarEntradasPorOc != null && ChkFiltrarEntradasPorOc.IsChecked != true)
            ChkFiltrarEntradasPorOc.IsChecked = true;
    }

    private void DgOrdenesCompra_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FilaClicada<OrdenCompra>(e) is OrdenCompra)
            MostrarDetalleOrdenCompra(true);
    }

    private void DgOrdenesCompra_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DgOrdenesCompra.SelectedItem is OrdenCompra)
        {
            MostrarDetalleOrdenCompra(true);
            e.Handled = true;
        }
    }

    private void BtnAbrirOrdenSeleccionada_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra)
            MostrarDetalleOrdenCompra(true);
    }

    private void BtnVolverListaOrdenes_Click(object sender, RoutedEventArgs e) =>
        MostrarDetalleOrdenCompra(false);

    private void BtnAbrirModalRecepcionOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra oc)
        {
            MostrarModalRecepcionOrdenCompraAgrupada(oc);
        }
    }

    private void DgEntradasHistorial_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgEntradasHistorial.SelectedItem is EntradaAlmacen ent)
        {
            MostrarModalAccionEntradaAlmacen(ent);
        }
    }

    private void DgMovimientosAlmacen_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgMovimientosAlmacen.SelectedItem is MovimientoAlmacenFila filaMov)
        {
            if (filaMov.SalidaOriginal != null)
            {
                var sal = DatabaseService.ObtenerSalidasAlmacen().FirstOrDefault(s => s.Id == filaMov.SalidaOriginal.Id) ?? filaMov.SalidaOriginal;
                MostrarModalDevolucionMaterialSalida(sal);
            }
            else if (filaMov.EntradaOriginal != null)
            {
                var ent = DatabaseService.ObtenerEntradasAlmacen().FirstOrDefault(x => x.Id == filaMov.EntradaOriginal.Id) ?? filaMov.EntradaOriginal;
                MostrarModalAccionEntradaAlmacen(ent);
            }
            else
            {
                AbrirVistaPreviaMovimientoAlmacen(filaMov);
            }
        }
    }

    private void MostrarModalRecepcionOrdenCompraAgrupada(OrdenCompra? ocPreseleccionada)
    {
        var todasOcs = DatabaseService.ObtenerOrdenesCompra();
        if (todasOcs.Count == 0)
        {
            DialogoFluent.Show(this,
                "Aún no existen Órdenes de Compra (OC-xxxx) emitidas en el sistema.",
                "Sin órdenes de compra", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var ubicacionesCatalogo = ObtenerCatalogoUbicacionesDesplegable();

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Recepción Agrupada de Orden de Compra en Almacén (OC-xxxx → ENT-xxxx)",
            Width = 1040,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        topPanel.Children.Add(new TextBlock
        {
            Text = "Recepción de Mercancía por Orden de Compra (OC-xxxx → Vale de Entrada ENT-xxxx)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("TextFillColorPrimaryBrush")
        });
        topPanel.Children.Add(new TextBlock
        {
            Text = "Marque con ☑ los artículos que SÍ llegaron del proveedor hoy, ajuste la cantidad recibida si fue entrega parcial y asigne su ubicación en anaquel:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var selGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        selGrid.Children.Add(new TextBlock
        {
            Text = "Orden de Compra (OC-xxxx):",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        var cboOcs = new ComboBox
        {
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = todasOcs
        };
        Grid.SetColumn(cboOcs, 1);
        selGrid.Children.Add(cboOcs);

        var btnRecibirTodo = new System.Windows.Controls.Button
        {
            Content = "☑ Llegó Todo lo Pendiente",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnRecibirTodo, 2);
        selGrid.Children.Add(btnRecibirTodo);

        var btnLimpiarCero = new System.Windows.Controls.Button
        {
            Content = "☐ Desmarcar Todo (0)",
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnLimpiarCero, 3);
        selGrid.Children.Add(btnLimpiarCero);
        topPanel.Children.Add(selGrid);

        // Barra rápida para escanear productos con el modal abierto
        var scanGrid = new Grid();
        scanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scanGrid.Children.Add(new TextBlock
        {
            Text = "Escanear artículo dentro de la OC:",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(3, 105, 161)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var txtScanProdModal = new System.Windows.Controls.TextBox
        {
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Escanee un código PRD-xxxx o EAN con esta ventana abierta para marcarlo o sumar +1 recibido"
        };
        Grid.SetColumn(txtScanProdModal, 1);
        scanGrid.Children.Add(txtScanProdModal);
        topPanel.Children.Add(scanGrid);

        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        var infoBorder = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var txtInfoOc = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("TextFillColorPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        infoBorder.Child = txtInfoOc;
        Grid.SetRow(infoBorder, 1);
        root.Children.Add(infoBorder);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Background = Tk("ControlFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var colCheckOc = new DataGridTemplateColumn { Header = "☑ ¿Llegó Hoy?", Width = 120 };
        var chkOcFactory = new FrameworkElementFactory(typeof(CheckBox));
        chkOcFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("SeleccionadoRecibir")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        chkOcFactory.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("TienePendientePorRecibir"));
        chkOcFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        chkOcFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        colCheckOc.CellTemplate = new DataTemplate { VisualTree = chkOcFactory };
        dg.Columns.Add(colCheckOc);

        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Código", Binding = new System.Windows.Data.Binding("ProductoCodigo"), IsReadOnly = true, Width = 85 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Artículo / Concepto", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "U. Compra", Binding = new System.Windows.Data.Binding("UnidadCompra"), IsReadOnly = true, Width = 95 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Pedida", Binding = new System.Windows.Data.Binding("CantidadPedida") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 75 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Recibida", Binding = new System.Windows.Data.Binding("CantidadRecibida") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 90 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Pendiente", Binding = new System.Windows.Data.Binding("CantidadPendiente") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 95 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Recibir Hoy", Binding = new System.Windows.Data.Binding("CantidadRecibirAhora") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, IsReadOnly = false, Width = 125 });

        var colUbiOc = new DataGridTemplateColumn { Header = "Ubicación en Almacén", Width = 185 };
        var cboUbiFactory = new FrameworkElementFactory(typeof(ComboBox));
        cboUbiFactory.SetValue(ComboBox.IsEditableProperty, true);
        cboUbiFactory.SetValue(ItemsControl.ItemsSourceProperty, ubicacionesCatalogo);
        cboUbiFactory.SetBinding(ComboBox.TextProperty, new System.Windows.Data.Binding("UbicacionDestino")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus
        });
        cboUbiFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        colUbiOc.CellTemplate = new DataTemplate { VisualTree = cboUbiFactory };
        dg.Columns.Add(colUbiOc);

        Grid.SetRow(dg, 2);
        root.Children.Add(dg);

        var extraGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var spRem = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        spRem.Children.Add(new TextBlock { Text = "Folio Remisión / Factura Proveedor:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtRemision = new System.Windows.Controls.TextBox { Padding = new Thickness(6, 5, 6, 5) };
        spRem.Children.Add(txtRemision);
        Grid.SetColumn(spRem, 0);
        extraGrid.Children.Add(spRem);

        var spObs = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        spObs.Children.Add(new TextBlock { Text = "Observaciones de la Recepción:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtNotas = new System.Windows.Controls.TextBox { Padding = new Thickness(6, 5, 6, 5), Text = "Recepción verificada en almacén." };
        spObs.Children.Add(txtNotas);
        Grid.SetColumn(spObs, 1);
        extraGrid.Children.Add(spObs);

        var chkReservar = new CheckBox
        {
            Content = "Reservar stock p/ Proyecto\n(Desmarque para pasar a Salida)",
            IsChecked = false,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2)
        };
        Grid.SetColumn(chkReservar, 2);
        extraGrid.Children.Add(chkReservar);

        Grid.SetRow(extraGrid, 3);
        root.Children.Add(extraGrid);

        var bottomDock = new DockPanel();
        var btnSoloImprimirOc = new System.Windows.Controls.Button
        {
            Content = "Solo Ver / Reimprimir OC",
            Padding = new Thickness(12, 7, 12, 7),
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(btnSoloImprimirOc, Dock.Left);
        bottomDock.Children.Add(btnSoloImprimirOc);

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
            Content = "Registrar Entrada de Seleccionados (☑) e Imprimir Vale ENT-xxxx",
            Padding = new Thickness(16, 7, 16, 7),
            Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        bottomPanel.Children.Add(btnCancelar);
        bottomPanel.Children.Add(btnConfirmar);
        bottomDock.Children.Add(bottomPanel);
        Grid.SetRow(bottomDock, 4);
        root.Children.Add(bottomDock);

        void CargarPartidasDeOc(OrdenCompra? oc)
        {
            if (oc == null)
            {
                txtInfoOc.Text = "Seleccione una Orden de Compra...";
                dg.ItemsSource = null;
                btnConfirmar.IsEnabled = false;
                return;
            }

            foreach (var p in oc.Partidas)
            {
                p.CantidadRecibirAhora = p.CantidadPendiente;
                if (p.ProductoId > 0 && prodsDict.TryGetValue(p.ProductoId, out var prodExist) && !string.IsNullOrWhiteSpace(prodExist.Ubicacion))
                {
                    p.UbicacionDestino = prodExist.Ubicacion;
                }
            }

            bool tienePendientes = oc.Partidas.Any(x => x.CantidadPendiente > 0m) && oc.Estatus != "Cancelada";
            btnConfirmar.IsEnabled = tienePendientes;

            if (!tienePendientes)
            {
                infoBorder.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
                infoBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                txtInfoOc.Text = $"{oc.Folio} ({oc.ProveedorNombre}) — Estatus: {oc.Estatus} ({oc.AvanceRecepcionTexto}). Esta Orden de Compra ya no tiene artículos pendientes por recibir. Puede consultar o reimprimir el documento con el botón inferior izquierdo.";
            }
            else
            {
                infoBorder.Background = Tk("ControlFillColorDefaultBrush");
                infoBorder.BorderBrush = Tk("ControlStrokeColorDefaultBrush");
                txtInfoOc.Text = $"Folio: {oc.Folio}  |  Proveedor: {oc.ProveedorNombre}  |  Destino: {oc.ProyectoNombre} ({oc.DepartamentoNombre})  |  Estatus: {oc.Estatus} ({oc.AvanceRecepcionTexto})";
            }

            dg.ItemsSource = null;
            dg.ItemsSource = oc.Partidas;
        }

        cboOcs.SelectionChanged += (_, _) => CargarPartidasDeOc(cboOcs.SelectedItem as OrdenCompra);

        btnRecibirTodo.Click += (_, _) =>
        {
            if (cboOcs.SelectedItem is OrdenCompra oc)
            {
                foreach (var p in oc.Partidas)
                    p.CantidadRecibirAhora = p.CantidadPendiente;
                dg.Items.Refresh();
            }
        };

        btnLimpiarCero.Click += (_, _) =>
        {
            if (cboOcs.SelectedItem is OrdenCompra oc)
            {
                foreach (var p in oc.Partidas)
                    p.CantidadRecibirAhora = 0m;
                dg.Items.Refresh();
            }
        };

        txtScanProdModal.KeyDown += (_, ev) =>
        {
            if (ev.Key == Key.Enter && cboOcs.SelectedItem is OrdenCompra oc)
            {
                string code = NormalizarCodigoEscaneado(txtScanProdModal.Text);
                txtScanProdModal.Clear();
                if (string.IsNullOrWhiteSpace(code)) return;

                var prod = DatabaseService.BuscarProductoPorCodigo(code);
                var part = oc.Partidas.FirstOrDefault(p =>
                    (prod != null && p.ProductoId == prod.Id) ||
                    CoincideFolioFlexible(p.ProductoCodigo, code) ||
                    p.ProductoNombre.Contains(code, StringComparison.OrdinalIgnoreCase));

                if (part != null && part.CantidadPendiente > 0m)
                {
                    if (!part.SeleccionadoRecibir)
                        part.CantidadRecibirAhora = 1m;
                    else
                        part.CantidadRecibirAhora = Math.Min(part.CantidadPendiente, part.CantidadRecibirAhora + 1m);
                    dg.SelectedItem = part;
                    dg.ScrollIntoView(part);
                    dg.Items.Refresh();
                }
                ev.Handled = true;
            }
        };

        btnSoloImprimirOc.Click += (_, _) =>
        {
            if (cboOcs.SelectedItem is OrdenCompra oc)
            {
                win.Close();
                BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, oc);
            }
        };

        btnCancelar.Click += (_, _) => win.Close();

        btnConfirmar.Click += (_, _) =>
        {
            dg.CommitEdit(DataGridEditingUnit.Cell, true);
            dg.CommitEdit(DataGridEditingUnit.Row, true);

            if (cboOcs.SelectedItem is not OrdenCompra ocSel)
                return;

            var aRecibir = ocSel.Partidas
                .Where(p => p.SeleccionadoRecibir && p.CantidadRecibirAhora > 0)
                .Select(p => (p, p.CantidadRecibirAhora))
                .ToList();

            if (aRecibir.Count == 0)
            {
                DialogoFluent.Show(win,
                    "Marque con ☑ al menos un artículo y verifique que su cantidad en '✏️ Recibir Hoy' sea mayor a 0.",
                    "Sin cantidades por recibir", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var conceptosAbiertos = aRecibir.Where(x => x.p.ProductoId <= 0).Select(x => x.p).ToList();
            if (conceptosAbiertos.Count > 0 && !ConfirmarNombresConceptosAbiertosEnRecepcion(ocSel, conceptosAbiertos))
                return;

            string rem = txtRemision.Text.Trim();
            string not = txtNotas.Text.Trim();
            string notasCompletas = !string.IsNullOrWhiteSpace(rem)
                ? (string.IsNullOrWhiteSpace(not) ? $"Remisión/Nota Proveedor: {rem}" : $"Remisión/Nota: {rem} | {not}")
                : not;

            bool reservarProy = chkReservar.IsChecked == true;
            var entrada = DatabaseService.RegistrarRecepcionOrdenCompra(ocSel, aRecibir, notasCompletas, reservarProy);

            // Actualizar ubicación en almacén si el almacenista la especificó en la tabla de recepción
            var prodsActualizados = DatabaseService.ObtenerProductos().ToDictionary(x => x.Id);
            foreach (var (partOc, _) in aRecibir)
            {
                string ubiElegida = ExtraerCodigoUbicacionLimpio(partOc.UbicacionDestino);
                if (partOc.ProductoId > 0 && !string.IsNullOrWhiteSpace(ubiElegida) &&
                    prodsActualizados.TryGetValue(partOc.ProductoId, out var prodDb) &&
                    !string.Equals(prodDb.Ubicacion, ubiElegida, StringComparison.OrdinalIgnoreCase))
                {
                    prodDb.Ubicacion = ubiElegida;
                    DatabaseService.GuardarProducto(prodDb);
                }
            }

            // Si se recibieron herramientas, abrir el modal de registro HER-xxxx
            prodsActualizados = DatabaseService.ObtenerProductos().ToDictionary(x => x.Id);
            var partidasHer = entrada.Partidas
                .Where(ep => prodsActualizados.TryGetValue(ep.ProductoId, out var pr) && (pr.EsHerramienta || pr.CategoriaId == 4))
                .ToList();
            foreach (var ep in partidasHer)
            {
                if (prodsActualizados.TryGetValue(ep.ProductoId, out var prodHer))
                {
                    int cantHer = Math.Max(1, (int)Math.Round(ep.CantidadIngresadaAlmacen));
                    MostrarModalRegistroHerramienta(prodHer, cantHer);
                }
            }

            CargarTodoElSistema();
            win.Close();
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, entrada);

            if (!reservarProy && entrada.Partidas.Count > 0)
            {
                var respSal = DialogoFluent.Show(this,
                    $"¿Desea cargar los artículos recién recibidos de {ocSel.Folio} ({entrada.Folio}) en 'Salida de Material' para entregarlos de una vez al área solicitante ({ocSel.DepartamentoNombre})?",
                    "Pasar a Salida de Material (SAL-xxxx)",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (respSal == System.Windows.MessageBoxResult.Yes)
                {
                    CargarPartidasDeEntradaEnSalidaPos(entrada, ocSel);
                    CambiarSeccion(5);
                }
            }
        };

        var inicial = ocPreseleccionada != null
            ? todasOcs.FirstOrDefault(o => o.Id == ocPreseleccionada.Id)
            : todasOcs.FirstOrDefault(o => o.Partidas.Any(p => p.CantidadPendiente > 0)) ?? todasOcs.FirstOrDefault();

        cboOcs.SelectedItem = inicial;
        CargarPartidasDeOc(inicial);

        win.Content = root;
        win.ShowDialog();
    }

    private void MostrarModalAccionEntradaAlmacen(EntradaAlmacen? entradaPreseleccionada)
    {
        var todasEntradas = DatabaseService.ObtenerEntradasAlmacen();
        if (todasEntradas.Count == 0)
        {
            DialogoFluent.Show(this,
                "Aún no existen Vales de Entrada (ENT-xxxx) registrados en el sistema.",
                "Sin entradas registradas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var ubicacionesCatalogo = ObtenerCatalogoUbicacionesDesplegable();

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Siguiente Acción del Vale de Entrada (ENT-xxxx): Acomodo en Anaquel o Salida de Material",
            Width = 960,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        topPanel.Children.Add(new TextBlock
        {
            Text = "Siguiente Acción para Vale de Entrada (ENT-xxxx): Acomodar Ubicación o Surtir en Salida (SAL-xxxx)",
            FontSize = 15.5,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("TextFillColorPrimaryBrush")
        });
        topPanel.Children.Add(new TextBlock
        {
            Text = "Seleccione qué artículos de esta entrada desea surtir en un Vale de Salida (SAL-xxxx) o actualice su coordenada de anaquel en almacén:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 2, 0, 8)
        });

        var selGrid = new Grid();
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selGrid.Children.Add(new TextBlock
        {
            Text = "Vale de Entrada (ENT-xxxx):",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var cboEntradas = new ComboBox { ItemsSource = todasEntradas };
        Grid.SetColumn(cboEntradas, 1);
        selGrid.Children.Add(cboEntradas);
        topPanel.Children.Add(selGrid);
        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        var infoBorder = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var txtInfoEnt = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("TextFillColorPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        infoBorder.Child = txtInfoEnt;
        Grid.SetRow(infoBorder, 1);
        root.Children.Add(infoBorder);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Background = Tk("ControlFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var colChk = new DataGridTemplateColumn { Header = "☑ Surtir", Width = 90 };
        var chkFactory = new FrameworkElementFactory(typeof(CheckBox));
        chkFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("SeleccionadoAccion")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        chkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        chkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        colChk.CellTemplate = new DataTemplate { VisualTree = chkFactory };
        dg.Columns.Add(colChk);

        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Código", Binding = new System.Windows.Data.Binding("ProductoCodigo"), IsReadOnly = true, Width = 90 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Artículo Ingresado", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Ingresó a Stock", Binding = new System.Windows.Data.Binding("CantidadIngresadaAlmacen") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 135 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadAlmacen"), IsReadOnly = true, Width = 75 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Cant. a Surtir", Binding = new System.Windows.Data.Binding("CantidadParaSalida") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, IsReadOnly = false, Width = 145 });

        var colUbi = new DataGridTemplateColumn { Header = "📍 Ubicación en Almacén (Anaquel)", Width = 250 };
        var cboUbiFactory = new FrameworkElementFactory(typeof(ComboBox));
        cboUbiFactory.SetValue(ComboBox.IsEditableProperty, true);
        cboUbiFactory.SetValue(ItemsControl.ItemsSourceProperty, ubicacionesCatalogo);
        cboUbiFactory.SetBinding(ComboBox.TextProperty, new System.Windows.Data.Binding("UbicacionAlmacen")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus
        });
        cboUbiFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        colUbi.CellTemplate = new DataTemplate { VisualTree = cboUbiFactory };
        dg.Columns.Add(colUbi);

        Grid.SetRow(dg, 2);
        root.Children.Add(dg);

        var bottomDock = new DockPanel();
        var btnSoloImprimirEnt = new System.Windows.Controls.Button
        {
            Content = "Solo Ver / Reimprimir Vale ENT-xxxx",
            Padding = new Thickness(12, 7, 12, 7),
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(btnSoloImprimirEnt, Dock.Left);
        bottomDock.Children.Add(btnSoloImprimirEnt);

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancelar = new System.Windows.Controls.Button
        {
            Content = "Cancelar",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        var btnGuardarUbicaciones = new System.Windows.Controls.Button
        {
            Content = "📍 Guardar Ubicaciones en Anaquel",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        var btnPasarASalida = new System.Windows.Controls.Button
        {
            Content = "Cargar Seleccionados (☑) en Salida de Material (SAL-xxxx)",
            Padding = new Thickness(16, 7, 16, 7),
            Background = Tk("AccentFillColorDefaultBrush"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        rightButtons.Children.Add(btnCancelar);
        rightButtons.Children.Add(btnGuardarUbicaciones);
        rightButtons.Children.Add(btnPasarASalida);
        bottomDock.Children.Add(rightButtons);
        Grid.SetRow(bottomDock, 3);
        root.Children.Add(bottomDock);

        void CargarPartidasDeEntrada(EntradaAlmacen? ent)
        {
            if (ent == null)
            {
                txtInfoEnt.Text = "Seleccione un vale de entrada...";
                dg.ItemsSource = null;
                return;
            }

            foreach (var p in ent.Partidas)
            {
                p.CantidadParaSalida = p.CantidadIngresadaAlmacen;
                p.SeleccionadoAccion = true;
                if (prodsDict.TryGetValue(p.ProductoId, out var prodObj))
                {
                    p.UbicacionAlmacen = prodObj.Ubicacion;
                }
            }

            txtInfoEnt.Text = $"Folio: {ent.Folio}  |  Fecha: {ent.Fecha}  |  Proveedor: {ent.ProveedorNombre}  |  OC Origen: {(string.IsNullOrWhiteSpace(ent.OrdenCompraFolio) ? "Directa" : ent.OrdenCompraFolio)}  |  Proyecto: {ent.ProyectoNombre}";
            dg.ItemsSource = null;
            dg.ItemsSource = ent.Partidas;
        }

        void GuardarUbicacionesDePartidas(EntradaAlmacen ent)
        {
            dg.CommitEdit(DataGridEditingUnit.Cell, true);
            dg.CommitEdit(DataGridEditingUnit.Row, true);
            var dictActual = DatabaseService.ObtenerProductos().ToDictionary(x => x.Id);
            foreach (var ep in ent.Partidas)
            {
                string ubiLimpia = ExtraerCodigoUbicacionLimpio(ep.UbicacionAlmacen);
                if (ep.ProductoId > 0 && !string.IsNullOrWhiteSpace(ubiLimpia) &&
                    dictActual.TryGetValue(ep.ProductoId, out var prodDb) &&
                    !string.Equals(prodDb.Ubicacion, ubiLimpia, StringComparison.OrdinalIgnoreCase))
                {
                    prodDb.Ubicacion = ubiLimpia;
                    DatabaseService.GuardarProducto(prodDb);
                }
            }
        }

        cboEntradas.SelectionChanged += (_, _) => CargarPartidasDeEntrada(cboEntradas.SelectedItem as EntradaAlmacen);

        btnSoloImprimirEnt.Click += (_, _) =>
        {
            if (cboEntradas.SelectedItem is EntradaAlmacen ent)
            {
                win.Close();
                BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent);
            }
        };

        btnCancelar.Click += (_, _) => win.Close();

        btnGuardarUbicaciones.Click += (_, _) =>
        {
            if (cboEntradas.SelectedItem is not EntradaAlmacen ent) return;
            GuardarUbicacionesDePartidas(ent);
            CargarTodoElSistema();
            DialogoFluent.Show(win, "Las coordenadas de almacén de los artículos de esta entrada se actualizaron correctamente.", "Ubicaciones Guardadas", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        btnPasarASalida.Click += (_, _) =>
        {
            if (cboEntradas.SelectedItem is not EntradaAlmacen ent) return;
            GuardarUbicacionesDePartidas(ent);

            var seleccionadas = ent.Partidas.Where(x => x.SeleccionadoAccion && x.CantidadParaSalida > 0).ToList();
            if (seleccionadas.Count == 0)
            {
                DialogoFluent.Show(win, "Marque con ☑ al menos un artículo para cargarlo en el Vale de Salida.", "Sin artículos seleccionados", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CargarTodoElSistema();
            _partidasSalidaPos.Clear();
            var dictProds = _cacheProductos.ToDictionary(x => x.Id);
            foreach (var ep in seleccionadas)
            {
                if (dictProds.TryGetValue(ep.ProductoId, out var prod))
                {
                    AgregarProductoAValeSalidaPos(prod, ep.CantidadParaSalida);
                }
            }

            if (ent.OrdenCompraId.HasValue)
            {
                var ocOrigen = DatabaseService.ObtenerOrdenesCompra().FirstOrDefault(o => o.Id == ent.OrdenCompraId.Value);
                if (ocOrigen != null)
                {
                    if (CboSalDepartamento != null && !string.IsNullOrWhiteSpace(ocOrigen.DepartamentoNombre))
                    {
                        var dep = CboSalDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Nombre.Equals(ocOrigen.DepartamentoNombre, StringComparison.OrdinalIgnoreCase));
                        if (dep != null) CboSalDepartamento.SelectedItem = dep;
                    }
                    if (CboSalProyecto != null && ocOrigen.ProyectoId > 0)
                    {
                        var proy = CboSalProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == ocOrigen.ProyectoId);
                        if (proy != null) CboSalProyecto.SelectedItem = proy;
                    }
                }
            }
            if (TxtSalNotas != null)
            {
                TxtSalNotas.Text = $"Surtido desde Vale de Entrada {ent.Folio}";
            }

            win.Close();
            CambiarSeccion(5);
        };

        var inicial = entradaPreseleccionada != null
            ? todasEntradas.FirstOrDefault(x => x.Id == entradaPreseleccionada.Id)
            : todasEntradas.FirstOrDefault();

        cboEntradas.SelectedItem = inicial;
        CargarPartidasDeEntrada(inicial);

        win.Content = root;
        win.ShowDialog();
    }

    private void MostrarModalDevolucionPrestamoHerramienta(PrestamoHerramienta? prestamoPreseleccionado, string? codigoHerEscaneado = null)
    {
        var todosPrestamos = DatabaseService.ObtenerPrestamosHerramienta();
        if (todosPrestamos.Count == 0)
        {
            DialogoFluent.Show(this,
                "Aún no existen Vales de Préstamo de Herramienta (PRE-xxxx) registrados en el sistema.",
                "Sin préstamos registrados", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string estadoGlobalUI = (CboEstadoRetornoHerramienta?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bueno (Completo)";
        string obsGlobalUI = TxtObsRetornoHerramienta?.Text?.Trim() ?? string.Empty;

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Devolución Agrupada de Préstamo de Herramientas (PRE-xxxx)",
            Width = 940,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        topPanel.Children.Add(new TextBlock
        {
            Text = "Devolución Agrupada de Herramientas por Vale de Resguardo (PRE-xxxx)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("TextFillColorPrimaryBrush")
        });
        topPanel.Children.Add(new TextBlock
        {
            Text = "Marque con ☑ las herramientas que SÍ regresaron hoy y desmarque (☐) las que NO llegaron (seguirán pendientes en resguardo). Puede indicar el estado individual de cada pieza:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var selGrid = new Grid();
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        selGrid.Children.Add(new TextBlock
        {
            Text = "Vale de Préstamo (PRE-xxxx):",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        var cboPrestamos = new ComboBox
        {
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = todosPrestamos
        };
        Grid.SetColumn(cboPrestamos, 1);
        selGrid.Children.Add(cboPrestamos);

        var btnMarcarTodas = new System.Windows.Controls.Button
        {
            Content = "☑ Llegaron Todas (Bueno)",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnMarcarTodas, 2);
        selGrid.Children.Add(btnMarcarTodas);

        var btnDesmarcarTodas = new System.Windows.Controls.Button
        {
            Content = "☐ Desmarcar Todas",
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnDesmarcarTodas, 3);
        selGrid.Children.Add(btnDesmarcarTodas);

        topPanel.Children.Add(selGrid);
        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        var infoBorder = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var infoStack = new StackPanel();
        var txtInfoPrestamo = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("TextFillColorPrimaryBrush"),
            Text = "Seleccione un vale de préstamo..."
        };
        infoStack.Children.Add(txtInfoPrestamo);

        var scanRowPre = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        scanRowPre.Children.Add(new TextBlock
        {
            Text = "Escanear Código Individual (HER-xxxx) para marcar ☑:",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("AccentFillColorDefaultBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var txtScanHerModal = new System.Windows.Controls.TextBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = "Escanee el código HER-xxxx de la herramienta devuelta para marcarla automáticamente"
        };
        scanRowPre.Children.Add(txtScanHerModal);
        infoStack.Children.Add(scanRowPre);

        infoBorder.Child = infoStack;
        Grid.SetRow(infoBorder, 1);
        root.Children.Add(infoBorder);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Background = Tk("ControlFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var colCheck = new DataGridTemplateColumn { Header = "☑ ¿Llegó Hoy?", Width = 120 };
        var chkFactory = new FrameworkElementFactory(typeof(CheckBox));
        chkFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("SeleccionadoDevolver")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        chkFactory.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("EstaPendienteDevolucion"));
        chkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        chkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        colCheck.CellTemplate = new DataTemplate { VisualTree = chkFactory };
        dg.Columns.Add(colCheck);

        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Código HER", Binding = new System.Windows.Data.Binding("CodigoIndividual"), IsReadOnly = true, Width = 100 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Herramienta", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Marca / Modelo", Binding = new System.Windows.Data.Binding("MarcaModelo"), IsReadOnly = true, Width = 135 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Estatus Actual", Binding = new System.Windows.Data.Binding("EstadoDevolucion"), IsReadOnly = true, Width = 125 });

        var estadosRetorno = new[] { "Bueno (Completo)", "Incompleto / Falta Pieza", "Dañado", "Extraviado" };
        var colEstado = new DataGridTemplateColumn { Header = "Estado en que Regresa", Width = 175 };
        var cboEstFactory = new FrameworkElementFactory(typeof(ComboBox));
        cboEstFactory.SetValue(ItemsControl.ItemsSourceProperty, estadosRetorno);
        cboEstFactory.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new System.Windows.Data.Binding("EstadoRetornoSeleccionado")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        cboEstFactory.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("EstaPendienteDevolucion"));
        cboEstFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        colEstado.CellTemplate = new DataTemplate { VisualTree = cboEstFactory };
        dg.Columns.Add(colEstado);

        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "Observaciones / Incidencia",
            Binding = new System.Windows.Data.Binding("ObservacionesRetorno") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            IsReadOnly = false,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        Grid.SetRow(dg, 2);
        root.Children.Add(dg);

        var obsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        obsStack.Children.Add(new TextBlock
        {
            Text = "Nota general de recepción (se aplicará a las herramientas que no tengan observación individual):",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var txtObsGeneral = new System.Windows.Controls.TextBox
        {
            Padding = new Thickness(8, 6, 8, 6),
            Text = !string.IsNullOrWhiteSpace(obsGlobalUI) ? obsGlobalUI : "Devolución recibida en almacén."
        };
        obsStack.Children.Add(txtObsGeneral);
        Grid.SetRow(obsStack, 3);
        root.Children.Add(obsStack);

        var bottomDock = new DockPanel();
        var btnSoloImprimir = new System.Windows.Controls.Button
        {
            Content = "Solo Ver / Reimprimir Vale",
            Padding = new Thickness(12, 7, 12, 7),
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(btnSoloImprimir, Dock.Left);
        bottomDock.Children.Add(btnSoloImprimir);

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
            Content = "Registrar Devolución de Seleccionadas (☑) e Imprimir",
            Padding = new Thickness(16, 7, 16, 7),
            Background = Tk("AccentFillColorDefaultBrush"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        bottomPanel.Children.Add(btnCancelar);
        bottomPanel.Children.Add(btnConfirmar);
        bottomDock.Children.Add(bottomPanel);
        Grid.SetRow(bottomDock, 4);
        root.Children.Add(bottomDock);

        void CargarPartidasDePrestamo(PrestamoHerramienta? pre)
        {
            if (pre == null)
            {
                txtInfoPrestamo.Text = "Seleccione un vale de préstamo...";
                dg.ItemsSource = null;
                return;
            }

            foreach (var p in pre.Partidas)
            {
                if (p.EstaPendienteDevolucion)
                {
                    p.SeleccionadoDevolver = string.IsNullOrWhiteSpace(codigoHerEscaneado) ||
                                             CoincideFolioFlexible(p.CodigoIndividual, codigoHerEscaneado);
                    p.EstadoRetornoSeleccionado = estadoGlobalUI;
                    p.ObservacionesRetorno = obsGlobalUI;
                }
                else
                {
                    p.SeleccionadoDevolver = false;
                    p.EstadoRetornoSeleccionado = p.EstadoDevolucion;
                    p.ObservacionesRetorno = p.Observaciones;
                }
            }

            bool tienePendientes = pre.Partidas.Any(p => p.EstaPendienteDevolucion);
            btnConfirmar.IsEnabled = tienePendientes;
            btnMarcarTodas.IsEnabled = tienePendientes;

            string avisoPreCompleto = tienePendientes
                ? ""
                : "  ⚠️ [100% DEVUELTO - Use '🖨️ Solo Ver / Reimprimir Vale' para consultar]";
            txtInfoPrestamo.Text = $"Folio: {pre.Folio}  |  Recibió: {pre.PersonaRecibeEfectiva} ({pre.DepartamentoNombre})  |  Proyecto: {pre.ProyectoNombre}  |  Fecha Compromiso: {pre.FechaCompromiso}  |  Pendientes: {pre.PendientesDevolver} de {pre.TotalHerramientas}{avisoPreCompleto}";
            dg.ItemsSource = null;
            dg.ItemsSource = pre.Partidas;
        }

        txtScanHerModal.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            string scan = NormalizarCodigoEscaneado(txtScanHerModal.Text);
            txtScanHerModal.Clear();
            if (string.IsNullOrWhiteSpace(scan) || cboPrestamos.SelectedItem is not PrestamoHerramienta pre) return;

            var match = pre.Partidas.FirstOrDefault(p =>
                CoincideFolioFlexible(p.CodigoIndividual, scan) ||
                p.ProductoNombre.Contains(scan, StringComparison.OrdinalIgnoreCase));

            if (match != null && match.EstaPendienteDevolucion)
            {
                match.SeleccionadoDevolver = true;
                dg.SelectedItem = match;
                dg.ScrollIntoView(match);
                dg.Items.Refresh();
            }
            e.Handled = true;
        };

        cboPrestamos.SelectionChanged += (_, _) =>
        {
            codigoHerEscaneado = null;
            CargarPartidasDePrestamo(cboPrestamos.SelectedItem as PrestamoHerramienta);
        };

        btnMarcarTodas.Click += (_, _) =>
        {
            if (cboPrestamos.SelectedItem is PrestamoHerramienta pre)
            {
                foreach (var p in pre.Partidas.Where(x => x.EstaPendienteDevolucion))
                {
                    p.SeleccionadoDevolver = true;
                    p.EstadoRetornoSeleccionado = "Bueno (Completo)";
                }
                dg.Items.Refresh();
            }
        };

        btnDesmarcarTodas.Click += (_, _) =>
        {
            if (cboPrestamos.SelectedItem is PrestamoHerramienta pre)
            {
                foreach (var p in pre.Partidas)
                    p.SeleccionadoDevolver = false;
                dg.Items.Refresh();
            }
        };

        btnSoloImprimir.Click += (_, _) =>
        {
            if (cboPrestamos.SelectedItem is PrestamoHerramienta pre)
            {
                win.Close();
                BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, pre);
            }
        };

        btnCancelar.Click += (_, _) => win.Close();

        btnConfirmar.Click += (_, _) =>
        {
            dg.CommitEdit(DataGridEditingUnit.Cell, true);
            dg.CommitEdit(DataGridEditingUnit.Row, true);

            if (cboPrestamos.SelectedItem is not PrestamoHerramienta preSel)
            {
                DialogoFluent.Show(win, "Seleccione un Vale de Préstamo (PRE-xxxx).", "Falta vale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var seleccionadas = preSel.Partidas
                .Where(p => p.EstaPendienteDevolucion && p.SeleccionadoDevolver)
                .ToList();

            if (seleccionadas.Count == 0)
            {
                DialogoFluent.Show(win,
                    "Marque con ☑ al menos una herramienta pendiente que haya regresado hoy (o use '🖨️ Solo Ver / Reimprimir Vale' si solo desea consultar el documento).",
                    "Sin herramientas marcadas", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string obsGen = txtObsGeneral.Text.Trim();
            foreach (var part in seleccionadas)
            {
                string obsFinal = !string.IsNullOrWhiteSpace(part.ObservacionesRetorno) ? part.ObservacionesRetorno.Trim() : obsGen;
                DatabaseService.DevolverHerramientaPartida(part.Id, part.HerramientaActivoId, preSel.Id, part.EstadoRetornoSeleccionado, obsFinal);
            }

            TxtObsRetornoHerramienta?.Clear();
            CargarTodoElSistema();
            win.Close();

            var prestamoActualizado = DatabaseService.ObtenerPrestamosHerramienta().FirstOrDefault(p => p.Id == preSel.Id);
            if (prestamoActualizado != null)
            {
                BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, prestamoActualizado);
            }
        };

        var inicial = prestamoPreseleccionado != null
            ? todosPrestamos.FirstOrDefault(p => p.Id == prestamoPreseleccionado.Id)
            : todosPrestamos.FirstOrDefault(p => p.PendientesDevolver > 0) ?? todosPrestamos.FirstOrDefault();

        cboPrestamos.SelectedItem = inicial;
        CargarPartidasDePrestamo(inicial);

        win.Content = root;
        win.ShowDialog();
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
            DialogoFluent.Show(this,
                "Seleccione primero una herramienta o préstamo de la tabla para abrir e imprimir su Hoja de Préstamo / Devolución.",
                "Seleccione un préstamo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnDescargarValePrestamo_Click(object sender, RoutedEventArgs e)
    {
        if (DgPrestamosHerramientas.SelectedItem is PrestamoVistaFila fila)
        {
            var actualizado = DatabaseService.ObtenerPrestamosHerramienta().FirstOrDefault(p => p.Id == fila.PrestamoId) ?? fila.PrestamoOriginal;
            BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, actualizado, soloDescargar: true);
        }
        else
        {
            DialogoFluent.Show(this,
                "Seleccione primero una herramienta o préstamo de la tabla para descargar su Hoja de Préstamo / Devolución (PDF, PNG o Excel CSV).",
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
            DialogoFluent.Show(this,
                "Aún no existen Vales de Salida de Material (SAL-xxxx) registrados en el sistema.",
                "Sin salidas registradas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = "Devolución / Reingreso de Material a Almacén (Completa o Parcial)",
            Width = 860,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush")
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
            Foreground = Tk("TextFillColorPrimaryBrush")
        });
        topPanel.Children.Add(new TextBlock
        {
            Text = "Seleccione el Vale de Salida (SAL-xxxx), indique la cantidad devuelta por partida (parcial o completa) y genere el Comprobante Imprimible de Devolución sin precios.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
            Content = "Devolver Todo (Completo)",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnDevolverTodo, 2);
        selGrid.Children.Add(btnDevolverTodo);

        var btnLimpiarCant = new System.Windows.Controls.Button
        {
            Content = "Poner en 0",
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
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var infoStackSal = new StackPanel();
        var txtInfoSalida = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("TextFillColorPrimaryBrush"),
            Text = "Seleccione un vale de salida..."
        };
        infoStackSal.Children.Add(txtInfoSalida);

        var scanRowSal = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        scanRowSal.Children.Add(new TextBlock
        {
            Text = "Escanear Código de Producto (PRD-xxxx / EAN) para marcar ☑:",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("AccentFillColorDefaultBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var txtScanSalModal = new System.Windows.Controls.TextBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = "Escanee un artículo dentro de este vale para marcarlo y enfocar su fila"
        };
        scanRowSal.Children.Add(txtScanSalModal);
        infoStackSal.Children.Add(scanRowSal);

        infoBorder.Child = infoStackSal;
        Grid.SetRow(infoBorder, 1);
        root.Children.Add(infoBorder);

        // Tabla editable de partidas a devolver
        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Background = Tk("ControlFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var colCheckMat = new DataGridTemplateColumn { Header = "☑ ¿Regresó?", Width = 110 };
        var chkMatFactory = new FrameworkElementFactory(typeof(CheckBox));
        chkMatFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("SeleccionadoDevolver")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        chkMatFactory.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("TienePendientePorDevolver"));
        chkMatFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        chkMatFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        colCheckMat.CellTemplate = new DataTemplate { VisualTree = chkMatFactory };
        dg.Columns.Add(colCheckMat);

        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Código", Binding = new System.Windows.Data.Binding("ProductoCodigo"), IsReadOnly = true, Width = 85 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Material / Artículo", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadAlmacen"), IsReadOnly = true, Width = 75 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Entregado", Binding = new System.Windows.Data.Binding("CantidadEntregada") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 95 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Ya Devuelto", Binding = new System.Windows.Data.Binding("CantidadDevuelta") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 110 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Pendiente Máx.", Binding = new System.Windows.Data.Binding("CantidadPendienteDevolver") { StringFormat = "{0:0.##}" }, IsReadOnly = true, Width = 125 });
        dg.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Cuánto Regresó Hoy", Binding = new System.Windows.Data.Binding("CantidadDevolverAhora") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, IsReadOnly = false, Width = 175 });

        Grid.SetRow(dg, 2);
        root.Children.Add(dg);

        // Observaciones
        var obsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        obsStack.Children.Add(new TextBlock
        {
            Text = "Motivo / Observaciones de la Devolución:",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("TextFillColorSecondaryBrush"),
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
        var bottomDock = new DockPanel();
        var btnSoloReimprimir = new System.Windows.Controls.Button
        {
            Content = "Solo Ver / Reimprimir Vale Original",
            Padding = new Thickness(12, 7, 12, 7),
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(btnSoloReimprimir, Dock.Left);
        bottomDock.Children.Add(btnSoloReimprimir);

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
            Content = "Registrar Devolución e Imprimir Comprobante",
            Padding = new Thickness(16, 7, 16, 7),
            Background = Tk("AccentFillColorDefaultBrush"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        bottomPanel.Children.Add(btnCancelar);
        bottomPanel.Children.Add(btnConfirmar);
        bottomDock.Children.Add(bottomPanel);
        Grid.SetRow(bottomDock, 4);
        root.Children.Add(bottomDock);

        btnSoloReimprimir.Click += (_, _) =>
        {
            if (cboSalidas.SelectedItem is SalidaAlmacen sal)
            {
                win.Close();
                BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, sal);
            }
        };

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
            bool tienePendientes = sal.Partidas.Any(p => p.TienePendientePorDevolver);
            btnConfirmar.IsEnabled = tienePendientes;
            btnDevolverTodo.IsEnabled = tienePendientes;

            string avisoSalCompleta = tienePendientes
                ? ""
                : "  ⚠️ [100% DEVUELTO - Use '🖨️ Solo Ver / Reimprimir Vale Original' para consultar]";
            txtInfoSalida.Text = $"Folio: {sal.Folio}  |  Recibió: {sal.PersonaRecibeEfectiva} ({sal.DepartamentoNombre})  |  Proyecto: {sal.ProyectoNombre}  |  Estado actual: {sal.EstatusDevolucionTexto}{avisoSalCompleta}";
            dg.ItemsSource = null;
            dg.ItemsSource = sal.Partidas;
        }

        txtScanSalModal.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            string scan = NormalizarCodigoEscaneado(txtScanSalModal.Text);
            txtScanSalModal.Clear();
            if (string.IsNullOrWhiteSpace(scan) || cboSalidas.SelectedItem is not SalidaAlmacen sal) return;

            var match = sal.Partidas.FirstOrDefault(p => CoincideFolioFlexible(p.ProductoCodigo, scan));
            if (match == null)
            {
                var prod = DatabaseService.BuscarProductoPorCodigo(scan);
                if (prod != null)
                    match = sal.Partidas.FirstOrDefault(p => p.ProductoId == prod.Id);
            }

            if (match != null && match.TienePendientePorDevolver)
            {
                match.SeleccionadoDevolver = true;
                dg.SelectedItem = match;
                dg.ScrollIntoView(match);
                dg.Items.Refresh();
            }
            e.Handled = true;
        };

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
                DialogoFluent.Show(win, "Seleccione un Vale de Salida (SAL-xxxx).", "Falta vale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var devoluciones = salSel.Partidas
                .Where(p => p.CantidadDevolverAhora > 0)
                .Select(p => (p.Id, p.ProductoId, Math.Min(p.CantidadDevolverAhora, p.CantidadPendienteDevolver)))
                .Where(x => x.Item3 > 0)
                .ToList();

            if (devoluciones.Count == 0)
            {
                DialogoFluent.Show(win,
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
                DialogoFluent.Show(win, ex.Message, "Error al registrar devolución", MessageBoxButton.OK, MessageBoxImage.Error);
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
        MostrarModalRegistroHerramienta(null, 1);
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

                    // Buscar si ya existe una OC emitida o recibida con remisión de ese mismo proveedor que aún no tenga CFDI XML fiscal
                    if (CboXmlOrdenCompra.ItemsSource is IEnumerable<OrdenCompra> listaOcs)
                    {
                        var idsConFacturaFiscal = DatabaseService.ObtenerFacturasXml()
                            .Where(f => f.OrdenCompraId.HasValue && !f.EsNotaSinFactura)
                            .Select(f => f.OrdenCompraId!.Value)
                            .ToHashSet();

                        var ocMismoProv = listaOcs.FirstOrDefault(o =>
                            o.Id > 0 &&
                            o.ProveedorRfc.Equals(_facturaXmlEnPantalla.EmisorRfc, StringComparison.OrdinalIgnoreCase) &&
                            o.Estatus != "Cancelada" &&
                            !idsConFacturaFiscal.Contains(o.Id));

                        CboXmlOrdenCompra.SelectedItem = ocMismoProv ?? listaOcs.FirstOrDefault(o => o.Id == 0);
                        if (ocMismoProv != null && CboXmlProyecto.ItemsSource is IEnumerable<Proyecto> proysOc)
                        {
                            var proyOc = proysOc.FirstOrDefault(p => p.Id == ocMismoProv.ProyectoId);
                            if (proyOc != null) CboXmlProyecto.SelectedItem = proyOc;
                        }
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
            DialogoFluent.Show(this, $"Error al leer el archivo XML:\n{ex.Message}", "Error XML", MessageBoxButton.OK, MessageBoxImage.Error);
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
            DialogoFluent.Show(this, "Seleccione un concepto en la tabla y el producto de su catálogo con el que desea relacionarlo.", "Vincular concepto", MessageBoxButton.OK, MessageBoxImage.Information);
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

        DialogoFluent.Show(this, "Cargue un archivo XML o seleccione una factura registrada en la tabla inferior.", "Vincular concepto", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnMarcarConceptoComoNuevo_Click(object sender, RoutedEventArgs e)
    {
        if (DgXmlConceptosActual.SelectedItem is not FacturaConcepto concepto)
        {
            DialogoFluent.Show(this, "Seleccione un concepto en la tabla para marcarlo como producto nuevo.", "Seleccione concepto", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, "Primero cargue una factura XML o seleccione una factura registrada.", "Sin factura", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DialogoFluent.Show(this, "Todos los conceptos ya están vinculados a productos registrados en el catálogo.\nSi desea crear uno nuevo a partir de un concepto ya vinculado, selecciónelo y haga clic en 'Marcar como Nuevo'.", "Productos ya registrados", MessageBoxButton.OK, MessageBoxImage.Information);
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
        DialogoFluent.Show(this,
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

            var resp = DialogoFluent.Show(this,
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
            var resp = DialogoFluent.Show(this,
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

        DialogoFluent.Show(this,
            "Seleccione una factura en la tabla 'Facturas y Notas Registradas' para eliminarla.",
            "Factura no seleccionada",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void BtnGuardarFacturaXml_Click(object sender, RoutedEventArgs e)
    {
        if (_facturaXmlEnPantalla == null)
        {
            DialogoFluent.Show(this, "Primero seleccione un archivo .xml o cargue el XML Demo.", "Sin factura cargada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DatabaseService.ObtenerFacturasXml().Any(f => f.Uuid.Equals(_facturaXmlEnPantalla.Uuid, StringComparison.OrdinalIgnoreCase)))
        {
            DialogoFluent.Show(this, $"La factura con UUID {_facturaXmlEnPantalla.Uuid} ya se encuentra registrada en el sistema.", "Factura duplicada", MessageBoxButton.OK, MessageBoxImage.Information);
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
        bool ingresarAuto = ChkXmlIngresarAlmacenAuto?.IsChecked == true;

        await EjecutarConIndicadorCargaAsync("Guardando factura XML, orden de compra y productos...", () =>
        {
            int productosAntes = DatabaseService.ObtenerProductos().Count;
            facturaGuardada = DatabaseService.GuardarFacturaXml(_facturaXmlEnPantalla, ingresarAuto);
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
            DialogoFluent.Show(this,
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
            DialogoFluent.Show(this, "Seleccione un producto del catálogo para agregarlo a la nota.", "Producto requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        decimal cant = ParseDecimal(TxtSinFacCantidad.Text, 1m);
        decimal precio = ParseDecimal(TxtSinFacPrecioUnitario.Text, 0m);
        if (cant <= 0 || precio <= 0)
        {
            DialogoFluent.Show(this, "Capture una cantidad y un precio unitario mayores a 0.", "Datos requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Seleccione o escriba el nombre del Proveedor.", "Proveedor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CboSinFacProyecto.SelectedItem is not Proyecto proy)
        {
            DialogoFluent.Show(this, "Seleccione el Proyecto destino.", "Proyecto requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_partidasCompraSinFactura.Count == 0)
        {
            DialogoFluent.Show(this, "Agregue al menos un artículo a la nota de compra.", "Sin partidas", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // 4. Ranking General de Materiales Concurrentes y Proyección de Compras
        if (DgRankingMaterialesAnalisis != null)
        {
            DgRankingMaterialesAnalisis.ItemsSource = DatabaseService.ObtenerRankingMaterialesConcurrentes();
        }

        RenderizarGraficasProductoAnalisis();
    }

    private void DgRankingMaterialesAnalisis_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoProductoAnalisis) return;
        if (DgRankingMaterialesAnalisis.SelectedItem is MaterialRankingAnalisis item && item.ProductoId > 0 && CboProductoAnalisis != null)
        {
            _sincronizandoProductoAnalisis = true;
            try
            {
                if (CboProductoAnalisis.ItemsSource is IEnumerable<Producto> items)
                {
                    var match = items.FirstOrDefault(p => p.Id == item.ProductoId);
                    if (match != null)
                    {
                        CboProductoAnalisis.SelectedItem = match;
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

    private void BtnEnviarRankingASolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (DgRankingMaterialesAnalisis?.SelectedItem is not MaterialRankingAnalisis item)
        {
            DialogoFluent.Show(this, "Seleccione un artículo en la tabla de Ranking de Materiales para enviarlo a una Solicitud / Cotización.", "Seleccione un artículo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        decimal cantSugerida = item.SugerenciaCompra > 0 ? item.SugerenciaCompra : 1m;
        if (item.ProductoId > 0)
        {
            var prod = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == item.ProductoId);
            if (prod != null)
            {
                AgregarProductoAPartidasSolicitud(prod, cantSugerida, $"Reabastecimiento / Recotización sugerida ({item.VigenciaPrecioTexto})");
            }
        }
        else
        {
            AgregarConceptoLibreAPartidasSolicitud(item.ProductoNombre, cantSugerida, $"Recotización de concepto recurrente ({item.VigenciaPrecioTexto})");
        }

        CambiarSeccion(2);
        DialogoFluent.Show(this,
            $"Se agregó '{item.ProductoNombre}' (Cantidad sugerida: {cantSugerida:0.##} {item.UnidadAlmacen}) al editor de Solicitudes para cotizar o abastecer.",
            "Agregado a Solicitud", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Foreground = Tk("TextFillColorSecondaryBrush")
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
        var ejeY = new Line { X1 = padLeft, Y1 = padTop, X2 = padLeft, Y2 = padTop + plotH, Stroke = Tk("ControlStrokeColorDefaultBrush"), StrokeThickness = 1.2 };
        var ejeX = new Line { X1 = padLeft, Y1 = padTop + plotH, X2 = padLeft + plotW, Y2 = padTop + plotH, Stroke = Tk("ControlStrokeColorDefaultBrush"), StrokeThickness = 1.2 };
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
                Stroke = Tk("ControlFillColorSecondaryBrush"),
                StrokeThickness = 1
            };
            canvas.Children.Add(gridLine);

            var lblY = new TextBlock
            {
                Text = $"${val:N0}",
                FontSize = 10,
                Foreground = Tk("TextFillColorSecondaryBrush")
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
                    Foreground = Tk("TextFillColorSecondaryBrush")
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
            Stroke = Tk("ControlStrokeColorDefaultBrush"),
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
                Foreground = Tk("TextFillColorPrimaryBrush")
            };
            Canvas.SetLeft(lblVal, x + Math.Max(-8, (barW - 44) / 2.0));
            Canvas.SetTop(lblVal, Math.Max(2, y - 16));
            canvas.Children.Add(lblVal);

            string etqCorta = b.etiqueta.Length > 13 ? b.etiqueta[..12] + "…" : b.etiqueta;
            var lblEtq = new TextBlock
            {
                Text = etqCorta,
                FontSize = 9.5,
                Foreground = Tk("TextFillColorSecondaryBrush"),
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

        var cots = DatabaseService.ObtenerCotizacionesBancoHistorico(proveedorId: p.Id);
        foreach (var c in cots)
        {
            string tipoCot = c.EsLineaAbierta ? "Cotización (Línea Abierta)" : $"Cotización [{c.ProductoCodigo}]";
            string notaExtra = !string.IsNullOrWhiteSpace(c.NotasCotizacion) ? $" · {c.NotasCotizacion}" : "";
            desglose.Add(new
            {
                Tipo = tipoCot,
                Folio = c.OrigenFolio,
                Fecha = c.FechaCotizacion,
                Detalle = $"{c.DescripcionConcepto} ({c.DiasEntrega}d ent.){notaExtra}",
                Estatus = c.EstadoVigenciaCorto,
                Monto = c.PrecioSinIva,
                CotizacionObj = c
            });
        }
        int vigCount = cots.Count(x => x.EstaVigente);
        TxtDesgloseProveedorTitulo.Text = $"Historial de {p.NombreMostrar} — {cots.Count} Cotizaciones ({vigCount} vigentes) | {ocs.Count} OC ({ocs.Sum(x => x.TotalConIva):C2}) | {cfdis.Count} Facturas CFDI";
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
        if (string.IsNullOrWhiteSpace(TxtProvComercial.Text) && string.IsNullOrWhiteSpace(TxtProvRazon.Text))
        {
            DialogoFluent.Show(this, "Escriba al menos el Nombre Comercial del proveedor (ej. 'Home Depot', 'FerreIndustrial', 'Grainger').", "Nombre Comercial requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string nomComercial = !string.IsNullOrWhiteSpace(TxtProvComercial.Text) ? TxtProvComercial.Text.Trim() : Proveedor.ExtraerNombreComercialCorto(TxtProvRazon.Text.Trim());
        string razonSocial = !string.IsNullOrWhiteSpace(TxtProvRazon.Text) ? TxtProvRazon.Text.Trim() : nomComercial;
        string rfcProv = !string.IsNullOrWhiteSpace(TxtProvRfc.Text) ? TxtProvRfc.Text.Trim().ToUpperInvariant() : "XAXX010101000";
        DatabaseService.GuardarProveedor(new Proveedor
        {
            Id = _proveedorEditandoId,


            NombreComercial = nomComercial,
            RazonSocial = razonSocial,
            Rfc = rfcProv,
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
            DialogoFluent.Show(this, "Seleccione un proveedor de la tabla para eliminarlo.", "Proveedor no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar proveedor '{TxtProvComercial.Text}'"))
            return;

        if (DialogoFluent.Show(this, "¿Desea eliminar este proveedor del directorio?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarProveedor(_proveedorEditandoId);
            BtnNuevoProveedor_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se puede eliminar este proveedor porque tiene documentos asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Escriba el código y nombre del proyecto.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Seleccione un proyecto de la tabla para eliminarlo.", "Proyecto no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar proyecto '{TxtProyCodigo.Text}'"))
            return;

        if (DialogoFluent.Show(this, "¿Desea eliminar este proyecto?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarProyecto(_proyectoEditandoId);
            BtnNuevoProyecto_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se puede eliminar este proyecto porque tiene documentos asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Seleccione un departamento y escriba el nombre completo.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Seleccione un registro de personal para eliminarlo.", "Personal no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar personal '{TxtAltaEncargadoNombre.Text}'"))
            return;

        if (DialogoFluent.Show(this, "¿Desea eliminar a esta persona del directorio?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarEncargado(_encargadoEditandoId);
            BtnNuevoEncargado_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se puede eliminar este registro de personal porque tiene documentos asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Escriba el código y nombre del departamento.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Seleccione un departamento para eliminarlo.", "Departamento no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar departamento '{TxtDeptoNombre.Text}'"))
            return;

        if (DialogoFluent.Show(this, "¿Desea eliminar este departamento?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarDepartamento(_departamentoEditandoId);
            BtnNuevoDepartamento_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se puede eliminar este departamento porque tiene registros asociados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (TxtDirHerUbicacion != null)
        {
            TxtDirHerUbicacion.SelectedIndex = -1;
            TxtDirHerUbicacion.Text = string.Empty;
        }
        TxtDirHerNotas.Clear();
    }

    private void BtnGuardarHerramientaDir_Click(object sender, RoutedEventArgs e)
    {
        if (CboDirHerProducto.SelectedItem is not Producto prod)
        {
            DialogoFluent.Show(this, "Seleccione el producto base para la herramienta.", "Falta producto", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string codigo = string.IsNullOrWhiteSpace(TxtDirHerCodigo.Text)
            ? DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual")
            : TxtDirHerCodigo.Text.Trim();
        string estado = (CboDirHerEstado.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Disponible";
        string ubiLimpiaHer = ExtraerCodigoUbicacionLimpio(TxtDirHerUbicacion.Text);

        DatabaseService.GuardarHerramientaActivo(new HerramientaActivo
        {
            Id = _herramientaEditandoId,
            ProductoId = prod.Id,
            CodigoIndividual = codigo,
            NumeroSerie = string.IsNullOrWhiteSpace(TxtDirHerSerie.Text) ? "S/N" : TxtDirHerSerie.Text.Trim(),
            MarcaModelo = TxtDirHerMarca.Text.Trim(),
            Estado = estado,
            Ubicacion = string.IsNullOrWhiteSpace(ubiLimpiaHer) ? "GABINETE-01" : ubiLimpiaHer,
            NotasEstado = TxtDirHerNotas.Text.Trim()
        });

        BtnNuevaHerramientaDir_Click(sender, e);
        CargarTodoElSistema();
    }

    private void BtnEliminarHerramientaDir_Click(object sender, RoutedEventArgs e)
    {
        if (_herramientaEditandoId == 0)
        {
            DialogoFluent.Show(this, "Seleccione una herramienta de la tabla para eliminarla.", "Herramienta no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar herramienta individual '{TxtDirHerCodigo.Text}'"))
            return;

        if (DialogoFluent.Show(this, "¿Desea eliminar esta herramienta individual?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarHerramientaActivo(_herramientaEditandoId);
            BtnNuevaHerramientaDir_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se puede eliminar esta herramienta porque tiene préstamos registrados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Escriba el nombre de la categoría.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DialogoFluent.Show(this, "Seleccione una categoría para eliminarla.", "Categoría no seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SolicitarAutorizacionAdmin($"Eliminar categoría '{TxtDirCatNombre.Text}'"))
            return;

        if (DialogoFluent.Show(this, "¿Desea eliminar esta categoría?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarCategoria(_categoriaEditandoId);
            BtnNuevaCategoriaDir_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            DialogoFluent.Show(this, $"No se puede eliminar esta categoría porque tiene productos vinculados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRespaldarDb_Click(object sender, RoutedEventArgs e)
    {
        string ruta = DatabaseService.CrearRespaldoBaseDatos();
        DialogoFluent.Show(this, $"Respaldo local creado exitosamente en:\n{ruta}", "Respaldo SQLite", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =====================================================================
    // MAPA GRÁFICO DE ANAQUELES (A–F y PISO) Y CONSULTA POR CUADRANTE (3x3)
    // =====================================================================

    private static List<string> ObtenerCatalogoUbicacionesDesplegable()
    {
        var lista = new List<string>
        {
            "Almacén General — Sin anaquel específico (Pendiente de acomodar)",
            "DETRAS-A — Detrás de A (Entre Anaquel A y Pared)",
            "PISO — Zona Piso / Exterior",
            "GABINETE-01 — Gabinete Nivel 1 (Inferior)",
            "GABINETE-02 — Gabinete Nivel 2",
            "GABINETE-03 — Gabinete Nivel 3 (Medio)",
            "GABINETE-04 — Gabinete Nivel 4",
            "GABINETE-05 — Gabinete Nivel 5 (Superior)"
        };

        string[] anaqueles = { "A", "B", "C", "D", "E", "F" };
        foreach (var est in anaqueles)
        {
            for (int i = 1; i <= 9; i++)
            {
                string nivel = i <= 3 ? "Inferior" : (i <= 6 ? "Medio" : "Superior");
                lista.Add($"{est}-{i:00} — Anaquel {est} (Nivel {nivel})");
            }
        }

        return lista;
    }

    private static string ExtraerCodigoUbicacionLimpio(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        string s = texto.Trim();
        int idxGuionLargo = s.IndexOf(" — ", StringComparison.Ordinal);
        if (idxGuionLargo > 0)
            s = s[..idxGuionLargo].Trim();

        int idxParen = s.IndexOf(" (", StringComparison.Ordinal);
        if (idxParen > 0)
            s = s[..idxParen].Trim();

        return s;
    }

    private void CboUbicacionAlmacen_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cbo || cbo.SelectedItem is not string itemSel)
            return;

        string limpio = ExtraerCodigoUbicacionLimpio(itemSel);
        if (!string.IsNullOrEmpty(limpio) && !string.Equals(cbo.Text, limpio, StringComparison.Ordinal))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                cbo.SelectedIndex = -1;
                cbo.Text = limpio;
            }), DispatcherPriority.Background);
        }
    }

    private void BtnAbrirMapaAnaqueles_Click(object sender, RoutedEventArgs e)
    {
        MostrarSelectorYMapaAnaqueles(null, null, this);
    }

    private void BtnSelectorUbicacionProducto_Click(object sender, RoutedEventArgs e)
    {
        MostrarSelectorYMapaAnaqueles(ExtraerCodigoUbicacionLimpio(TxtProdUbicacion?.Text), nuevaCoord =>
        {
            if (TxtProdUbicacion != null)
            {
                TxtProdUbicacion.SelectedIndex = -1;
                TxtProdUbicacion.Text = nuevaCoord;
            }
        }, this);
    }

    private void BtnSelectorUbicacionDirHer_Click(object sender, RoutedEventArgs e)
    {
        MostrarSelectorYMapaAnaqueles(ExtraerCodigoUbicacionLimpio(TxtDirHerUbicacion?.Text), nuevaCoord =>
        {
            if (TxtDirHerUbicacion != null)
            {
                TxtDirHerUbicacion.SelectedIndex = -1;
                TxtDirHerUbicacion.Text = nuevaCoord;
            }
        }, this);
    }

    private static (string Estante, string Cuadrante, string SubContenedor) ParsearCoordenadaAlmacen(string? ubicacion)
    {
        string limpio = ExtraerCodigoUbicacionLimpio(ubicacion);
        if (string.IsNullOrWhiteSpace(limpio))
            return ("", "", "");

        string raw = limpio.Trim().ToUpperInvariant()
            .Replace("ESTANTE ", "")
            .Replace("ANAQUEL ", "")
            .Replace("RACK ", "")
            .Trim();

        // 0. Almacén General (sin anaquel específico asignado aún) -> Ubicación única sin más divisiones
        if (raw.StartsWith("ALMACEN GENERAL", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("ALMACÉN GENERAL", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("ALMACEN-GENERAL", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("ALMACÉN-GENERAL", StringComparison.OrdinalIgnoreCase))
        {
            return ("ALMACEN-GENERAL", "01", "");
        }

        // 1. Detrás de A (Entre Anaquel A y Pared) -> Ubicación única sin más divisiones
        string[] prefijosDetrasA = { "DETRAS-A", "DETRÁS-A", "DETRAS DE A", "DETRÁS DE A", "DETRAS A", "PARED-A", "PARED A" };
        foreach (var pref in prefijosDetrasA)
        {
            if (raw.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
            {
                string resto = raw[pref.Length..].Trim('-', ' ', '/');
                string subDet = "";
                if (!string.IsNullOrEmpty(resto))
                {
                    var pResto = resto.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var tokSub = pResto.FirstOrDefault(t => t.StartsWith("G", StringComparison.OrdinalIgnoreCase) || t.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                    if (tokSub != null) subDet = tokSub;
                }
                return ("DETRAS-A", "01", subDet);
            }
        }

        // 2. Gabinete de Almacén -> 5 niveles (01 Inferior a 05 Superior) sin más divisiones
        if (raw.StartsWith("GABINETE", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("GAB", StringComparison.OrdinalIgnoreCase))
        {
            string prefGab = raw.StartsWith("GABINETE", StringComparison.OrdinalIgnoreCase) ? "GABINETE" : "GAB";
            string resto = raw[prefGab.Length..].Trim('-', ' ', '/');
            if (string.IsNullOrEmpty(resto))
                return ("GABINETE", "01", "");

            var pResto = resto.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tok in pResto)
            {
                string tLimpio = tok.TrimStart('N', 'n');
                if (int.TryParse(tLimpio, out int nGab) && nGab >= 1)
                {
                    int nivelGab = Math.Clamp(nGab, 1, 5);
                    string subGab = pResto.Length >= 2 && pResto[^1] != tok ? pResto[^1] : "";
                    return ("GABINETE", nivelGab.ToString("00"), subGab);
                }
            }
            return ("GABINETE", "01", "");
        }

        // 3. Zona PISO / Exterior -> Ubicación única sin más divisiones
        if (raw.StartsWith("PISO", StringComparison.OrdinalIgnoreCase))
        {
            string resto = raw["PISO".Length..].Trim('-', ' ', '/');
            string subPiso = "";
            if (!string.IsNullOrEmpty(resto))
            {
                var pResto = resto.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                var tokSub = pResto.FirstOrDefault(t => t.StartsWith("G", StringComparison.OrdinalIgnoreCase) || t.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                if (tokSub != null) subPiso = tokSub;
            }
            return ("PISO", "01", subPiso);
        }

        // 4. Buscar patrón A-01..F-09 (con o sin subcontenedor -G1 / -C1)
        var partes = raw.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length >= 2)
        {
            string est = partes[0];
            if (est is "A" or "B" or "C" or "D" or "E" or "F")
            {
                if (int.TryParse(partes[1], out int num) && num >= 1 && num <= 9)
                {
                    string cuad = num.ToString("00");
                    string sub = partes.Length >= 3 ? partes[2] : "";
                    return (est, cuad, sub);
                }
            }
        }
        else if (partes.Length == 1 && partes[0] is "A" or "B" or "C" or "D" or "E" or "F")
        {
            return (partes[0], "01", "");
        }

        return ("", "", "");
    }

    private static bool CoincideConEstanteYCuadrante(string? ubicacion, string estante, string? cuadrante = null)
    {
        var parsed = ParsearCoordenadaAlmacen(ubicacion);
        if (string.IsNullOrEmpty(parsed.Estante))
            return false;

        if (!string.Equals(parsed.Estante, estante, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(estante, "ALMACEN-GENERAL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(estante, "DETRAS-A", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(estante, "PISO", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(cuadrante) || cuadrante == "TODOS")
            return true;

        return string.Equals(parsed.Cuadrante, cuadrante, StringComparison.OrdinalIgnoreCase);
    }

    private void MostrarSelectorYMapaAnaqueles(string? coordenadaInicial = null, Action<string>? alSeleccionarCoordenada = null, Window? owner = null)
    {
        bool modoSelector = alSeleccionarCoordenada != null;
        var parsedInicial = ParsearCoordenadaAlmacen(coordenadaInicial);

        string estanteActual = !string.IsNullOrEmpty(parsedInicial.Estante) ? parsedInicial.Estante : "A";
        string cuadranteActual = !string.IsNullOrEmpty(parsedInicial.Cuadrante) ? parsedInicial.Cuadrante : "05";
        string subContenedorActual = parsedInicial.SubContenedor;

        var win = new Window
        { Foreground = Tk("TextFillColorPrimaryBrush"), MaxHeight = SystemParameters.WorkArea.Height - 20, MaxWidth = SystemParameters.WorkArea.Width - 20,
            Title = modoSelector
                ? "🗺️ Selector Gráfico de Coordenadas e Inventario por Cuadrante (Anaqueles A–F y PISO)"
                : "🗺️ Mapa Logístico de Almacén — Consulta e Inventario por Cuadrante (Anaqueles A–F y PISO)",
            Width = 1220,
            Height = 760,
            MinWidth = 1040,
            MinHeight = 660,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner ?? this,
            Background = Tk("ControlFillColorSecondaryBrush")
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header + Rack Selector
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Main Split (3x3 Grid + Right Inventory)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Footer

        // --- ROW 0: ENCABEZADO Y PESTAÑAS DE ESTANTES (A..F + PISO) ---
        var topCard = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var topStack = new StackPanel();
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        var spSubCont = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        spSubCont.Children.Add(new TextBlock
        {
            Text = "Gaveta / Caja (Opcional):",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        var cboSubContenedor = new ComboBox { Width = 165, Padding = new Thickness(6, 4, 6, 4), FontSize = 11.5 };
        cboSubContenedor.Items.Add("(Sin caja / Directo)");
        for (int g = 1; g <= 9; g++) cboSubContenedor.Items.Add($"G{g} (Gaveta {g})");
        for (int c = 1; c <= 9; c++) cboSubContenedor.Items.Add($"C{c} (Caja {c})");
        cboSubContenedor.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(subContenedorActual))
        {
            for (int i = 1; i < cboSubContenedor.Items.Count; i++)
            {
                if (cboSubContenedor.Items[i]?.ToString()?.StartsWith(subContenedorActual + " ", StringComparison.OrdinalIgnoreCase) == true)
                {
                    cboSubContenedor.SelectedIndex = i;
                    break;
                }
            }
        }
        spSubCont.Children.Add(cboSubContenedor);
        DockPanel.SetDock(spSubCont, Dock.Right);
        headerDock.Children.Add(spSubCont);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "Sistema de Coordenadas por Estante y Cuadrante (Estándar Bottom-Up 3×3)",
            FontSize = 15.5,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("TextFillColorPrimaryBrush")
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Seleccione un Anaquel (A a F), Detrás de A (Entre Anaquel y Pared), Gabinete o Zona PISO y haga clic en cualquiera de sus 9 posiciones.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush")
        });
        headerDock.Children.Add(titleStack);
        topStack.Children.Add(headerDock);

        var rackButtonsPanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        topStack.Children.Add(rackButtonsPanel);
        topCard.Child = topStack;
        Grid.SetRow(topCard, 0);
        root.Children.Add(topCard);

        // --- ROW 1: PANEL DIVIDIDO (IZQUIERDA: MATRIZ 3x3 DEL ANAQUEL | DERECHA: INVENTARIO DEL CUADRANTE) ---
        var mainSplit = new Grid();
        mainSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        mainSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14, GridUnitType.Pixel) });
        mainSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });

        // Panel Izquierdo: Frente del Anaquel 3x3
        var leftCard = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var leftDock = new DockPanel();

        var leftHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var btnVerTodoEstante = new System.Windows.Controls.Button
        {
            Content = "👁️ Ver todo el anaquel",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            ToolTip = "Mostrar en la tabla derecha todos los artículos y herramientas de los 9 cuadrantes de este anaquel"
        };
        DockPanel.SetDock(btnVerTodoEstante, Dock.Right);
        leftHeader.Children.Add(btnVerTodoEstante);

        var txtTituloAnaquel = new TextBlock
        {
            Text = "Vista Frontal — Anaquel A",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Tk("AccentTextFillColorPrimaryBrush")
        };
        leftHeader.Children.Add(txtTituloAnaquel);
        DockPanel.SetDock(leftHeader, Dock.Top);
        leftDock.Children.Add(leftHeader);

        // Leyenda ergonómica de niveles
        var legendGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        legendGrid.Children.Add(CrearBadgeLeyendaNivel("🔼 Superior (07–09): Baja rotación / Escalera", Color.FromRgb(254, 243, 199), Color.FromRgb(146, 64, 14)));
        legendGrid.Children.Add(CrearBadgeLeyendaNivel("⏺️ Medio (04–06): Altura cabeza / Herramientas", Color.FromRgb(219, 234, 254), Color.FromRgb(30, 64, 175)));
        legendGrid.Children.Add(CrearBadgeLeyendaNivel("🔽 Inferior (01–03): Fácil acceso / Pesados", Color.FromRgb(220, 252, 231), Color.FromRgb(22, 101, 52)));
        DockPanel.SetDock(legendGrid, Dock.Top);
        leftDock.Children.Add(legendGrid);

        // Grid 3x3 del estante
        var shelfGrid = new Grid();
        for (int r = 0; r < 3; r++)
            shelfGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int c = 0; c < 3; c++)
            shelfGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        leftDock.Children.Add(shelfGrid);
        leftCard.Child = leftDock;
        Grid.SetColumn(leftCard, 0);
        mainSplit.Children.Add(leftCard);

        // Panel Derecho: Consulta e Inventario por Cuadrante + Reasignación Rápida
        var rightCard = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header Cuadrante + KPIs
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Barra de asignación rápida
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Tabla Productos
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.05, GridUnitType.Star) }); // 3: Tabla Herramientas HER-xxxx

        // Right Row 0: Header & KPIs
        var rightHeaderBorder = new Border
        {
            Background = Tk("ApplicationBackgroundBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var rightHeaderDock = new DockPanel();
        var btnExportarCuadranteCsv = new System.Windows.Controls.Button
        {
            Content = "Exportar CSV",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Exportar a Excel/CSV el listado de materiales y herramientas ubicados en este cuadrante o anaquel"
        };
        DockPanel.SetDock(btnExportarCuadranteCsv, Dock.Right);
        rightHeaderDock.Children.Add(btnExportarCuadranteCsv);

        var rightHeaderStack = new StackPanel();
        var txtInventarioTitulo = new TextBlock
        {
            Text = "Inventario en Cuadrante A-05",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            Foreground = Tk("TextFillColorPrimaryBrush")
        };
        var txtInventarioSubtitulo = new TextBlock
        {
            Text = "Nivel Medio (Altura de cabeza / Media rotación / Herramientas)",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        rightHeaderStack.Children.Add(txtInventarioTitulo);
        rightHeaderStack.Children.Add(txtInventarioSubtitulo);
        rightHeaderDock.Children.Add(rightHeaderStack);
        rightHeaderBorder.Child = rightHeaderDock;
        Grid.SetRow(rightHeaderBorder, 0);
        rightGrid.Children.Add(rightHeaderBorder);

        // Right Row 1: Asignación rápida de artículo/herramienta al cuadrante actual (para el cuerpo técnico / almacén)
        var assignBorder = new Border
        {
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var assignStack = new StackPanel();
        assignStack.Children.Add(new TextBlock
        {
            Text = "📌Ubicar / Mover artículo o herramienta a este cuadrante (según acomodo físico):",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("AccentTextFillColorPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var assignRow = new Grid();
        assignRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        assignRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cboAsignarItem = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Escriba o seleccione un producto (PRD-xxxx) o herramienta individual (HER-xxxx) para asignarle la coordenada seleccionada"
        };
        Grid.SetColumn(cboAsignarItem, 0);
        assignRow.Children.Add(cboAsignarItem);

        var btnAsignarAqui = new System.Windows.Controls.Button
        {
            Content = "Ubicar Aquí",
            Padding = new Thickness(12, 4, 12, 4),
            FontWeight = FontWeights.SemiBold,
            Background = Tk("AccentFillColorDefaultBrush"),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(btnAsignarAqui, 1);
        assignRow.Children.Add(btnAsignarAqui);
        assignStack.Children.Add(assignRow);
        assignBorder.Child = assignStack;
        Grid.SetRow(assignBorder, 1);
        rightGrid.Children.Add(assignBorder);

        // Right Row 2: Tabla de Productos / Materiales en el cuadrante
        var prodDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var txtHeaderProd = new TextBlock
        {
            Text = "Materiales, Consumibles y Artículos de Catálogo (0)",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };
        DockPanel.SetDock(txtHeaderProd, Dock.Top);
        prodDock.Children.Add(txtHeaderProd);

        var dgProdCuadrante = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowBackground = Tk("ControlFillColorDefaultBrush"),
            AlternatingRowBackground = Tk("ControlFillColorSecondaryBrush"),
            FontSize = 11.5
        };
        dgProdCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Ubicación", Binding = new System.Windows.Data.Binding("Ubicacion"), Width = 95 });
        dgProdCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Código", Binding = new System.Windows.Data.Binding("CodigoInterno"), Width = 85 });
        dgProdCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Artículo / Material", Binding = new System.Windows.Data.Binding("Nombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dgProdCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Stock", Binding = new System.Windows.Data.Binding("StockActual") { StringFormat = "0.##" }, Width = 70 });
        dgProdCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadAlmacen"), Width = 75 });
        prodDock.Children.Add(dgProdCuadrante);
        Grid.SetRow(prodDock, 2);
        rightGrid.Children.Add(prodDock);

        // Right Row 3: Tabla de Herramientas Individuales (HER-xxxx) en el cuadrante
        var herDock = new DockPanel();
        var txtHeaderHer = new TextBlock
        {
            Text = "Herramientas Individuales HER-xxxx Asignadas a esta Posición (0)",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };
        DockPanel.SetDock(txtHeaderHer, Dock.Top);
        herDock.Children.Add(txtHeaderHer);

        var dgHerCuadrante = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowBackground = Tk("ControlFillColorDefaultBrush"),
            AlternatingRowBackground = Tk("ControlFillColorSecondaryBrush"),
            FontSize = 11.5
        };
        dgHerCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Ubicación", Binding = new System.Windows.Data.Binding("Ubicacion"), Width = 95 });
        dgHerCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "ID HER", Binding = new System.Windows.Data.Binding("CodigoIndividual"), Width = 85 });
        dgHerCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Herramienta", Binding = new System.Windows.Data.Binding("ProductoNombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dgHerCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Núm. Serie", Binding = new System.Windows.Data.Binding("NumeroSerie"), Width = 100 });
        dgHerCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Estado", Binding = new System.Windows.Data.Binding("Estado"), Width = 90 });
        dgHerCuadrante.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "En Resguardo De", Binding = new System.Windows.Data.Binding("EncargadoActual"), Width = 135 });
        herDock.Children.Add(dgHerCuadrante);
        Grid.SetRow(herDock, 3);
        rightGrid.Children.Add(herDock);

        rightCard.Child = rightGrid;
        Grid.SetColumn(rightCard, 2);
        mainSplit.Children.Add(rightCard);

        Grid.SetRow(mainSplit, 1);
        root.Children.Add(mainSplit);

        // --- ROW 2: BARRA INFERIOR DE ACCIONES ---
        var footerDock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(rightButtons, Dock.Right);

        var btnSeleccionar = new System.Windows.Controls.Button
        {
            Content = $"Usar Coordenada {estanteActual}-{cuadranteActual}",
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            Visibility = modoSelector ? Visibility.Visible : Visibility.Collapsed
        };
        rightButtons.Children.Add(btnSeleccionar);

        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = modoSelector ? "Cancelar" : "Cerrar Mapa",
            Padding = new Thickness(16, 7, 16, 7),
            Cursor = Cursors.Hand
        };
        btnCerrar.Click += (_, _) => win.Close();
        rightButtons.Children.Add(btnCerrar);
        footerDock.Children.Add(rightButtons);

        var txtResumenGlobal = new TextBlock
        {
            Text = "",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Tk("TextFillColorSecondaryBrush")
        };
        footerDock.Children.Add(txtResumenGlobal);
        Grid.SetRow(footerDock, 2);
        root.Children.Add(footerDock);

        // --- LÓGICA INTERACTIVA DE ACTUALIZACIÓN DEL MAPA E INVENTARIO ---
        List<Producto> todosProductos = new();
        List<HerramientaActivo> todasHerramientas = new();

        string ObtenerSubContenedorElegido()
        {
            if (cboSubContenedor.SelectedIndex <= 0) return "";
            string raw = cboSubContenedor.SelectedItem?.ToString() ?? "";
            int idx = raw.IndexOf(' ');
            return idx > 0 ? raw[..idx].Trim() : raw.Trim();
        }

        string ConstruirCoordenadaFinal()
        {
            string sub = ObtenerSubContenedorElegido();
            if (string.Equals(estanteActual, "ALMACEN-GENERAL", StringComparison.OrdinalIgnoreCase))
            {
                return "Almacén General";
            }

            if (string.Equals(estanteActual, "DETRAS-A", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(sub) ? "DETRAS-A" : $"DETRAS-A-{sub}";
            }

            if (string.Equals(estanteActual, "PISO", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(sub) ? "PISO" : $"PISO-{sub}";
            }

            if (string.Equals(estanteActual, "GABINETE", StringComparison.OrdinalIgnoreCase))
            {
                string nivGab = cuadranteActual is "01" or "02" or "03" or "04" or "05" ? cuadranteActual : "01";
                return string.IsNullOrEmpty(sub) ? $"GABINETE-{nivGab}" : $"GABINETE-{nivGab}-{sub}";
            }

            string cuad = cuadranteActual == "TODOS" ? "01" : cuadranteActual;
            string coord = $"{estanteActual}-{cuad}";
            if (!string.IsNullOrEmpty(sub))
                coord += $"-{sub}";
            return coord;
        }

        string ObtenerDescripcionNivel(string cuad)
        {
            if (string.Equals(estanteActual, "ALMACEN-GENERAL", StringComparison.OrdinalIgnoreCase))
                return "Artículos registrados en Almacén General (sin anaquel específico asignado aún)";

            if (string.Equals(estanteActual, "DETRAS-A", StringComparison.OrdinalIgnoreCase))
                return "Ubicación única detrás del Anaquel A (entre el anaquel y la pared)";

            if (string.Equals(estanteActual, "PISO", StringComparison.OrdinalIgnoreCase))
                return "Ubicación única en Zona Piso / Exterior (sin más divisiones)";

            if (string.Equals(estanteActual, "GABINETE", StringComparison.OrdinalIgnoreCase))
            {
                return cuad switch
                {
                    "05" => "Gabinete Nivel 5 (Superior) — Sin divisiones horizontales",
                    "04" => "Gabinete Nivel 4 — Sin divisiones horizontales",
                    "03" => "Gabinete Nivel 3 (Medio) — Sin divisiones horizontales",
                    "02" => "Gabinete Nivel 2 — Sin divisiones horizontales",
                    "01" => "Gabinete Nivel 1 (Inferior) — Sin divisiones horizontales",
                    _ => "Todos los 5 niveles del Gabinete de Almacén"
                };
            }

            return cuad switch
            {
                "07" or "08" or "09" => "Nivel Superior — Baja rotación / No alcanza sin escalera",
                "04" or "05" or "06" => "Nivel Medio — Altura de cabeza / Media rotación / Herramientas",
                "01" or "02" or "03" => "Nivel Inferior — Fácil acceso / Material pesado o frecuente",
                _ => "Todos los 9 cuadrantes del anaquel"
            };
        }

        void RecargarDatosBase()
        {
            todosProductos = DatabaseService.ObtenerProductos();
            todasHerramientas = DatabaseService.ObtenerHerramientas();

            var opcionesCombo = new List<ComboOpcionAsignacionUbicacion>();
            foreach (var p in todosProductos.OrderBy(x => x.Nombre))
            {
                string ubiActual = string.IsNullOrWhiteSpace(p.Ubicacion) ? "Sin ubicación" : p.Ubicacion;
                opcionesCombo.Add(new ComboOpcionAsignacionUbicacion
                {
                    EsHerramientaIndividual = false,
                    Id = p.Id,
                    Etiqueta = $"📦 [{p.CodigoInterno}] {p.Nombre} (Ubic. actual: {ubiActual})"
                });
            }
            foreach (var h in todasHerramientas.OrderBy(x => x.CodigoIndividual))
            {
                string ubiActual = string.IsNullOrWhiteSpace(h.Ubicacion) ? "Sin ubicación" : h.Ubicacion;
                opcionesCombo.Add(new ComboOpcionAsignacionUbicacion
                {
                    EsHerramientaIndividual = true,
                    Id = h.Id,
                    Etiqueta = $"🔧 [{h.CodigoIndividual}] {h.ProductoNombre} - Serie: {h.NumeroSerie} (Ubic. actual: {ubiActual})"
                });
            }
            cboAsignarItem.ItemsSource = opcionesCombo;
            cboAsignarItem.DisplayMemberPath = nameof(ComboOpcionAsignacionUbicacion.Etiqueta);
        }

        void RenderizarTodo()
        {
            // 1. Renderizar botones de Anaqueles (A..F, DETRAS-A, GABINETE, PISO y ALMACEN-GENERAL)
            rackButtonsPanel.Children.Clear();
            string[] estantes = { "A", "B", "C", "D", "E", "F", "DETRAS-A", "GABINETE", "PISO", "ALMACEN-GENERAL" };
            foreach (var est in estantes)
            {
                int cantProdEst = todosProductos.Count(p => CoincideConEstanteYCuadrante(p.Ubicacion, est, "TODOS"));
                int cantHerEst = todasHerramientas.Count(h => CoincideConEstanteYCuadrante(h.Ubicacion, est, "TODOS"));
                bool activo = string.Equals(estanteActual, est, StringComparison.OrdinalIgnoreCase);

                string tituloBtn = est switch
                {
                    "DETRAS-A" => $"🧱 Detrás de A / Pared ({cantProdEst + cantHerEst})",
                    "GABINETE" => $"🚪 Gabinete ({cantProdEst + cantHerEst})",
                    "PISO" => $"🏗️ Zona Piso / Ext ({cantProdEst + cantHerEst})",
                    "ALMACEN-GENERAL" => $"🏢 Almacén General ({cantProdEst + cantHerEst})",
                    _ => $"🗄️ Anaquel {est} ({cantProdEst + cantHerEst})"
                };

                var btnEst = new System.Windows.Controls.Button
                {
                    Content = tituloBtn,
                    Padding = new Thickness(12, 7, 12, 7),
                    Margin = new Thickness(0, 0, 6, 4),
                    FontWeight = activo ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 12,
                    Cursor = Cursors.Hand,
                    Background = activo
                        ? Tk("AccentTextFillColorPrimaryBrush")
                        : Tk("ControlFillColorSecondaryBrush"),
                    Foreground = activo ? Brushes.White : Tk("TextFillColorPrimaryBrush"),
                    BorderBrush = activo
                        ? Tk("AccentTextFillColorPrimaryBrush")
                        : Tk("ControlStrokeColorDefaultBrush")
                };
                string estCopy = est;
                btnEst.Click += (_, _) =>
                {
                    estanteActual = estCopy;
                    if (estanteActual is "DETRAS-A" or "PISO" or "ALMACEN-GENERAL")
                        cuadranteActual = "01";
                    else if (estanteActual == "GABINETE" && cuadranteActual is not ("01" or "02" or "03" or "04" or "05" or "TODOS"))
                        cuadranteActual = "03";
                    else if (cuadranteActual == "TODOS")
                        cuadranteActual = "05";
                    RenderizarTodo();
                };
                rackButtonsPanel.Children.Add(btnEst);
            }

            txtTituloAnaquel.Text = estanteActual switch
            {
                "DETRAS-A" => "Ubicación Única — Detrás de A (Entre Anaquel A y Pared, sin más divisiones)",
                "GABINETE" => "Vista Frontal — Gabinete de Almacén (5 Niveles sin divisiones horizontales)",
                "PISO" => "Ubicación Única — Zona PISO / Exterior (Sin más divisiones)",
                "ALMACEN-GENERAL" => "Ubicación General — Almacén General (Sin anaquel específico asignado)",
                _ => $"Vista Frontal Parado Frente al Anaquel {estanteActual} ({estanteActual}-01 a {estanteActual}-09)"
            };

            // 2. Renderizar estructura visual según el tipo de ubicación:
            //    - DETRAS-A, PISO y ALMACEN-GENERAL: 1 sola celda completa (sin divisiones)
            //    - GABINETE: 5 niveles horizontales (Nivel 5 Superior down to Nivel 1 Inferior)
            //    - A..F: Matriz 3x3 (01..09)
            shelfGrid.Children.Clear();
            shelfGrid.RowDefinitions.Clear();
            int totalFilasGrid = string.Equals(estanteActual, "GABINETE", StringComparison.OrdinalIgnoreCase) ? 5 : 3;
            for (int r = 0; r < totalFilasGrid; r++)
                shelfGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var celdasRender = new List<(int fila, int col, int rowSpan, int colSpan, string cuad, string coordLabel, string nivelCorto)>();

            if (string.Equals(estanteActual, "DETRAS-A", StringComparison.OrdinalIgnoreCase))
            {
                celdasRender.Add((0, 0, 3, 3, "01", "DETRAS-A", "Zona Única (Entre Anaquel A y Pared)"));
            }
            else if (string.Equals(estanteActual, "PISO", StringComparison.OrdinalIgnoreCase))
            {
                celdasRender.Add((0, 0, 3, 3, "01", "PISO", "Zona Única (Piso / Exterior)"));
            }
            else if (string.Equals(estanteActual, "ALMACEN-GENERAL", StringComparison.OrdinalIgnoreCase))
            {
                celdasRender.Add((0, 0, 3, 3, "01", "Almacén General", "Zona General (Por ubicar en anaquel)"));
            }
            else if (string.Equals(estanteActual, "GABINETE", StringComparison.OrdinalIgnoreCase))
            {
                celdasRender.Add((0, 0, 1, 3, "05", "GABINETE-05", "Nivel 5 — Superior (Entrepaño completo)"));
                celdasRender.Add((1, 0, 1, 3, "04", "GABINETE-04", "Nivel 4 — Medio-Alto (Entrepaño completo)"));
                celdasRender.Add((2, 0, 1, 3, "03", "GABINETE-03", "Nivel 3 — Medio (Entrepaño completo)"));
                celdasRender.Add((3, 0, 1, 3, "02", "GABINETE-02", "Nivel 2 — Medio-Bajo (Entrepaño completo)"));
                celdasRender.Add((4, 0, 1, 3, "01", "GABINETE-01", "Nivel 1 — Inferior (Entrepaño completo)"));
            }
            else
            {
                string[,] mapaCuadrantes = new string[3, 3]
                {
                    { "07", "08", "09" }, // Nivel Superior
                    { "04", "05", "06" }, // Nivel Medio
                    { "01", "02", "03" }  // Nivel Inferior
                };
                for (int r = 0; r < 3; r++)
                {
                    string niv = r switch
                    {
                        0 => "Superior (Escalera)",
                        1 => "Medio (Cabeza/Herr.)",
                        _ => "Inferior (Fácil acceso)"
                    };
                    for (int c = 0; c < 3; c++)
                    {
                        string q = mapaCuadrantes[r, c];
                        celdasRender.Add((r, c, 1, 1, q, $"{estanteActual}-{q}", niv));
                    }
                }
            }

            foreach (var celda in celdasRender)
            {
                int fila = celda.fila;
                int col = celda.col;
                string cuad = celda.cuad;
                string coordLabel = celda.coordLabel;
                string nivelCorto = celda.nivelCorto;
                bool seleccionado = string.Equals(estanteActual, "DETRAS-A", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(estanteActual, "PISO", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cuadranteActual, cuad, StringComparison.OrdinalIgnoreCase);

                var prodsCuad = todosProductos.Where(p => CoincideConEstanteYCuadrante(p.Ubicacion, estanteActual, cuad)).ToList();
                var hersCuad = todasHerramientas.Where(h => CoincideConEstanteYCuadrante(h.Ubicacion, estanteActual, cuad)).ToList();
                int hersPrestadas = hersCuad.Count(h => string.Equals(h.Estado, "En Préstamo", StringComparison.OrdinalIgnoreCase));

                // Colores por nivel ergonómico
                int bandaColor = totalFilasGrid == 5
                    ? (fila == 0 ? 0 : (fila <= 2 ? 1 : 2))
                    : fila;
                Color headerBg = bandaColor switch
                {
                    0 => Color.FromRgb(254, 243, 199), // Superior: Ámbar
                    1 => Color.FromRgb(219, 234, 254), // Medio: Azul
                    _ => Color.FromRgb(220, 252, 231)  // Inferior: Verde
                };
                Color headerFg = bandaColor switch
                {
                    0 => Color.FromRgb(146, 64, 14),
                    1 => Color.FromRgb(30, 64, 175),
                    _ => Color.FromRgb(22, 101, 52)
                };

                var cellBorder = new Border
                {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(seleccionado ? 2.5 : 1),
                    BorderBrush = seleccionado
                        ? Tk("AccentFillColorDefaultBrush")
                        : Tk("ControlStrokeColorDefaultBrush"),
                    Background = seleccionado
                        ? Tk("SubtleFillColorSecondaryBrush")
                        : Brushes.White,
                    Cursor = Cursors.Hand
                };

                var cellDock = new DockPanel();

                // Franja superior de la celda
                var cellHeader = new Border
                {
                    Background = new SolidColorBrush(headerBg),
                    CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Padding = new Thickness(8, 5, 8, 5)
                };
                var cellHeaderDock = new DockPanel();
                var txtNivelTag = new TextBlock
                {
                    Text = nivelCorto,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(headerFg),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(txtNivelTag, Dock.Right);
                cellHeaderDock.Children.Add(txtNivelTag);

                cellHeaderDock.Children.Add(new TextBlock
                {
                    Text = coordLabel,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(headerFg)
                });
                cellHeader.Child = cellHeaderDock;
                DockPanel.SetDock(cellHeader, Dock.Top);
                cellDock.Children.Add(cellHeader);

                // Cuerpo de la celda (Contadores y vista previa)
                var cellBody = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
                var badgesPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                badgesPanel.Children.Add(new TextBlock
                {
                    Text = $"{prodsCuad.Count} art.",
                    FontSize = 11.5,
                    FontWeight = prodsCuad.Count > 0 ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = prodsCuad.Count > 0 ? Tk("TextFillColorPrimaryBrush") : Tk("TextFillColorSecondaryBrush"),
                    Margin = new Thickness(0, 0, 10, 0)
                });
                badgesPanel.Children.Add(new TextBlock
                {
                    Text = $"{hersCuad.Count} herr.",
                    FontSize = 11.5,
                    FontWeight = hersCuad.Count > 0 ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = hersCuad.Count > 0 ? Tk("AccentTextFillColorPrimaryBrush") : Tk("TextFillColorSecondaryBrush")
                });
                cellBody.Children.Add(badgesPanel);

                if (hersPrestadas > 0)
                {
                    cellBody.Children.Add(new TextBlock
                    {
                        Text = $"{hersPrestadas} en préstamo fuera",
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
                        Margin = new Thickness(0, 0, 0, 2)
                    });
                }

                // Preview de nombres en esta posición
                int maxPreviews = celda.colSpan > 1 ? 6 : 3;
                var previews = prodsCuad.Select(p => p.Nombre)
                    .Concat(hersCuad.Select(h => $"{h.CodigoIndividual}: {h.ProductoNombre}"))
                    .Distinct()
                    .Take(maxPreviews)
                    .ToList();

                if (previews.Count > 0)
                {
                    foreach (var prev in previews)
                    {
                        cellBody.Children.Add(new TextBlock
                        {
                            Text = "• " + prev,
                            FontSize = 10.5,
                            Foreground = Tk("TextFillColorSecondaryBrush"),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                    }
                }
                else
                {
                    cellBody.Children.Add(new TextBlock
                    {
                        Text = "(Espacio vacío / disponible)",
                        FontSize = 10.5,
                        FontStyle = FontStyles.Italic,
                        Foreground = Tk("TextFillColorSecondaryBrush"),
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }

                cellDock.Children.Add(cellBody);
                cellBorder.Child = cellDock;

                string cuadCopy = cuad;
                cellBorder.MouseLeftButtonDown += (_, ev) =>
                {
                    cuadranteActual = cuadCopy;
                    RenderizarTodo();
                    if (ev.ClickCount == 2 && modoSelector)
                    {
                        alSeleccionarCoordenada?.Invoke(ConstruirCoordenadaFinal());
                        win.DialogResult = true;
                        win.Close();
                    }
                };

                // Menú contextual de clic derecho sobre la posición
                var ctxCell = new ContextMenu();
                var miVerCuad = new System.Windows.Controls.MenuItem { Header = $"Consultar inventario de {coordLabel}", FontWeight = FontWeights.Bold };
                miVerCuad.Click += (_, _) =>
                {
                    cuadranteActual = cuadCopy;
                    RenderizarTodo();
                };
                ctxCell.Items.Add(miVerCuad);

                var miAsignarACuad = new System.Windows.Controls.MenuItem { Header = $"Ubicar un artículo o herramienta en {coordLabel}" };
                miAsignarACuad.Click += (_, _) =>
                {
                    cuadranteActual = cuadCopy;
                    RenderizarTodo();
                    cboAsignarItem.Focus();
                    cboAsignarItem.IsDropDownOpen = true;
                };
                ctxCell.Items.Add(miAsignarACuad);

                var miExportarCuad = new System.Windows.Controls.MenuItem { Header = $"Exportar inventario de {coordLabel} (CSV)" };
                miExportarCuad.Click += (_, _) =>
                {
                    cuadranteActual = cuadCopy;
                    RenderizarTodo();
                    btnExportarCuadranteCsv.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                };
                ctxCell.Items.Add(miExportarCuad);

                if (modoSelector)
                {
                    ctxCell.Items.Add(new Separator());
                    var miElegirCoord = new System.Windows.Controls.MenuItem { Header = $"Usar coordenada {coordLabel}", FontWeight = FontWeights.Bold };
                    miElegirCoord.Click += (_, _) =>
                    {
                        cuadranteActual = cuadCopy;
                        alSeleccionarCoordenada?.Invoke(ConstruirCoordenadaFinal());
                        win.DialogResult = true;
                        win.Close();
                    };
                    ctxCell.Items.Add(miElegirCoord);
                }
                cellBorder.ContextMenu = ctxCell;

                Grid.SetRow(cellBorder, fila);
                Grid.SetColumn(cellBorder, col);
                Grid.SetRowSpan(cellBorder, celda.rowSpan);
                Grid.SetColumnSpan(cellBorder, celda.colSpan);
                shelfGrid.Children.Add(cellBorder);
            }

            // 3. Actualizar Tablas de Inventario en el Panel Derecho
            var prodsFiltrados = todosProductos
                .Where(p => CoincideConEstanteYCuadrante(p.Ubicacion, estanteActual, cuadranteActual))
                .OrderBy(p => p.Ubicacion)
                .ThenBy(p => p.Nombre)
                .ToList();

            var hersFiltradas = todasHerramientas
                .Where(h => CoincideConEstanteYCuadrante(h.Ubicacion, estanteActual, cuadranteActual))
                .OrderBy(h => h.Ubicacion)
                .ThenBy(h => h.CodigoIndividual)
                .ToList();

            string nombreEstanteMostrar = estanteActual switch
            {
                "DETRAS-A" => "Detrás de A (Entre Anaquel y Pared)",
                "GABINETE" => "Gabinete de Almacén",
                "PISO" => "Zona Piso / Exterior",
                "ALMACEN-GENERAL" => "Almacén General",
                _ => $"Anaquel {estanteActual}"
            };

            string etiquetaPos = estanteActual switch
            {
                "DETRAS-A" => "DETRAS-A — Detrás de A (Entre Anaquel A y Pared)",
                "PISO" => "PISO — Zona Piso / Exterior",
                "ALMACEN-GENERAL" => "Almacén General — Sin anaquel específico asignado",
                "GABINETE" => cuadranteActual == "TODOS"
                    ? "Todo el Gabinete de Almacén (Niveles 1 a 5)"
                    : $"Gabinete de Almacén — Nivel {int.Parse(cuadranteActual)} (GABINETE-{cuadranteActual})",
                _ => cuadranteActual == "TODOS"
                    ? $"Todo {nombreEstanteMostrar} ({estanteActual}-01 a {estanteActual}-09)"
                    : $"{nombreEstanteMostrar} — Posición {estanteActual}-{cuadranteActual}"
            };

            txtInventarioTitulo.Text = $"📍 Inventario en {etiquetaPos}";
            txtInventarioSubtitulo.Text = ObtenerDescripcionNivel(cuadranteActual);

            int dispHer = hersFiltradas.Count(h => string.Equals(h.Estado, "Disponible", StringComparison.OrdinalIgnoreCase));
            int prestHer = hersFiltradas.Count(h => string.Equals(h.Estado, "En Préstamo", StringComparison.OrdinalIgnoreCase));

            txtHeaderProd.Text = $"Materiales, Consumibles y Artículos de Catálogo ({prodsFiltrados.Count} SKUs | {prodsFiltrados.Sum(x => x.StockActual):0.##} u. en stock)";
            txtHeaderHer.Text = $"Herramientas Individuales HER-xxxx ({hersFiltradas.Count} registradas: {dispHer} en sitio, {prestHer} en préstamo)";

            dgProdCuadrante.ItemsSource = prodsFiltrados;
            dgHerCuadrante.ItemsSource = hersFiltradas;

            string coordFinal = ConstruirCoordenadaFinal();
            btnSeleccionar.Content = $"Usar Coordenada {coordFinal}";
            btnAsignarAqui.Content = $"Ubicar en {coordFinal}";

            int totalUbicadosProd = todosProductos.Count(p => !string.IsNullOrEmpty(ParsearCoordenadaAlmacen(p.Ubicacion).Estante));
            int totalUbicadosHer = todasHerramientas.Count(h => !string.IsNullOrEmpty(ParsearCoordenadaAlmacen(h.Ubicacion).Estante));
            txtResumenGlobal.Text = $"Resumen Bodega (A–F y PISO): {totalUbicadosProd} de {todosProductos.Count} productos y {totalUbicadosHer} de {todasHerramientas.Count} herramientas tienen coordenada estandarizada.";
        }

        cboSubContenedor.SelectionChanged += (_, _) =>
        {
            string coordFinal = ConstruirCoordenadaFinal();
            btnSeleccionar.Content = $"Usar Coordenada {coordFinal}";
            btnAsignarAqui.Content = $"Ubicar en {coordFinal}";
        };

        btnVerTodoEstante.Click += (_, _) =>
        {
            cuadranteActual = "TODOS";
            RenderizarTodo();
        };

        btnAsignarAqui.Click += (_, _) =>
        {
            if (cboAsignarItem.SelectedItem is not ComboOpcionAsignacionUbicacion sel)
            {
                DialogoFluent.Show(win, "Seleccione un artículo del catálogo o una herramienta individual en el desplegable para asignarle esta ubicación.", "Seleccione un artículo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string coordDestino = ConstruirCoordenadaFinal();
            if (sel.EsHerramientaIndividual)
            {
                DatabaseService.ActualizarUbicacionHerramientaIndividual(sel.Id, coordDestino);
            }
            else
            {
                DatabaseService.ActualizarUbicacionProducto(sel.Id, coordDestino, sincronizarHerramientas: true);
                if (_productoEnEdicion != null && _productoEnEdicion.Id == sel.Id && TxtProdUbicacion != null)
                {
                    _productoEnEdicion.Ubicacion = coordDestino;
                    TxtProdUbicacion.Text = coordDestino;
                }
            }

            RecargarDatosBase();
            RenderizarTodo();
            CargarTodoElSistema();
        };

        btnExportarCuadranteCsv.Click += (_, _) =>
        {
            var prodsFiltrados = (dgProdCuadrante.ItemsSource as IEnumerable<Producto>)?.ToList() ?? new List<Producto>();
            var hersFiltradas = (dgHerCuadrante.ItemsSource as IEnumerable<HerramientaActivo>)?.ToList() ?? new List<HerramientaActivo>();

            string sufijo = cuadranteActual == "TODOS" ? $"Anaquel_{estanteActual}_Completo" : $"{estanteActual}-{cuadranteActual}";
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Inventario por Cuadrante / Anaquel",
                Filter = "Archivo CSV para Excel (*.csv)|*.csv",
                FileName = $"Inventario_Ubicacion_{sufijo}_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (sfd.ShowDialog(win) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("COORDENADA_UBICACION,TIPO_REGISTRO,CODIGO,DESCRIPCION_ARTICULO,SERIE_O_MARCA,CANTIDAD_STOCK,UNIDAD,ESTADO_ACTUAL,RESGUARDANTE_ACTUAL");
            foreach (var p in prodsFiltrados)
            {
                sb.AppendLine($"{EscaparCsv(p.Ubicacion)},MATERIAL_CATALOGO,{EscaparCsv(p.CodigoInterno)},{EscaparCsv(p.Nombre)},{EscaparCsv(p.CategoriaNombre)},{p.StockActual:0.##},{EscaparCsv(p.UnidadAlmacen)},{(p.StockActual > 0 ? "Con Existencia" : "Sin Stock")},Almacén");
            }
            foreach (var h in hersFiltradas)
            {
                sb.AppendLine($"{EscaparCsv(h.Ubicacion)},HERRAMIENTA_INDIVIDUAL,{EscaparCsv(h.CodigoIndividual)},{EscaparCsv(h.ProductoNombre)},{EscaparCsv($"{h.MarcaModelo} / SN:{h.NumeroSerie}")},1,Pieza,{EscaparCsv(h.Estado)},{EscaparCsv(h.EncargadoActual)}");
            }
            File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
            DialogoFluent.Show(win, $"Inventario de la posición {sufijo} exportado correctamente en:\n{sfd.FileName}", "Inventario Exportado", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        btnSeleccionar.Click += (_, _) =>
        {
            alSeleccionarCoordenada?.Invoke(ConstruirCoordenadaFinal());
            win.DialogResult = true;
            win.Close();
        };

        // Menús de clic derecho en las tablas derechas del Mapa de Anaqueles
        var ctxDgProd = new ContextMenu();
        var miProdCarrito = new System.Windows.Controls.MenuItem { Header = "Agregar a la Solicitud (+1)", FontWeight = FontWeights.Bold };
        miProdCarrito.Click += (_, _) =>
        {
            if (dgProdCuadrante.SelectedItem is Producto p)
                AgregarAlCarritoTienda(p, 1m);
        };
        ctxDgProd.Items.Add(miProdCarrito);

        var miProdEditar = new System.Windows.Controls.MenuItem { Header = "Editar Ficha del Producto en Catálogo" };
        miProdEditar.Click += (_, _) =>
        {
            if (dgProdCuadrante.SelectedItem is Producto p)
            {
                win.Close();
                CambiarSeccion(1);
                CargarProductoEnEditor(p);
            }
        };
        ctxDgProd.Items.Add(miProdEditar);

        var miProdCopiar = new System.Windows.Controls.MenuItem { Header = "Copiar Código Interno (PRD-xxxx)" };
        miProdCopiar.Click += (_, _) =>
        {
            if (dgProdCuadrante.SelectedItem is Producto p && !string.IsNullOrWhiteSpace(p.CodigoInterno))
                Clipboard.SetText(p.CodigoInterno);
        };
        ctxDgProd.Items.Add(miProdCopiar);
        dgProdCuadrante.ContextMenu = ctxDgProd;

        var ctxDgHer = new ContextMenu();
        var miHerPrestar = new System.Windows.Controls.MenuItem { Header = "Prestar Herramienta (Ir a Vale de Préstamo)", FontWeight = FontWeights.Bold };
        miHerPrestar.Click += (_, _) =>
        {
            if (dgHerCuadrante.SelectedItem is HerramientaActivo h)
            {
                win.Close();
                CambiarSeccion(5);
                if (CboHerramientaDisponible?.ItemsSource is IEnumerable<HerramientaActivo> disp)
                {
                    var match = disp.FirstOrDefault(x => x.Id == h.Id);
                    if (match != null)
                        CboHerramientaDisponible.SelectedItem = match;
                }
            }
        };
        ctxDgHer.Items.Add(miHerPrestar);

        var miHerDir = new System.Windows.Controls.MenuItem { Header = "Ver en Directorio de Herramientas" };
        miHerDir.Click += (_, _) =>
        {
            if (dgHerCuadrante.SelectedItem is HerramientaActivo h)
            {
                win.Close();
                MenuIrDirectorioHerramientas_Click(win, new RoutedEventArgs());
                if (DgDirHerramientas?.ItemsSource is IEnumerable<HerramientaActivo> lista)
                    DgDirHerramientas.SelectedItem = lista.FirstOrDefault(x => x.Id == h.Id);
            }
        };
        ctxDgHer.Items.Add(miHerDir);

        var miHerCopiar = new System.Windows.Controls.MenuItem { Header = "Copiar Código HER-xxxx y Serie" };
        miHerCopiar.Click += (_, _) =>
        {
            if (dgHerCuadrante.SelectedItem is HerramientaActivo h)
                Clipboard.SetText($"{h.CodigoIndividual} | SN: {h.NumeroSerie}");
        };
        ctxDgHer.Items.Add(miHerCopiar);
        dgHerCuadrante.ContextMenu = ctxDgHer;

        RecargarDatosBase();
        RenderizarTodo();

        win.Content = root;
        win.ShowDialog();
    }

    private static Border CrearBadgeLeyendaNivel(string texto, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(2, 0, 2, 0),
            Child = new TextBlock
            {
                Text = texto,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
    }

    private sealed class ComboOpcionAsignacionUbicacion
    {
        public bool EsHerramientaIndividual { get; set; }
        public int Id { get; set; }
        public string Etiqueta { get; set; } = string.Empty;
    }

    private static string EscaparCsv(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return "";
        string limpio = valor.Replace("\r", " ").Replace("\n", " ").Trim();
        if (limpio.Contains(',') || limpio.Contains('"') || limpio.Contains(';'))
            return "\"" + limpio.Replace("\"", "\"\"") + "\"";
        return limpio;
    }

    // =====================================================================
    // BARRA SUPERIOR DE COMANDOS, PANEL EDITOR COLAPSABLE Y MENÚS DE CLIC DERECHO
    // =====================================================================

    private void MostrarPanelEditorCatalogo(bool visible)
    {
        if (ColEditorCatalogo == null || PnlPanelProducto == null || BtnToggleEditorCatalogo == null || DockCatalogoIzquierda == null)
            return;

        if (visible)
        {
            ColEditorCatalogo.Width = new GridLength(TcPanelProducto?.SelectedIndex == 1 ? AnchoPanelEdicion : AnchoPanelFicha);
            PnlPanelProducto.Visibility = Visibility.Visible;
            DockCatalogoIzquierda.Margin = new Thickness(0, 0, 12, 0);
            BtnToggleEditorCatalogo.Content = "Ocultar panel";
        }
        else
        {
            ColEditorCatalogo.Width = new GridLength(0);
            PnlPanelProducto.Visibility = Visibility.Collapsed;
            DockCatalogoIzquierda.Margin = new Thickness(0);
            BtnToggleEditorCatalogo.Content = "Mostrar panel";
        }
    }

    private void BtnToggleEditorCatalogo_Click(object sender, RoutedEventArgs e)
    {
        bool actualmenteVisible = PnlPanelProducto?.Visibility == Visibility.Visible;
        MostrarPanelEditorCatalogo(!actualmenteVisible);
    }

    private void BtnToggleWhatsappSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (PnlWhatsappCaptura == null || BtnToggleWhatsappSolicitud == null) return;
        bool visible = PnlWhatsappCaptura.Visibility == Visibility.Visible;
        PnlWhatsappCaptura.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleWhatsappSolicitud.Content = visible ? "💬 Pegar Texto WhatsApp ▾" : "💬 Ocultar WhatsApp ▴";
        if (!visible)
            TxtWhatsappInput?.Focus();
    }

    private void BadgeUbicacionCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Producto p)
        {
            e.Handled = true;
            AppMediator.Instance.Publicar(new AbrirMapaAnaquelesMessage(p.Ubicacion, nuevaCoord =>
            {
                DatabaseService.ActualizarUbicacionProducto(p.Id, nuevaCoord, sincronizarHerramientas: true);
                p.Ubicacion = nuevaCoord;
                if (_productoEnEdicion.Id == p.Id && TxtProdUbicacion != null)
                    TxtProdUbicacion.Text = nuevaCoord;
                AppMediator.Instance.Publicar(new DatosActualizadosMessage("BadgeUbicacion"));
            }));
        }
    }

    // --- ACCIONES DE LA BARRA SUPERIOR DE MENÚS (VÍA APP MEDIATOR) ---
    private void MenuIrCatalogo_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Catalogo));
        TxtBuscarCatalogo?.Focus();
    }

    private void MenuNuevoProductoCatalogo_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Catalogo));
        LimpiarEditorProducto();
        TxtProdNombre?.Focus();
    }

    private void MenuNuevaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Solicitudes));
        IniciarNuevaSolicitudVacia();
        MostrarDetalleSolicitud(true);
    }

    private void MenuIrComparativa_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Comparativa));
    }

    private void MenuIrFacturasXml_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.FacturasXml));
    }

    private void MenuIrDirectorioHerramientas_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Directorios));
        if (DgDirHerramientas != null)
        {
            var tabControl = BuscarTabControlPadre(DgDirHerramientas);
            if (tabControl != null && tabControl.Items.Count >= 4)
                tabControl.SelectedIndex = 3;
        }
    }

    private void MenuIrAnalisisRanking_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Analisis));
        if (DgRankingMaterialesAnalisis != null)
        {
            var tabControl = BuscarTabControlPadre(DgRankingMaterialesAnalisis);
            if (tabControl != null && tabControl.Items.Count >= 1)
                tabControl.SelectedIndex = 0;
        }
    }

    private void MenuIrAnalisisGraficas_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Analisis));
        if (CboProductoAnalisis != null)
        {
            var tabControl = BuscarTabControlPadre(CboProductoAnalisis);
            if (tabControl != null && tabControl.Items.Count >= 2)
                tabControl.SelectedIndex = 1;
        }
    }

    private void MenuRefrescarSistema_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new DatosActualizadosMessage("MenuRefrescar", ConservarScrollCatalogo: false));
    }

    private void MenuIrDirectorios_Click(object sender, RoutedEventArgs e)
    {
        AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Directorios));
    }

    private static TabControl? BuscarTabControlPadre(DependencyObject? child)
    {
        while (child != null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is TabControl tc && tc.Name != "MainTabControl")
                return tc;
        }
        return null;
    }

    private static Producto? ExtraerProductoDesdeMenuItem(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem mi)
        {
            if (mi.Tag is Producto p1) return p1;
            if (mi.DataContext is Producto p2) return p2;
            if (mi.Parent is ContextMenu cm)
            {
                if (cm.Tag is Producto p3) return p3;
                if (cm.PlacementTarget is FrameworkElement fe && fe.Tag is Producto p4) return p4;
                if (cm.PlacementTarget is FrameworkElement fe2 && fe2.DataContext is Producto p5) return p5;
            }
        }
        return null;
    }

    // --- CLIC DERECHO EN TARJETAS DEL CATÁLOGO VISUAL ---
    private void CtxCardAgregarCarrito_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p != null) AgregarAlCarritoTienda(p, 1m);
    }

    private void CtxCardEnviarSalida_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p == null) return;
        AgregarProductoAValeSalidaPos(p, 1m);
        CambiarSeccion(5);
    }

    private void CtxCardEditarProducto_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p == null) return;
        CambiarSeccion(1);
        CargarProductoEnEditor(p, abrirEdicion: true);
    }

    private void CtxCardAbrirMapaUbicacion_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p == null) return;
        MostrarSelectorYMapaAnaqueles(p.Ubicacion, nuevaCoord =>
        {
            DatabaseService.ActualizarUbicacionProducto(p.Id, nuevaCoord, sincronizarHerramientas: true);
            p.Ubicacion = nuevaCoord;
            if (_productoEnEdicion.Id == p.Id && TxtProdUbicacion != null)
                TxtProdUbicacion.Text = nuevaCoord;
            CargarTodoElSistema();
        }, this);
    }

    private void CtxCardAbrirFichaHerramienta_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p == null) return;
        MostrarModalRegistroHerramienta(p);
    }

    private void CtxCardVerAnalisisPrecio_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p == null) return;
        MenuIrAnalisisGraficas_Click(sender, e);
        if (CboProductoAnalisis?.ItemsSource is IEnumerable<Producto> items)
        {
            var match = items.FirstOrDefault(x => x.Id == p.Id);
            if (match != null)
                CboProductoAnalisis.SelectedItem = match;
        }
    }

    private void CtxCardCopiarCodigo_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p != null && !string.IsNullOrWhiteSpace(p.CodigoInterno))
            Clipboard.SetText(p.CodigoInterno);
    }

    private void CtxCardEliminarProducto_Click(object sender, RoutedEventArgs e)
    {
        var p = ExtraerProductoDesdeMenuItem(sender);
        if (p == null) return;
        CargarProductoEnEditor(p);
        BtnEliminarProducto_Click(sender, e);
    }

    // --- CLIC DERECHO EN TABLAS DE INICIO (DASHBOARD OCS Y ALERTAS DE STOCK) ---
    private void CtxDashboardOcRecibir_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardOcs?.SelectedItem is OrdenCompra oc)
        {
            CambiarSeccion(4);
            if (DgOrdenesCompra?.ItemsSource is IEnumerable<OrdenCompra> lista)
                DgOrdenesCompra.SelectedItem = lista.FirstOrDefault(x => x.Id == oc.Id);
        }
    }

    private void CtxDashboardOcImprimir_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardOcs?.SelectedItem is OrdenCompra oc)
        {
            CambiarSeccion(4);
            if (DgOrdenesCompra?.ItemsSource is IEnumerable<OrdenCompra> lista)
                DgOrdenesCompra.SelectedItem = lista.FirstOrDefault(x => x.Id == oc.Id);
            BtnImprimirOcSeleccionada_Click(sender, e);
        }
    }

    private void CtxDashboardOcCopiarFolio_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardOcs?.SelectedItem is OrdenCompra oc && !string.IsNullOrWhiteSpace(oc.Folio))
            Clipboard.SetText(oc.Folio);
    }

    private void CtxAlertaStockAgregarSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAlertas?.SelectedItem is Producto p)
        {
            decimal sugerido = Math.Max(1m, p.StockMinimo - p.StockActual);
            AgregarProductoAPartidasSolicitud(p, sugerido, "Reabastecimiento por alerta de stock mínimo");
            CambiarSeccion(2);
        }
    }

    private void CtxAlertaStockMapa_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAlertas?.SelectedItem is Producto p)
        {
            MostrarSelectorYMapaAnaqueles(p.Ubicacion, nuevaCoord =>
            {
                DatabaseService.ActualizarUbicacionProducto(p.Id, nuevaCoord, sincronizarHerramientas: true);
                CargarTodoElSistema();
            }, this);
        }
    }

    private void CtxAlertaStockEditar_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAlertas?.SelectedItem is Producto p)
        {
            CambiarSeccion(1);
            CargarProductoEnEditor(p, abrirEdicion: true);
        }
    }

    // --- CLIC DERECHO EN ÓRDENES DE COMPRA, RANKING Y DIRECTORIO DE HERRAMIENTAS ---
    private void CtxOrdenCompraCopiarFolio_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra?.SelectedItem is OrdenCompra oc && !string.IsNullOrWhiteSpace(oc.Folio))
            Clipboard.SetText(oc.Folio);
    }

    private void CtxRankingAbrirMapa_Click(object sender, RoutedEventArgs e)
    {
        if (DgRankingMaterialesAnalisis?.SelectedItem is MaterialRankingAnalisis item && item.ProductoId > 0)
        {
            var prod = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == item.ProductoId);
            if (prod != null)
            {
                MostrarSelectorYMapaAnaqueles(prod.Ubicacion, nuevaCoord =>
                {
                    DatabaseService.ActualizarUbicacionProducto(prod.Id, nuevaCoord, sincronizarHerramientas: true);
                    CargarTodoElSistema();
                }, this);
            }
        }
    }

    private void CtxRankingEditarProducto_Click(object sender, RoutedEventArgs e)
    {
        if (DgRankingMaterialesAnalisis?.SelectedItem is MaterialRankingAnalisis item && item.ProductoId > 0)
        {
            var prod = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == item.ProductoId);
            if (prod != null)
            {
                CambiarSeccion(1);
                CargarProductoEnEditor(prod);
            }
        }
    }

    private void CtxDirHerPrestar_Click(object sender, RoutedEventArgs e)
    {
        if (DgDirHerramientas?.SelectedItem is not HerramientaActivo h) return;
        CambiarSeccion(5);
        if (CboHerramientaDisponible?.ItemsSource is IEnumerable<HerramientaActivo> disp)
        {
            var match = disp.FirstOrDefault(x => x.Id == h.Id);
            if (match != null)
                CboHerramientaDisponible.SelectedItem = match;
        }
    }

    private void CtxDirHerReubicarMapa_Click(object sender, RoutedEventArgs e)
    {
        if (DgDirHerramientas?.SelectedItem is not HerramientaActivo h) return;
        MostrarSelectorYMapaAnaqueles(h.Ubicacion, nuevaCoord =>
        {
            DatabaseService.ActualizarUbicacionHerramientaIndividual(h.Id, nuevaCoord);
            if (TxtDirHerUbicacion != null)
                TxtDirHerUbicacion.Text = nuevaCoord;
            CargarTodoElSistema();
        }, this);
    }

    private void CtxDirHerMarcarDisponible_Click(object sender, RoutedEventArgs e)
    {
        if (DgDirHerramientas?.SelectedItem is not HerramientaActivo h) return;
        h.Estado = "Disponible";
        DatabaseService.GuardarHerramientaActivo(h);
        CargarTodoElSistema();
    }

    private void CtxDirHerMarcarDanada_Click(object sender, RoutedEventArgs e)
    {
        if (DgDirHerramientas?.SelectedItem is not HerramientaActivo h) return;
        h.Estado = "Dañada";
        DatabaseService.GuardarHerramientaActivo(h);
        CargarTodoElSistema();
    }

    private void CtxDirHerCopiarCodigo_Click(object sender, RoutedEventArgs e)
    {
        if (DgDirHerramientas?.SelectedItem is HerramientaActivo h)
            Clipboard.SetText($"{h.CodigoIndividual} | SN: {h.NumeroSerie}");
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
