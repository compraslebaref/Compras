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
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace SistemaComprasAlmacen;

public partial class MainWindow : FluentWindow
{
    private bool _temaOscuro = false;
    private Producto _productoEnEdicion = new();
    private int _proveedorEditandoId = 0;
    private int _proyectoEditandoId = 0;
    private int _encargadoEditandoId = 0;
    private int _departamentoEditandoId = 0;
    private int _herramientaEditandoId = 0;
    private int _categoriaEditandoId = 0;
    private readonly List<(Producto producto, decimal cantidad)> _carritoTienda = new();
    private readonly ObservableCollection<SolicitudPartida> _partidasNuevaSolicitud = new();
    private readonly ObservableCollection<CotizacionRenglonComparativo> _renglonesComparativa = new();
    private readonly ObservableCollection<SalidaPartida> _partidasSalidaPos = new();
    private FacturaXml? _facturaXmlEnPantalla;

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

    public MainWindow()
    {
        InitializeComponent();

        DgPartidasNuevaSolicitud.ItemsSource = _partidasNuevaSolicitud;
        DgComparativa.ItemsSource = _renglonesComparativa;
        DgPartidasSalidaPos.ItemsSource = _partidasSalidaPos;

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            CargarTodoElSistema();
            LimpiarEditorProducto();
            TxtOmniScanner.Focus();
        };
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
        var productos = DatabaseService.ObtenerProductos();

        // Filtro categorías en catálogo
        var catsConTodas = new List<Categoria> { new() { Id = 0, Nombre = "Todas las categorías" } };
        catsConTodas.AddRange(categorias);
        CboFiltroCategoria.ItemsSource = catsConTodas;
        if (CboFiltroCategoria.SelectedIndex < 0) CboFiltroCategoria.SelectedIndex = 0;

        CboProdCategoria.ItemsSource = categorias;
        if (CboProdCategoria.SelectedIndex < 0) CboProdCategoria.SelectedIndex = 0;

        // Solicitudes
        CboSolDepartamento.ItemsSource = deptos;
        if (CboSolDepartamento.SelectedIndex < 0 && deptos.Count > 0) CboSolDepartamento.SelectedIndex = 0;
        CboSolProyecto.ItemsSource = proyectos;
        if (CboSolProyecto.SelectedIndex < 0 && proyectos.Count > 0) CboSolProyecto.SelectedIndex = 0;
        CboSolProductoAgregar.ItemsSource = productos;
        if (CboSolProductoAgregar.SelectedIndex < 0 && productos.Count > 0) CboSolProductoAgregar.SelectedIndex = 0;

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
        ApplicationThemeManager.Apply(_temaOscuro ? ApplicationTheme.Dark : ApplicationTheme.Light);
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

    private void ProcesarCodigoEscaneado(string? rawInput)
    {
        string code = (rawInput ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            return;

        TxtOmniScanner.Clear();
        TxtUltimoEscaneo.Text = $"Último escaneo: {code}";

        // 1. ¿Es el folio de una Orden de Compra (OC-xxxx)? -> Abrir recepción parcial/total de esa OC
        if (code.StartsWith("OC-"))
        {
            var ocs = DatabaseService.ObtenerOrdenesCompra();
            var oc = ocs.FirstOrDefault(o => o.Folio.Equals(code, StringComparison.OrdinalIgnoreCase));
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

            var filas = DgPrestamosHerramientas.Items.Cast<PrestamoVistaFila>().ToList();
            var match = filas.FirstOrDefault(f =>
                (f.FolioVale.Equals(code, StringComparison.OrdinalIgnoreCase) || f.CodigoIndividual.Equals(code, StringComparison.OrdinalIgnoreCase)) &&
                f.EstadoDevolucion == "Pendiente")
                ?? filas.FirstOrDefault(f => f.FolioVale.Equals(code, StringComparison.OrdinalIgnoreCase) || f.CodigoIndividual.Equals(code, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                DgPrestamosHerramientas.SelectedItem = match;
                System.Windows.MessageBox.Show(this,
                    $"Resguardo localizado: {match.FolioVale}\nHerramienta: {match.CodigoIndividual} ({match.HerramientaDescripcion})\nEncargado: {match.EncargadoNombre}\n\nSeleccione el estado de retorno (Bueno / Dañado / Extraviado) y haga clic en 'Registrar Devolución'.",
                    "Escaneo de Herramienta / Resguardo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Si es una herramienta disponible, seleccionarla para prestarla
            var disp = CboHerramientaDisponible.Items.Cast<HerramientaActivo>().FirstOrDefault(h => h.CodigoIndividual.Equals(code, StringComparison.OrdinalIgnoreCase));
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
            var sol = sols.FirstOrDefault(s => s.Folio.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (sol != null)
            {
                CambiarSeccion(3);
                CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(x => x.Id == sol.Id);
                return;
            }
        }

        // 4. ¿Es un Vale de Entrada (ENT-xxxx) o Vale de Salida (SAL-xxxx)? -> Abrir su vista previa impresa
        if (code.StartsWith("ENT-"))
        {
            var ent = DatabaseService.ObtenerEntradasAlmacen().FirstOrDefault(x => x.Folio.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (ent != null)
            {
                BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent);
                return;
            }
        }
        if (code.StartsWith("SAL-"))
        {
            var sal = DatabaseService.ObtenerSalidasAlmacen().FirstOrDefault(x => x.Folio.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (sal != null)
            {
                BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, sal);
                return;
            }
        }

        // 5. ¿Es un Producto del Catálogo (PRD-xxxx o Código de Barras EAN/UPC)?
        var prod = DatabaseService.BuscarProductoPorCodigo(code);
        if (prod != null)
        {
            // Si estamos en Almacén POS (Tab 5), agregar directo como caja registradora
            if (MainTabControl.SelectedIndex == 5)
            {
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
            $"No se encontró ningún documento o producto con el código '{code}'.\nPuede registrarlo como nuevo producto en el Catálogo Visual.",
            "Código no registrado", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ==================== TAB 0: DASHBOARD ====================

    private void RefrescarDashboard()
    {
        var sols = DatabaseService.ObtenerSolicitudes();
        var ocs = DatabaseService.ObtenerOrdenesCompra();
        var prods = DatabaseService.ObtenerProductos();
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
        string? filtro = TxtBuscarCatalogo?.Text;
        int? catId = (CboFiltroCategoria?.SelectedItem as Categoria)?.Id;
        var lista = DatabaseService.ObtenerProductos(filtro, catId);
        IcCatalogoVisual.ItemsSource = lista;
    }

    private void TxtBuscarCatalogo_TextChanged(object sender, TextChangedEventArgs e) => RefrescarCatalogoVisual();
    private void CboFiltroCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefrescarCatalogoVisual();

    private void AgregarAlCarritoTienda(Producto p, decimal cantidad)
    {
        var idx = _carritoTienda.FindIndex(x => x.producto.Id == p.Id);
        if (idx >= 0)
            _carritoTienda[idx] = (p, _carritoTienda[idx].cantidad + cantidad);
        else
            _carritoTienda.Add((p, cantidad));

        decimal totalItems = _carritoTienda.Sum(x => x.cantidad);
        TxtStatusCarrito.Text = $"Carrito: {totalItems:0.##} art. ({_carritoTienda.Count} SKUs)";
    }

    private void BtnAgregarProductoCarrito_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Producto p)
        {
            AgregarAlCarritoTienda(p, 1m);
        }
    }

    private void BtnEditarProductoCatalogo_Click(object sender, RoutedEventArgs e)
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
        TxtProdUnidadCompra.Text = p.UnidadCompra;
        TxtProdUnidadAlmacen.Text = p.UnidadAlmacen;
        TxtProdFactor.Text = p.FactorConversion.ToString("0.##", CultureInfo.InvariantCulture);
        TxtProdStock.Text = p.StockActual.ToString("0.##", CultureInfo.InvariantCulture);
        TxtProdStockMin.Text = p.StockMinimo.ToString("0.##", CultureInfo.InvariantCulture);
        TxtProdCosto.Text = p.UltimoCostoCompra.ToString("0.00", CultureInfo.InvariantCulture);
        TxtProdUbicacion.Text = p.Ubicacion;
        ChkProdEsHerramienta.IsChecked = p.EsHerramienta;

        var cat = CboProdCategoria.Items.Cast<Categoria>().FirstOrDefault(c => c.Id == p.CategoriaId);
        if (cat != null) CboProdCategoria.SelectedItem = cat;

        ImgPreviewProducto.Source = ImageStandardizerService.CargarImagenSegura(p.ImagenRuta);
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(p.CodigoEfectivo);
    }

    private void LimpiarEditorProducto()
    {
        string sig = DatabaseService.GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
        _productoEnEdicion = new Producto { CodigoInterno = sig, CodigoBarras = sig };
        TxtProdCodigoInterno.Text = sig;
        TxtProdCodigoBarras.Text = sig;
        TxtProdNombre.Text = string.Empty;
        TxtProdUnidadCompra.Text = "Caja (100 pzas)";
        TxtProdUnidadAlmacen.Text = "Pieza";
        TxtProdFactor.Text = "100";
        TxtProdStock.Text = "0";
        TxtProdStockMin.Text = "5";
        TxtProdCosto.Text = "0.00";
        TxtProdUbicacion.Text = "A-01";
        ChkProdEsHerramienta.IsChecked = false;
        ImgPreviewProducto.Source = null;
        ImgBarcodeProductoEditor.Source = BarcodePrintService.CrearImagenCodigoBarras(sig);
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

    private void BtnGuardarProducto_Click(object sender, RoutedEventArgs e)
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
        _productoEnEdicion.UnidadCompra = string.IsNullOrWhiteSpace(TxtProdUnidadCompra.Text) ? "Pieza" : TxtProdUnidadCompra.Text.Trim();
        _productoEnEdicion.UnidadAlmacen = string.IsNullOrWhiteSpace(TxtProdUnidadAlmacen.Text) ? "Pieza" : TxtProdUnidadAlmacen.Text.Trim();
        _productoEnEdicion.FactorConversion = ParseDecimal(TxtProdFactor.Text, 1m);
        _productoEnEdicion.StockActual = ParseDecimal(TxtProdStock.Text, 0m);
        _productoEnEdicion.StockMinimo = ParseDecimal(TxtProdStockMin.Text, 5m);
        _productoEnEdicion.UltimoCostoCompra = ParseDecimal(TxtProdCosto.Text, 0m);
        _productoEnEdicion.Ubicacion = TxtProdUbicacion.Text.Trim();
        _productoEnEdicion.EsHerramienta = ChkProdEsHerramienta.IsChecked == true;

        DatabaseService.GuardarProducto(_productoEnEdicion);
        CargarTodoElSistema();
        CargarProductoEnEditor(_productoEnEdicion);
    }

    private void BtnEliminarProducto_Click(object sender, RoutedEventArgs e)
    {
        if (_productoEnEdicion.Id == 0)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un producto guardado para eliminarlo.", "Producto no seleccionado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (System.Windows.MessageBox.Show(this, $"¿Desea eliminar el producto '{_productoEnEdicion.Nombre}' del catálogo?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseService.EliminarProducto(_productoEnEdicion.Id);
            BtnNuevoProducto_Click(sender, e);
            CargarTodoElSistema();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"No se puede eliminar este producto porque ya tiene movimientos vinculados.\n{ex.Message}", "No se puede eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnEnviarCarritoASolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (_carritoTienda.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Primero agregue productos al carrito haciendo clic en 'Agregar' en las tarjetas del catálogo.", "Carrito vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
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
                    StockActualAlmacen = prod.StockActual
                });
        }
        _carritoTienda.Clear();
        TxtStatusCarrito.Text = "Carrito: 0 artículos";
        DgPartidasNuevaSolicitud.Items.Refresh();
        CambiarSeccion(2);
    }

    private void BtnEnviarCarritoASalida_Click(object sender, RoutedEventArgs e)
    {
        if (_carritoTienda.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Primero agregue productos al carrito haciendo clic en 'Agregar'.", "Carrito vacío", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var (prod, cant) in _carritoTienda)
        {
            AgregarProductoAValeSalidaPos(prod, cant);
        }
        _carritoTienda.Clear();
        TxtStatusCarrito.Text = "Carrito: 0 artículos";
        CambiarSeccion(5);
    }

    private void BtnImprimirCatalogoCodigos_Click(object sender, RoutedEventArgs e)
    {
        var prods = DatabaseService.ObtenerProductos();
        var hers = DatabaseService.ObtenerHerramientas();
        BarcodePrintService.MostrarVistaPreviaCatalogoCodigosCarta(this, prods, hers);
    }

    // ==================== TAB 2: SOLICITUDES DE INGENIERÍA ====================

    private void CboSolDepartamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboSolDepartamento.SelectedItem is Departamento dep)
        {
            var encs = DatabaseService.ObtenerEncargados(dep.Id);
            CboSolEncargado.ItemsSource = encs;
            if (encs.Count > 0) CboSolEncargado.SelectedIndex = 0;
        }
    }

    private void RefrescarSolicitudes()
    {
        var sols = DatabaseService.ObtenerSolicitudes();
        DgHistorialSolicitudes.ItemsSource = sols;
        CboCompSolicitud.ItemsSource = sols;
        if (CboCompSolicitud.SelectedIndex < 0 && sols.Count > 0)
            CboCompSolicitud.SelectedIndex = 0;
    }

    private void BtnAgregarPartidaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (CboSolProductoAgregar.SelectedItem is not Producto prod)
            return;

        decimal cant = ParseDecimal(TxtSolCantidadAgregar.Text, 1m);
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
                StockActualAlmacen = prod.StockActual
            });
        }
    }

    private void BtnInterpretarWhatsapp_Click(object sender, RoutedEventArgs e)
    {
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
                        p.CodigoInterno.Contains(w, StringComparison.OrdinalIgnoreCase))
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (mejorProducto != null && mejorProducto.Score > 0)
            {
                var p = mejorProducto.Producto;
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
                agregados++;
            }
        }

        DgPartidasNuevaSolicitud.Items.Refresh();
        if (agregados > 0)
            TxtWhatsappInput.Clear();
        else
            System.Windows.MessageBox.Show(this, "No se encontraron coincidencias automáticas en el catálogo para ese texto. Puede elegirlos con el selector o darlos de alta en el Catálogo Visual.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
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
            DgPartidasNuevaSolicitud.Items.Refresh();
        }
    }

    private void BtnLimpiarPartidasSolicitud_Click(object sender, RoutedEventArgs e)
    {
        _partidasNuevaSolicitud.Clear();
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

        if (_partidasNuevaSolicitud.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Agregue al menos un producto a la solicitud.", "Solicitud vacía", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string origen = _partidasNuevaSolicitud.Any(p => p.NotasPartida.StartsWith("WhatsApp")) ? "WhatsApp" : "Catálogo";
        var creada = DatabaseService.CrearSolicitud(dep.Id, enc.Id, proy.Id, origen, TxtSolNotas.Text, _partidasNuevaSolicitud.ToList());

        _partidasNuevaSolicitud.Clear();
        TxtSolNotas.Clear();
        CargarTodoElSistema();

        // Pasar directo a la Comparativa con la solicitud recién creada
        CambiarSeccion(3);
        CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == creada.Id);
    }

    private void BtnEnviarSolicitudAComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (DgHistorialSolicitudes.SelectedItem is Solicitud sol)
        {
            CambiarSeccion(3);
            CboCompSolicitud.SelectedItem = CboCompSolicitud.Items.Cast<Solicitud>().FirstOrDefault(s => s.Id == sol.Id);
        }
    }

    // ==================== TAB 3: COMPARATIVA DE COTIZACIONES ====================

    private void CboCompSolicitud_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol)
            return;

        TxtCompInfoSolicitud.Text = $"Solicitud: {sol.Folio} | Depto: {sol.DepartamentoNombre} | Encargado: {sol.EncargadoNombre} | Proyecto: {sol.ProyectoNombre}";
        _renglonesComparativa.Clear();

        var productos = DatabaseService.ObtenerProductos().ToDictionary(p => p.Id);
        foreach (var part in sol.Partidas)
        {
            decimal precioBaseSinIva = 100m;
            if (productos.TryGetValue(part.ProductoId, out var pObj) && pObj.UltimoCostoCompra > 0)
            {
                precioBaseSinIva = Math.Round(pObj.UltimoCostoCompra / 1.16m, 2);
            }

            _renglonesComparativa.Add(new CotizacionRenglonComparativo
            {
                SolicitudPartidaId = part.Id,
                ProductoId = part.ProductoId,
                ProductoCodigo = part.ProductoCodigo,
                ProductoNombre = part.ProductoNombre,
                UnidadCompra = part.UnidadCompra,
                Cantidad = part.CantidadSolicitada,
                PrecioSinIva1 = precioBaseSinIva,
                PrecioSinIva2 = Math.Round(precioBaseSinIva * 1.04m, 2),
                PrecioSinIva3 = Math.Round(precioBaseSinIva * 0.97m, 2),
                ProveedorGanadorIndice = 3
            });
        }

        RecalcularTotalesComparativa();
    }

    private void CboCompProveedores_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalcularTotalesComparativa();
    private void TxtCompTotales_TextChanged(object sender, TextChangedEventArgs e) => RecalcularTotalesComparativa();
    private void BtnRecalcularComparativa_Click(object sender, RoutedEventArgs e) => RecalcularTotalesComparativa();

    private void DgComparativa_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(RecalcularTotalesComparativa), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RecalcularTotalesComparativa()
    {
        if (TxtResumenProv1 == null || _renglonesComparativa.Count == 0)
            return;

        DgComparativa.Items.Refresh();

        decimal flete1 = ParseDecimal(TxtCompFlete1.Text, 0m);
        decimal flete2 = ParseDecimal(TxtCompFlete2.Text, 0m);
        decimal flete3 = ParseDecimal(TxtCompFlete3.Text, 0m);

        decimal sub1 = _renglonesComparativa.Sum(r => Math.Round(r.Cantidad * r.PrecioSinIva1, 2));
        decimal sub2 = _renglonesComparativa.Sum(r => Math.Round(r.Cantidad * r.PrecioSinIva2, 2));
        decimal sub3 = _renglonesComparativa.Sum(r => Math.Round(r.Cantidad * r.PrecioSinIva3, 2));

        decimal tot1 = Math.Round(sub1 * 1.16m + flete1, 2);
        decimal tot2 = Math.Round(sub2 * 1.16m + flete2, 2);
        decimal tot3 = Math.Round(sub3 * 1.16m + flete3, 2);

        TxtResumenProv1.Text = $"Subtotal: ${sub1:N2} | Total: ${tot1:N2}";
        TxtResumenProv2.Text = $"Subtotal: ${sub2:N2} | Total: ${tot2:N2}";
        TxtResumenProv3.Text = $"Subtotal: ${sub3:N2} | Total: ${tot3:N2}";
    }

    private void BtnSeleccionarMejorPrecioPorPartida_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _renglonesComparativa)
        {
            var opciones = new[]
            {
                (idx: 1, precio: r.PrecioSinIva1 > 0 ? r.PrecioSinIva1 : decimal.MaxValue),
                (idx: 2, precio: r.PrecioSinIva2 > 0 ? r.PrecioSinIva2 : decimal.MaxValue),
                (idx: 3, precio: r.PrecioSinIva3 > 0 ? r.PrecioSinIva3 : decimal.MaxValue)
            };
            r.ProveedorGanadorIndice = opciones.OrderBy(o => o.precio).First().idx;
        }
        RecalcularTotalesComparativa();
    }

    private void BtnGenerarOrdenesDesdeComparativa_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
            return;

        var config = new Dictionary<int, (Proveedor? prov, decimal fleteConIva, int diasEntrega)>
        {
            [1] = (CboCompProv1.SelectedItem as Proveedor, ParseDecimal(TxtCompFlete1.Text, 0m), (int)ParseDecimal(TxtCompDias1.Text, 2m)),
            [2] = (CboCompProv2.SelectedItem as Proveedor, ParseDecimal(TxtCompFlete2.Text, 0m), (int)ParseDecimal(TxtCompDias2.Text, 4m)),
            [3] = (CboCompProv3.SelectedItem as Proveedor, ParseDecimal(TxtCompFlete3.Text, 0m), (int)ParseDecimal(TxtCompDias3.Text, 1m))
        };

        string folioComp = $"COT-{DateTime.Now:yyyy}-{sol.Id:D4}";
        var ordenesCreadas = DatabaseService.GenerarOrdenesDesdeComparativa(sol, folioComp, _renglonesComparativa.ToList(), config);

        CargarTodoElSistema();
        CambiarSeccion(4);

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
        DgPrestamosHerramientas.ItemsSource = filas;
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
        var ocs = DatabaseService.ObtenerOrdenesCompra();
        CboXmlOrdenCompra.ItemsSource = ocs;
        if (CboXmlOrdenCompra.SelectedIndex < 0 && ocs.Count > 0)
            CboXmlOrdenCompra.SelectedIndex = 0;

        DgFacturasRegistradas.ItemsSource = DatabaseService.ObtenerFacturasXml();
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

    private void CargarArchivoXmlEnPantalla(string rutaXml)
    {
        try
        {
            _facturaXmlEnPantalla = XmlCfdiService.LeerArchivoCfdiXml(rutaXml);
            TxtXmlResumenActual.Text = $"UUID: {_facturaXmlEnPantalla.Uuid} | Emisor: {_facturaXmlEnPantalla.EmisorNombre} ({_facturaXmlEnPantalla.EmisorRfc}) | Subtotal: ${_facturaXmlEnPantalla.Subtotal:N2} | IVA: ${_facturaXmlEnPantalla.Iva:N2} | Total: ${_facturaXmlEnPantalla.Total:N2}";
            DgXmlConceptosActual.ItemsSource = _facturaXmlEnPantalla.Conceptos;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Error al leer el archivo XML:\n{ex.Message}", "Error XML", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnVincularConceptoXmlConProducto_Click(object sender, RoutedEventArgs e)
    {
        if (_facturaXmlEnPantalla == null ||
            DgXmlConceptosActual.SelectedItem is not FacturaConcepto concepto ||
            CboXmlVincularProducto.SelectedItem is not Producto prod)
        {
            System.Windows.MessageBox.Show(this, "Seleccione un concepto en la tabla y el producto de su catálogo con el que desea relacionarlo.", "Vincular concepto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        concepto.ProductoIdVinculado = prod.Id;
        concepto.ProductoVinculadoNombre = prod.Nombre;
        DatabaseService.GuardarMapaProductoProveedor(_facturaXmlEnPantalla.EmisorRfc, concepto.NoIdentificacion, concepto.Descripcion, prod.Id, concepto.ValorUnitario);
        DgXmlConceptosActual.Items.Refresh();
    }

    private void BtnGuardarFacturaXml_Click(object sender, RoutedEventArgs e)
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

        if (CboXmlOrdenCompra.SelectedItem is OrdenCompra oc)
        {
            _facturaXmlEnPantalla.OrdenCompraId = oc.Id;
            _facturaXmlEnPantalla.OrdenCompraFolio = oc.Folio;
        }

        foreach (var c in _facturaXmlEnPantalla.Conceptos.Where(x => x.ProductoIdVinculado.HasValue))
        {
            DatabaseService.GuardarMapaProductoProveedor(
                _facturaXmlEnPantalla.EmisorRfc,
                c.NoIdentificacion,
                c.Descripcion,
                c.ProductoIdVinculado!.Value,
                c.ValorUnitario);
        }

        DatabaseService.GuardarFacturaXml(_facturaXmlEnPantalla);
        _facturaXmlEnPantalla = null;
        DgXmlConceptosActual.ItemsSource = null;
        TxtXmlResumenActual.Text = "Factura XML registrada y vinculada exitosamente.";
        CargarTodoElSistema();
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
            .GroupBy(p => new { p.ProductoId, p.ProductoCodigo, p.ProductoNombre, p.UnidadCompra })
            .Select(g => new
            {
                Codigo = g.Key.ProductoCodigo,
                Producto = g.Key.ProductoNombre,
                Frecuencia = $"{g.Count()} OCs",
                CantidadTotal = $"{g.Sum(x => x.CantidadPedida):0.##} {g.Key.UnidadCompra}",
                ImporteTotal = g.Sum(x => x.ImporteConIva)
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
        }).ToList();
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
    }

    private void BtnNuevaCategoriaDir_Click(object sender, RoutedEventArgs e)
    {
        _categoriaEditandoId = 0;
        DgDirCategorias.SelectedItem = null;
        TxtDirCatNombre.Clear();
    }

    private void BtnGuardarCategoriaDir_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDirCatNombre.Text))
        {
            System.Windows.MessageBox.Show(this, "Escriba el nombre de la categoría.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DatabaseService.GuardarCategoria(TxtDirCatNombre.Text, _categoriaEditandoId);
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
