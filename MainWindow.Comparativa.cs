using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using SistemaComprasAlmacen.Models;

namespace SistemaComprasAlmacen;

/// <summary>Comparativa: navegación por pasos (1 Artículos, 2 Comparar proveedores, 3 Emitir órdenes).</summary>
public partial class MainWindow
{
    private bool _cargandoProveedoresMatriz;
    private readonly Dictionary<(int Solicitud, string Partida), int[]> _seleccionProveedoresMatriz = new();

    private sealed record OfertaArticulo(int Opcion, int ProveedorId, string Proveedor, decimal Precio,
        int Entrega, string Vigencia, decimal Flete, bool Adjudicada, bool PuedeAdjudicar)
    {
        public string Estado => Adjudicada ? "Adjudicada" : Precio > 0 ? "Cotizada" : "Sin precio";
    }

    private static IEnumerable<OfertaArticulo> ObtenerOfertasArticulo(CotizacionRenglonComparativo r)
    {
        OfertaArticulo Oferta(int opcion, int id, string nombre, decimal precio, int entrega, int vigencia, string fecha, decimal flete) =>
            new(opcion, id, nombre, precio, entrega,
                CotizacionRenglonComparativo.FormatearVigencia(string.IsNullOrWhiteSpace(fecha) ? r.FechaCotizacion : fecha, vigencia, true),
                flete, r.ProveedorGanadorIndice == opcion, !r.TieneOcGenerada && precio > 0 && r.ProveedorGanadorIndice != opcion);

        if (r.Proveedor1Id > 0) yield return Oferta(1, r.Proveedor1Id, r.Proveedor1Nombre, r.PrecioSinIva1, r.DiasEntrega1, r.DiasVigencia1, r.FechaCotizacion1, r.FleteConIva1);
        if (r.Proveedor2Id > 0) yield return Oferta(2, r.Proveedor2Id, r.Proveedor2Nombre, r.PrecioSinIva2, r.DiasEntrega2, r.DiasVigencia2, r.FechaCotizacion2, r.FleteConIva2);
        if (r.Proveedor3Id > 0) yield return Oferta(3, r.Proveedor3Id, r.Proveedor3Nombre, r.PrecioSinIva3, r.DiasEntrega3, r.DiasVigencia3, r.FechaCotizacion3, r.FleteConIva3);
        foreach (var ex in r.ProveedoresExtra.Where(x => x.ProveedorId > 0))
            yield return Oferta(ex.NumeroOpcion, ex.ProveedorId, ex.ProveedorNombre, ex.PrecioSinIva, ex.DiasEntrega, ex.DiasVigencia, ex.FechaCotizacion, ex.FleteConIva);
    }

    private void MostrarOfertasArticulo(CotizacionRenglonComparativo? r)
    {
        if (DgCompOfertasArticulo == null) return;
        var ofertas = r == null ? new List<OfertaArticulo>() : ObtenerOfertasArticulo(r)
            .OrderBy(o => o.Precio <= 0).ThenBy(o => o.Precio).ThenBy(o => o.Proveedor).ToList();
        DgCompOfertasArticulo.ItemsSource = ofertas;
        TxtCompSinOfertas.Visibility = ofertas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnAdjudicarOfertaArticulo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OfertaArticulo oferta } && oferta.PuedeAdjudicar)
            ElegirOpcionGanadoraProductoActual(oferta.Opcion);
    }

    private sealed record ProveedorMatriz(int Id, string Nombre, int Cotizados = 0, int Articulos = 0)
    {
        public string Etiqueta => Id == 0 ? Nombre : $"{Nombre} ({Cotizados}/{Articulos})";
    }

    private sealed class FilaMatrizProveedor
    {
        public CotizacionRenglonComparativo Articulo { get; }
        public RenglonHojaCotizacionProveedor?[] Ofertas { get; }

        public FilaMatrizProveedor(CotizacionRenglonComparativo articulo, IReadOnlyList<ProveedorMatriz?> proveedores,
            Action<RenglonHojaCotizacionProveedor> guardar)
        {
            Articulo = articulo;
            Ofertas = proveedores.Select(p => p == null ? null :
                new RenglonHojaCotizacionProveedor(articulo, 4, guardar, p.Id, p.Nombre)
                { ConservarProveedoresAsignados = true }).ToArray();
        }

        public decimal? Precio1 { get => Precio(0); set => CambiarPrecio(0, value); }
        public decimal? Precio2 { get => Precio(1); set => CambiarPrecio(1, value); }
        public decimal? Precio3 { get => Precio(2); set => CambiarPrecio(2, value); }
        public decimal? Total1 => Total(0);
        public decimal? Total2 => Total(1);
        public decimal? Total3 => Total(2);

        private decimal? Precio(int indice) => Ofertas[indice]?.PrecioSinIva is decimal precio && precio > 0 ? precio : null;
        private decimal? Total(int indice) => Precio(indice).HasValue ? Ofertas[indice]?.ImporteTotalConIva : null;
        private void CambiarPrecio(int indice, decimal? valor)
        {
            if (!Articulo.TieneOcGenerada && Ofertas[indice] is { } oferta &&
                oferta.PrecioSinIva != (valor ?? 0m))
                oferta.PrecioSinIva = valor ?? 0m;
        }
    }

    private (int Solicitud, string Partida) ClaveSeleccionMatriz =>
        ((CboCompSolicitud.SelectedItem as Solicitud)?.Id ?? 0,
            (CboCompFiltroPartida.SelectedItem as OpcionPartida)?.Nombre.ToUpperInvariant() ?? string.Empty);

    private ComboBox[] SelectoresProveedoresMatriz => new[] { CboMatrizProveedor1, CboMatrizProveedor2, CboMatrizProveedor3 };

    private void CboMatrizProveedor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoProveedoresMatriz || sender is not ComboBox selector) return;
        ConfirmarEdicionesPendientesComparativa();
        var ids = SelectoresProveedoresMatriz.Select(c => (c.SelectedItem as ProveedorMatriz)?.Id ?? 0).ToArray();
        int indice = Array.IndexOf(SelectoresProveedoresMatriz, selector);
        for (int i = 0; i < ids.Length; i++)
            if (i != indice && ids[i] == ids[indice]) ids[i] = 0;
        _seleccionProveedoresMatriz[ClaveSeleccionMatriz] = ids;
        ConstruirMatrizComparativaRapida();
    }

    private void ConstruirMatrizPorProveedor()
    {
        var articulos = ArticulosComparativaVisibles.ToList();
        var proveedores = articulos.SelectMany(ObtenerOfertasArticulo)
            .GroupBy(o => o.ProveedorId)
            .OrderByDescending(g => g.Count(o => o.Precio > 0))
            .ThenBy(g => g.First().Proveedor, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new ProveedorMatriz(g.Key, g.First().Proveedor,
                articulos.Count(r => ObtenerOfertasArticulo(r).Any(o => o.ProveedorId == g.Key && o.Precio > 0)), articulos.Count)).ToList();
        var opciones = new List<ProveedorMatriz> { new(0, "Sin proveedor") };
        opciones.AddRange(proveedores);
        var ids = _seleccionProveedoresMatriz.TryGetValue(ClaveSeleccionMatriz, out var seleccion)
            ? seleccion : proveedores.Select(p => p.Id).Take(3).ToArray();
        var visibles = new ProveedorMatriz?[3];
        var precios = new[] { ColMatrizProv1, ColMatrizProv2, ColMatrizProv3 };
        var totales = new[] { ColMatrizTotal1, ColMatrizTotal2, ColMatrizTotal3 };
        var selectores = SelectoresProveedoresMatriz;
        _cargandoProveedoresMatriz = true;
        try
        {
            for (int i = 0; i < selectores.Length; i++)
            {
                var proveedor = opciones.FirstOrDefault(p => p.Id == ids.ElementAtOrDefault(i)) ?? opciones[0];
                visibles[i] = proveedor.Id > 0 ? proveedor : null;
                selectores[i].ItemsSource = opciones;
                selectores[i].SelectedItem = proveedor;
                selectores[i].IsEnabled = proveedores.Count > 0;
                precios[i].Header = $"P.U. {proveedor.Nombre}";
                totales[i].Header = $"Total {proveedor.Nombre}";
                precios[i].Visibility = proveedor.Id > 0 ? Visibility.Visible : Visibility.Collapsed;
                totales[i].Visibility = proveedor.Id > 0 && ChkMatrizMostrarTotalConIva.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        finally { _cargandoProveedoresMatriz = false; }

        void GuardarPrecio(RenglonHojaCotizacionProveedor fila) =>
            Dispatcher.BeginInvoke(new Action(() => AlModificarCeldaHojaProveedor(fila, usarProveedorSeleccionado: false)));

        var filas = articulos.Select(r => new FilaMatrizProveedor(r, visibles, GuardarPrecio)).ToList();
        DgMatrizComparativaRapida.ItemsSource = filas;
        DgMatrizComparativaRapida.SelectedItem = filas.FirstOrDefault(f => ReferenceEquals(f.Articulo, DgComparativa.SelectedItem));
        TxtMatrizSinProveedores.Visibility = proveedores.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _cargandoFiltroPartidas;
    private int _solicitudFiltroPartidasId;

    private IEnumerable<CotizacionRenglonComparativo> ArticulosComparativaVisibles =>
        CboCompFiltroPartida?.SelectedItem is OpcionPartida partida
            ? _renglonesComparativa.Where(r => string.Equals(r.NombrePartida, partida.Nombre, StringComparison.OrdinalIgnoreCase))
            : _renglonesComparativa;

    private sealed record OpcionPartida(string Nombre)
    {
        public override string ToString() => Nombre.Length == 0 ? "Sin partida asignada" : Nombre;
    }

    private void CargarPartidasComparativa(Solicitud? solicitud)
    {
        if (PnlCompFiltroPartida == null) return;
        bool usaPartidas = solicitud?.UsaPartidas == true;
        PnlCompFiltroPartida.Visibility = usaPartidas ? Visibility.Visible : Visibility.Collapsed;
        _cargandoFiltroPartidas = true;
        try
        {
            string? anterior = solicitud?.Id == _solicitudFiltroPartidasId
                ? (CboCompFiltroPartida.SelectedItem as OpcionPartida)?.Nombre : null;
            _solicitudFiltroPartidasId = solicitud?.Id ?? 0;
            var nombres = usaPartidas
                ? solicitud!.Partidas.Select(p => p.NombrePartida)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => new OpcionPartida(n)).ToList()
                : null;
            CboCompFiltroPartida.ItemsSource = nombres;
            CboCompFiltroPartida.SelectedItem = nombres?.FirstOrDefault(n => string.Equals(n.Nombre, anterior, StringComparison.OrdinalIgnoreCase) &&
                    _renglonesComparativa.Any(r => string.Equals(r.NombrePartida, n.Nombre, StringComparison.OrdinalIgnoreCase)))
                ?? nombres?.FirstOrDefault(n => _renglonesComparativa.Any(r => string.Equals(r.NombrePartida, n.Nombre, StringComparison.OrdinalIgnoreCase)))
                ?? nombres?.FirstOrDefault();
        }
        finally { _cargandoFiltroPartidas = false; }
        ActualizarMensajePartidaComparativa();
    }

    private void ActualizarMensajePartidaComparativa()
    {
        if (TxtCompFiltroPartida == null) return;
        int cantidad = ArticulosComparativaVisibles.Count();
        TxtCompFiltroPartida.Text = cantidad == 0
            ? "Esta partida está vacía."
            : $"Artículos disponibles: {cantidad}";
    }

    private void CboCompFiltroPartida_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoFiltroPartidas) return;
        ConfirmarEdicionesPendientesComparativa();
        DgComparativa.SelectedItem = ArticulosComparativaVisibles.FirstOrDefault();
        CargarProductoEnTarjetasComparativa(DgComparativa.SelectedItem as CotizacionRenglonComparativo);
        ActualizarMensajePartidaComparativa();
        RefrescarHojaProveedorYAnalisisComparativa();
    }

    private void AbrirDetalleArticuloComparativa()
    {
        if (DgComparativa.SelectedItem is CotizacionRenglonComparativo articulo &&
            CboCompFiltroPartida.Items.OfType<OpcionPartida>().FirstOrDefault(n =>
                string.Equals(n.Nombre, articulo.NombrePartida, StringComparison.OrdinalIgnoreCase)) is OpcionPartida partida)
        {
            CboCompFiltroPartida.SelectedItem = partida;
            DgComparativa.SelectedItem = articulo;
        }
        TcComparativa.SelectedIndex = 1;
    }

    private void DgComparativa_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Solo al hacer clic sobre una fila (no en encabezados ni al editar la cantidad)
        if (FilaClicada<object>(e) != null && Keyboard.FocusedElement is not TextBox)
            AbrirDetalleArticuloComparativa();
    }

    private void DgComparativa_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DgComparativa.SelectedItem != null && Keyboard.FocusedElement is not TextBox)
        {
            AbrirDetalleArticuloComparativa();
            e.Handled = true;
        }
    }

    private void BtnCompAnterior_Click(object sender, System.Windows.RoutedEventArgs e) => MoverSeleccionComparativa(-1);

    private void BtnCompSiguiente_Click(object sender, System.Windows.RoutedEventArgs e) => MoverSeleccionComparativa(1);

    private void MoverSeleccionComparativa(int delta)
    {
        var visibles = ArticulosComparativaVisibles.ToList();
        int total = visibles.Count;
        if (total == 0) return;

        int actual = DgComparativa.SelectedItem is CotizacionRenglonComparativo seleccionado
            ? visibles.IndexOf(seleccionado) : -1;
        int nuevo = actual < 0 ? 0 : System.Math.Clamp(actual + delta, 0, total - 1);
        if (nuevo == actual) return;

        DgComparativa.SelectedItem = visibles[nuevo];
        DgComparativa.ScrollIntoView(DgComparativa.SelectedItem);
    }
}
