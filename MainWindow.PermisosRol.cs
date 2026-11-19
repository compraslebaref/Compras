using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;
using SistemaComprasAlmacen.Services;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SistemaComprasAlmacen;

/// <summary>
/// Control formal de permisos por Tipo de Usuario, gestión de cuentas/contraseñas de auxiliares,
/// cierre/cambio de sesión y panel de sincronización con el servidor multiusuario ERPNext.
/// </summary>
public partial class MainWindow
{
    public bool ValidarPermisoAccion(bool permitido, string accionDescripcion, string perfilesAutorizados)
    {
        if (permitido)
            return true;

        var perm = SesionUsuarioService.Permisos;
        DialogoFluent.Show(
            this,
            $"Tu cuenta actual (@{SesionUsuarioService.UsuarioActual.Username} · {perm.TipoUsuario}) no tiene autorización para:\n" +
            $"• {accionDescripcion}\n\n" +
            $"Perfiles autorizados: {perfilesAutorizados}.",
            "Acción restringida por tipo de usuario",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public void AplicarPermisosSesionActual(bool navegarSiPestanaOculta = true)
    {
        if (!SesionUsuarioService.SesionIniciada)
        {
            var usrDb = DatabaseService.ObtenerUsuarioActivo();
            SesionUsuarioService.EstablecerSesionActiva(usrDb);
        }

        var usr = SesionUsuarioService.UsuarioActual;
        var perm = SesionUsuarioService.Permisos;

        _sesionAdminActiva = perm.EsAdministrador;

        // Si no es Administrador, forzar la vista operativa correspondiente a su rol;
        // si es Responsable de Compras, respetar si eligió previsualizar Almacén o Compras.
        if (!perm.EsAdministrador)
        {
            _rolOperativoActual = perm.VistaOperativaInicial;
            DatabaseService.GuardarRolOperativoActivo(_rolOperativoActual);
        }

        bool ocultarPrecios = !perm.PuedeVerPrecios || EsModoAlmacen;
        var visPrecios = ocultarPrecios ? Visibility.Collapsed : Visibility.Visible;
        Application.Current.Resources["VisibilidadPreciosPerfil"] = visPrecios;

        // Botones superiores de sesión y rol
        if (BtnRolOperativo != null)
        {
            string sufijoErp = SesionUsuarioService.ConectadoAErpNext ? " · ERP" : "";
            BtnRolOperativo.Content = $"{usr.NombreCompleto} · {perm.TipoUsuario}{sufijoErp}";
            BtnRolOperativo.ToolTip = $"Sesión activa: {usr.NombreCompleto} (@{usr.Username})\nTipo: {perm.TipoUsuario} | {SesionUsuarioService.EtiquetaModoConexion}\nClic para ver permisos, cambiar contraseña o cerrar sesión.";
            BtnRolOperativo.Appearance = ocultarPrecios ? ControlAppearance.Secondary : ControlAppearance.Primary;
        }

        if (BtnPerfilAdmin != null)
        {
            if (perm.EsAdministrador)
            {
                BtnPerfilAdmin.Content = SesionUsuarioService.ConectadoAErpNext ? "ERP Conectado" : "Admin Activo";
                BtnPerfilAdmin.Appearance = ControlAppearance.Primary;
                BtnPerfilAdmin.ToolTip = "Administrar Usuarios, Auxiliares, Contraseñas y Conexión ERPNext";
            }
            else
            {
                BtnPerfilAdmin.Content = SesionUsuarioService.ConectadoAErpNext ? "ERP en Línea" : "Cerrar Sesión";
                BtnPerfilAdmin.Appearance = ControlAppearance.Secondary;
                BtnPerfilAdmin.ToolTip = "Ver estado de tu cuenta o cambiar de usuario";
            }
        }

        // Columnas de montos en tablas principales
        if (ColDashboardOcTotal != null) ColDashboardOcTotal.Visibility = visPrecios;
        if (ColOrdenesTotal != null) ColOrdenesTotal.Visibility = visPrecios;

        // Menús desplegables superiores según capacidades del rol
        if (MenuComprasTop != null)
            MenuComprasTop.Visibility = (perm.PuedeCotizarComparativa || perm.PuedeAutorizarOc || perm.PuedeGestionarFacturas) && !EsModoAlmacen
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (MenuReportesTop != null)
            MenuReportesTop.Visibility = perm.PuedeVerAnalisis && !ocultarPrecios
                ? Visibility.Visible
                : Visibility.Collapsed;

        // Visibilidad de pestañas laterales de navegación
        bool verComparativa = (perm.PuedeCotizarComparativa || perm.PuedeAutorizarOc) && !EsModoAlmacen;
        bool verOrdenes = perm.TipoUsuario != "Solicitante";
        bool verFacturas = perm.PuedeGestionarFacturas && !EsModoAlmacen;
        bool verAlmacen = perm.TipoUsuario != "Solicitante" && perm.TipoUsuario != "Finanzas";
        bool verAnalisis = perm.PuedeVerAnalisis && !ocultarPrecios;
        bool verDirectorios = perm.PuedeVerDirectorios;
        bool verFlotilla = perm.PuedeVerFlotilla;

        if (NavComparativa != null) NavComparativa.Visibility = verComparativa ? Visibility.Visible : Visibility.Collapsed;
        if (NavOrdenes != null)
        {
            NavOrdenes.Visibility = verOrdenes ? Visibility.Visible : Visibility.Collapsed;
            if (!_sidebarColapsado)
                NavOrdenes.Content = (perm.TipoUsuario == "Auxiliar de Almacén" || EsModoAlmacen) ? "Recepción OC" : "Órdenes de Compra";
        }
        if (BdgSidebarOcs != null && !verOrdenes) BdgSidebarOcs.Visibility = Visibility.Collapsed;

        if (NavFacturasXml != null) NavFacturasXml.Visibility = verFacturas ? Visibility.Visible : Visibility.Collapsed;
        if (NavAlmacenPos != null) NavAlmacenPos.Visibility = verAlmacen ? Visibility.Visible : Visibility.Collapsed;
        if (NavAnalisis != null) NavAnalisis.Visibility = verAnalisis ? Visibility.Visible : Visibility.Collapsed;
        if (NavDirectorios != null) NavDirectorios.Visibility = verDirectorios ? Visibility.Visible : Visibility.Collapsed;
        if (NavFlotilla != null) NavFlotilla.Visibility = verFlotilla ? Visibility.Visible : Visibility.Collapsed;

        if (LblSecAlmacen != null)
            LblSecAlmacen.Visibility = (!_sidebarColapsado && (verAlmacen || verAnalisis || verDirectorios || verFlotilla))
                ? Visibility.Visible
                : Visibility.Collapsed;

        // KPI Grid en Inicio
        if (UgKpisInicio != null && ActualWidth >= 1100)
            UgKpisInicio.Columns = ocultarPrecios ? 4 : 5;

        // Controles específicos dentro de Comparativa, OC, Almacén y Catálogo
        if (BtnGenerarOrdenesDesdeComparativa != null)
        {
            BtnGenerarOrdenesDesdeComparativa.IsEnabled = perm.PuedeGenerarBorradorOc;
            BtnGenerarOrdenesDesdeComparativa.Content = perm.PuedeAutorizarOc
                ? "Emitir Órdenes de Compra"
                : "Generar Borrador de OC (Vo.Bo.)";
        }

        if (TabOcEstatusPago != null)
        {
            TabOcEstatusPago.Visibility = (perm.PuedeAutorizarOc || perm.PuedeProgramarPagosOc) && !ocultarPrecios
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (BtnFiniquitarOcParcial != null)
            BtnFiniquitarOcParcial.Visibility = perm.PuedeAutorizarOc ? Visibility.Visible : Visibility.Collapsed;

        if (BtnEntradaSinOcAlmacen != null)
            BtnEntradaSinOcAlmacen.Visibility = perm.PuedeRecibirAlmacen ? Visibility.Visible : Visibility.Collapsed;

        if (BtnEntradaRegistrarOc != null)
            BtnEntradaRegistrarOc.IsEnabled = perm.PuedeRecibirAlmacen;

        if (BtnNuevoProductoCatalogo != null)
            BtnNuevoProductoCatalogo.Visibility = perm.PuedeEditarCatalogosMaestros ? Visibility.Visible : Visibility.Collapsed;

        if (BtnGuardarProductoCatalogo != null)
            BtnGuardarProductoCatalogo.IsEnabled = perm.PuedeEditarCatalogosMaestros;

        // Redirigir a Inicio si la pestaña activa quedó oculta para este tipo de usuario
        if (navegarSiPestanaOculta && MainTabControl != null)
        {
            int idx = MainTabControl.SelectedIndex;
            bool oculta =
                (idx == 3 && !verComparativa) ||
                (idx == 4 && !verOrdenes) ||
                (idx == 5 && !verAlmacen) ||
                (idx == 6 && !verFacturas) ||
                (idx == 7 && !verAnalisis) ||
                (idx == 8 && !verDirectorios) ||
                (idx == 9 && !verFlotilla);

            if (oculta)
                CambiarSeccion(0);
        }

        if (_productoEnFicha != null)
            ActualizarFichaProducto(_productoEnFicha);

        ActualizarBadgeNotificacionesCheckpoints();
    }

    public void MostrarDialogoSesionYCuenta()
    {
        var usr = SesionUsuarioService.UsuarioActual;
        var perm = SesionUsuarioService.Permisos;

        var win = new Window
        {
            Title = "Mi Sesión, Permisos y Cambio de Usuario",
            Width = 600,
            Height = perm.EsAdministrador ? 580 : 470,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(20) };

        var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var btnBitacora = new Wpf.Ui.Controls.Button
        {
            Content = "📋 Bitácora por Usuario",
            FontSize = 11.5,
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(btnBitacora, Dock.Right);
        topDock.Children.Add(btnBitacora);
        topDock.Children.Add(new TextBlock
        {
            Text = "Sesión Activa y Nivel de Acceso",
            FontSize = 16.5,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(topDock);

        // Tarjeta de identidad del usuario autenticado
        var cardUser = new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(8),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spInfo = new StackPanel();
        spInfo.Children.Add(new TextBlock
        {
            Text = $"{usr.NombreCompleto}  (@{usr.Username})",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold
        });
        spInfo.Children.Add(new TextBlock
        {
            Text = $"Tipo de Usuario: {perm.TipoUsuario}   |   Conexión: {SesionUsuarioService.EtiquetaModoConexion}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("AccentTextFillColorPrimaryBrush"),
            Margin = new Thickness(0, 3, 0, 6)
        });
        spInfo.Children.Add(new TextBlock
        {
            Text = $"✓ Habilitado: {perm.ResumenCapacidades}",
            FontSize = 11.3,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        spInfo.Children.Add(new TextBlock
        {
            Text = $"• Limitaciones: {perm.ResumenLimitaciones}",
            FontSize = 11.2,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        cardUser.Child = spInfo;
        root.Children.Add(cardUser);

        // Acciones rápidas para el propio usuario (cambiar su contraseña)
        var wrapAcciones = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var btnCambiarMiClave = new Wpf.Ui.Controls.Button
        {
            Content = "🔑 Cambiar mi contraseña",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 6)
        };
        wrapAcciones.Children.Add(btnCambiarMiClave);

        if (perm.PuedeAdministrarUsuarios)
        {
            var btnGestionUsuarios = new Wpf.Ui.Controls.Button
            {
                Content = "👥 Usuarios, Auxiliares y Contraseñas",
                Appearance = ControlAppearance.Primary,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 6)
            };
            btnGestionUsuarios.Click += (_, _) =>
            {
                win.Close();
                MostrarVentanaAdministracionUsuarios();
            };
            wrapAcciones.Children.Add(btnGestionUsuarios);

            var btnSyncErp = new Wpf.Ui.Controls.Button
            {
                Content = "🔄 Servidor ERPNext y Migración",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };
            btnSyncErp.Click += (_, _) =>
            {
                win.Close();
                MostrarVentanaSincronizacionErpNext();
            };
            wrapAcciones.Children.Add(btnSyncErp);
        }
        root.Children.Add(wrapAcciones);

        // Solo el Responsable de Compras / Admin puede alternar vista previa entre Compras y Almacén
        RadioButton? rbVistaCompras = null;
        RadioButton? rbVistaAlmacen = null;
        if (perm.EsAdministrador)
        {
            var bdrVista = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(6),
                Background = Tk("SubtleFillColorSecondaryBrush"),
                BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var spVista = new StackPanel();
            spVista.Children.Add(new TextBlock
            {
                Text = "Vista operativa del Administrador (previsualización):",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            rbVistaCompras = new RadioButton
            {
                Content = "Vista Compras Completa (con costos, comparativa, facturas y análisis)",
                IsChecked = !EsModoAlmacen,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 4)
            };
            rbVistaAlmacen = new RadioButton
            {
                Content = "Vista Almacén / Bodega (oculta precios temporalmente en esta pantalla)",
                IsChecked = EsModoAlmacen,
                FontSize = 11.5
            };
            spVista.Children.Add(rbVistaCompras);
            spVista.Children.Add(rbVistaAlmacen);
            bdrVista.Child = spVista;
            root.Children.Add(bdrVista);
        }

        btnBitacora.Click += (_, _) => MostrarModalBitacoraUsuarios(win);
        btnCambiarMiClave.Click += (_, _) => MostrarModalCambiarMiContrasena(win);

        // Pie: Cerrar sesión / Cambiar de usuario vs Cerrar diálogo
        var dockPie = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var btnCerrarSesion = new Wpf.Ui.Controls.Button
        {
            Content = "🚪 Cerrar Sesión / Cambiar de Usuario",
            Padding = new Thickness(14, 7, 14, 7)
        };
        DockPanel.SetDock(btnCerrarSesion, Dock.Left);
        dockPie.Children.Add(btnCerrarSesion);

        var spDerecha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCerrarModal = new Wpf.Ui.Controls.Button
        {
            Content = perm.EsAdministrador ? "Guardar y Continuar" : "Continuar",
            Appearance = ControlAppearance.Primary,
            Padding = new Thickness(16, 7, 16, 7)
        };
        spDerecha.Children.Add(btnCerrarModal);
        dockPie.Children.Add(spDerecha);
        root.Children.Add(dockPie);

        btnCerrarModal.Click += (_, _) =>
        {
            if (perm.EsAdministrador && rbVistaAlmacen != null)
            {
                string vista = rbVistaAlmacen.IsChecked == true ? "Almacén" : "Compras";
                AplicarRolOperativo(vista);
            }
            win.DialogResult = true;
        };

        btnCerrarSesion.Click += async (_, _) =>
        {
            win.DialogResult = false;
            win.Close();
            await CerrarSesionYVolverALoginAsync();
        };

        win.Content = root;
        win.ShowDialog();
    }

    private void MostrarModalCambiarMiContrasena(Window owner)
    {
        var usr = SesionUsuarioService.UsuarioActual;
        var modal = new Window
        {
            Title = $"Cambiar Contraseña — @{usr.Username}",
            Width = 410,
            Height = 310,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var sp = new StackPanel { Margin = new Thickness(18) };
        sp.Children.Add(new TextBlock { Text = $"Actualizar contraseña de {usr.NombreCompleto}", FontSize = 14.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

        sp.Children.Add(new TextBlock { Text = "Contraseña actual *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var pwdActual = new System.Windows.Controls.PasswordBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(pwdActual);

        sp.Children.Add(new TextBlock { Text = "Nueva contraseña (mínimo 4 caracteres) *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var pwdNueva = new System.Windows.Controls.PasswordBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(pwdNueva);

        sp.Children.Add(new TextBlock { Text = "Confirmar nueva contraseña *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var pwdConfirm = new System.Windows.Controls.PasswordBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 14) };
        sp.Children.Add(pwdConfirm);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var bCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var bGuardar = new Wpf.Ui.Controls.Button { Content = "Guardar Contraseña", Appearance = ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5) };
        btns.Children.Add(bCancel);
        btns.Children.Add(bGuardar);
        sp.Children.Add(btns);

        bCancel.Click += (_, _) => modal.DialogResult = false;
        bGuardar.Click += (_, _) =>
        {
            var (okActual, _, _) = DatabaseService.AutenticarUsuarioLocal(usr.Username, pwdActual.Password);
            if (!okActual && !SesionUsuarioService.ConectadoAErpNext)
            {
                DialogoFluent.Show(modal, "La contraseña actual no coincide.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string nueva = (pwdNueva.Password ?? string.Empty).Trim();
            if (nueva.Length < 4)
            {
                DialogoFluent.Show(modal, "La nueva contraseña debe tener al menos 4 caracteres.", "Contraseña corta", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.Equals(nueva, (pwdConfirm.Password ?? string.Empty).Trim(), StringComparison.Ordinal))
            {
                DialogoFluent.Show(modal, "La confirmación no coincide con la nueva contraseña.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DatabaseService.CambiarPasswordUsuario(usr.Id, nueva);
            DatabaseService.RegistrarBitacoraUsuario("Usuarios", "CAMBIO_PASSWORD", $"@{usr.Username}", "El usuario actualizó su contraseña personal.");
            DialogoFluent.Show(modal, "Tu contraseña se actualizó correctamente.", "Contraseña actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
            modal.DialogResult = true;
        };

        modal.Content = sp;
        modal.Loaded += (_, _) => pwdActual.Focus();
        modal.ShowDialog();
    }

    public void MostrarVentanaAdministracionUsuarios()
    {
        if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.PuedeAdministrarUsuarios,
                "Administrar cuentas de usuarios, roles y contraseñas", "Responsable de Compras"))
            return;

        var win = new Window
        {
            Title = "Administración de Usuarios, Auxiliares y Tipos de Acceso",
            Width = 960,
            Height = 610,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var rootGrid = new Grid { Margin = new Thickness(18) };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });

        // Izquierda: Lista de usuarios
        var bdrLista = new Border
        {
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
        var dockLista = new DockPanel();
        var hdrLista = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var btnNuevoUsr = new Wpf.Ui.Controls.Button
        {
            Content = "+ Nuevo Auxiliar / Usuario",
            Appearance = ControlAppearance.Primary,
            Padding = new Thickness(10, 4, 10, 4)
        };
        DockPanel.SetDock(btnNuevoUsr, Dock.Right);
        hdrLista.Children.Add(btnNuevoUsr);
        hdrLista.Children.Add(new TextBlock { Text = "Cuentas de Acceso al Sistema", FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(hdrLista, Dock.Top);
        dockLista.Children.Add(hdrLista);

        var dgUsuarios = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Single
        };
        dgUsuarios.Columns.Add(new DataGridTextColumn { Header = "Usuario", Binding = new System.Windows.Data.Binding("Username"), Width = 95 });
        dgUsuarios.Columns.Add(new DataGridTextColumn { Header = "Nombre Completo", Binding = new System.Windows.Data.Binding("NombreCompleto"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dgUsuarios.Columns.Add(new DataGridTextColumn { Header = "Tipo de Usuario", Binding = new System.Windows.Data.Binding("TipoUsuarioNormalizado"), Width = 150 });
        dgUsuarios.Columns.Add(new DataGridTextColumn { Header = "Cuenta ERPNext", Binding = new System.Windows.Data.Binding("CorreoErpMostrar"), Width = 135 });
        dgUsuarios.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new System.Windows.Data.Binding("EstadoTexto"), Width = 70 });
        dockLista.Children.Add(dgUsuarios);
        bdrLista.Child = dockLista;
        Grid.SetColumn(bdrLista, 0);
        rootGrid.Children.Add(bdrLista);

        // Derecha: Formulario de alta / edición
        var bdrForm = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
        var svForm = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var spForm = new StackPanel();
        var lblModoForm = new TextBlock { Text = "Nuevo Usuario / Auxiliar", FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
        spForm.Children.Add(lblModoForm);

        spForm.Children.Add(new TextBlock { Text = "Usuario de acceso (sin espacios) *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtUsername = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. maria.compras o luis.almacen", Margin = new Thickness(0, 0, 0, 8) };
        spForm.Children.Add(txtUsername);

        spForm.Children.Add(new TextBlock { Text = "Nombre completo *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtNombre = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. María López (Auxiliar Compras)", Margin = new Thickness(0, 0, 0, 8) };
        spForm.Children.Add(txtNombre);

        spForm.Children.Add(new TextBlock { Text = "Puesto / Área", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtPuesto = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. Auxiliar de Compras", Margin = new Thickness(0, 0, 0, 8) };
        spForm.Children.Add(txtPuesto);

        spForm.Children.Add(new TextBlock { Text = "Tipo de Usuario y Limitaciones *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var cboRol = new ComboBox { ItemsSource = SesionUsuarioService.TiposUsuarioOficiales, SelectedIndex = 1, Margin = new Thickness(0, 0, 0, 6) };
        spForm.Children.Add(cboRol);

        var bdrExpRol = new Border
        {
            Padding = new Thickness(9, 7, 9, 7),
            CornerRadius = new CornerRadius(5),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var txtExpRol = new TextBlock { FontSize = 10.8, TextWrapping = TextWrapping.Wrap, Opacity = 0.88 };
        bdrExpRol.Child = txtExpRol;
        spForm.Children.Add(bdrExpRol);

        spForm.Children.Add(new TextBlock { Text = "Correo vinculado en Servidor ERPNext (opcional)", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtCorreoErp = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. compras@lebaref.test", Margin = new Thickness(0, 0, 0, 8) };
        spForm.Children.Add(txtCorreoErp);

        var lblPwd = new TextBlock { Text = "Contraseña inicial *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
        spForm.Children.Add(lblPwd);
        var pwdUsr = new System.Windows.Controls.PasswordBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 8) };
        spForm.Children.Add(pwdUsr);

        var chkActivo = new CheckBox { Content = "Cuenta activa (puede iniciar sesión)", IsChecked = true, Margin = new Thickness(0, 0, 0, 12) };
        spForm.Children.Add(chkActivo);

        var btnGuardar = new Wpf.Ui.Controls.Button
        {
            Content = "Guardar Usuario y Permisos",
            Appearance = ControlAppearance.Primary,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8)
        };
        spForm.Children.Add(btnGuardar);

        svForm.Content = spForm;
        bdrForm.Child = svForm;
        Grid.SetColumn(bdrForm, 1);
        rootGrid.Children.Add(bdrForm);

        int idEditando = 0;

        void ActualizarTextoPermisosSeleccionados()
        {
            string tipo = cboRol.SelectedItem?.ToString() ?? "Auxiliar de Compras";
            var prm = SesionUsuarioService.ObtenerPermisosPorTipo(tipo);
            txtExpRol.Text = $"✓ {prm.ResumenCapacidades}\n• {prm.ResumenLimitaciones}";
        }

        void LimpiarFormulario()
        {
            idEditando = 0;
            lblModoForm.Text = "Nuevo Usuario / Auxiliar";
            txtUsername.Text = string.Empty;
            txtNombre.Text = string.Empty;
            txtPuesto.Text = string.Empty;
            cboRol.SelectedItem = "Auxiliar de Compras";
            txtCorreoErp.Text = string.Empty;
            pwdUsr.Password = string.Empty;
            lblPwd.Text = "Contraseña inicial (si lo deja vacío será 1234)";
            chkActivo.IsChecked = true;
            dgUsuarios.SelectedItem = null;
            ActualizarTextoPermisosSeleccionados();
        }

        void RecargarTabla(int? seleccionarId = null)
        {
            var lista = DatabaseService.ObtenerUsuariosSistema(soloActivos: false);
            dgUsuarios.ItemsSource = lista;
            if (seleccionarId.HasValue)
                dgUsuarios.SelectedItem = lista.FirstOrDefault(x => x.Id == seleccionarId.Value);
        }

        cboRol.SelectionChanged += (_, _) => ActualizarTextoPermisosSeleccionados();
        btnNuevoUsr.Click += (_, _) =>
        {
            LimpiarFormulario();
            txtUsername.Focus();
        };

        dgUsuarios.SelectionChanged += (_, _) =>
        {
            if (dgUsuarios.SelectedItem is not UsuarioSistema u) return;
            idEditando = u.Id;
            lblModoForm.Text = $"Editando: @{u.Username}";
            txtUsername.Text = u.Username;
            txtNombre.Text = u.NombreCompleto;
            txtPuesto.Text = u.Puesto;
            cboRol.SelectedItem = u.TipoUsuarioNormalizado;
            txtCorreoErp.Text = u.CorreoErp;
            pwdUsr.Password = string.Empty;
            lblPwd.Text = "Nueva contraseña (dejar vacío para conservar la actual)";
            chkActivo.IsChecked = u.Activo;
            ActualizarTextoPermisosSeleccionados();
        };

        btnGuardar.Click += (_, _) =>
        {
            string uname = (txtUsername.Text ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", ".");
            string nom = (txtNombre.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(uname) || string.IsNullOrWhiteSpace(nom))
            {
                DialogoFluent.Show(win, "Escriba tanto el usuario de acceso como el nombre completo.", "Datos requeridos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (idEditando == SesionUsuarioService.UsuarioActual.Id && chkActivo.IsChecked == false)
            {
                DialogoFluent.Show(win, "No puede desactivar su propia cuenta actualmente en sesión.", "Cuenta activa", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pwdNueva = (pwdUsr.Password ?? string.Empty).Trim();
            if (pwdNueva.Length > 0 && pwdNueva.Length < 4)
            {
                DialogoFluent.Show(win, "La contraseña debe tener al menos 4 caracteres.", "Contraseña corta", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string rolSel = cboRol.SelectedItem?.ToString() ?? "Auxiliar de Compras";
                int idGuardado = DatabaseService.GuardarUsuarioSistema(new UsuarioSistema
                {
                    Id = idEditando,
                    Username = uname,
                    NombreCompleto = nom,
                    Puesto = string.IsNullOrWhiteSpace(txtPuesto.Text) ? rolSel : txtPuesto.Text.Trim(),
                    Rol = rolSel,
                    CorreoErp = (txtCorreoErp.Text ?? string.Empty).Trim(),
                    Activo = chkActivo.IsChecked != false
                }, string.IsNullOrWhiteSpace(pwdNueva) ? null : pwdNueva);

                RecargarTabla(idGuardado);
                if (idGuardado == SesionUsuarioService.UsuarioActual.Id)
                {
                    var actualizado = DatabaseService.ObtenerUsuariosSistema(soloActivos: false).First(x => x.Id == idGuardado);
                    SesionUsuarioService.EstablecerSesionActiva(actualizado, SesionUsuarioService.ClienteErp, SesionUsuarioService.IdentidadErpNext, SesionUsuarioService.RolesErpNext);
                    AplicarPermisosSesionActual(navegarSiPestanaOculta: false);
                }

                pwdUsr.Password = string.Empty;
                DialogoFluent.Show(win, $"La cuenta @{uname} ({rolSel}) quedó guardada correctamente.", "Usuario guardado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"No se pudo guardar el usuario (verifique que @{uname} no esté duplicado):\n{ex.Message}", "Error al guardar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        RecargarTabla();
        LimpiarFormulario();
        win.Content = rootGrid;
        win.ShowDialog();
    }

    public void MostrarVentanaSincronizacionErpNext()
    {
        if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.EsAdministrador,
                "Configurar servidor multiusuario ERPNext y migrar catálogos maestros", "Responsable de Compras"))
            return;

        var win = new Window
        {
            Title = "Servidor Multiusuario ERPNext y Migración de Catálogos Maestros",
            Width = 680,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Conexión y Migración hacia ERPNext (Multiusuario)",
            FontSize = 16.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Conecte este sistema al servidor ERPNext (local en Docker/WSL o en red de oficina) y sincronice de forma segura sus Proveedores, Proyectos y 595 Productos sin modificar su base SQLite local.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Configuración de URL y prueba
        var gridUrl = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        gridUrl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridUrl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtUrl = new Wpf.Ui.Controls.TextBox { Text = ErpNextApiClient.ObtenerUrlServidorConfigurada(), FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 8, 0) };
        var btnGuardarUrl = new Wpf.Ui.Controls.Button { Content = "Guardar y Probar URL", Padding = new Thickness(12, 5, 12, 5) };
        Grid.SetColumn(txtUrl, 0);
        Grid.SetColumn(btnGuardarUrl, 1);
        gridUrl.Children.Add(txtUrl);
        gridUrl.Children.Add(btnGuardarUrl);
        root.Children.Add(gridUrl);

        // Panel de conexión de cuenta ERPNext si no está autenticada aún
        var bdrLoginErp = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var spLoginErp = new StackPanel();
        var lblEstadoSesionErp = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        spLoginErp.Children.Add(lblEstadoSesionErp);

        var gridCredErp = new Grid();
        gridCredErp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        gridCredErp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCredErp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtErpUser = new Wpf.Ui.Controls.TextBox
        {
            Text = !string.IsNullOrWhiteSpace(SesionUsuarioService.UsuarioActual.CorreoErp) ? SesionUsuarioService.UsuarioActual.CorreoErp : "Administrator",
            PlaceholderText = "Usuario ERPNext (ej. autoriza@lebaref.test)",
            Margin = new Thickness(0, 0, 6, 0)
        };
        var pwdErp = new System.Windows.Controls.PasswordBox { Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) };
        var btnConectarErp = new Wpf.Ui.Controls.Button { Content = "Conectar Sesión ERP", Appearance = ControlAppearance.Primary, Padding = new Thickness(12, 5, 12, 5) };
        Grid.SetColumn(txtErpUser, 0);
        Grid.SetColumn(pwdErp, 1);
        Grid.SetColumn(btnConectarErp, 2);
        gridCredErp.Children.Add(txtErpUser);
        gridCredErp.Children.Add(pwdErp);
        gridCredErp.Children.Add(btnConectarErp);
        spLoginErp.Children.Add(gridCredErp);
        bdrLoginErp.Child = spLoginErp;
        root.Children.Add(bdrLoginErp);

        // Resumen comparativo Local vs ERPNext
        var bdrResumen = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var txtResumenConteos = new TextBlock { FontSize = 11.8, TextWrapping = TextWrapping.Wrap, Text = "Consultando estado del servidor..." };
        bdrResumen.Child = txtResumenConteos;
        root.Children.Add(bdrResumen);

        // Bitácora en vivo de migración
        var txtBitacoraSync = new System.Windows.Controls.TextBox
        {
            Height = 135,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12),
            Text = "Listo. Cuando el servidor ERPNext esté en línea y conectado, presione 'Sincronizar Catálogos Maestros' para cargar Proveedores, Proyectos y Productos."
        };
        root.Children.Add(txtBitacoraSync);

        var dockBotones = new DockPanel();
        var btnEjecutarSync = new Wpf.Ui.Controls.Button
        {
            Content = "🚀 Sincronizar Catálogos Maestros (SQLite → ERPNext)",
            Appearance = ControlAppearance.Primary,
            Padding = new Thickness(14, 7, 14, 7)
        };
        var btnCerrar = new System.Windows.Controls.Button { Content = "Cerrar", Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(btnEjecutarSync, Dock.Left);
        dockBotones.Children.Add(btnEjecutarSync);
        dockBotones.Children.Add(btnCerrar);
        root.Children.Add(dockBotones);

        async Task RefrescarEstadoPanelErpAsync()
        {
            var (disp, det) = await ErpNextApiClient.ProbarConexionServidorAsync(timeoutMs: 2000);
            if (!disp)
            {
                lblEstadoSesionErp.Text = $"🟡 Servidor ERPNext apagado o inalcanzable ({ErpNextApiClient.ObtenerUrlServidorConfigurada().TrimEnd('/')})";
                txtResumenConteos.Text = $"Catálogo local listo para migrar: {DatabaseService.ObtenerProveedores().Count} proveedores · {DatabaseService.ObtenerProyectos().Count} obras · {DatabaseService.ObtenerProductos().Count} productos.\nInicie el contenedor ERPNext (Entorno-Pruebas.ps1) para conectar.";
                btnEjecutarSync.IsEnabled = false;
                return;
            }

            if (SesionUsuarioService.ConectadoAErpNext && SesionUsuarioService.ClienteErp != null)
            {
                lblEstadoSesionErp.Text = $"🟢 Sesión ERPNext activa como: {SesionUsuarioService.IdentidadErpNext}";
                gridCredErp.Visibility = Visibility.Collapsed;
                btnEjecutarSync.IsEnabled = true;

                var comp = await ErpNextSincronizacionService.ObtenerResumenComparativoAsync(SesionUsuarioService.ClienteErp);
                txtResumenConteos.Text =
                    $"Empresa ERPNext: {(string.IsNullOrWhiteSpace(comp.EmpresaPrincipal) ? "Configurada" : comp.EmpresaPrincipal)}\n" +
                    $"• Proveedores: {comp.ProveedoresErp} en ERPNext  (vs. {comp.ProveedoresLocales} en SQLite local)\n" +
                    $"• Obras / Proyectos: {comp.ObrasErp} en ERPNext  (vs. {comp.ObrasLocales} en SQLite local)\n" +
                    $"• Artículos / SKUs: {comp.ArticulosErp} en ERPNext  (vs. {comp.ArticulosLocales} en SQLite local)\n" +
                    $"• Documentos en ERPNext: {comp.SolicitudesErp} Solicitudes · {comp.OrdenesCompraErp} Órdenes de Compra";
            }
            else
            {
                lblEstadoSesionErp.Text = $"🟢 Servidor ERPNext disponible ({det}) — Ingrese su clave para enlazar la sesión:";
                gridCredErp.Visibility = Visibility.Visible;
                btnEjecutarSync.IsEnabled = false;
                txtResumenConteos.Text = "Autentique su cuenta arriba (ej. Administrator o autoriza@lebaref.test) para consultar conteos y migrar catálogos.";
            }
        }

        btnGuardarUrl.Click += async (_, _) =>
        {
            try
            {
                ErpNextApiClient.GuardarUrlServidorConfigurada(txtUrl.Text);
                txtUrl.Text = ErpNextApiClient.ObtenerUrlServidorConfigurada();
                await RefrescarEstadoPanelErpAsync();
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, ex.Message, "URL inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        btnConectarErp.Click += async (_, _) =>
        {
            string uErp = (txtErpUser.Text ?? string.Empty).Trim();
            string pErp = pwdErp.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(uErp) || string.IsNullOrWhiteSpace(pErp))
            {
                DialogoFluent.Show(win, "Escriba el usuario y contraseña del servidor ERPNext.", "Credenciales ERPNext", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btnConectarErp.IsEnabled = false;
            var cli = new ErpNextApiClient();
            try
            {
                var info = await cli.SignInAsync(uErp, pErp);
                SesionUsuarioService.EstablecerSesionActiva(SesionUsuarioService.UsuarioActual, cli, info.Identidad, info.Roles);
                AplicarPermisosSesionActual(navegarSiPestanaOculta: false);
                await RefrescarEstadoPanelErpAsync();
            }
            catch (Exception ex)
            {
                cli.Dispose();
                DialogoFluent.Show(win, ex.Message, "Error de acceso ERPNext", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btnConectarErp.IsEnabled = true;
            }
        };

        btnEjecutarSync.Click += async (_, _) =>
        {
            if (SesionUsuarioService.ClienteErp == null) return;
            btnEjecutarSync.IsEnabled = false;
            txtBitacoraSync.Clear();

            var progreso = new Progress<string>(linea =>
            {
                txtBitacoraSync.AppendText($"[{DateTime.Now:HH:mm:ss}] {linea}\r\n");
                txtBitacoraSync.ScrollToEnd();
            });

            try
            {
                var res = await ErpNextSincronizacionService.SincronizarCatalogosMaestrosAsync(SesionUsuarioService.ClienteErp, progreso);
                txtBitacoraSync.AppendText(
                    $"\r\n=== RESUMEN FINAL ===\r\n" +
                    $"Proveedores nuevos: {res.ProveedoresCreados} (ya existían: {res.ProveedoresYaExistentes})\r\n" +
                    $"Obras nuevas: {res.ObrasCreadas} (ya existían: {res.ObrasYaExistentes})\r\n" +
                    $"Artículos nuevos: {res.ArticulosCreados} (ya existían: {res.ArticulosYaExistentes})\r\n");

                if (res.Advertencias.Count > 0)
                {
                    txtBitacoraSync.AppendText($"Avisos ({res.Advertencias.Count}):\r\n - " + string.Join("\r\n - ", res.Advertencias) + "\r\n");
                }
                txtBitacoraSync.ScrollToEnd();
                DatabaseService.RegistrarBitacoraUsuario("ERPNext", "MIGRACION_MAESTROS", "CATÁLOGOS",
                    $"Sincronización hacia ERPNext: +{res.ProveedoresCreados} prov, +{res.ObrasCreadas} obras, +{res.ArticulosCreados} artículos.");
                await RefrescarEstadoPanelErpAsync();
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"La sincronización se detuvo: {ex.Message}", "Sincronización ERPNext", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btnEjecutarSync.IsEnabled = SesionUsuarioService.ConectadoAErpNext;
            }
        };

        btnCerrar.Click += (_, _) => win.Close();
        win.Loaded += async (_, _) => await RefrescarEstadoPanelErpAsync();
        win.Content = root;
        win.ShowDialog();
    }

    public async Task CerrarSesionYVolverALoginAsync()
    {
        try
        {
            _timerImapSolicitudes?.Stop();
            _timerBusquedaCatalogo?.Stop();
            _timerAutoEscaneoPistola?.Stop();
        }
        catch { }

        await SesionUsuarioService.CerrarSesionAsync();

        if (Application.Current is App app)
        {
            app.CambiarUsuarioReabriendoLogin(this);
        }
        else
        {
            Close();
        }
    }
}
