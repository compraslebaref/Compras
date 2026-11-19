using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;
using SistemaComprasAlmacen.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SistemaComprasAlmacen;

public partial class LoginWindow : FluentWindow
{
    private List<UsuarioSistema> _usuariosActivos = new();
    private bool _sincronizandoSelector;
    private bool _autenticando;

    public LoginWindow()
    {
        InitializeComponent();

        try
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
        }
        catch
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, false);
        }

        Loaded += async (_, _) =>
        {
            TxtUrlServidor.Text = ErpNextApiClient.ObtenerUrlServidorConfigurada();
            CargarUsuariosRegistrados();
            await VerificarEstadoServidorAsync();
        };
    }

    private void CargarUsuariosRegistrados()
    {
        _sincronizandoSelector = true;
        try
        {
            _usuariosActivos = DatabaseService.ObtenerUsuariosSistema(soloActivos: true);
            CboUsuariosRapidos.ItemsSource = _usuariosActivos;

            var ultimo = DatabaseService.ObtenerUsuarioActivo();
            var preseleccion = _usuariosActivos.FirstOrDefault(u => u.Id == ultimo.Id) ?? _usuariosActivos.FirstOrDefault();
            if (preseleccion != null)
            {
                CboUsuariosRapidos.SelectedItem = preseleccion;
                TxtUsuario.Text = preseleccion.Username;
                ActualizarVistaPreviaRol(preseleccion.Username);
                PwdContrasena.Focus();
            }
            else
            {
                TxtUsuario.Focus();
            }
        }
        finally
        {
            _sincronizandoSelector = false;
        }
    }

    private async Task VerificarEstadoServidorAsync()
    {
        DotEstadoServidor.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        TxtEstadoServidor.Text = "Verificando servidor ERPNext...";

        var (ok, detalle) = await ErpNextApiClient.ProbarConexionServidorAsync(timeoutMs: 1800);
        if (ok)
        {
            BdrEstadoServidor.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
            BdrEstadoServidor.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
            DotEstadoServidor.Fill = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            TxtEstadoServidor.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
            TxtEstadoServidor.Text = $"Servidor ERPNext conectado ({detalle}) — Sesión multiusuario activa";
        }
        else
        {
            BdrEstadoServidor.Background = new SolidColorBrush(Color.FromRgb(254, 252, 232));
            BdrEstadoServidor.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 224, 71));
            DotEstadoServidor.Fill = new SolidColorBrush(Color.FromRgb(217, 119, 6));
            TxtEstadoServidor.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
            TxtEstadoServidor.Text = $"Servidor ERPNext sin conexión ({ErpNextApiClient.ObtenerUrlServidorConfigurada().TrimEnd('/')}) — Acceso con cuenta local habilitado";
        }
    }

    private void BtnToggleConfigServidor_Click(object sender, RoutedEventArgs e)
    {
        PnlConfigServidor.Visibility = PnlConfigServidor.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void BtnProbarServidor_Click(object sender, RoutedEventArgs e)
    {
        await VerificarEstadoServidorAsync();
    }

    private async void BtnGuardarServidor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErpNextApiClient.GuardarUrlServidorConfigurada(TxtUrlServidor.Text);
            TxtUrlServidor.Text = ErpNextApiClient.ObtenerUrlServidorConfigurada();
            PnlConfigServidor.Visibility = Visibility.Collapsed;
            OcultarError();
            await VerificarEstadoServidorAsync();
        }
        catch (Exception ex)
        {
            MostrarError(ex.Message);
        }
    }

    private void CboUsuariosRapidos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sincronizandoSelector) return;
        if (CboUsuariosRapidos.SelectedItem is UsuarioSistema u)
        {
            _sincronizandoSelector = true;
            TxtUsuario.Text = u.Username;
            _sincronizandoSelector = false;
            ActualizarVistaPreviaRol(u.Username);
            OcultarError();
            if (ChkMostrarPassword.IsChecked == true)
                TxtContrasenaVisible.Focus();
            else
                PwdContrasena.Focus();
        }
    }

    private void TxtUsuario_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_sincronizandoSelector) return;
        string texto = (TxtUsuario.Text ?? string.Empty).Trim();
        ActualizarVistaPreviaRol(texto);
    }

    private void ActualizarVistaPreviaRol(string entradaUsuario)
    {
        string limpio = entradaUsuario.StartsWith("@", StringComparison.Ordinal) ? entradaUsuario[1..] : entradaUsuario;
        var match = _usuariosActivos.FirstOrDefault(u =>
            string.Equals(u.Username, limpio, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(u.CorreoErp) && string.Equals(u.CorreoErp, limpio, StringComparison.OrdinalIgnoreCase)));

        if (match != null)
        {
            var perm = SesionUsuarioService.ObtenerPermisosPorTipo(match.TipoUsuarioNormalizado);
            TxtResumenRolTitulo.Text = $"Tipo de usuario: {perm.TipoUsuario}";
            TxtResumenRolDetalle.Text = perm.ResumenCapacidades;
        }
        else if (limpio.Contains('@') || string.Equals(limpio, "Administrator", StringComparison.OrdinalIgnoreCase))
        {
            TxtResumenRolTitulo.Text = "Cuenta directa de Servidor ERPNext";
            TxtResumenRolDetalle.Text = "Al autenticar en ERPNext se asignarán automáticamente los permisos del perfil del usuario.";
        }
        else
        {
            TxtResumenRolTitulo.Text = "Acceso por Usuario y Contraseña";
            TxtResumenRolDetalle.Text = "El programa habilitará módulos, precios y autorizaciones según el rol de su cuenta.";
        }
    }

    private void ChkMostrarPassword_Changed(object sender, RoutedEventArgs e)
    {
        bool mostrar = ChkMostrarPassword.IsChecked == true;
        if (mostrar)
        {
            TxtContrasenaVisible.Text = PwdContrasena.Password;
            PwdContrasena.Visibility = Visibility.Collapsed;
            TxtContrasenaVisible.Visibility = Visibility.Visible;
            TxtContrasenaVisible.Focus();
            TxtContrasenaVisible.CaretIndex = TxtContrasenaVisible.Text.Length;
        }
        else
        {
            PwdContrasena.Password = TxtContrasenaVisible.Text;
            TxtContrasenaVisible.Visibility = Visibility.Collapsed;
            PwdContrasena.Visibility = Visibility.Visible;
            PwdContrasena.Focus();
        }
    }

    private void CampoLogin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (ReferenceEquals(sender, TxtUsuario))
            {
                string pwdActual = ObtenerPasswordCapturado();
                if (string.IsNullOrWhiteSpace(pwdActual))
                {
                    if (ChkMostrarPassword.IsChecked == true) TxtContrasenaVisible.Focus();
                    else PwdContrasena.Focus();
                    return;
                }
            }
            _ = EjecutarInicioSesionAsync();
        }
    }

    private string ObtenerPasswordCapturado() =>
        ChkMostrarPassword.IsChecked == true
            ? (TxtContrasenaVisible.Text ?? string.Empty)
            : (PwdContrasena.Password ?? string.Empty);

    private async void BtnEntrar_Click(object sender, RoutedEventArgs e)
    {
        await EjecutarInicioSesionAsync();
    }

    private async Task EjecutarInicioSesionAsync()
    {
        if (_autenticando) return;
        OcultarError();

        string usuario = (TxtUsuario.Text ?? string.Empty).Trim();
        string password = ObtenerPasswordCapturado();

        _autenticando = true;
        BtnEntrar.IsEnabled = false;
        BtnEntrar.Content = "Verificando acceso...";
        try
        {
            var (exito, mensajeError) = await SesionUsuarioService.IniciarSesionAsync(usuario, password);
            if (!exito)
            {
                MostrarError(mensajeError);
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MostrarError($"Error inesperado al iniciar sesión: {ex.Message}");
        }
        finally
        {
            _autenticando = false;
            BtnEntrar.IsEnabled = true;
            BtnEntrar.Content = "Entrar al Sistema";
        }
    }

    private void MostrarError(string mensaje)
    {
        TxtErrorLogin.Text = mensaje;
        BdrErrorLogin.Visibility = Visibility.Visible;
    }

    private void OcultarError()
    {
        BdrErrorLogin.Visibility = Visibility.Collapsed;
        TxtErrorLogin.Text = string.Empty;
    }
}
