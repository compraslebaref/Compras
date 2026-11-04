using System.Windows.Controls;
using System.Windows.Input;

namespace SistemaComprasAlmacen;

/// <summary>Comparativa: navegación por pasos (1 Artículos, 2 Comparar proveedores, 3 Emitir órdenes).</summary>
public partial class MainWindow
{
    private void DgComparativa_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Solo al hacer clic sobre una fila (no en encabezados ni al editar la cantidad)
        if (FilaClicada<object>(e) != null && Keyboard.FocusedElement is not TextBox)
            TcComparativa.SelectedIndex = 1;
    }

    private void DgComparativa_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DgComparativa.SelectedItem != null && Keyboard.FocusedElement is not TextBox)
        {
            TcComparativa.SelectedIndex = 1;
            e.Handled = true;
        }
    }

    private void BtnCompAnterior_Click(object sender, System.Windows.RoutedEventArgs e) => MoverSeleccionComparativa(-1);

    private void BtnCompSiguiente_Click(object sender, System.Windows.RoutedEventArgs e) => MoverSeleccionComparativa(1);

    private void MoverSeleccionComparativa(int delta)
    {
        int total = DgComparativa.Items.Count;
        if (total == 0) return;

        int actual = DgComparativa.SelectedIndex;
        int nuevo = actual < 0 ? 0 : System.Math.Clamp(actual + delta, 0, total - 1);
        if (nuevo == actual) return;

        DgComparativa.SelectedIndex = nuevo;
        DgComparativa.ScrollIntoView(DgComparativa.SelectedItem);
    }
}
