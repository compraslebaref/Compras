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
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace SistemaComprasAlmacen;

public partial class MainWindow : FluentWindow
{
    private const int CatalogoTamanoLote = 48;
    private bool _temaOscuro = false;
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
    private List<PrestamoVistaFila> _todosPrestamosFilas = new();
    private List<MovimientoAlmacenFila> _todosMovimientosAlmacen = new();
    private FacturaXml? _facturaXmlEnPantalla;
    private bool _actualizandoCombosXml = false;

    public class PrestamoVistaFila
    {
        public int PrestamoId { get; set; }
        public int PrestamoPartidaId { get; set; }
        public int HerramientaId { get; set; }
        public string FolioVale { get; set; } = string.Empty;
        public string CodigoIndividual { get; set; } = string.Empty;
        public string HerramientaDescripcion { get; set; } = string.Empty;
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

        IcCatalogoVisual.ItemsSource = _catalogoVisible;
        DgPartidasNuevaSolicitud.ItemsSource = _partidasNuevaSolicitud;
        DgComparativa.ItemsSource = _renglonesComparativa;
        DgPartidasSalidaPos.ItemsSource = _partidasSalidaPos;

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
                CargarTodoElSistema();
                LimpiarEditorProducto();
            });
            TxtOmniScanner.Focus();
        };
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

    private void CargarTodoElSistema()
    {
        CargarCombosBase();
        RefrescarDashboard();
        RefrescarCatalogoVisual();
        RefrescarSolicitudes();
        RefrescarOrdenesYEntradas();
        RefrescarAlmacenPosYPrestamos();
        RefrescarFacturasXml();
        RefrescarAnalisis();
        RefrescarDirectorios();
    }

    private void CargarCombosBase()
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

        CboProdCategoria.ItemsSource = categorias;
        if (CboProdCategoria.SelectedIndex < 0) CboProdCategoria.SelectedIndex = 0;

        // Solicitudes
        CboSolDepartamento.ItemsSource = deptos;
        if (CboSolDepartamento.SelectedIndex < 0 && deptos.Count > 0) CboSolDepartamento.SelectedIndex = 0;
        CboSolProyecto.ItemsSource = proyectos;
        if (CboSolProyecto.SelectedIndex < 0 && proyectos.Count > 0) CboSolProyecto.SelectedIndex = 0;
        FiltrarComboProductoSolicitud();

        // Comparativa proveedores
        CboCompProv1.ItemsSource = proveedores;
        CboCompProv2.ItemsSource = proveedores;
        CboCompProv3.ItemsSource = proveedores;
        if (proveedores.Count >= 1 && CboCompProv1.SelectedIndex < 0) CboCompProv1.SelectedIndex = 0;
        if (proveedores.Count >= 2 && CboCompProv2.SelectedIndex < 0) CboCompProv2.SelectedIndex = 1;
        if (proveedores.Count >= 3 && CboCompProv3.SelectedIndex < 0) CboCompProv3.SelectedIndex = 2;

        // Almacén POS
        CboSalDepartamento.ItemsSource = deptos;
        if (CboSalDepartamento.SelectedIndex < 0 && deptos.Count > 0) CboSalDepartamento.SelectedIndex = 0;
        CboSalProyecto.ItemsSource = proyectos;
        if (CboSalProyecto.SelectedIndex < 0 && proyectos.Count > 0) CboSalProyecto.SelectedIndex = 0;
        CboSalProductoPos.ItemsSource = productos.Where(p => !p.EsHerramienta).ToList();
        if (CboSalProductoPos.SelectedIndex < 0) CboSalProductoPos.SelectedIndex = 0;

        // XML y Directorios
        CboXmlVincularProducto.ItemsSource = productos;
        if (CboXmlVincularProducto.SelectedIndex < 0 && productos.Count > 0) CboXmlVincularProducto.SelectedIndex = 0;
        CboAltaEncargadoDepto.ItemsSource = deptos;
        if (CboAltaEncargadoDepto.SelectedIndex < 0 && deptos.Count > 0) CboAltaEncargadoDepto.SelectedIndex = 0;
        CboDirHerProducto.ItemsSource = productos.Where(p => p.EsHerramienta).ToList();
        if (CboDirHerProducto.SelectedIndex < 0 && CboDirHerProducto.Items.Count > 0) CboDirHerProducto.SelectedIndex = 0;
    }

    // ==================== NAVEGACIÓN Y TEMA WINDOWS 11 ====================

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

    private void BtnCambiarTema_Click(object sender, RoutedEventArgs e)
    {
        _temaOscuro = !_temaOscuro;
        ApplicationThemeManager.Apply(_temaOscuro ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica, false);
    }

    private void BtnIrRecepciones_Click(object sender, RoutedEventArgs e) => CambiarSeccion(4);
    private void BtnIrAlmacenPos_Click(object sender, RoutedEventArgs e) => CambiarSeccion(5);

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

    // ==================== TAB 0: DASHBOARD ====================

    private void RefrescarDashboard()
    {
        var sols = DatabaseService.ObtenerSolicitudes();
        var ocs = DatabaseService.ObtenerOrdenesCompra();
        var prods = _cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos();
        var her = DatabaseService.ObtenerHerramientas();

        KpiSolicitudes.Text = sols.Count(s => s.Estatus == "Pendiente").ToString();
        KpiOcsAbiertas.Text = ocs.Count(o => o.Estatus != "Recibida Completa").ToString();
        KpiStockBajo.Text = prods.Count(p => p.StockBajo).ToString();
        KpiHerramientasPrestadas.Text = her.Count(h => h.Estado == "En Préstamo").ToString();
        KpiGastoTotal.Text = ocs.Sum(o => o.TotalConIva).ToString("C2", CultureInfo.GetCultureInfo("es-MX"));

        DgDashboardOcs.ItemsSource = ocs.Where(o => o.Estatus != "Recibida Completa").ToList();
        DgDashboardAlertas.ItemsSource = prods.Where(p => p.StockBajo).ToList();
    }

    // ==================== TAB 1: CATÁLOGO VISUAL Y EDITOR ====================

    private void RefrescarCatalogoVisual()
    {
        if (_cacheProductos.Count == 0)
        {
            _cacheProductos = DatabaseService.ObtenerProductos();
        }

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

        IEnumerable<Producto> query = _cacheProductos;
        if (catId > 0)
        {
            query = query.Where(p => p.CategoriaId == catId);
        }

        if (!string.IsNullOrWhiteSpace(filtroRaw))
        {
            query = query.Where(p =>
                p.Nombre.Contains(filtroRaw, StringComparison.OrdinalIgnoreCase) ||
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
        if (!IsLoaded) return;
        RefrescarCatalogoVisual();
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
        if (cat != null)
        {
            CboProdCategoria.SelectedItem = cat;
            ActualizarEtiquetasFichaPorCategoria(cat);
        }

        ImgPreviewProducto.Source = ImageStandardizerService.CargarImagenSegura(p.ImagenRuta);
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(p.CodigoEfectivo);
        ActualizarSeleccionCatalogo();
    }

    private void LimpiarEditorProducto()
    {
        string sig = DatabaseService.GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
        _productoEnEdicion = new Producto { CodigoInterno = sig, CodigoBarras = sig };
        TxtProdCodigoInterno.Text = sig;
        TxtProdCodigoBarras.Text = sig;
        TxtProdNombre.Text = string.Empty;
        TxtProdMarca.Text = string.Empty;
        TxtProdModelo.Text = string.Empty;
        TxtProdMedidas.Text = string.Empty;
        TxtProdCapacidadElectrica.Text = string.Empty;
        TxtProdEspecificacion1.Text = string.Empty;
        TxtProdEspecificacion2.Text = string.Empty;
        TxtProdDescripcion.Text = string.Empty;
        TxtProdUnidadCompra.Text = "Pieza";
        TxtProdUnidadAlmacen.Text = "Pieza";
        TxtProdFactor.Text = "1";
        TxtProdStock.Text = "0";
        TxtProdStockMin.Text = "5";
        TxtProdCosto.Text = "0.00";
        TxtProdUbicacion.Text = "A-01";
        ChkProdEsHerramienta.IsChecked = false;
        ImgPreviewProducto.Source = null;
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(sig);
        ActualizarEtiquetasFichaPorCategoria(CboProdCategoria?.SelectedItem as Categoria);
        ActualizarSeleccionCatalogo();
    }

    private void BtnNuevoProducto_Click(object sender, RoutedEventArgs e) => LimpiarEditorProducto();

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
            System.Windows.MessageBox.Show(this, "Escriba el nombre del producto.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _productoEnEdicion.CodigoInterno = TxtProdCodigoInterno.Text.Trim();
        _productoEnEdicion.CodigoBarras = TxtProdCodigoBarras.Text.Trim();
        _productoEnEdicion.Nombre = TxtProdNombre.Text.Trim();
        _productoEnEdicion.CategoriaId = (CboProdCategoria.SelectedItem as Categoria)?.Id ?? 1;
        _productoEnEdicion.Marca = TxtProdMarca.Text.Trim();
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
        _productoEnEdicion.Ubicacion = TxtProdUbicacion.Text.Trim();
        _productoEnEdicion.EsHerramienta = ChkProdEsHerramienta.IsChecked == true;

        await EjecutarConIndicadorCargaAsync("Guardando producto...", () =>
        {
            DatabaseService.GuardarProducto(_productoEnEdicion);
            CargarTodoElSistema();
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
                    LimpiarEditorProducto();
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
            if (encs.Count > 0) CboSolEncargado.SelectedIndex = 0;
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
                TxtContadorFiltroProductoSol.Text = $"{filtrados.Count} coincidencias";
        }

        CboSolProductoAgregar.ItemsSource = filtrados;
        if (filtrados.Count > 0)
        {
            CboSolProductoAgregar.SelectedItem = filtrados.FirstOrDefault(p => p.Id == prevId) ?? filtrados[0];
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
            }
            else if (CboCompSolicitud.SelectedIndex < 0 && sols.Count > 0)
            {
                CboCompSolicitud.SelectedIndex = 0;
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
                UnidadCompra = prod.UnidadCompra,
                CantidadSolicitada = cant,
                StockActualAlmacen = prod.StockActual,
                NotasPartida = notas
            });
        }

        PersistirCambioSiSolicitudEnEdicion();
    }

    private void BtnAgregarPartidaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (CboSolProductoAgregar.SelectedItem is not Producto prod)
            return;

        decimal cant = ParseDecimal(TxtSolCantidadAgregar.Text, 1m);
        AgregarProductoAPartidasSolicitud(prod, cant, "Manual");
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
                        UnidadCompra = p.UnidadCompra,
                        CantidadSolicitada = cant,
                        StockActualAlmacen = p.StockActual,
                        NotasPartida = $"WhatsApp: {l}"
                    });
                }
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
                            UnidadCompra = prod.UnidadCompra,
                            CantidadSolicitada = cant,
                            StockActualAlmacen = prod.StockActual,
                            NotasPartida = nota
                        });
                    }
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
            return;

        int prevProdId = (DgComparativa.SelectedItem as CotizacionRenglonComparativo)?.ProductoId ?? 0;

        TxtCompInfoSolicitud.Text = $"Solicitud: {sol.Folio} | Depto: {sol.DepartamentoNombre} | Solicitante: {sol.EncargadoNombre} | Proyecto: {sol.ProyectoNombre} | Estatus: {sol.Estatus}";
        _renglonesComparativa.Clear();

        var productos = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var proveedores = DatabaseService.ObtenerProveedores();
        var provDict = proveedores.ToDictionary(p => p.Id);

        foreach (var part in sol.Partidas)
        {
            productos.TryGetValue(part.ProductoId, out var pObj);
            decimal ultimoCostoConIva = pObj?.UltimoCostoCompra ?? 0m;
            decimal precioBaseSinIva = ultimoCostoConIva > 0 ? Math.Round(ultimoCostoConIva / 1.16m, 2) : 0m;

            var (sugerencias, refHist) = DatabaseService.ObtenerSugerenciasCotizacionPorProducto(part.ProductoId);

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

            bool sinProveedoresGuardados = pv1 == 0 && pv2 == 0 && pv3 == 0;
            if (sinProveedoresGuardados)
            {
                // Pre-llenar automáticamente con proveedores que ya han vendido este producto antes, o del catálogo
                var usados = new HashSet<int>();
                if (sugerencias.Count >= 1)
                {
                    pv1 = sugerencias[0].proveedorId;
                    if (pr1 <= 0) pr1 = sugerencias[0].precioSinIva;
                    d1 = sugerencias[0].diasEntrega;
                    if (string.IsNullOrWhiteSpace(nc1)) nc1 = sugerencias[0].notaSugerida;
                    usados.Add(pv1);
                }
                if (sugerencias.Count >= 2)
                {
                    pv2 = sugerencias[1].proveedorId;
                    if (pr2 <= 0) pr2 = sugerencias[1].precioSinIva;
                    d2 = sugerencias[1].diasEntrega;
                    if (string.IsNullOrWhiteSpace(nc2)) nc2 = sugerencias[1].notaSugerida;
                    usados.Add(pv2);
                }
                if (sugerencias.Count >= 3)
                {
                    pv3 = sugerencias[2].proveedorId;
                    if (pr3 <= 0) pr3 = sugerencias[2].precioSinIva;
                    d3 = sugerencias[2].diasEntrega;
                    if (string.IsNullOrWhiteSpace(nc3)) nc3 = sugerencias[2].notaSugerida;
                    usados.Add(pv3);
                }

                // Completar opciones vacías con proveedores generales disponibles
                foreach (var provCat in proveedores)
                {
                    if (usados.Contains(provCat.Id)) continue;
                    if (pv1 == 0) { pv1 = provCat.Id; d1 = provCat.DiasEntregaPromedio > 0 ? provCat.DiasEntregaPromedio : 2; usados.Add(pv1); }
                    else if (pv2 == 0) { pv2 = provCat.Id; d2 = provCat.DiasEntregaPromedio > 0 ? provCat.DiasEntregaPromedio : 3; usados.Add(pv2); }
                    else if (pv3 == 0) { pv3 = provCat.Id; d3 = provCat.DiasEntregaPromedio > 0 ? provCat.DiasEntregaPromedio : 1; usados.Add(pv3); break; }
                }

                if (pr1 <= 0 && precioBaseSinIva > 0)
                    pr1 = precioBaseSinIva;
            }

            var renglon = new CotizacionRenglonComparativo
            {
                SolicitudPartidaId = part.Id,
                ProductoId = part.ProductoId,
                ProductoCodigo = part.ProductoCodigo,
                ProductoNombre = part.ProductoNombre,
                UnidadCompra = part.UnidadCompra,
                Cantidad = part.CantidadSolicitada,
                StockActualAlmacen = part.StockActualAlmacen,
                UltimoCostoCompraConIva = ultimoCostoConIva,
                ReferenciaHistorica = refHist,

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

            if (sinProveedoresGuardados && renglon.SolicitudPartidaId > 0)
            {
                DatabaseService.GuardarCotizacionPartida(renglon);
            }

            _renglonesComparativa.Add(renglon);
        }

        if (_renglonesComparativa.Count > 0)
        {
            var aSeleccionar = _renglonesComparativa.FirstOrDefault(r => r.ProductoId == prevProdId) ?? _renglonesComparativa[0];
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
                TxtCompProdTitulo.Text = "2. Seleccione un producto de la lista";
                TxtCompProdSubtitulo.Text = "No hay producto seleccionado.";
                TxtCompProdHistorial.Text = "";
                TxtCompProdCantidad.Text = "1";
                return;
            }

            TxtCompProdTitulo.Text = $"{r.ProductoCodigo} — {r.ProductoNombre}";
            TxtCompProdSubtitulo.Text = $"Unidad: {r.UnidadCompra} | Stock actual en almacén: {r.StockActualAlmacen:0.##} | Últ. Costo: {(r.UltimoCostoCompraConIva > 0 ? $"{r.UltimoCostoCompraConIva:C2} c/IVA" : "N/D")}";
            TxtCompProdHistorial.Text = r.ReferenciaHistorica;
            TxtCompProdCantidad.Text = r.Cantidad.ToString("0.##", CultureInfo.InvariantCulture);

            var proveedores = CboCompProv1.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores();
            CboCompProv1.SelectedItem = proveedores.FirstOrDefault(p => p.Id == r.Proveedor1Id);
            CboCompProv2.SelectedItem = proveedores.FirstOrDefault(p => p.Id == r.Proveedor2Id);
            CboCompProv3.SelectedItem = proveedores.FirstOrDefault(p => p.Id == r.Proveedor3Id);

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

            ActualizarResumenYAparienciaTarjetasProducto(r);
        }
        finally
        {
            _cargandoDetalleComparativa = false;
        }
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
        if (CboCompProv2.SelectedItem is Proveedor p2)
        {
            r.Proveedor2Id = p2.Id;
            r.Proveedor2Nombre = p2.NombreMostrar;
        }
        if (CboCompProv3.SelectedItem is Proveedor p3)
        {
            r.Proveedor3Id = p3.Id;
            r.Proveedor3Nombre = p3.NombreMostrar;
        }

        r.PrecioSinIva1 = ParseDecimal(TxtCompPrecioSinIva1.Text, 0m);
        r.PrecioSinIva2 = ParseDecimal(TxtCompPrecioSinIva2.Text, 0m);
        r.PrecioSinIva3 = ParseDecimal(TxtCompPrecioSinIva3.Text, 0m);

        r.FleteConIva1 = ParseDecimal(TxtCompFlete1.Text, 0m);
        r.FleteConIva2 = ParseDecimal(TxtCompFlete2.Text, 0m);
        r.FleteConIva3 = ParseDecimal(TxtCompFlete3.Text, 0m);

        r.DiasEntrega1 = Math.Max(1, (int)ParseDecimal(TxtCompDias1.Text, 2m));
        r.DiasEntrega2 = Math.Max(1, (int)ParseDecimal(TxtCompDias2.Text, 3m));
        r.DiasEntrega3 = Math.Max(1, (int)ParseDecimal(TxtCompDias3.Text, 1m));

        r.NotasCotizacion1 = TxtCompNotas1.Text ?? string.Empty;
        r.NotasCotizacion2 = TxtCompNotas2.Text ?? string.Empty;
        r.NotasCotizacion3 = TxtCompNotas3.Text ?? string.Empty;

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
        var ocs = DatabaseService.ObtenerOrdenesCompra();
        DgOrdenesCompra.ItemsSource = ocs;
        if (DgOrdenesCompra.SelectedIndex < 0 && ocs.Count > 0)
            DgOrdenesCompra.SelectedIndex = 0;

        DgEntradasHistorial.ItemsSource = DatabaseService.ObtenerEntradasAlmacen();
    }

    private void DgOrdenesCompra_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is OrdenCompra oc)
        {
            TxtTituloRecepcionOc.Text = $"Recepción — {oc.Folio}";
            DgPartidasRecepcionOc.ItemsSource = oc.Partidas;
        }
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

        var aRecibir = oc.Partidas
            .Where(p => p.CantidadRecibirAhora > 0)
            .Select(p => (p, p.CantidadRecibirAhora))
            .ToList();

        if (aRecibir.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Indique en la columna 'Recibir Hoy' al menos una cantidad mayor a 0.", "Sin cantidades por recibir", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entrada = DatabaseService.RegistrarRecepcionOrdenCompra(oc, aRecibir, TxtNotasRecepcionOc.Text);
        TxtNotasRecepcionOc.Clear();
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, entrada);
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
            CboSalEncargado.ItemsSource = encs;
            if (encs.Count > 0) CboSalEncargado.SelectedIndex = 0;
        }
    }

    private void RefrescarAlmacenPosYPrestamos()
    {
        var herramientas = DatabaseService.ObtenerHerramientas();
        CboHerramientaDisponible.ItemsSource = herramientas.Where(h => h.Estado == "Disponible").ToList();
        if (CboHerramientaDisponible.Items.Count > 0 && CboHerramientaDisponible.SelectedIndex < 0)
            CboHerramientaDisponible.SelectedIndex = 0;

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
                    EncargadoNombre = pre.EncargadoNombre,
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
            string detalle = ent.Partidas.Count > 0
                ? string.Join(", ", ent.Partidas.Select(p => $"{p.CantidadIngresadaAlmacen:0.##} {p.UnidadAlmacen} {p.ProductoNombre}"))
                : ent.Notas;
            movs.Add(new MovimientoAlmacenFila
            {
                TipoMovimiento = "ENTRADA",
                Folio = ent.Folio,
                Fecha = ent.Fecha,
                ResponsableOProveedor = ent.ProveedorNombre,
                ProyectoNombre = string.IsNullOrWhiteSpace(ent.OrdenCompraFolio)
                    ? "Almacén General"
                    : $"OC: {ent.OrdenCompraFolio}",
                DetalleResumen = detalle,
                EntradaOriginal = ent
            });
        }

        var salidas = DatabaseService.ObtenerSalidasAlmacen();
        foreach (var sal in salidas)
        {
            string detalle = sal.Partidas.Count > 0
                ? string.Join(", ", sal.Partidas.Select(p => $"{p.CantidadEntregada:0.##} {p.UnidadAlmacen} {p.ProductoNombre}"))
                : sal.Notas;
            movs.Add(new MovimientoAlmacenFila
            {
                TipoMovimiento = "SALIDA",
                Folio = sal.Folio,
                Fecha = sal.Fecha,
                ResponsableOProveedor = $"{sal.EncargadoNombre} ({sal.DepartamentoNombre})",
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
            CboSalProductoPos.ItemsSource = todosProds;
            if (CboSalProductoPos.Items.Count > 0 && CboSalProductoPos.SelectedIndex < 0)
                CboSalProductoPos.SelectedIndex = 0;

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

    private void BtnLimpiarSalidaPos_Click(object sender, RoutedEventArgs e) => _partidasSalidaPos.Clear();

    private void BtnRegistrarSalidaAlmacen_Click(object sender, RoutedEventArgs e)
    {
        if (CboSalDepartamento.SelectedItem is not Departamento dep ||
            CboSalEncargado.SelectedItem is not Encargado enc ||
            CboSalProyecto.SelectedItem is not Proyecto proy)
        {
            System.Windows.MessageBox.Show(this, "Seleccione Departamento, Encargado y Proyecto.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_partidasSalidaPos.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Agregue o escanee al menos un material para el vale de salida.", "Vale vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var salida = DatabaseService.RegistrarSalidaAlmacen(dep.Id, enc.Id, proy.Id, TxtSalNotas.Text, _partidasSalidaPos.ToList());
        _partidasSalidaPos.Clear();
        TxtSalNotas.Clear();
        CargarTodoElSistema();

        BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, salida);
    }

    private void BtnPrestarHerramienta_Click(object sender, RoutedEventArgs e)
    {
        if (CboSalDepartamento.SelectedItem is not Departamento dep ||
            CboSalEncargado.SelectedItem is not Encargado enc ||
            CboSalProyecto.SelectedItem is not Proyecto proy)
        {
            System.Windows.MessageBox.Show(this, "Seleccione a la izquierda el Departamento, Encargado y Proyecto al que se le prestará la herramienta.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CboHerramientaDisponible.SelectedItem is not HerramientaActivo her)
        {
            System.Windows.MessageBox.Show(this, "No hay una herramienta disponible seleccionada.", "Sin herramienta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prestamo = DatabaseService.RegistrarPrestamoHerramienta(dep.Id, enc.Id, proy.Id, TxtSalNotas.Text, new List<HerramientaActivo> { her });
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

        string estado = (CboEstadoRetornoHerramienta.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bueno";
        DatabaseService.DevolverHerramientaPartida(fila.PrestamoPartidaId, fila.HerramientaId, fila.PrestamoId, estado, TxtObsRetornoHerramienta.Text);
        TxtObsRetornoHerramienta.Clear();
        CargarTodoElSistema();

        System.Windows.MessageBox.Show(this,
            $"Devolución registrada para {fila.CodigoIndividual} con estado '{estado}'.",
            "Herramienta Devuelta", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnReimprimirValePrestamo_Click(object sender, RoutedEventArgs e)
    {
        if (DgPrestamosHerramientas.SelectedItem is PrestamoVistaFila fila)
        {
            var actualizado = DatabaseService.ObtenerPrestamosHerramienta().FirstOrDefault(p => p.Id == fila.PrestamoId) ?? fila.PrestamoOriginal;
            BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, actualizado);
        }
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
            var proyDefault = proyectos.FirstOrDefault(p => proyIdPrevio.HasValue && p.Id == proyIdPrevio.Value)
                ?? proyectos.FirstOrDefault(p => p.EsStockGeneral)
                ?? proyectos.FirstOrDefault();
            CboXmlProyecto.SelectedItem = proyDefault;

            var ocs = new List<OrdenCompra>
            {
                new() { Id = 0, Folio = "➕ Crear nueva OC automáticamente" }
            };
            ocs.AddRange(DatabaseService.ObtenerOrdenesCompra());
            CboXmlOrdenCompra.ItemsSource = ocs;
            if (CboXmlOrdenCompra.SelectedIndex < 0)
                CboXmlOrdenCompra.SelectedIndex = 0;

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
                }
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
            var resp = System.Windows.MessageBox.Show(this,
                $"¿Desea eliminar la factura {fac.SerieFolio} (UUID: {fac.Uuid}) del proveedor '{fac.EmisorNombre}' por {fac.Total:C2}?",
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
                    TxtXmlResumenActual.Text = "Factura eliminada correctamente.";
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
            "Seleccione una factura en la tabla 'Facturas Registradas' para eliminarla.",
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

    // ==================== TAB 7: ANÁLISIS Y CATÁLOGOS BASE ====================

    private void RefrescarAnalisis()
    {
        var proveedores = DatabaseService.ObtenerProveedores();
        var ocs = DatabaseService.ObtenerOrdenesCompra();
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
                return new
                {
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
        TxtProvCondiciones.Text = "Contado";
        TxtProvDias.Text = "3";
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
        CboProyEstatus.SelectedIndex = 0;
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
        TxtDirHerCodigo.Text = DatabaseService.GenerarSiguienteCodigoProducto("HER", "HerramientasActivos", "CodigoIndividual");
        TxtDirHerSerie.Clear();
        TxtDirHerMarca.Clear();
        CboDirHerEstado.SelectedIndex = 0;
        TxtDirHerUbicacion.Text = "Almacén Herramientas";
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
        TxtDirCatCampo1.Text = "Capacidad Eléctrica / Tipo";
        TxtDirCatCampo2.Text = "Especificación / Material";
        TxtDirCatCampo3.Text = "Detalle Adicional / Color";
    }

    private void BtnGuardarCategoriaDir_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDirCatNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Escriba el nombre de la categoría.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DatabaseService.GuardarCategoria(
            TxtDirCatNombre.Text,
            _categoriaEditandoId,
            TxtDirCatCampo1.Text,
            TxtDirCatCampo2.Text,
            TxtDirCatCampo3.Text);
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
