using System;
using System.Windows;
using System.Windows.Controls;

namespace SistemaComprasAlmacen;

/// <summary>
/// Comportamiento adaptable: la ventana puede reducirse a media pantalla sin cortar contenido.
/// - Barra lateral: se contrae sola en ventanas angostas y se restaura al ensancharse.
/// - Barra superior: oculta botones secundarios cuando falta espacio.
/// - Contenido: ancho mínimo con desplazamiento horizontal de respaldo.
/// - Inicio: las tarjetas KPI se reacomodan en 3 columnas.
/// </summary>
public partial class MainWindow
{
    private const double AnchoMinimoContenido = 860;
    private const double AnchoContraerSidebar = 1150;
    private const double AnchoRestaurarSidebar = 1250;
    private const double AnchoVentanaCompacta = 1100;

    private bool _sidebarAutoColapsado;
    private bool _toggleSidebarAutomatico;

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;

        double w = ActualWidth;

        if (w < AnchoContraerSidebar && !_sidebarColapsado)
        {
            _toggleSidebarAutomatico = true;
            try { BtnToggleSidebar_Click(this, new RoutedEventArgs()); }
            finally { _toggleSidebarAutomatico = false; }
            _sidebarAutoColapsado = true;
        }
        else if (w >= AnchoRestaurarSidebar && _sidebarColapsado && _sidebarAutoColapsado)
        {
            _toggleSidebarAutomatico = true;
            try { BtnToggleSidebar_Click(this, new RoutedEventArgs()); }
            finally { _toggleSidebarAutomatico = false; }
            _sidebarAutoColapsado = false;
        }

        var visBotonesSecundarios = w < AnchoVentanaCompacta ? Visibility.Collapsed : Visibility.Visible;
        if (BtnMapaTopbar != null) BtnMapaTopbar.Visibility = visBotonesSecundarios;
        if (BtnAlternarTema != null) BtnAlternarTema.Visibility = visBotonesSecundarios;
    }

    private void SvContenidoPrincipal_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainTabControl == null || SvContenidoPrincipal == null) return;

        double disponible = SvContenidoPrincipal.ActualWidth;
        MainTabControl.Width = Math.Max(disponible, AnchoMinimoContenido);

        if (UgKpisInicio != null)
            UgKpisInicio.Columns = disponible < 1050 ? 3 : 5;
    }
}
