using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;
using SistemaComprasAlmacen.Services;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SistemaComprasAlmacen;

/// <summary>
/// Catálogo: panel de producto con pestañas "Ficha" (lectura) y "Editar", y vista de historial detallado.
/// </summary>
public partial class MainWindow
{
    private const double AnchoPanelFicha = 390;
    private const double AnchoPanelEdicion = 540;

    private Producto? _productoEnFicha;

    // ---------------------------------------------------------------
    // Pestañas Ficha / Editar
    // ---------------------------------------------------------------

    private void TcPanelProducto_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Los ComboBox del formulario también burbujean SelectionChanged: solo atender al TabControl
        if (!ReferenceEquals(e.OriginalSource, TcPanelProducto)) return;

        AjustarAnchoPanelProducto();
        if (TcPanelProducto.SelectedIndex == 0)
            ActualizarFichaProducto(_productoEnEdicion);
    }

    private void AjustarAnchoPanelProducto()
    {
        if (ColEditorCatalogo == null || PnlPanelProducto == null || TcPanelProducto == null) return;
        if (PnlPanelProducto.Visibility != Visibility.Visible) return;
        ColEditorCatalogo.Width = new GridLength(TcPanelProducto.SelectedIndex == 1 ? AnchoPanelEdicion : AnchoPanelFicha);
    }

    private void SeleccionarPestanaPanelProducto(bool editar)
    {
        if (TcPanelProducto == null) return;
        TcPanelProducto.SelectedIndex = editar ? 1 : 0;
        AjustarAnchoPanelProducto();
    }

    // ---------------------------------------------------------------
    // Ficha de lectura
    // ---------------------------------------------------------------

    private void ActualizarFichaProducto(Producto? p)
    {
        if (TxtFichaVacia == null || PnlFichaContenido == null) return;

        _productoEnFicha = p == null || p.Id == 0 ? null : p;
        bool hay = _productoEnFicha != null;
        TxtFichaVacia.Visibility = hay ? Visibility.Collapsed : Visibility.Visible;
        PnlFichaContenido.Visibility = hay ? Visibility.Visible : Visibility.Collapsed;
        if (!hay) return;

        var x = _productoEnFicha!;
        TxtFichaNombre.Text = x.Nombre;

        var partesCodigo = new List<string> { x.CodigoInterno };
        if (!string.IsNullOrWhiteSpace(x.Familia)) partesCodigo.Add(x.Familia);
        if (!string.IsNullOrWhiteSpace(x.CategoriaNombre)) partesCodigo.Add(x.CategoriaNombre);
        TxtFichaCodigo.Text = string.Join("  ·  ", partesCodigo);

        ImgFichaProducto.Source = ImageStandardizerService.CargarImagenSegura(x.ImagenRuta);

        TxtFichaExistencia.Text = $"{x.StockActual:0.##} {x.UnidadAlmacen}";
        string detalleStock = $"Mínimo {x.StockMinimo:0.##}";
        if (x.StockReservadoProyecto > 0)
            detalleStock += $"  ·  Reservado {x.StockReservadoProyecto:0.##}  ·  Libre {x.StockLibre:0.##}";
        TxtFichaStockDetalle.Text = detalleStock;

        if (x.StockActual <= 0)
            AplicarEstadoStockFicha("Agotado", "#FEE2E2", "#991B1B");
        else if (x.StockBajo)
            AplicarEstadoStockFicha("Stock bajo", "#FEF3C7", "#92400E");
        else
            AplicarEstadoStockFicha("Disponible", "#DCFCE7", "#166534");

        TxtFichaUbicacion.Text = string.IsNullOrWhiteSpace(x.Ubicacion) ? "Sin ubicación" : x.Ubicacion.Trim();

        ConstruirDetallesFicha(x);
    }

    private void AplicarEstadoStockFicha(string texto, string fondo, string texto2)
    {
        TxtFichaEstadoStock.Text = texto;
        BdrFichaEstadoStock.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(fondo)!;
        TxtFichaEstadoStock.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(texto2)!;
    }

    private void ConstruirDetallesFicha(Producto x)
    {
        PnlFichaDetalles.Children.Clear();

        string etiqueta1 = LblProdCampo1?.Text ?? "Especificación 1";
        string etiqueta2 = LblProdCampo2?.Text ?? "Especificación 2";
        string etiqueta3 = LblProdCampo3?.Text ?? "Especificación 3";

        AgregarSeccionFicha("Ficha técnica", new (string, string?)[]
        {
            ("Marca", x.Marca),
            ("Modelo / N° parte", x.Modelo),
            ("Medidas", x.Medidas),
            (etiqueta1, x.CapacidadElectrica),
            (etiqueta2, x.Especificacion1),
            (etiqueta3, x.Especificacion2),
            ("Notas", x.Descripcion)
        });

        AgregarSeccionFicha(EsModoAlmacen ? "Unidades y conversión" : "Compra y conversión", new (string, string?)[]
        {
            ("Unidad de compra", x.UnidadCompra),
            ("Unidad de entrega", x.UnidadAlmacen),
            ("Conversión", x.ConversionTexto),
            ("Último costo (c/IVA)", !EsModoAlmacen && x.UltimoCostoCompra > 0 ? x.UltimoCostoCompra.ToString("C2") : null),
            ("Costo por unidad de entrega", !EsModoAlmacen && x.UltimoCostoCompra > 0 ? x.CostoUnitarioAlmacen.ToString("C2") : null),
            ("Código de barras", x.CodigoEfectivo),
            ("Tipo", x.EsHerramienta ? "Herramienta de resguardo" : null)
        });

        if (!EsModoAlmacen && x.Id > 0)
        {
            var cotizaciones = DatabaseService.ObtenerCotizacionesBancoHistorico(productoId: x.Id);
            if (cotizaciones.Count > 0)
            {
                var filasCot = cotizaciones
                    .GroupBy(c => c.ProveedorId)
                    .Select(g =>
                    {
                        var ult = g.First();
                        return (ult.ProveedorNombre, (string?)$"{ult.PrecioSinIva:C2} s/IVA ({ult.PrecioConIva:C2} c/IVA) · {ult.EstadoVigenciaCorto}");
                    })
                    .Take(5)
                    .ToList();
                AgregarSeccionFicha("Cotizaciones y vigencias", filasCot);
            }
        }
    }

    private void AgregarSeccionFicha(string titulo, IEnumerable<(string Etiqueta, string? Valor)> filas)
    {
        var validas = filas.Where(f => !string.IsNullOrWhiteSpace(f.Valor)).ToList();
        if (validas.Count == 0) return;

        var encabezado = new TextBlock
        {
            Text = titulo,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            Margin = new Thickness(0, 12, 0, 6)
        };
        PnlFichaDetalles.Children.Add(encabezado);
        PnlFichaDetalles.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 0, 6),
            Background = Tk("ControlStrokeColorDefaultBrush")
        });

        foreach (var (etiqueta, valor) in validas)
        {
            var fila = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            fila.Children.Add(new TextBlock
            {
                Text = etiqueta,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Tk("TextFillColorSecondaryBrush")
            });

            var txtValor = new TextBlock
            {
                Text = valor,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(txtValor, 1);
            fila.Children.Add(txtValor);

            PnlFichaDetalles.Children.Add(fila);
        }
    }

    // ---------------------------------------------------------------
    // Acciones de la ficha
    // ---------------------------------------------------------------

    private void BtnFichaAgregarSolicitud_Click(object sender, RoutedEventArgs e)
    {
        if (_productoEnFicha != null) AgregarAlCarritoTienda(_productoEnFicha, 1m);
    }

    private void BtnFichaEnviarSalida_Click(object sender, RoutedEventArgs e)
    {
        if (_productoEnFicha == null) return;
        AgregarProductoAValeSalidaPos(_productoEnFicha, 1m);
        CambiarSeccion(5);
    }

    private void BtnFichaEditar_Click(object sender, RoutedEventArgs e) =>
        SeleccionarPestanaPanelProducto(true);

    private void BtnFichaMapa_Click(object sender, RoutedEventArgs e)
    {
        var p = _productoEnFicha;
        if (p == null) return;

        MostrarSelectorYMapaAnaqueles(p.Ubicacion, nuevaCoord =>
        {
            DatabaseService.ActualizarUbicacionProducto(p.Id, nuevaCoord, sincronizarHerramientas: true);
            p.Ubicacion = nuevaCoord;
            if (_productoEnEdicion.Id == p.Id && TxtProdUbicacion != null)
                TxtProdUbicacion.Text = nuevaCoord;
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            ActualizarFichaProducto(p);
        }, this);
    }

    private void BtnFichaHistorial_Click(object sender, RoutedEventArgs e)
    {
        if (_productoEnFicha != null) MostrarHistorialProducto(_productoEnFicha);
    }

    // ---------------------------------------------------------------
    // Historial detallado del producto (reemplaza el área del catálogo)
    // ---------------------------------------------------------------

    private void MostrarHistorialProducto(Producto p)
    {
        TxtHistProdTitulo.Text = p.Nombre;
        string ubic = string.IsNullOrWhiteSpace(p.Ubicacion) ? "sin ubicación" : p.Ubicacion.Trim();
        TxtHistProdResumen.Text = $"{p.CodigoInterno}  ·  Existencia {p.StockActual:0.##} {p.UnidadAlmacen}  ·  Ubicación {ubic}";
        ImgHistProducto.Source = ImageStandardizerService.CargarImagenSegura(p.ImagenRuta);

        // --- Precios de compra ---
        var precios = DatabaseService.ObtenerHistorialComprasProductoParaAnalisis(p.Id)
            .OrderByDescending(x => x.FechaOrden)
            .ToList();
        DgHistPrecios.ItemsSource = precios;
        if (precios.Count > 0)
        {
            var conIva = precios.Select(x => x.PrecioUnitarioConIva).ToList();
            TxtHistPreciosResumen.Text =
                $"Último: {precios[0].PrecioUnitarioConIva:C2}  ·  Mínimo: {conIva.Min():C2}  ·  Máximo: {conIva.Max():C2}  ·  Promedio: {conIva.Average():C2}   (precios c/IVA por unidad de compra)";
        }
        else
        {
            TxtHistPreciosResumen.Text = "Este producto aún no tiene compras registradas.";
        }
        TabHistPrecios.Header = $"Precios de compra ({precios.Count})";

        // --- Cotizaciones y vigencias (incluye borradores y opciones no adjudicadas) ---
        var cotizaciones = DatabaseService.ObtenerCotizacionesBancoHistorico(productoId: p.Id);
        if (DgHistCotizacionesProducto != null)
            DgHistCotizacionesProducto.ItemsSource = cotizaciones;
        if (TabHistCotizaciones != null)
            TabHistCotizaciones.Header = $"Cotizaciones y Vigencias ({cotizaciones.Count})";

        // --- Entradas ---
        var entradas = DatabaseService.ObtenerEntradasAlmacen()
            .SelectMany(en => en.Partidas
                .Where(pp => pp.ProductoId == p.Id)
                .Select(pp => new FilaHistorialEntrada
                {
                    Fecha = en.Fecha,
                    Folio = en.Folio,
                    OrdenCompra = string.IsNullOrWhiteSpace(en.OrdenCompraFolio) ? "Directa" : en.OrdenCompraFolio,
                    Proveedor = en.ProveedorNombre,
                    Cantidad = $"{pp.CantidadIngresadaAlmacen:0.##} {pp.UnidadAlmacen}",
                    Tipo = en.TipoEntrada
                }))
            .OrderByDescending(x => x.Fecha)
            .ToList();
        DgHistEntradas.ItemsSource = entradas;
        TabHistEntradas.Header = $"Entradas ({entradas.Count})";

        // --- Salidas y devoluciones ---
        var salidas = DatabaseService.ObtenerSalidasAlmacen()
            .SelectMany(s => s.Partidas
                .Where(pp => pp.ProductoId == p.Id)
                .Select(pp => new FilaHistorialSalida
                {
                    Fecha = s.Fecha,
                    Folio = s.Folio,
                    Recibe = s.PersonaRecibeEfectiva,
                    Proyecto = s.ProyectoNombre,
                    Entregado = pp.CantidadEntregada,
                    Devuelto = pp.CantidadDevuelta
                }))
            .OrderByDescending(x => x.Fecha)
            .ToList();
        DgHistSalidas.ItemsSource = salidas;
        TabHistSalidas.Header = $"Salidas y devoluciones ({salidas.Count})";

        // --- Préstamos (solo herramientas) ---
        var idsHerramienta = DatabaseService.ObtenerHerramientas()
            .Where(h => h.ProductoId == p.Id)
            .Select(h => h.Id)
            .ToHashSet();
        var prestamos = DatabaseService.ObtenerPrestamosHerramienta()
            .SelectMany(pr => pr.Partidas
                .Where(pp => idsHerramienta.Contains(pp.HerramientaActivoId))
                .Select(pp => new FilaHistorialPrestamo
                {
                    FechaSalida = pr.FechaSalida,
                    Folio = pr.Folio,
                    CodigoHerramienta = pp.CodigoIndividual,
                    Recibe = pr.PersonaRecibeEfectiva,
                    Proyecto = pr.ProyectoNombre,
                    Estado = pp.EstadoDevolucion,
                    FechaDevuelto = pp.FechaDevuelto
                }))
            .OrderByDescending(x => x.FechaSalida)
            .ToList();
        DgHistPrestamos.ItemsSource = prestamos;
        TabHistPrestamos.Header = $"Préstamos ({prestamos.Count})";
        TabHistPrestamos.Visibility = p.EsHerramienta || prestamos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (EsModoAlmacen && TabHistEntradas?.Parent is TabControl tcHist && (tcHist.SelectedIndex == 0 || tcHist.SelectedIndex == 1))
            tcHist.SelectedItem = TabHistEntradas;

        PnlCatalogoPrincipal.Visibility = Visibility.Collapsed;
        PnlHistorialProducto.Visibility = Visibility.Visible;
    }

    private void BtnVolverCatalogoDesdeHistorial_Click(object sender, RoutedEventArgs e)
    {
        PnlHistorialProducto.Visibility = Visibility.Collapsed;
        PnlCatalogoPrincipal.Visibility = Visibility.Visible;
    }
}
