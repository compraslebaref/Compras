using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SistemaComprasAlmacen.Data;
using SistemaComprasAlmacen.Models;
using SistemaComprasAlmacen.Services;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SistemaComprasAlmacen;

/// <summary>
/// Motor de Etapas, Checkpoints, Notificaciones por Rol (Compras / Almacén / Agentes Externos)
/// y Privacidad de Costos.
/// </summary>
public partial class MainWindow
{
    private string _rolOperativoActual = "Compras";

    public bool EsModoAlmacen =>
        string.Equals(_rolOperativoActual, "Almacén", StringComparison.OrdinalIgnoreCase);

    // =========================================================================
    // PASO 1: PERFIL ACTIVO (COMPRAS vs ALMACÉN) Y PRIVACIDAD DE PRECIOS
    // =========================================================================

    private bool _suscritoEventosCorreo;
    private System.Windows.Threading.DispatcherTimer? _timerImapSolicitudes;
    private bool _sincronizandoImapEnFondo;

    private void InicializarRolYCheckpoints()
    {
        if (!_suscritoEventosCorreo)
        {
            _suscritoEventosCorreo = true;
            DatabaseService.OnNotificacionCorreoProcesada += reg =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (reg.Id <= 0) return; // Ignorar pruebas manuales desde el modal
                    string titulo = reg.EstadoEnvio.StartsWith("Enviado", StringComparison.OrdinalIgnoreCase)
                        ? $"Correo enviado a {reg.GerenteNombre}"
                        : $"Aviso a {reg.GerenteNombre} ({reg.EstadoEnvio})";
                    MostrarToastDiscreto(reg.MensajePrincipal, titulo);
                });
            };

            DatabaseService.OnCorreoSolicitudRecibido += correo =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ActualizarBadgeBandejaCorreosSolicitud();
                    if (correo.SolicitudCreadaId > 0)
                    {
                        RefrescarSolicitudes(correo.SolicitudCreadaId);
                        MostrarToastDiscreto(
                            $"Se creó automáticamente la solicitud {correo.SolicitudCreadaFolio} ({correo.PartidasDetectadasCount} partidas) de {correo.RemitenteNombre} ({correo.DepartamentoDetectadoNombre}).",
                            $"Solicitud por Correo: {correo.SolicitudCreadaFolio}");
                    }
                    else
                    {
                        MostrarToastDiscreto(
                            $"Correo recibido de {correo.RemitenteNombre}: \"{correo.Asunto}\" ({correo.PartidasDetectadasCount} partidas detectadas). Disponible en Bandeja de Correo.",
                            "Correo Entrante en Bandeja");
                    }
                });
            };
        }

        if (_timerImapSolicitudes == null)
        {
            _timerImapSolicitudes = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            _timerImapSolicitudes.Tick += async (_, _) => await EjecutarSincronizacionImapSilenciosaAsync();
            _timerImapSolicitudes.Start();
            // Primera revisión suave a los 12 segundos de iniciar la app
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(12000);
                await Dispatcher.InvokeAsync(async () => await EjecutarSincronizacionImapSilenciosaAsync());
            });
        }

        string rolGuardado = DatabaseService.ObtenerRolOperativoActivo();
        AplicarRolOperativo(rolGuardado, navegarSiPestanaOculta: false);
        ActualizarBadgeNotificacionesCheckpoints();
        ActualizarBadgeBandejaCorreosSolicitud();
    }

    private async System.Threading.Tasks.Task EjecutarSincronizacionImapSilenciosaAsync()
    {
        if (_sincronizandoImapEnFondo) return;
        try
        {
            var cfg = DatabaseService.ObtenerConfiguracionSmtpCorreo();
            if (!cfg.ImapRecepcionActiva || !cfg.TieneCredencialesCompletas) return;

            _sincronizandoImapEnFondo = true;
            var (_, _, autoCreadas, _) = await DatabaseService.SincronizarCorreosEntrantesImapAsync();
            ActualizarBadgeBandejaCorreosSolicitud();
            if (autoCreadas > 0)
            {
                RefrescarSolicitudes();
            }
        }
        catch
        {
            // Silencioso en segundo plano
        }
        finally
        {
            _sincronizandoImapEnFondo = false;
        }
    }

    private void ActualizarBadgeBandejaCorreosSolicitud()
    {
        try
        {
            if (BtnBandejaCorreosSol == null) return;
            var pendientes = DatabaseService.ObtenerCorreosEntrantesSolicitud("PENDIENTES");
            BtnBandejaCorreosSol.Content = pendientes.Count > 0
                ? $"Bandeja Correo ({pendientes.Count})"
                : "Bandeja Correo";
        }
        catch
        {
            // Ignorar si aún no está inicializado
        }
    }

    private void AplicarRolOperativo(string rol, bool navegarSiPestanaOculta = true)
    {
        _rolOperativoActual = string.Equals(rol, "Almacén", StringComparison.OrdinalIgnoreCase)
            ? "Almacén"
            : "Compras";

        DatabaseService.GuardarRolOperativoActivo(_rolOperativoActual);
        AplicarPermisosSesionActual(navegarSiPestanaOculta);
    }

    private void BtnRolOperativo_Click(object sender, RoutedEventArgs e)
    {
        MostrarDialogoSesionYCuenta();
    }

    // =========================================================================
    // BANDEJA DE NOTIFICACIONES Y BITÁCORA DE CHECKPOINTS POR ROL
    // =========================================================================

    private void RegistrarTransicionCheckpoint(
        string etapa,
        string codigoCheckpoint,
        string folio,
        string estatusAnterior,
        string estatusNuevo,
        string rolesDestino,
        string mensaje)
    {
        DatabaseService.RegistrarCheckpoint(
            etapa,
            codigoCheckpoint,
            folio,
            estatusAnterior,
            estatusNuevo,
            _rolOperativoActual,
            rolesDestino,
            mensaje);

        ActualizarBadgeNotificacionesCheckpoints();
    }

    private void ActualizarBadgeNotificacionesCheckpoints()
    {
        if (BtnNotificacionesCheckpoints == null) return;
        int noLeidas = DatabaseService.ContarNotificacionesNoLeidas(_rolOperativoActual);
        BtnNotificacionesCheckpoints.Content = noLeidas > 0
            ? $"Avisos ({noLeidas})"
            : "Avisos";
    }

    private void BtnNotificacionesCheckpoints_Click(object sender, RoutedEventArgs e)
    {
        var usrActual = DatabaseService.ObtenerUsuarioActivo();
        var win = new Window
        {
            Title = $"Bandeja de Avisos, Checkpoints y Mensajes Internos — Usuario: {usrActual.Nombre} ({_rolOperativoActual})",
            Width = 980,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var cboFiltroRol = new ComboBox { Width = 230, Margin = new Thickness(8, 0, 8, 0) };
        cboFiltroRol.Items.Add($"Para mí ({usrActual.Nombre} / {_rolOperativoActual})");
        cboFiltroRol.Items.Add("Todos los avisos y mensajes");
        cboFiltroRol.Items.Add("Compras");
        cboFiltroRol.Items.Add("Almacén");
        cboFiltroRol.Items.Add("Solicitante");
        cboFiltroRol.Items.Add("Dirección");
        cboFiltroRol.Items.Add("Finanzas");
        cboFiltroRol.SelectedIndex = 0;

        var btnMarcarLeidas = new System.Windows.Controls.Button
        {
            Content = "Marcar leídos",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var btnVerBitacora = new System.Windows.Controls.Button
        {
            Content = "Bitácora de Usuarios",
            Padding = new Thickness(12, 5, 12, 5)
        };

        var rightBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightBar.Children.Add(new TextBlock { Text = "Filtrar bandeja:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        rightBar.Children.Add(cboFiltroRol);
        rightBar.Children.Add(btnMarcarLeidas);
        rightBar.Children.Add(btnVerBitacora);
        DockPanel.SetDock(rightBar, Dock.Right);
        header.Children.Add(rightBar);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "Bandeja de Checkpoints y Mensajes Dirigidos entre Usuarios (@Usuario)", FontSize = 16, FontWeight = FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Envíe avisos internos vinculados a cualquier folio y consulte las transiciones operativas. Doble clic en un renglón para ir directo al folio.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush")
        });
        header.Children.Add(titleStack);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Barra para redactar mensaje directo a un Usuario o Rol vinculado a un folio
        var bdrMsg = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
        var gridMsg = new Grid();
        gridMsg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        gridMsg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        gridMsg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        gridMsg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridMsg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var usuariosActivos = DatabaseService.ObtenerUsuariosSistema(soloActivos: true);
        var cboDestUsuario = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ToolTip = "Persona específica a quien va dirigido el aviso (o Todos)" };
        cboDestUsuario.Items.Add("A cualquier usuario del rol");
        foreach (var u in usuariosActivos)
            cboDestUsuario.Items.Add(u.Nombre);
        cboDestUsuario.SelectedIndex = 0;

        var cboDestRol = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ToolTip = "Departamento / Rol destinatario" };
        cboDestRol.Items.Add("Almacén");
        cboDestRol.Items.Add("Compras");
        cboDestRol.Items.Add("Solicitante");
        cboDestRol.Items.Add("Dirección");
        cboDestRol.Items.Add("Finanzas");
        cboDestRol.SelectedIndex = EsModoAlmacen ? 1 : 0;

        var txtFolioRef = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Folio (ej. OC-2026-0001)", Margin = new Thickness(0, 0, 6, 0) };
        var txtMensajeDir = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Escribir aviso o instrucción para el usuario (ej. Favor de recoger material hoy con proveedor)...", Margin = new Thickness(0, 0, 6, 0) };
        var btnEnviarMsg = new Wpf.Ui.Controls.Button { Content = "Enviar Aviso", Appearance = ControlAppearance.Primary, Padding = new Thickness(12, 5, 12, 5) };

        Grid.SetColumn(cboDestUsuario, 0);
        Grid.SetColumn(cboDestRol, 1);
        Grid.SetColumn(txtFolioRef, 2);
        Grid.SetColumn(txtMensajeDir, 3);
        Grid.SetColumn(btnEnviarMsg, 4);
        gridMsg.Children.Add(cboDestUsuario);
        gridMsg.Children.Add(cboDestRol);
        gridMsg.Children.Add(txtFolioRef);
        gridMsg.Children.Add(txtMensajeDir);
        gridMsg.Children.Add(btnEnviarMsg);
        bdrMsg.Child = gridMsg;
        DockPanel.SetDock(bdrMsg, Dock.Top);
        root.Children.Add(bdrMsg);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Fecha", Binding = new System.Windows.Data.Binding("FechaHora"), Width = 120, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Tipo", Binding = new System.Windows.Data.Binding("CheckpointCodigo"), Width = 65, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "De (Origen)", Binding = new System.Windows.Data.Binding("RemitenteTexto"), Width = 135, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Para (Destino)", Binding = new System.Windows.Data.Binding("DestinatarioTexto"), Width = 145, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Folio", Binding = new System.Windows.Data.Binding("FolioReferencia"), Width = 120, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Estatus / Asunto", Binding = new System.Windows.Data.Binding("TransicionTexto"), Width = 155, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Mensaje / Detalle", Binding = new System.Windows.Data.Binding("Mensaje"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });

        void RecargarTabla()
        {
            string sel = cboFiltroRol.SelectedIndex switch
            {
                0 => _rolOperativoActual,
                1 => "Todos",
                _ => cboFiltroRol.SelectedItem?.ToString() ?? "Todos"
            };
            dg.ItemsSource = DatabaseService.ObtenerNotificacionesCheckpoint(sel, 150);
        }

        btnEnviarMsg.Click += (_, _) =>
        {
            string texto = txtMensajeDir.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(texto))
            {
                DialogoFluent.Show(win, "Escriba el mensaje o instrucción que desea enviar.", "Mensaje vacío", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string usrDest = cboDestUsuario.SelectedIndex <= 0 ? "Todos" : (cboDestUsuario.SelectedItem?.ToString() ?? "Todos");
            string rolDest = cboDestRol.SelectedItem?.ToString() ?? "Todos";
            string folio = txtFolioRef.Text?.Trim() ?? string.Empty;

            DatabaseService.EnviarNotificacionEntreUsuarios(usrDest, rolDest, folio, "Aviso Directo", texto);
            txtMensajeDir.Clear();
            txtFolioRef.Clear();
            RecargarTabla();
            ActualizarBadgeNotificacionesCheckpoints();
        };

        cboFiltroRol.SelectionChanged += (_, _) => RecargarTabla();
        btnMarcarLeidas.Click += (_, _) =>
        {
            DatabaseService.MarcarNotificacionesLeidas(_rolOperativoActual);
            ActualizarBadgeNotificacionesCheckpoints();
            RecargarTabla();
        };
        btnVerBitacora.Click += (_, _) => MostrarModalBitacoraUsuarios(win);

        dg.MouseDoubleClick += (_, _) =>
        {
            if (dg.SelectedItem is NotificacionCheckpoint notif && !string.IsNullOrWhiteSpace(notif.FolioReferencia) && notif.FolioReferencia != "GENERAL")
            {
                string folioLimpio = notif.FolioReferencia.Split(' ', '(')[0].Trim();
                win.Close();
                if (TxtOmniScanner != null)
                {
                    TxtOmniScanner.Text = folioLimpio;
                    BtnProcesarOmniScanner_Click(this, new RoutedEventArgs());
                }
            }
        };

        DatabaseService.MarcarNotificacionesLeidas(_rolOperativoActual);
        ActualizarBadgeNotificacionesCheckpoints();
        RecargarTabla();

        root.Children.Add(dg);
        win.Content = root;
        win.ShowDialog();
    }

    // =========================================================================
    // PASO 2: CHECKPOINTS DE SOLICITUDES (SOL-xxxx) Y ANEXOS (SOL-xxxx-A1)
    // =========================================================================

    private void BtnAprobarSurtirStockSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var sol = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId)
                  ?? DgHistorialSolicitudes.SelectedItem as Solicitud;

        if (sol == null || sol.Id <= 0)
        {
            DialogoFluent.Show(this, "Guarde primero la solicitud antes de aprobarla para surtido de stock.", "Solicitud en captura", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string estatusPrevio = sol.Estatus;
        DatabaseService.ActualizarEstatusSolicitud(sol.Id, "Autorizada para Surtir Stock");

        RegistrarTransicionCheckpoint(
            "Etapa 1: Solicitud",
            "CP-1A",
            sol.Folio,
            estatusPrevio,
            "Autorizada para Surtir Stock",
            "Almacén, Solicitante",
            $"Compras aprobó que la solicitud {sol.Folio} ({sol.ProyectoMostrar}) se surta directamente con stock de Almacén.");

        RefrescarSolicitudes(sol.Id);
        var solActualizada = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == sol.Id);
        if (solActualizada != null)
            CargarSolicitudEnEditor(solActualizada);
        RefrescarDashboard();

        var resp = DialogoFluent.Show(this,
            $"La solicitud {sol.Folio} cambió al estatus 'Autorizada para Surtir Stock' (Checkpoint CP-1A) y se notificó a Almacén.\n\n" +
            "¿Desea cargar ahora mismo sus artículos en el módulo de Salida de Material (SAL-xxxx)?",
            "Solicitud Aprobada para Surtir Stock",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp == MessageBoxResult.Yes && solActualizada != null)
        {
            CargarSolicitudEnSalidaPos(solActualizada);
            CambiarSeccion(5);
        }
    }

    private void BtnCrearAnexoSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var solBase = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId)
                      ?? DgHistorialSolicitudes.SelectedItem as Solicitud;

        if (solBase == null || solBase.Id <= 0)
        {
            DialogoFluent.Show(this, "Seleccione o guarde primero una solicitud existente para generarle un Anexo.", "Seleccione solicitud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Extraer folio raíz (por si ya es un anexo SOL-2026-0001-A1 -> raíz SOL-2026-0001)
        string folioRaiz = solBase.Folio;
        int idxAnexo = folioRaiz.IndexOf("-A", StringComparison.OrdinalIgnoreCase);
        if (idxAnexo > 0)
            folioRaiz = folioRaiz[..idxAnexo];

        var todas = DatabaseService.ObtenerSolicitudes();
        int numAnexo = 1;
        while (todas.Any(s => string.Equals(s.Folio, $"{folioRaiz}-A{numAnexo}", StringComparison.OrdinalIgnoreCase)))
        {
            numAnexo++;
        }
        string nuevoFolioAnexo = $"{folioRaiz}-A{numAnexo}";

        // Preparar en el editor una nueva solicitud vinculada como Anexo, conservando Departamento, Encargado y Proyecto
        _solicitudEditandoId = 0;
        _partidasNuevaSolicitud.Clear();
        DgPartidasNuevaSolicitud.Items.Refresh();

        var dep = CboSolDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Id == solBase.DepartamentoId);
        if (dep != null)
        {
            CboSolDepartamento.SelectedItem = dep;
            var encs = DatabaseService.ObtenerEncargados(dep.Id);
            CboSolEncargado.ItemsSource = encs;
            var enc = encs.FirstOrDefault(x => x.Id == solBase.EncargadoId);
            if (enc != null) CboSolEncargado.SelectedItem = enc;
        }

        if (CboSolProyecto != null)
        {
            var proy = solBase.ProyectoId > 0 ? CboSolProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == solBase.ProyectoId) : null;
            CboSolProyecto.SelectedItem = proy;
        }

        string notaAnexo = $"[ANEXO {nuevoFolioAnexo} de Solicitud {folioRaiz}] Artículos adicionales solicitados posterior al bloqueo de comparativa.";
        TxtSolNotas.Text = notaAnexo;

        var creada = DatabaseService.CrearSolicitud(
            solBase.DepartamentoId,
            solBase.EncargadoId,
            solBase.ProyectoId,
            "Anexo",
            notaAnexo,
            new List<SolicitudPartida>(),
            solBase.TipoSolicitud,
            nuevoFolioAnexo);

        RegistrarTransicionCheckpoint(
            "Etapa 1: Solicitud (Anexo)",
            "CP-1B-ANX",
            creada.Folio,
            solBase.Estatus,
            "Pendiente",
            "Compras",
            $"Se generó el Anexo {creada.Folio} vinculado a la solicitud {folioRaiz} para agregar partidas adicionales.");

        RefrescarSolicitudes(creada.Id);
        CargarSolicitudEnEditor(creada);
        MostrarDetalleSolicitud(true);

        DialogoFluent.Show(this,
            $"Se creó el Anexo {creada.Folio} vinculado a {folioRaiz}.\n\nAhora puede agregar los artículos adicionales sin alterar la cotización original.",
            "Anexo Creado",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // =========================================================================
    // PASO 3: RECEPCIÓN FLEXIBLE SIN OC EN ALMACÉN Y FINIQUITO DE OC PARCIAL
    // =========================================================================

    private void BtnFiniquitarOcParcial_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.PuedeAutorizarOc,
                "Finiquitar o cerrar parcialmente una Orden de Compra", "Responsable de Compras o Dirección"))
            return;

        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra.", "Sin OC seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var resp = DialogoFluent.Show(this,
            $"¿Desea finiquitar y dar por cerrada parcialmente la Orden de Compra {oc.Folio} ({oc.ProveedorNombre}) con el avance actual ({oc.AvanceRecepcionTexto})?\n\n" +
            "• Dejará de aparecer como entrega pendiente en la bandeja de Almacén.\n" +
            "• El stock recibido hasta hoy se conserva intacto.",
            "Finiquitar Orden de Compra Parcial (CP-6)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp != MessageBoxResult.Yes) return;

        string estatusPrevio = oc.Estatus;
        DatabaseService.ForzarEstatusOrdenCompraAdmin(oc.Id, "Cerrada Parcial Definitiva", "Finiquitada por Compras (CP-6): el proveedor ya no entregará el saldo restante.");

        RegistrarTransicionCheckpoint(
            "Etapa 4: Cierre Parcial OC",
            "CP-6",
            oc.Folio,
            estatusPrevio,
            "Cerrada Parcial (Admin)",
            "Almacén, Finanzas",
            $"Compras finiquitó la {oc.Folio} con avance {oc.AvanceRecepcionTexto}. Ya no se espera entrega del resto.");

        CargarTodoElSistema();
        DialogoFluent.Show(this, $"La Orden de Compra {oc.Folio} quedó marcada como 'Cerrada Parcial (Admin)' y salió de pendientes de Almacén.", "OC Finiquitada", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnEntradaSinOcAlmacen_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.PuedeRecibirAlmacen,
                "Registrar entrada física directa de material a Almacén", "Auxiliar de Almacén o Responsable de Compras"))
            return;

        var win = new Window
        {
            Title = "Registrar Entrada Directa a Almacén sin Orden de Compra (Por Regularizar - CP-5C)",
            Width = 780,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };

        var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        topStack.Children.Add(new TextBlock
        {
            Text = "Entrada de Material sin Orden de Compra Previa",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        topStack.Children.Add(new TextBlock
        {
            Text = "Use esta ventana cuando el proveedor entrega material en Almacén antes de que se haya emitido la OC. Capture el folio de remisión/factura impresa y las cantidades físicas; Compras recibirá una alerta para regularizar precios y subir el XML.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10)
        });

        var formGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var spProv = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spProv.Children.Add(new TextBlock { Text = "Proveedor que entrega *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboProv = new ComboBox
        {
            ItemsSource = DatabaseService.ObtenerProveedores(),
            IsEditable = true
        };
        spProv.Children.Add(cboProv);

        var spFolio = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        spFolio.Children.Add(new TextBlock { Text = "Folio Remisión / Factura impresa *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtFolioDoc = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. REM-8841 o FAC-1029" };
        spFolio.Children.Add(txtFolioDoc);

        var spProy = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        spProy.Children.Add(new TextBlock { Text = "Proyecto / Referencia (Opcional)", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboProy = new ComboBox
        {
            ItemsSource = DatabaseService.ObtenerProyectos(),
            IsEditable = true
        };
        spProy.Children.Add(cboProy);

        Grid.SetColumn(spProv, 0);
        Grid.SetColumn(spFolio, 1);
        Grid.SetColumn(spProy, 2);
        formGrid.Children.Add(spProv);
        formGrid.Children.Add(spFolio);
        formGrid.Children.Add(spProy);
        topStack.Children.Add(formGrid);

        // Barra para agregar artículos
        var addBar = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var productosCat = DatabaseService.ObtenerProductos();
        var cboProd = new ComboBox
        {
            ItemsSource = productosCat,
            IsEditable = true,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var txtCant = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Cant. (1)", Text = "1", Margin = new Thickness(0, 0, 6, 0) };
        var txtUbic = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ubicación (A-01)", Text = "A-01", Margin = new Thickness(0, 0, 6, 0) };
        var btnAdd = new Wpf.Ui.Controls.Button { Content = "+ Agregar artículo", Appearance = ControlAppearance.Primary };

        cboProd.SelectionChanged += (_, _) =>
        {
            if (cboProd.SelectedItem is Producto pSel && !string.IsNullOrWhiteSpace(pSel.Ubicacion))
                txtUbic.Text = pSel.Ubicacion;
        };

        Grid.SetColumn(cboProd, 0);
        Grid.SetColumn(txtCant, 1);
        Grid.SetColumn(txtUbic, 2);
        Grid.SetColumn(btnAdd, 3);
        addBar.Children.Add(cboProd);
        addBar.Children.Add(txtCant);
        addBar.Children.Add(txtUbic);
        addBar.Children.Add(btnAdd);
        topStack.Children.Add(addBar);

        DockPanel.SetDock(topStack, Dock.Top);
        root.Children.Add(topStack);

        var partidas = new ObservableCollection<EntradaPartida>();
        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            ItemsSource = partidas
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("ProductoCodigo"), IsReadOnly = true, Width = 100 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Producto", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Cantidad Recibida", Binding = new System.Windows.Data.Binding("CantidadCompraRecibida"), Width = 130 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadAlmacen"), IsReadOnly = true, Width = 95 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Ubicación Anaquel", Binding = new System.Windows.Data.Binding("UbicacionAlmacen"), Width = 135 });

        btnAdd.Click += (_, _) =>
        {
            if (cboProd.SelectedItem is not Producto prod)
            {
                DialogoFluent.Show(win, "Seleccione un artículo del catálogo.", "Artículo requerido", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            decimal cant = ParseDecimal(txtCant.Text, 1m);
            if (cant <= 0) cant = 1m;
            string ubic = string.IsNullOrWhiteSpace(txtUbic.Text) ? prod.Ubicacion : txtUbic.Text.Trim();

            var existente = partidas.FirstOrDefault(x => x.ProductoId == prod.Id);
            if (existente != null)
            {
                existente.CantidadCompraRecibida += cant;
                existente.CantidadIngresadaAlmacen = existente.CantidadCompraRecibida * existente.FactorConversion;
                dg.Items.Refresh();
            }
            else
            {
                decimal factor = prod.FactorConversion > 0 ? prod.FactorConversion : 1m;
                partidas.Add(new EntradaPartida
                {
                    ProductoId = prod.Id,
                    ProductoCodigo = prod.CodigoInterno,
                    ProductoNombre = prod.Nombre,
                    CantidadCompraRecibida = cant,
                    UnidadCompra = prod.UnidadCompra,
                    FactorConversion = factor,
                    CantidadIngresadaAlmacen = cant * factor,
                    UnidadAlmacen = prod.UnidadAlmacen,
                    UbicacionAlmacen = ubic,
                    CostoUnitarioCompraConIva = prod.UltimoCostoCompra
                });
            }
        };

        var bottomBar = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var txtNotas = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Observaciones de la recepción sin OC (quién autorizó verbalmente, urgencia, etc.)...", Width = 440, HorizontalAlignment = HorizontalAlignment.Left };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Registrar Entrada (ENT-xxxx)", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnSave);
        DockPanel.SetDock(btnPanel, Dock.Right);
        bottomBar.Children.Add(btnPanel);
        bottomBar.Children.Add(txtNotas);
        DockPanel.SetDock(bottomBar, Dock.Bottom);
        root.Children.Add(bottomBar);
        root.Children.Add(dg);

        EntradaAlmacen? entradaCreada = null;
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnSave.Click += (_, _) =>
        {
            string provNombre = (cboProv.SelectedItem as Proveedor)?.NombreMostrar ?? cboProv.Text?.Trim() ?? string.Empty;
            string folioDoc = txtFolioDoc.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(provNombre))
            {
                DialogoFluent.Show(win, "Indique el proveedor que entregó el material.", "Proveedor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(folioDoc))
            {
                DialogoFluent.Show(win, "Capture el Folio de Remisión o Factura impresa del proveedor para que Compras pueda vincular el XML.", "Folio requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (partidas.Count == 0)
            {
                DialogoFluent.Show(win, "Agregue al menos un artículo recibido.", "Sin artículos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string proyNombre = (cboProy.SelectedItem as Proyecto)?.Nombre ?? cboProy.Text?.Trim() ?? "Stock General (Bolsa Libre)";
            string notasFinal = $"Folio Remisión/Factura: {folioDoc}" + (string.IsNullOrWhiteSpace(txtNotas.Text) ? "" : $" | {txtNotas.Text.Trim()}");

            entradaCreada = DatabaseService.RegistrarEntradaDirectaSinOc(provNombre, proyNombre, folioDoc, notasFinal, partidas.ToList());
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && entradaCreada != null)
        {
            RegistrarTransicionCheckpoint(
                "Etapa 4: Recepción sin OC",
                "CP-5C",
                entradaCreada.Folio,
                "Sin OC previa",
                "Por Regularizar en Compras",
                "Compras, Finanzas",
                $"Almacén registró {entradaCreada.Folio} sin OC previa (Proveedor: {entradaCreada.ProveedorNombre}, Folio doc: {entradaCreada.FacturaUuid}). Pendiente subir XML / regularizar OC.");

            CargarTodoElSistema();
            DialogoFluent.Show(this,
                $"Se registró la entrada {entradaCreada.Folio} en bolsa libre de stock y se envió una notificación (Checkpoint CP-5C) a Compras para regularizar la Orden de Compra / subir el XML de la factura '{entradaCreada.FacturaUuid}'.",
                "Entrada Directa Registrada",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    // =========================================================================
    // PASO 4: SALIDAS DE MATERIAL (SAL-xxxx) PRECARGADAS DESDE SOL U OC
    // =========================================================================

    private void BtnCargarMaterialOcActualEnSalida_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra.", "Sin OC seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (oc.Partidas.All(p => p.CantidadRecibida <= 0m))
        {
            DialogoFluent.Show(this,
                $"La Orden de Compra {oc.Folio} aún no tiene recepciones registradas en Almacén.\n\nPrimero registre la entrada física (ENT-xxxx) antes de generar un Vale de Salida.",
                "OC aún no recibida en Almacén",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CargarOrdenCompraEnSalidaPos(oc);
        if (_partidasSalidaPos.Count > 0)
            CambiarSeccion(5);
    }

    private void CargarOrdenCompraEnSalidaPos(OrdenCompra oc)
    {
        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var yaEntregadasOc = DatabaseService.ObtenerCantidadesEntregadasPorFolioEnSalidas(oc.Folio);
        _partidasSalidaPos.Clear();

        int omitidosPorYaEntregados = 0;
        foreach (var part in oc.Partidas)
        {
            if (part.ProductoId > 0 && part.CantidadRecibida > 0m && prodsDict.TryGetValue(part.ProductoId, out var prod))
            {
                decimal factor = part.FactorConversion > 0 ? part.FactorConversion : (prod.FactorConversion > 0 ? prod.FactorConversion : 1m);
                decimal cantAlmacen = Math.Round(part.CantidadRecibida * factor, 2);
                if (yaEntregadasOc.TryGetValue(part.ProductoId, out decimal yaSalio) && yaSalio > 0m)
                {
                    cantAlmacen = Math.Max(0m, cantAlmacen - yaSalio);
                    if (cantAlmacen <= 0m)
                        omitidosPorYaEntregados++;
                }

                if (cantAlmacen > 0 && prod.StockActual > 0)
                    AgregarProductoAValeSalidaPos(prod, Math.Min(cantAlmacen, prod.StockActual));
            }
        }

        if (_partidasSalidaPos.Count == 0 && omitidosPorYaEntregados > 0)
        {
            DialogoFluent.Show(this,
                $"Los artículos recibidos de la Orden de Compra {oc.Folio} ya fueron entregados previamente en un Vale de Salida (SAL-xxxx).",
                "Material ya entregado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (CboSalDepartamento != null && !string.IsNullOrWhiteSpace(oc.DepartamentoNombre))
        {
            var dep = CboSalDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Nombre.Equals(oc.DepartamentoNombre, StringComparison.OrdinalIgnoreCase));
            if (dep != null) CboSalDepartamento.SelectedItem = dep;
        }
        if (CboSalEncargado != null && !string.IsNullOrWhiteSpace(oc.EncargadoNombre))
        {
            CboSalEncargado.Text = oc.EncargadoNombre;
        }
        if (CboSalProyecto != null && oc.ProyectoId > 0)
        {
            var proy = CboSalProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == oc.ProyectoId);
            if (proy != null) CboSalProyecto.SelectedItem = proy;
        }
        if (TxtSalNotas != null)
        {
            string refSol = !string.IsNullOrWhiteSpace(oc.SolicitudFolio) ? $" (Solicitud {oc.SolicitudFolio})" : "";
            TxtSalNotas.Text = $"Entrega de material de {oc.Folio}{refSol}";
        }
    }

    private void CargarSolicitudEnSalidaPos(Solicitud sol)
    {
        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var yaEntregadasSol = DatabaseService.ObtenerCantidadesEntregadasPorFolioEnSalidas(sol.Folio);
        _partidasSalidaPos.Clear();

        int agregados = 0;
        int yaCubiertosPrevios = 0;
        foreach (var part in sol.Partidas)
        {
            if (part.ProductoId > 0 && prodsDict.TryGetValue(part.ProductoId, out var prod))
            {
                decimal cantRequerida = DatabaseService.ConvertirCantidadSolicitudAUnidadAlmacen(part, prod);
                if (yaEntregadasSol.TryGetValue(part.ProductoId, out decimal yaSalio) && yaSalio > 0m)
                {
                    cantRequerida = Math.Max(0m, cantRequerida - yaSalio);
                    if (cantRequerida <= 0m)
                    {
                        yaCubiertosPrevios++;
                        continue;
                    }
                }

                if (prod.StockActual > 0 && cantRequerida > 0)
                {
                    decimal cantEntregar = Math.Min(cantRequerida, prod.StockActual);
                    if (cantEntregar > 0)
                    {
                        AgregarProductoAValeSalidaPos(prod, cantEntregar);
                        agregados++;
                    }
                }
            }
        }

        if (CboSalDepartamento != null && sol.DepartamentoId > 0)
        {
            var dep = CboSalDepartamento.Items.Cast<Departamento>().FirstOrDefault(d => d.Id == sol.DepartamentoId);
            if (dep != null) CboSalDepartamento.SelectedItem = dep;
        }
        if (CboSalEncargado != null && !string.IsNullOrWhiteSpace(sol.EncargadoNombre))
        {
            CboSalEncargado.Text = sol.EncargadoNombre;
        }
        if (CboSalProyecto != null && sol.ProyectoId > 0)
        {
            var proy = CboSalProyecto.Items.Cast<Proyecto>().FirstOrDefault(p => p.Id == sol.ProyectoId);
            if (proy != null) CboSalProyecto.SelectedItem = proy;
        }
        if (TxtSalNotas != null)
        {
            TxtSalNotas.Text = $"Surtido de Solicitud {sol.Folio} — {sol.ProyectoMostrar}";
        }

        if (agregados == 0)
        {
            if (yaCubiertosPrevios > 0)
            {
                DialogoFluent.Show(this,
                    $"Los artículos de la solicitud {sol.Folio} ya fueron surtidos previamente en un Vale de Salida (SAL-xxxx).",
                    "Solicitud ya surtida",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                DialogoFluent.Show(this,
                    $"Ninguno de los artículos pendientes de la solicitud {sol.Folio} tiene stock disponible (> 0) en este momento en Almacén.",
                    "Sin stock disponible para surtir",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void BtnCargarMaterialSolicitudEnSalida_Click(object sender, RoutedEventArgs e)
    {
        var solicitudes = DatabaseService.ObtenerSolicitudes()
            .Where(s => !string.Equals(s.Estatus, "Atendida", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => string.Equals(s.Estatus, "Autorizada para Surtir Stock", StringComparison.OrdinalIgnoreCase) ? 2 :
                                    string.Equals(s.Estatus, "OC Generada", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(s => s.Id)
            .ToList();

        if (solicitudes.Count == 0)
        {
            DialogoFluent.Show(this, "No hay solicitudes activas pendientes de surtir.", "Sin solicitudes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = "Seleccionar Solicitud (SOL-xxxx) para Precargar Salida de Almacén",
            Width = 680,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock { Text = "Precargar Artículos desde una Solicitud (SOL-xxxx)", FontSize = 15, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock
        {
            Text = "Seleccione la solicitud cuyos artículos desea entregar hoy en Almacén (se cargarán automáticamente las existencias disponibles en bolsa libre):",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = solicitudes
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Folio", Binding = new System.Windows.Data.Binding("Folio"), Width = 135 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Proyecto / Destino", Binding = new System.Windows.Data.Binding("ProyectoMostrar"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Solicitante", Binding = new System.Windows.Data.Binding("EncargadoNombre"), Width = 140 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Artículos", Binding = new System.Windows.Data.Binding("ResumenArticulos"), Width = 120 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Estatus", Binding = new System.Windows.Data.Binding("Estatus"), Width = 165 });
        if (solicitudes.Count > 0) dg.SelectedIndex = 0;

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnOk = new Wpf.Ui.Controls.Button { Content = "Cargar Artículos en Salida", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        bottom.Children.Add(btnCancel);
        bottom.Children.Add(btnOk);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(dg);

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click += (_, _) =>
        {
            if (dg.SelectedItem is not Solicitud) return;
            win.DialogResult = true;
        };
        dg.MouseDoubleClick += (_, _) =>
        {
            if (dg.SelectedItem is Solicitud) win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && dg.SelectedItem is Solicitud solElegida)
        {
            CargarSolicitudEnSalidaPos(solElegida);
        }
    }

    // =========================================================================
    // ENCABEZADOS CON NOMBRE COMERCIAL EN CUADRO COMPARATIVO Y ALTA RÁPIDA DE PROVEEDOR
    // =========================================================================

    private void ActualizarEncabezadosColumnasTablaComparativa()
    {
        if (ColCompTablaProv1 == null || ColCompTablaProv2 == null || ColCompTablaProv3 == null)
            return;

        var provs1 = _renglonesComparativa
            .Where(r => r.Proveedor1Id > 0 && !string.IsNullOrWhiteSpace(r.Proveedor1Nombre) && r.Proveedor1Nombre != "Sin asignar")
            .Select(r => Proveedor.ExtraerNombreComercialCorto(r.Proveedor1Nombre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var provs2 = _renglonesComparativa
            .Where(r => r.Proveedor2Id > 0 && (r.NumeroOpcionesVisibles >= 2 || r.PrecioSinIva2 > 0) && !string.IsNullOrWhiteSpace(r.Proveedor2Nombre) && r.Proveedor2Nombre != "Sin asignar")
            .Select(r => Proveedor.ExtraerNombreComercialCorto(r.Proveedor2Nombre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var provs3 = _renglonesComparativa
            .Where(r => r.Proveedor3Id > 0 && (r.NumeroOpcionesVisibles >= 3 || r.PrecioSinIva3 > 0) && !string.IsNullOrWhiteSpace(r.Proveedor3Nombre) && r.Proveedor3Nombre != "Sin asignar")
            .Select(r => Proveedor.ExtraerNombreComercialCorto(r.Proveedor3Nombre))
            .Concat(_renglonesComparativa.SelectMany(r => r.ProveedoresExtra)
                .Where(ex => ex.ProveedorId > 0 && !string.IsNullOrWhiteSpace(ex.ProveedorNombre))
                .Select(ex => Proveedor.ExtraerNombreComercialCorto(ex.ProveedorNombre)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ColCompTablaProv1.Header = provs1.Count == 1
            ? AbreviarNombreProveedorSlot(provs1[0], 20)
            : (provs1.Count > 1 ? string.Join(" / ", provs1.Take(2).Select(p => AbreviarNombreProveedorSlot(p, 12))) : "Proveedor 1");

        ColCompTablaProv2.Header = provs2.Count == 1
            ? AbreviarNombreProveedorSlot(provs2[0], 20)
            : (provs2.Count > 1 ? string.Join(" / ", provs2.Take(2).Select(p => AbreviarNombreProveedorSlot(p, 12))) : "Proveedor 2");

        ColCompTablaProv3.Header = provs3.Count == 1
            ? AbreviarNombreProveedorSlot(provs3[0], 20)
            : (provs3.Count > 1 ? string.Join(" / ", provs3.Take(2).Select(p => AbreviarNombreProveedorSlot(p, 12))) + (provs3.Count > 2 ? $" +{provs3.Count - 2}" : "") : "Proveedor 3");

        bool mostrarCol2 = _renglonesComparativa.Any(r => r.NumeroOpcionesVisibles >= 2 || r.Proveedor2Id > 0 || r.PrecioSinIva2 > 0);
        bool mostrarCol3 = _renglonesComparativa.Any(r => r.NumeroOpcionesVisibles >= 3 || r.Proveedor3Id > 0 || r.PrecioSinIva3 > 0 || r.ProveedoresExtra.Count > 0);

        ColCompTablaProv2.Visibility = mostrarCol2 ? Visibility.Visible : Visibility.Collapsed;
        ColCompTablaProv3.Visibility = mostrarCol3 ? Visibility.Visible : Visibility.Collapsed;

        // Actualizar Tabla Final de Totales por Proveedor Adjudicado
        var adjudicados = _renglonesComparativa
            .Where(r => r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .GroupBy(r => r.ProveedorGanadorId)
            .Select(g =>
            {
                string nomCom = Proveedor.ExtraerNombreComercialCorto(g.First().ProveedorGanadorNombre);
                decimal subPartidas = g.Sum(x => Math.Round(x.Cantidad * x.PrecioUnitarioGanadorSinIva, 2));
                decimal fleteConIva = g.Sum(x => x.FleteGanadorConIva);
                decimal fleteSinIva = Math.Round(fleteConIva / 1.16m, 2);
                decimal subTotal = Math.Round(subPartidas + fleteSinIva, 2);
                decimal iva = Math.Round(subTotal * 0.16m, 2);
                decimal total = Math.Round(subTotal + iva, 2);
                var foliosOc = g.Where(x => x.TieneOcGenerada).Select(x => x.OrdenCompraFolio).Distinct().ToList();
                int pendientesOc = g.Count(x => !x.TieneOcGenerada);
                string estado = foliosOc.Count > 0 && pendientesOc == 0
                    ? $"OC: {string.Join(", ", foliosOc)}"
                    : (foliosOc.Count > 0 ? $"Parcial ({string.Join(", ", foliosOc)})" : "Lista para emitir OC");

                return new ResumenOcPrevia
                {
                    ProveedorId = g.Key,
                    ProveedorNombre = nomCom,
                    CantidadPartidas = g.Count(),
                    ProductosResumen = string.Join(", ", g.Select(x => string.IsNullOrWhiteSpace(x.ProductoCodigo) ? "CONCEPTO" : x.ProductoCodigo)),
                    DiasEntregaMax = g.Max(x => x.DiasEntregaGanador),
                    SubtotalSinIva = subTotal,
                    FleteConIva = fleteConIva,
                    IvaTotal = iva,
                    TotalConIva = total,
                    EstadoEmision = estado
                };
            })
            .OrderByDescending(x => x.TotalConIva)
            .ToList();

        if (DgTotalesAdjudicadosComp != null)
            DgTotalesAdjudicadosComp.ItemsSource = adjudicados;

        if (TxtGranTotalAdjudicadoComp != null)
        {
            decimal granTotal = adjudicados.Sum(x => x.TotalConIva);
            int totalPartAdj = adjudicados.Sum(x => x.CantidadPartidas);
            TxtGranTotalAdjudicadoComp.Text = $"Proveedores Adjudicados: {adjudicados.Count} ({totalPartAdj} partidas) | Gran Total c/IVA: {granTotal:C2}";
        }
    }

    private void BtnNuevoProveedorRapidoComp_Click(object sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Registrar Nuevo Proveedor (Nombre Comercial)",
            Width = 480,
            Height = 410,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Alta Rápida de Proveedor para Cuadro Comparativo",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "El Nombre Comercial aparecerá como encabezado en las tarjetas y en el Cuadro Comparativo impreso.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Nombre Comercial * (Ej. Home Depot, FerreIndustrial, Grainger):",
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 3)
        });
        var txtComercial = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Nombre comercial corto con el que se identifica al proveedor",
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(txtComercial);

        root.Children.Add(new TextBlock
        {
            Text = "Razón Social (Opcional al cotizar — para Facturación):",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        });
        var txtRazon = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Ej. Ferretería Industrial S.A. de C.V. (si se omite usa el Nombre Comercial)",
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(txtRazon);

        var gridFila = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        gridFila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridFila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridFila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });

        var spRfc = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spRfc.Children.Add(new TextBlock { Text = "RFC (Opcional):", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtRfc = new Wpf.Ui.Controls.TextBox { PlaceholderText = "XAXX010101000" };
        spRfc.Children.Add(txtRfc);

        var spTel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spTel.Children.Add(new TextBlock { Text = "Teléfono / Contacto:", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtTel = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. 999-123-4567" };
        spTel.Children.Add(txtTel);

        var spDias = new StackPanel();
        spDias.Children.Add(new TextBlock { Text = "Entrega (días):", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtDias = new Wpf.Ui.Controls.TextBox { Text = "2" };
        spDias.Children.Add(txtDias);

        Grid.SetColumn(spRfc, 0);
        Grid.SetColumn(spTel, 1);
        Grid.SetColumn(spDias, 2);
        gridFila.Children.Add(spRfc);
        gridFila.Children.Add(spTel);
        gridFila.Children.Add(spDias);
        root.Children.Add(gridFila);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Guardar Proveedor", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnSave);
        root.Children.Add(btnPanel);

        int nuevoProvId = 0;
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnSave.Click += (_, _) =>
        {
            string nomCom = (txtComercial.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nomCom))
            {
                DialogoFluent.Show(win, "Escriba el Nombre Comercial del proveedor.", "Nombre Comercial requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string razon = string.IsNullOrWhiteSpace(txtRazon.Text) ? nomCom : txtRazon.Text.Trim();
            string rfc = string.IsNullOrWhiteSpace(txtRfc.Text) ? "XAXX010101000" : txtRfc.Text.Trim().ToUpperInvariant();
            int dias = Math.Max(1, (int)ParseDecimal(txtDias.Text, 2m));

            var existente = DatabaseService.ObtenerProveedores().FirstOrDefault(p =>
                string.Equals(p.NombreComercial.Trim(), nomCom, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.RazonSocial.Trim(), razon, StringComparison.OrdinalIgnoreCase) ||
                (!string.Equals(rfc, "XAXX010101000", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(p.Rfc.Trim(), rfc, StringComparison.OrdinalIgnoreCase)));

            if (existente != null)
            {
                if (!string.Equals(rfc, "XAXX010101000", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existente.Rfc.Trim(), "XAXX010101000", StringComparison.OrdinalIgnoreCase))
                {
                    existente.Rfc = rfc;
                    DatabaseService.GuardarProveedor(existente);
                }
                nuevoProvId = existente.Id;
            }
            else
            {
                nuevoProvId = DatabaseService.GuardarProveedor(new Proveedor
                {
                    NombreComercial = nomCom,
                    RazonSocial = razon,
                    Rfc = rfc,
                    Telefono = (txtTel.Text ?? string.Empty).Trim(),
                    DiasEntregaPromedio = dias,
                    CondicionesPago = "Contado"
                });
            }
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && nuevoProvId > 0)
        {
            var provs = DatabaseService.ObtenerProveedores();
            int p1Prev = (CboCompProv1?.SelectedItem as Proveedor)?.Id ?? 0;
            int p2Prev = (CboCompProv2?.SelectedItem as Proveedor)?.Id ?? 0;
            int p3Prev = (CboCompProv3?.SelectedItem as Proveedor)?.Id ?? 0;
            int hojaPrev = (CboHojaProveedor?.SelectedItem as Proveedor)?.Id ?? 0;

            bool prevCargando = _cargandoDetalleComparativa;
            bool prevSincHoja = _sincronizandoHojaProveedor;
            _cargandoDetalleComparativa = true;
            _sincronizandoHojaProveedor = true;
            try
            {
                if (CboCompProv1 != null)
                {
                    CboCompProv1.ItemsSource = provs;
                    CboCompProv1.SelectedItem = p1Prev > 0 ? provs.FirstOrDefault(p => p.Id == p1Prev) : null;
                }
                if (CboCompProv2 != null)
                {
                    CboCompProv2.ItemsSource = provs;
                    CboCompProv2.SelectedItem = p2Prev > 0 ? provs.FirstOrDefault(p => p.Id == p2Prev) : null;
                }
                if (CboCompProv3 != null)
                {
                    CboCompProv3.ItemsSource = provs;
                    CboCompProv3.SelectedItem = p3Prev > 0 ? provs.FirstOrDefault(p => p.Id == p3Prev) : null;
                }
                if (CboHojaProveedor != null)
                {
                    CboHojaProveedor.ItemsSource = provs;
                    CboHojaProveedor.SelectedItem = hojaPrev > 0 ? provs.FirstOrDefault(p => p.Id == hojaPrev) : null;
                }
                if (DgDirProveedores != null) DgDirProveedores.ItemsSource = provs;
            }
            finally
            {
                _cargandoDetalleComparativa = prevCargando;
                _sincronizandoHojaProveedor = prevSincHoja;
            }

            var nuevoProv = provs.FirstOrDefault(p => p.Id == nuevoProvId);
            if (nuevoProv != null && CboHojaProveedor != null && TcModoCapturaComparativa?.SelectedIndex == 0)
            {
                var cols = ObtenerColumnasProveedoresComparativa();
                var colActiva = cols.FirstOrDefault(c => c.indiceColumna == _indiceSlotHojaProveedorActiva);
                int cotActiva = colActiva.provId > 0 ? ContarCotizadosDeProveedor(colActiva.provId) : 0;

                if (colActiva.provId > 0 && cotActiva > 0)
                {
                    // La columna actual ya tiene precios de otro proveedor: abrir el nuevo proveedor en una columna nueva (o libre)
                    ActivarOAbrirHojaDeProveedor(nuevoProv.Id);
                }
                else
                {
                    CboHojaProveedor.SelectedItem = nuevoProv;
                }
            }
            else if (nuevoProv != null && DgComparativa.SelectedItem is CotizacionRenglonComparativo r && !r.TieneOcGenerada)
            {
                if (r.Proveedor1Id <= 0 && CboCompProv1 != null)
                {
                    CboCompProv1.SelectedItem = nuevoProv;
                }
                else if (r.NumeroOpcionesVisibles >= 2 && r.Proveedor2Id <= 0 && CboCompProv2 != null)
                {
                    CboCompProv2.SelectedItem = nuevoProv;
                }
                else if (r.NumeroOpcionesVisibles >= 3 && r.Proveedor3Id <= 0 && CboCompProv3 != null)
                {
                    CboCompProv3.SelectedItem = nuevoProv;
                }
                else
                {
                    // Si las opciones visibles ya tienen proveedor, abrir la siguiente tarjeta o registrarlo en Hoja
                    if (r.NumeroOpcionesVisibles < 2 && CboCompProv2 != null)
                    {
                        r.NumeroOpcionesVisibles = 2;
                        ActualizarVisibilidadTarjetasComparativa(r);
                        CboCompProv2.SelectedItem = nuevoProv;
                    }
                    else if (r.NumeroOpcionesVisibles < 3 && CboCompProv3 != null)
                    {
                        r.NumeroOpcionesVisibles = 3;
                        ActualizarVisibilidadTarjetasComparativa(r);
                        CboCompProv3.SelectedItem = nuevoProv;
                    }
                    else
                    {
                        ActivarOAbrirHojaDeProveedor(nuevoProv.Id);
                    }
                }
            }
        }
    }

    private void BtnVincularLineaAbiertaComp_Click(object sender, RoutedEventArgs e)
    {
        if (DgComparativa?.SelectedItem is not CotizacionRenglonComparativo r)
        {
            DialogoFluent.Show(this, "Seleccione primero una partida en la tabla de comparativa.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (r.ProductoId > 0)
        {
            DialogoFluent.Show(this, $"Esta partida ya está vinculada al producto [{r.ProductoCodigo}] {r.ProductoNombre}.", "Ya vinculado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SincronizarRenglonActualDesdeTarjetas();
        string desc = !string.IsNullOrWhiteSpace(r.DescripcionLibre) ? r.DescripcionLibre : r.ProductoNombre;
        decimal mejorPrecio = new[] { r.PrecioSinIva1, r.PrecioSinIva2, r.PrecioSinIva3 }
            .Where(x => x > 0)
            .DefaultIfEmpty(0m)
            .Min();

        var prodVinculado = MostrarModalAsociarLineaAbiertaAProducto(
            descripcionConcepto: desc,
            familiaSugerida: r.Familia,
            unidadSugerida: r.UnidadCompra,
            costoSugeridoSinIva: mejorPrecio,
            solicitudPartidaId: r.SolicitudPartidaId,
            bancoPrecioId: null);

        if (prodVinculado != null)
        {
            r.ProductoId = prodVinculado.Id;
            r.ProductoCodigo = prodVinculado.CodigoInterno;
            r.ProductoNombre = prodVinculado.Nombre;
            r.Familia = prodVinculado.Familia;
            r.StockActualAlmacen = prodVinculado.StockActual;
            r.UltimoCostoCompraConIva = prodVinculado.UltimoCostoCompra;

            string folSol = (CboCompSolicitud?.SelectedItem as Solicitud)?.Folio ?? "COT";
            DatabaseService.GuardarCotizacionPartida(r, folSol);
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            CargarProductoEnTarjetasComparativa(r);
            RecalcularTotalesComparativa();
        }
    }

    private void BtnAsociarLineaAbiertaDirProv_Click(object sender, RoutedEventArgs e)
    {
        if (DgDesgloseProveedor?.SelectedItem == null)
        {
            DialogoFluent.Show(this, "Seleccione en la tabla una fila de 'Cotización (Línea Abierta)' para asociarla o convertirla en un SKU del Catálogo.", "Seleccione una cotización", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var itemSel = DgDesgloseProveedor.SelectedItem;
        var propCot = itemSel.GetType().GetProperty("CotizacionObj");
        var cot = propCot?.GetValue(itemSel) as RegistroCotizacionVigencia;
        if (cot == null)
        {
            DialogoFluent.Show(this, "Seleccione una fila que corresponda a una Cotización registrada para poder asociarla a un SKU del Catálogo.", "Fila no es cotización", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (cot.ProductoId > 0)
        {
            DialogoFluent.Show(this, $"Esta cotización ya está asociada al SKU [{cot.ProductoCodigo}] {cot.DescripcionConcepto}.", "Ya asociada a SKU", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prodVinculado = MostrarModalAsociarLineaAbiertaAProducto(
            descripcionConcepto: cot.DescripcionConcepto,
            familiaSugerida: cot.Familia,
            unidadSugerida: "Pieza",
            costoSugeridoSinIva: cot.PrecioSinIva,
            solicitudPartidaId: cot.SolicitudPartidaId > 0 ? cot.SolicitudPartidaId : null,
            bancoPrecioId: cot.Id > 0 ? cot.Id : null);

        if (prodVinculado != null)
        {
            var provSel = DgDirProveedores?.SelectedItem as Proveedor;
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            if (provSel != null && DgDirProveedores != null)
            {
                var reSel = (DgDirProveedores.ItemsSource as IEnumerable<Proveedor>)?.FirstOrDefault(x => x.Id == provSel.Id);
                if (reSel != null)
                    DgDirProveedores.SelectedItem = reSel;
            }
        }
    }

    private Producto? MostrarModalAsociarLineaAbiertaAProducto(
        string descripcionConcepto,
        string familiaSugerida,
        string unidadSugerida,
        decimal costoSugeridoSinIva,
        int? solicitudPartidaId,
        int? bancoPrecioId)
    {
        var todosProductos = DatabaseService.ObtenerProductos();
        Producto? resultado = null;

        var win = new Window
        {
            Foreground = Tk("TextFillColorPrimaryBrush"),
            Background = Tk("ControlFillColorDefaultBrush"),
            Title = "Asociar o Convertir Línea Abierta a Producto (SKU) del Catálogo",
            Width = 620,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "🔗 Asociar Línea Abierta al Catálogo de Productos",
            FontSize = 15.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Concepto cotizado: \"{descripcionConcepto}\"\nTodas las cotizaciones, precios y vigencias de este concepto quedarán vinculadas al SKU elegido.",
            FontSize = 11.5,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Sección 1: Asociar a SKU existente
        var bdrExistente = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderBrush = Tk("ControlElevationBorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spExistente = new StackPanel();
        spExistente.Children.Add(new TextBlock
        {
            Text = "Opción A · Asociar a un Producto (SKU) ya existente en el Catálogo",
            FontWeight = FontWeights.Bold,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var txtFiltro = new System.Windows.Controls.TextBox
        {
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "Escriba para filtrar por código, nombre o familia..."
        };
        spExistente.Children.Add(txtFiltro);

        var cboExistente = new ComboBox
        {
            ItemsSource = todosProductos,
            DisplayMemberPath = "EtiquetaCompleta",
            Margin = new Thickness(0, 0, 0, 8)
        };
        spExistente.Children.Add(cboExistente);

        txtFiltro.TextChanged += (_, _) =>
        {
            string f = (txtFiltro.Text ?? string.Empty).Trim();
            var filtrados = string.IsNullOrWhiteSpace(f)
                ? todosProductos
                : todosProductos.Where(p =>
                    p.Nombre.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    p.CodigoInterno.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    p.Familia.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            cboExistente.ItemsSource = filtrados;
            if (filtrados.Count > 0) cboExistente.SelectedIndex = 0;
        };

        var btnVincularExistente = new Wpf.Ui.Controls.Button
        {
            Content = "Vincular a este SKU Existente",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 6, 14, 6)
        };
        spExistente.Children.Add(btnVincularExistente);
        bdrExistente.Child = spExistente;
        root.Children.Add(bdrExistente);

        // Sección 2: Crear nuevo SKU y vincular
        var bdrNuevo = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderBrush = Tk("AccentFillColorDefaultBrush"),
            BorderThickness = new Thickness(1.2),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spNuevo = new StackPanel();
        spNuevo.Children.Add(new TextBlock
        {
            Text = "Opción B · Dar de alta como Nuevo Producto (SKU) en el Catálogo y vincular",
            FontWeight = FontWeights.Bold,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 8)
        });

        string sigSku = DatabaseService.GenerarSiguienteCodigoProducto("PRD", "Productos", "CodigoInterno");
        var gridNuevo = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        gridNuevo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        gridNuevo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridNuevo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var spSku = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spSku.Children.Add(new TextBlock { Text = "Nuevo SKU:", FontSize = 11 });
        var txtNuevoSku = new System.Windows.Controls.TextBox { Text = sigSku, Padding = new Thickness(6, 4, 6, 4) };
        spSku.Children.Add(txtNuevoSku);
        Grid.SetColumn(spSku, 0);
        gridNuevo.Children.Add(spSku);

        var spNom = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spNom.Children.Add(new TextBlock { Text = "Nombre en Catálogo:", FontSize = 11 });
        var txtNuevoNom = new System.Windows.Controls.TextBox { Text = descripcionConcepto, Padding = new Thickness(6, 4, 6, 4) };
        spNom.Children.Add(txtNuevoNom);
        Grid.SetColumn(spNom, 1);
        gridNuevo.Children.Add(spNom);

        var spFam = new StackPanel();
        spFam.Children.Add(new TextBlock { Text = "Familia:", FontSize = 11 });
        var txtNuevaFam = new System.Windows.Controls.TextBox
        {
            Text = !string.IsNullOrWhiteSpace(familiaSugerida) ? familiaSugerida : DatabaseService.ExtraerFamiliaSugerida(descripcionConcepto),
            Padding = new Thickness(6, 4, 6, 4)
        };
        spFam.Children.Add(txtNuevaFam);
        Grid.SetColumn(spFam, 2);
        gridNuevo.Children.Add(spFam);
        spNuevo.Children.Add(gridNuevo);

        var btnCrearYVincular = new Wpf.Ui.Controls.Button
        {
            Content = $"+ Crear SKU {sigSku} y Vincular Cotizaciones",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Success,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 6, 14, 6)
        };
        spNuevo.Children.Add(btnCrearYVincular);
        bdrNuevo.Child = spNuevo;
        root.Children.Add(bdrNuevo);

        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = "Cancelar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 6, 16, 6)
        };
        btnCerrar.Click += (_, _) => win.DialogResult = false;
        root.Children.Add(btnCerrar);

        btnVincularExistente.Click += (_, _) =>
        {
            if (cboExistente.SelectedItem is not Producto prodSel)
            {
                DialogoFluent.Show(win, "Seleccione un producto existente de la lista desplegable.", "Producto requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DatabaseService.VincularLineaAbiertaAProductoCatalogo(prodSel.Id, solicitudPartidaId, bancoPrecioId, descripcionConcepto);
            resultado = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == prodSel.Id) ?? prodSel;
            DialogoFluent.Show(win,
                $"Se vinculó el concepto '{descripcionConcepto}' y todas sus cotizaciones históricas al producto [{resultado.CodigoInterno}] {resultado.Nombre}.",
                "Línea Abierta Vinculada", MessageBoxButton.OK, MessageBoxImage.Information);
            win.DialogResult = true;
        };

        btnCrearYVincular.Click += (_, _) =>
        {
            string nomProd = (txtNuevoNom.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nomProd))
            {
                DialogoFluent.Show(win, "Escriba el nombre para el nuevo producto.", "Nombre requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string codProd = string.IsNullOrWhiteSpace(txtNuevoSku.Text) ? sigSku : txtNuevoSku.Text.Trim().ToUpperInvariant();
            string famProd = string.IsNullOrWhiteSpace(txtNuevaFam.Text) ? "General" : txtNuevaFam.Text.Trim();
            string uni = string.IsNullOrWhiteSpace(unidadSugerida) ? "Pieza" : unidadSugerida.Trim();

            DatabaseService.GuardarProducto(new Producto
            {
                CodigoInterno = codProd,
                CodigoBarras = codProd,
                Nombre = nomProd,
                Familia = famProd,
                UnidadCompra = uni,
                UnidadAlmacen = uni,
                FactorConversion = 1m,
                StockMinimo = 1m,
                UltimoCostoCompra = Math.Round(costoSugeridoSinIva * 1.16m, 2),
                Ubicacion = "POR ASIGNAR"
            });

            var recienCreado = DatabaseService.ObtenerProductos().FirstOrDefault(p => string.Equals(p.CodigoInterno, codProd, StringComparison.OrdinalIgnoreCase))
                               ?? DatabaseService.ObtenerProductos().LastOrDefault();
            if (recienCreado != null)
            {
                DatabaseService.VincularLineaAbiertaAProductoCatalogo(recienCreado.Id, solicitudPartidaId, bancoPrecioId, descripcionConcepto);
                resultado = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == recienCreado.Id) ?? recienCreado;
                DialogoFluent.Show(win,
                    $"Se creó el nuevo producto [{resultado.CodigoInterno}] {resultado.Nombre} y se le asociaron todas las cotizaciones y vigencias del concepto '{descripcionConcepto}'.",
                    "Nuevo SKU Creado y Vinculado", MessageBoxButton.OK, MessageBoxImage.Information);
                win.DialogResult = true;
            }
        };

        win.Content = root;
        win.ShowDialog();
        return resultado;
    }

    // =========================================================================
    // BOOST DASHBOARD: AGENDA Y CALENDARIO + PENDIENTES DE ATENCIÓN
    // =========================================================================

    private static EventoCalendario? ExtraerEventoDesdeSender(object? sender, System.Windows.Controls.DataGrid? fallbackGrid = null)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is EventoCalendario evTag) return evTag;
            if (fe.DataContext is EventoCalendario evCtx) return evCtx;
        }
        return fallbackGrid?.SelectedItem as EventoCalendario;
    }

    private void CompletarEventoUnificadoDashboard(object? sender)
    {
        var ev = ExtraerEventoDesdeSender(sender, DgDashboardAgenda);
        if (ev == null) return;
        DatabaseService.AlternarCompletadoEventoUnificado(ev);
        RefrescarCalendarioYAgendaDashboard();
    }

    private void EliminarEventoUnificadoDashboard(object? sender)
    {
        var ev = ExtraerEventoDesdeSender(sender, DgDashboardAgenda);
        if (ev == null) return;
        DatabaseService.EliminarODescartarEventoUnificado(ev);
        RefrescarCalendarioYAgendaDashboard();
    }

    private void RefrescarAgendaConFiltrosRapidos()
    {
        if (DgDashboardAgenda == null) return;
        var todos = DatabaseService.ObtenerEventosCalendarioUnificados(_filtroFechaAgendaDashboard);
        IEnumerable<EventoCalendario> filtrados = todos;

        if (ChkAgendaOcultarCompletados?.IsChecked == true)
        {
            filtrados = filtrados.Where(e => !e.Completado);
        }

        int idxFiltro = CboFiltroCategoriaAgenda?.SelectedIndex ?? 0;
        filtrados = idxFiltro switch
        {
            1 => filtrados.Where(e => !e.Completado && (e.EstadoTiempoTexto.StartsWith("Vencido", StringComparison.OrdinalIgnoreCase) || e.EstadoTiempoTexto.Equals("Hoy", StringComparison.OrdinalIgnoreCase))),
            2 => filtrados.Where(e => e.Categoria.Contains("Pago", StringComparison.OrdinalIgnoreCase) || e.Categoria.Contains("Dirección", StringComparison.OrdinalIgnoreCase) || e.Categoria.Contains("Compra", StringComparison.OrdinalIgnoreCase)),
            3 => filtrados.Where(e => e.Categoria.Contains("Entrega", StringComparison.OrdinalIgnoreCase) || e.Categoria.Contains("Herramienta", StringComparison.OrdinalIgnoreCase) || e.Categoria.Contains("Inventario", StringComparison.OrdinalIgnoreCase)),
            4 => filtrados.Where(e => e.Categoria.Contains("Cotización", StringComparison.OrdinalIgnoreCase) || e.Categoria.Contains("Vigencia", StringComparison.OrdinalIgnoreCase)),
            5 => filtrados.Where(e => !e.EsAutomatico),
            _ => filtrados
        };

        var listaFinal = filtrados.ToList();
        DgDashboardAgenda.ItemsSource = listaFinal;

        if (TxtFiltroFechaAgenda != null)
        {
            int vencidos = listaFinal.Count(e => !e.Completado && e.EstadoTiempoTexto.StartsWith("Vencido", StringComparison.OrdinalIgnoreCase));
            int hoy = listaFinal.Count(e => !e.Completado && e.EstadoTiempoTexto.Equals("Hoy", StringComparison.OrdinalIgnoreCase));
            string prefijoFecha = string.IsNullOrWhiteSpace(_filtroFechaAgendaDashboard) ? "Todos" : $"Día {_filtroFechaAgendaDashboard}";
            TxtFiltroFechaAgenda.Text = $"{prefijoFecha}: {listaFinal.Count} evento(s) · {vencidos} vencido(s) · {hoy} para hoy";
        }
    }

    private void FiltroAgendaRapido_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RefrescarAgendaConFiltrosRapidos();
    }

    private void BtnLimpiarCompletadosAgenda_Click(object sender, RoutedEventArgs e)
    {
        int limpios = DatabaseService.LimpiarEventosCompletadosCalendario();
        RefrescarCalendarioYAgendaDashboard();
        if (limpios == 0)
        {
            DialogoFluent.Show(this, "No hay eventos marcados como completados para limpiar en este momento.", "Agenda limpia", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnPosponer1DiaEventoCalendario_Click(object sender, RoutedEventArgs e)
    {
        var ev = ExtraerEventoDesdeSender(sender, DgDashboardAgenda);
        if (ev == null) return;
        DatabaseService.PosponerOEditarEventoUnificado(ev, diasExtra: 1);
        RefrescarCalendarioYAgendaDashboard();
    }

    private void BtnIrDocumentoEventoCalendario_Click(object sender, RoutedEventArgs e)
    {
        var ev = ExtraerEventoDesdeSender(sender, DgDashboardAgenda);
        if (ev == null) return;
        NavegarADocumentoDesdeEventoAgenda(ev);
    }

    private void NavegarADocumentoDesdeEventoAgenda(EventoCalendario ev)
    {
        string fol = (ev.ReferenciaFolio ?? string.Empty).Trim();
        if (fol.StartsWith("OC-", StringComparison.OrdinalIgnoreCase) ||
            fol.StartsWith("PRE-", StringComparison.OrdinalIgnoreCase) ||
            fol.StartsWith("ENT-", StringComparison.OrdinalIgnoreCase) ||
            fol.StartsWith("PRD-", StringComparison.OrdinalIgnoreCase))
        {
            ProcesarCodigoEscaneado(fol);
            return;
        }
        if (fol.StartsWith("SOL-", StringComparison.OrdinalIgnoreCase))
        {
            CambiarSeccion(3);
            var sol = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Folio.Equals(fol, StringComparison.OrdinalIgnoreCase));
            if (sol != null && CboCompSolicitud != null)
            {
                var item = (CboCompSolicitud.ItemsSource as IEnumerable<Solicitud>)?.FirstOrDefault(x => x.Id == sol.Id);
                if (item != null) CboCompSolicitud.SelectedItem = item;
            }
            return;
        }
        if (ev.Categoria.Contains("Cotización", StringComparison.OrdinalIgnoreCase))
        {
            CambiarSeccion(3);
            return;
        }
        if (ev.Categoria.Contains("Inventario", StringComparison.OrdinalIgnoreCase))
        {
            CambiarSeccion(1);
            return;
        }
        MostrarModalEditarEventoCalendario(ev);
    }

    private void DgDashboardAgenda_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
        {
            MostrarModalEditarEventoCalendario(ev);
        }
    }

    private void CtxAgendaCompletar_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
        {
            DatabaseService.AlternarCompletadoEventoUnificado(ev);
            RefrescarCalendarioYAgendaDashboard();
        }
    }

    private void CtxAgendaIrDocumento_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
            NavegarADocumentoDesdeEventoAgenda(ev);
    }

    private void CtxAgendaPosponer1_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
        {
            DatabaseService.PosponerOEditarEventoUnificado(ev, diasExtra: 1);
            RefrescarCalendarioYAgendaDashboard();
        }
    }

    private void CtxAgendaPosponer3_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
        {
            DatabaseService.PosponerOEditarEventoUnificado(ev, diasExtra: 3);
            RefrescarCalendarioYAgendaDashboard();
        }
    }

    private void CtxAgendaPosponer7_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
        {
            DatabaseService.PosponerOEditarEventoUnificado(ev, diasExtra: 7);
            RefrescarCalendarioYAgendaDashboard();
        }
    }

    private void CtxAgendaEditar_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
            MostrarModalEditarEventoCalendario(ev);
    }

    private void CtxAgendaEliminar_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAgenda?.SelectedItem is EventoCalendario ev)
        {
            DatabaseService.EliminarODescartarEventoUnificado(ev);
            RefrescarCalendarioYAgendaDashboard();
        }
    }

    private void MostrarModalEditarEventoCalendario(EventoCalendario ev)
    {
        var win = new Window
        {
            Foreground = Tk("TextFillColorPrimaryBrush"),
            Background = Tk("ControlFillColorDefaultBrush"),
            Title = "Editar o Reprogramar Evento de Agenda",
            Width = 480,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "✏️ Editar / Reprogramar Evento",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var gridTop = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        gridTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        var spFec = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        spFec.Children.Add(new TextBlock { Text = "Fecha programada:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var dpFec = new DatePicker
        {
            SelectedDate = DateTime.TryParse(ev.Fecha, out var dt) ? dt : DateTime.Today
        };
        spFec.Children.Add(dpFec);
        Grid.SetColumn(spFec, 0);
        gridTop.Children.Add(spFec);

        var spPrio = new StackPanel();
        spPrio.Children.Add(new TextBlock { Text = "Prioridad:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboPrio = new ComboBox();
        foreach (var pr in new[] { "Alta", "Media", "Baja" }) cboPrio.Items.Add(pr);
        cboPrio.SelectedItem = ev.Prioridad;
        if (cboPrio.SelectedIndex < 0) cboPrio.SelectedIndex = 1;
        spPrio.Children.Add(cboPrio);
        Grid.SetColumn(spPrio, 1);
        gridTop.Children.Add(spPrio);
        root.Children.Add(gridTop);

        root.Children.Add(new TextBlock { Text = "Título del evento / recordatorio:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtTit = new System.Windows.Controls.TextBox { Text = ev.Titulo, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(txtTit);

        root.Children.Add(new TextBlock { Text = "Detalle / Notas:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtDesc = new System.Windows.Controls.TextBox { Text = ev.Descripcion, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(txtDesc);

        var pnlBtns = new DockPanel();
        var btnQuitar = new System.Windows.Controls.Button
        {
            Content = "✕ Quitar de Agenda",
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 6, 12, 6)
        };
        DockPanel.SetDock(btnQuitar, Dock.Left);
        pnlBtns.Children.Add(btnQuitar);

        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Guardar Cambios", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 6, 14, 6) };
        rightBtns.Children.Add(btnCancel);
        rightBtns.Children.Add(btnSave);
        pnlBtns.Children.Add(rightBtns);
        root.Children.Add(pnlBtns);

        btnQuitar.Click += (_, _) =>
        {
            DatabaseService.EliminarODescartarEventoUnificado(ev);
            win.DialogResult = true;
        };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnSave.Click += (_, _) =>
        {
            string nuevaFec = (dpFec.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            string nuevoTit = string.IsNullOrWhiteSpace(txtTit.Text) ? ev.Titulo : txtTit.Text.Trim();
            string nuevaPrio = cboPrio.SelectedItem?.ToString() ?? ev.Prioridad;
            DatabaseService.PosponerOEditarEventoUnificado(ev, 0, nuevaFec, nuevoTit, nuevaPrio, txtDesc.Text);
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true)
        {
            RefrescarCalendarioYAgendaDashboard();
        }
    }

    // --- ACCIONES RÁPIDAS EN PENDIENTES DE ATENCIÓN: ÓRDENES PENDIENTES ---

    private void BtnDashboardOcAccionPrincipal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OrdenCompra oc })
            return;

        if (string.Equals(oc.Estatus, "Pendiente Vo.Bo. Dirección", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.PuedeAutorizarOc,
                    $"Autorizar Vo.Bo. de la Orden de Compra {oc.Folio}", "Responsable de Compras o Dirección"))
                return;

            bool esCredito = oc.CondicionPago.StartsWith("Crédito", StringComparison.OrdinalIgnoreCase);
            string nuevoEst = esCredito ? "Emitida" : "Autorizada / Pendiente Pago Finanzas";
            string fProg = string.IsNullOrWhiteSpace(oc.FechaProgramadaPago) ? DateTime.Today.AddDays(1).ToString("yyyy-MM-dd") : oc.FechaProgramadaPago;
            DatabaseService.ActualizarEstatusYCondicionOrdenCompra(oc.Id, nuevoEst, oc.CondicionPago, fProg);
            RegistrarTransicionCheckpoint("Etapa 3: Vo.Bo. Dirección", "CP-3A", oc.Folio, oc.Estatus, nuevoEst, "Compras, Almacén, Finanzas", $"Dirección otorgó Vo.Bo. a la {oc.Folio} ({oc.ProveedorNombre}) desde el Panel de Control.");
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            return;
        }

        if (string.Equals(oc.Estatus, "Autorizada / Pendiente Pago Finanzas", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.PuedeProgramarPagosOc,
                    $"Confirmar pago de la Orden de Compra {oc.Folio}", "Responsable de Compras, Dirección o Finanzas"))
                return;
            string fProg = string.IsNullOrWhiteSpace(oc.FechaProgramadaPago) ? DateTime.Today.ToString("yyyy-MM-dd") : oc.FechaProgramadaPago;
            string tiempoSugerido = DatabaseService.CalcularTiempoRelativoEntregaOc(oc, fProg);

            var winPago = new Window
            {
                Title = $"Confirmar Pago y Tiempo de Entrega — {oc.Folio}",
                Width = 460,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = Tk("ControlFillColorDefaultBrush"),
                Foreground = Tk("TextFillColorPrimaryBrush")
            };
            var spPago = new StackPanel { Margin = new Thickness(16) };
            spPago.Children.Add(new TextBlock
            {
                Text = $"Marcar {oc.Folio} ({oc.ProveedorNombre}) como Pagada",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 6)
            });
            spPago.Children.Add(new TextBlock
            {
                Text = "Tiempo aproximado de entrega para el correo al gerente (\"La cotización ya fue pagada, tiempo aprox de entrega xxx\"):",
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var cboTiempoPagoDash = new ComboBox
            {
                IsEditable = true,
                Text = tiempoSugerido,
                Margin = new Thickness(0, 0, 0, 8)
            };
            cboTiempoPagoDash.Items.Add("Hoy mismo");
            cboTiempoPagoDash.Items.Add("24 a 48 hrs");
            cboTiempoPagoDash.Items.Add("1 día hábil");
            cboTiempoPagoDash.Items.Add("1 a 2 días hábiles");
            cboTiempoPagoDash.Items.Add("2 a 3 días hábiles");
            cboTiempoPagoDash.Items.Add("3 a 5 días hábiles");
            if (!cboTiempoPagoDash.Items.Contains(tiempoSugerido))
                cboTiempoPagoDash.Items.Insert(0, tiempoSugerido);
            spPago.Children.Add(cboTiempoPagoDash);

            var chkEnviarCorreoPago = new CheckBox
            {
                Content = "Enviar correo de aviso de pago al gerente solicitante",
                IsChecked = true,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 12)
            };
            spPago.Children.Add(chkEnviarCorreoPago);

            var pnlBtnsPago = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancPago = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            var btnOkPago = new Wpf.Ui.Controls.Button { Content = "Confirmar Pago y Notificar", Appearance = ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5) };
            btnCancPago.Click += (_, _) => winPago.DialogResult = false;
            btnOkPago.Click += (_, _) => winPago.DialogResult = true;
            pnlBtnsPago.Children.Add(btnCancPago);
            pnlBtnsPago.Children.Add(btnOkPago);
            spPago.Children.Add(pnlBtnsPago);
            winPago.Content = spPago;

            if (winPago.ShowDialog() != true)
                return;

            DatabaseService.ActualizarEstatusYCondicionOrdenCompra(
                oc.Id,
                "Pagada / Lista para Almacén",
                oc.CondicionPago,
                fProg,
                cboTiempoPagoDash.Text?.Trim(),
                chkEnviarCorreoPago.IsChecked == true);
            RegistrarTransicionCheckpoint("Etapa 3: Pago Finanzas", "CP-4A", oc.Folio, oc.Estatus, "Pagada / Lista para Almacén", "Compras, Almacén", $"Se confirmó pago de {oc.Folio} ({oc.ProveedorNombre}); lista para recepción en Almacén.");
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            return;
        }

        // Si ya está lista para recibir en almacén, ir directo a Recepción con esa OC seleccionada
        ProcesarCodigoEscaneado(oc.Folio);
    }

    private void BtnDashboardOcCerrarPendiente_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidarPermisoAccion(SesionUsuarioService.Permisos.PuedeAutorizarOc,
                "Cerrar o cancelar una Orden de Compra pendiente", "Responsable de Compras o Dirección"))
            return;

        if (sender is not FrameworkElement { DataContext: OrdenCompra oc })
            return;

        bool tieneRecibido = oc.TotalUnidadesRecibidas > 0;
        string nuevoEstatus = tieneRecibido ? "Cerrada Parcial Definitiva" : "Cancelada";
        string accionTexto = tieneRecibido
            ? $"cerrar definitivamente como Parcial la orden {oc.Folio} ({oc.ProveedorNombre})"
            : $"cancelar y quitar de pendientes la orden {oc.Folio} ({oc.ProveedorNombre})";

        var resp = DialogoFluent.Show(
            this,
            $"¿Desea {accionTexto}?\n\nDejará de aparecer en la lista de Órdenes Pendientes y en los recordatorios automáticos.",
            "Quitar Orden de Pendientes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp != MessageBoxResult.Yes)
            return;

        DatabaseService.ActualizarEstatusYCondicionOrdenCompra(oc.Id, nuevoEstatus, oc.CondicionPago, oc.FechaProgramadaPago);
        CargarTodoElSistema(conservarPosicionCatalogo: true);
    }

    // --- ACCIONES RÁPIDAS EN PENDIENTES DE ATENCIÓN: ALERTAS DE STOCK ---

    private void BtnDashboardAlertaPedir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Producto p })
            return;

        decimal cantPedir = Math.Max(1m, p.FaltanteStock);
        AgregarAlCarritoTienda(p, cantPedir);
        DialogoFluent.Show(
            this,
            $"Se agregaron {cantPedir:0.##} {p.UnidadCompra} de [{p.CodigoInterno}] {p.Nombre} a la Solicitud de Compra en curso.",
            "Agregado a Solicitud",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnDashboardAlertaDescartar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Producto p })
            return;

        decimal nuevoMin = Math.Max(0m, p.StockActual);
        DatabaseService.ActualizarStockMinimoProducto(p.Id, nuevoMin);
        p.StockMinimo = nuevoMin;
        CargarTodoElSistema(conservarPosicionCatalogo: true);
    }

    private void BtnReabastecerTodasAlertasStock_Click(object sender, RoutedEventArgs e)
    {
        if (DgDashboardAlertas?.ItemsSource is not IEnumerable<Producto> alertas)
            return;

        var listaAlertas = alertas.ToList();
        if (listaAlertas.Count == 0)
        {
            DialogoFluent.Show(this, "No hay productos con déficit de stock en este momento.", "Sin alertas de stock", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var prod in listaAlertas)
        {
            decimal faltante = Math.Max(1m, prod.FaltanteStock);
            AgregarAlCarritoTienda(prod, faltante);
        }

        CambiarSeccion(2);
        MostrarDetalleSolicitud(true);
        DialogoFluent.Show(
            this,
            $"Se cargaron los {listaAlertas.Count} artículo(s) con déficit de stock en la Solicitud de Compra con su cantidad faltante lista para guardar.",
            "Solicitud de Reabastecimiento Lista",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // --- TERCER PANEL DE PENDIENTES: SOLICITUDES, XML Y VIGENCIAS ---

    private void RefrescarPendientesOperativosComprasDashboard()
    {
        if (DgDashboardPendientesCompras == null) return;
        var pendientes = DatabaseService.ObtenerPendientesOperativosCompras();
        DgDashboardPendientesCompras.ItemsSource = pendientes;
        if (TxtTituloPendientesComprasDashboard != null)
        {
            TxtTituloPendientesComprasDashboard.Text = pendientes.Count > 0
                ? $"Compras: solicitudes por cotizar, entradas sin XML y cotizaciones por vencer ({pendientes.Count})"
                : "Compras: ✓ sin pendientes de solicitudes, XML ni vigencias";
        }
    }

    private void BtnAtenderPendienteCompra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PendienteOperativoCompra item })
            return;

        if (item.Tipo.Contains("Solicitud", StringComparison.OrdinalIgnoreCase))
        {
            CambiarSeccion(3);
            if (CboCompSolicitud?.ItemsSource is IEnumerable<Solicitud> sols)
            {
                var target = sols.FirstOrDefault(s => s.Id == item.EntidadId || s.Folio.Equals(item.Folio, StringComparison.OrdinalIgnoreCase));
                if (target != null) CboCompSolicitud.SelectedItem = target;
            }
            return;
        }

        if (item.Tipo.Contains("XML", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(item.Folio))
                Clipboard.SetText(item.Folio);
            CambiarSeccion(6);
            return;
        }

        if (item.Tipo.Contains("Vigencia", StringComparison.OrdinalIgnoreCase))
        {
            if (item.Folio.StartsWith("SOL-", StringComparison.OrdinalIgnoreCase))
            {
                CambiarSeccion(3);
                if (CboCompSolicitud?.ItemsSource is IEnumerable<Solicitud> sols)
                {
                    var target = sols.FirstOrDefault(s => s.Folio.Equals(item.Folio, StringComparison.OrdinalIgnoreCase));
                    if (target != null) CboCompSolicitud.SelectedItem = target;
                }
            }
            else
            {
                CambiarSeccion(3);
            }
        }
    }

    private void BtnDescartarPendienteCompra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PendienteOperativoCompra item })
            return;

        if (item.Tipo.Contains("Solicitud", StringComparison.OrdinalIgnoreCase))
        {
            var resp = DialogoFluent.Show(
                this,
                $"¿Desea descartar / cancelar la solicitud {item.Folio} ({item.Titulo}) para quitarla de pendientes?",
                "Descartar Solicitud",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;

            DatabaseService.ActualizarTipoYEstatusSolicitud(item.EntidadId, "Requerimiento de Compra", "Cancelada");
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            return;
        }

        if (item.Tipo.Contains("XML", StringComparison.OrdinalIgnoreCase))
        {
            DatabaseService.MarcarEntradaXmlResueltaManual(item.EntidadId, "SIN-FACTURA-DESCARTADO");
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            return;
        }

        if (item.Tipo.Contains("Vigencia", StringComparison.OrdinalIgnoreCase))
        {
            DatabaseService.DescartarAlertaVigenciaCotizacion(item.EntidadId);
            CargarTodoElSistema(conservarPosicionCatalogo: true);
        }
    }

    // =========================================================================
    // CAPTURA DE COTIZACIÓN POR PROVEEDOR (HOJA COMPLETA), MATRIZ Y ANÁLISIS PASO 3
    // =========================================================================

    private int _indiceSlotHojaProveedorActiva = 1;
    private int _solicitudIdHojaActual;
    private readonly List<int> _proveedoresExtraHojaManual = new();
    private bool _sincronizandoHojaProveedor;

    private void RefrescarHojaProveedorYAnalisisComparativa()
    {
        if (_sincronizandoHojaProveedor)
            return;

        int solIdActual = (CboCompSolicitud?.SelectedItem as Solicitud)?.Id ?? 0;
        if (solIdActual != _solicitudIdHojaActual)
        {
            _solicitudIdHojaActual = solIdActual;
            _proveedoresExtraHojaManual.Clear();
            _indiceSlotHojaProveedorActiva = 1;
        }

        _sincronizandoHojaProveedor = true;
        try
        {
            var proveedores = CboCompProv1?.ItemsSource as IList<Proveedor> ?? DatabaseService.ObtenerProveedores();
            if (CboHojaProveedor != null && CboHojaProveedor.ItemsSource == null)
            {
                CboHojaProveedor.ItemsSource = proveedores;
            }
            else if (CboHojaProveedor != null && !ReferenceEquals(CboHojaProveedor.ItemsSource, proveedores))
            {
                CboHojaProveedor.ItemsSource = proveedores;
            }

            ActualizarBotonesSlotsHojaProveedor();
            ConstruirHojaCotizacionProveedorActiva(proveedores);
            ConstruirMatrizComparativaRapida();
            ActualizarAnalisisPreciosPaso3();
        }
        finally
        {
            _sincronizandoHojaProveedor = false;
        }
    }

    private (int provId, string nombreCorto, string nombreCompleto) ObtenerProveedorPredominanteSlotInfo(int slot, HashSet<int>? excluirIds = null)
    {
        var candidatos = _renglonesComparativa
            .Select(r => slot switch
            {
                1 => (id: r.Proveedor1Id, nom: r.Proveedor1Nombre, conPrecio: r.PrecioSinIva1 > 0),
                2 => (id: r.Proveedor2Id, nom: r.Proveedor2Nombre, conPrecio: r.PrecioSinIva2 > 0),
                3 => (id: r.Proveedor3Id, nom: r.Proveedor3Nombre, conPrecio: r.PrecioSinIva3 > 0),
                _ => (id: 0, nom: string.Empty, conPrecio: false)
            })
            .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.nom) && x.nom != "Sin asignar" && (excluirIds == null || !excluirIds.Contains(x.id)))
            .GroupBy(x => x.id)
            .OrderByDescending(g => g.Count(x => x.conPrecio))
            .ThenByDescending(g => g.Count())
            .FirstOrDefault();

        if (candidatos == null)
            return (0, $"Proveedor {slot}", string.Empty);

        string completo = candidatos.First().nom;
        string corto = Proveedor.ExtraerNombreComercialCorto(completo);
        return (candidatos.Key, string.IsNullOrWhiteSpace(corto) ? $"Proveedor {slot}" : corto, completo);
    }

    private (int provId, string nombreCorto) ObtenerProveedorPredominanteSlot(int slot)
    {
        var info = ObtenerProveedorPredominanteSlotInfo(slot);
        return (info.provId, info.nombreCorto);
    }

    private List<(int indiceColumna, int provId, string nombreCorto, string nombreCompleto)> ObtenerColumnasProveedoresComparativa()
    {
        var resultado = new List<(int indiceColumna, int provId, string nombreCorto, string nombreCompleto)>();
        var usados = new HashSet<int>();

        // 1. Primeras 3 columnas base (Slots 1, 2, 3)
        for (int slot = 1; slot <= 3; slot++)
        {
            var info = ObtenerProveedorPredominanteSlotInfo(slot, usados);
            if (info.provId > 0)
                usados.Add(info.provId);
            resultado.Add((slot, info.provId, info.nombreCorto, info.nombreCompleto));
        }

        // 2. Cualquier proveedor adicional asignado en algún artículo (en Slot 1/2/3 individual o en ProveedoresExtra Opción 4+)
        var adicionales = new List<(int provId, string nomCompleto)>();
        foreach (var r in _renglonesComparativa)
        {
            if (r.Proveedor1Id > 0 && !usados.Contains(r.Proveedor1Id) && !string.IsNullOrWhiteSpace(r.Proveedor1Nombre) && r.Proveedor1Nombre != "Sin asignar")
            {
                usados.Add(r.Proveedor1Id);
                adicionales.Add((r.Proveedor1Id, r.Proveedor1Nombre));
            }
            if (r.Proveedor2Id > 0 && (r.NumeroOpcionesVisibles >= 2 || r.PrecioSinIva2 > 0) && !usados.Contains(r.Proveedor2Id) && !string.IsNullOrWhiteSpace(r.Proveedor2Nombre) && r.Proveedor2Nombre != "Sin asignar")
            {
                usados.Add(r.Proveedor2Id);
                adicionales.Add((r.Proveedor2Id, r.Proveedor2Nombre));
            }
            if (r.Proveedor3Id > 0 && (r.NumeroOpcionesVisibles >= 3 || r.PrecioSinIva3 > 0) && !usados.Contains(r.Proveedor3Id) && !string.IsNullOrWhiteSpace(r.Proveedor3Nombre) && r.Proveedor3Nombre != "Sin asignar")
            {
                usados.Add(r.Proveedor3Id);
                adicionales.Add((r.Proveedor3Id, r.Proveedor3Nombre));
            }
            foreach (var ex in r.ProveedoresExtra)
            {
                if (ex.ProveedorId > 0 && !usados.Contains(ex.ProveedorId) && !string.IsNullOrWhiteSpace(ex.ProveedorNombre))
                {
                    usados.Add(ex.ProveedorId);
                    adicionales.Add((ex.ProveedorId, ex.ProveedorNombre));
                }
            }
        }

        // 3. Proveedores agregados manualmente a la barra de hojas en esta sesión
        var catProvs = CboCompProv1?.ItemsSource as IEnumerable<Proveedor> ?? DatabaseService.ObtenerProveedores();
        foreach (int idMan in _proveedoresExtraHojaManual.ToList())
        {
            if (idMan > 0 && !usados.Contains(idMan))
            {
                var pObj = catProvs.FirstOrDefault(p => p.Id == idMan);
                if (pObj != null)
                {
                    usados.Add(idMan);
                    adicionales.Add((idMan, pObj.NombreMostrar));
                }
            }
        }

        // Si alguno de los slots 1..3 estaba vacío (provId == 0) y hay proveedores adicionales, ocupar primero el slot vacío
        foreach (var ad in adicionales)
        {
            string corto = Proveedor.ExtraerNombreComercialCorto(ad.nomCompleto);
            if (string.IsNullOrWhiteSpace(corto)) corto = ad.nomCompleto;

            int idxVacio = resultado.FindIndex(c => c.indiceColumna <= 3 && c.provId <= 0);
            if (idxVacio >= 0)
            {
                int colNum = resultado[idxVacio].indiceColumna;
                resultado[idxVacio] = (colNum, ad.provId, corto, ad.nomCompleto);
            }
            else
            {
                int sigCol = resultado.Count + 1;
                resultado.Add((sigCol, ad.provId, corto, ad.nomCompleto));
            }
        }

        return resultado;
    }

    private int ContarCotizadosDeProveedor(int provId)
    {
        if (provId <= 0) return 0;
        return _renglonesComparativa.Count(r =>
            (r.Proveedor1Id == provId && r.PrecioSinIva1 > 0) ||
            (r.Proveedor2Id == provId && r.PrecioSinIva2 > 0) ||
            (r.Proveedor3Id == provId && r.PrecioSinIva3 > 0) ||
            r.ProveedoresExtra.Any(ex => ex.ProveedorId == provId && ex.PrecioSinIva > 0));
    }

    public void ActivarOAbrirHojaDeProveedor(int provId)
    {
        if (provId <= 0) return;

        var cols = ObtenerColumnasProveedoresComparativa();
        var existente = cols.FirstOrDefault(c => c.provId == provId);
        if (existente.indiceColumna > 0)
        {
            _indiceSlotHojaProveedorActiva = existente.indiceColumna;
        }
        else
        {
            if (!_proveedoresExtraHojaManual.Contains(provId))
                _proveedoresExtraHojaManual.Add(provId);
            cols = ObtenerColumnasProveedoresComparativa();
            var nueva = cols.FirstOrDefault(c => c.provId == provId);
            if (nueva.indiceColumna > 0)
                _indiceSlotHojaProveedorActiva = nueva.indiceColumna;
        }

        RefrescarHojaProveedorYAnalisisComparativa();
    }

    private static string AbreviarNombreProveedorSlot(string nombre, int maxLen = 20)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return string.Empty;

        string s = nombre.Trim();
        if (s.Length <= maxLen)
            return s;

        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bDISTRIBUIDORA\b", "DIST.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bCOMERCIALIZADORA\b", "COMERC.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bFERRETERIA\b", "FERRET.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bFERRETERA\b", "FERRET.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bSUMINISTROS\b", "SUMIN.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bMATERIALES\b", "MAT.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bINDUSTRIALES\b", "IND.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bINDUSTRIAL\b", "IND.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (s.Length <= maxLen)
            return s;

        return s[..(maxLen - 1)].TrimEnd() + "…";
    }

    private void ActualizarBotonesSlotsHojaProveedor()
    {
        if (BtnHojaSlot1 == null || BtnHojaSlot2 == null || BtnHojaSlot3 == null)
            return;

        var cols = ObtenerColumnasProveedoresComparativa();
        if (_indiceSlotHojaProveedorActiva < 1 || _indiceSlotHojaProveedorActiva > cols.Count)
            _indiceSlotHojaProveedorActiva = 1;

        int tot = _renglonesComparativa.Count;

        void ConfigurarBotonSlot(Wpf.Ui.Controls.Button btn, (int indiceColumna, int provId, string nombreCorto, string nombreCompleto) col)
        {
            int cot = col.provId > 0 ? ContarCotizadosDeProveedor(col.provId) : 0;
            btn.Content = col.provId > 0
                ? $"{col.indiceColumna} · {AbreviarNombreProveedorSlot(col.nombreCorto, 15)} ({cot}/{tot})"
                : $"Prov. {col.indiceColumna} ({cot}/{tot})";
            btn.ToolTip = col.provId > 0
                ? $"Opción {col.indiceColumna}: {col.nombreCorto} ({cot} de {tot} cotizados)"
                : $"Opción {col.indiceColumna} (Sin proveedor asignado)";
            btn.FontSize = 10.5;
            btn.Padding = new Thickness(7, 3, 7, 3);
            btn.Appearance = _indiceSlotHojaProveedorActiva == col.indiceColumna
                ? Wpf.Ui.Controls.ControlAppearance.Primary
                : Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        ConfigurarBotonSlot(BtnHojaSlot1, cols[0]);
        ConfigurarBotonSlot(BtnHojaSlot2, cols[1]);
        ConfigurarBotonSlot(BtnHojaSlot3, cols[2]);

        // Mostrar solo las hojas de proveedores registrados (la hoja vacía aparece al usar "+ Proveedor")
        BtnHojaSlot2.Visibility = cols[1].provId > 0 || _indiceSlotHojaProveedorActiva == cols[1].indiceColumna ? Visibility.Visible : Visibility.Collapsed;
        BtnHojaSlot3.Visibility = cols[2].provId > 0 || _indiceSlotHojaProveedorActiva == cols[2].indiceColumna ? Visibility.Visible : Visibility.Collapsed;

        if (WpBotonesSlotsExtraHoja != null)
        {
            WpBotonesSlotsExtraHoja.Children.Clear();
            for (int i = 3; i < cols.Count; i++)
            {
                var colExtra = cols[i];
                var btnEx = new Wpf.Ui.Controls.Button
                {
                    FontSize = 10.5,
                    Padding = new Thickness(7, 3, 7, 3),
                    Margin = new Thickness(0, 1, 4, 1),
                    Tag = colExtra.indiceColumna
                };
                ConfigurarBotonSlot(btnEx, colExtra);
                btnEx.Click += (_, _) => CambiarSlotHojaProveedor(colExtra.indiceColumna);
                WpBotonesSlotsExtraHoja.Children.Add(btnEx);
            }
        }
    }

    private void ConstruirHojaCotizacionProveedorActiva(IList<Proveedor> proveedores)
    {
        if (DgHojaCotizacionProveedor == null)
            return;

        var cols = ObtenerColumnasProveedoresComparativa();
        if (_indiceSlotHojaProveedorActiva < 1 || _indiceSlotHojaProveedorActiva > cols.Count)
            _indiceSlotHojaProveedorActiva = 1;

        var colActiva = cols[_indiceSlotHojaProveedorActiva - 1];
        int slot = colActiva.indiceColumna;
        var provObj = colActiva.provId > 0 ? proveedores.FirstOrDefault(p => p.Id == colActiva.provId) : null;

        if (CboHojaProveedor != null)
        {
            CboHojaProveedor.SelectedItem = provObj;
        }

        var filasHoja = ArticulosComparativaVisibles
            .Select(r => new RenglonHojaCotizacionProveedor(
                r,
                slot,
                AlModificarCeldaHojaProveedor,
                colActiva.provId,
                provObj?.NombreMostrar ?? colActiva.nombreCompleto))
            .ToList();

        if (filasHoja.Count > 0)
        {
            var refFila = filasHoja.FirstOrDefault(f => f.PrecioSinIva > 0) ?? filasHoja[0];
            int diasEnt = refFila.DiasEntrega;
            int diasVig = refFila.DiasVigencia;
            decimal fleteSuma = filasHoja.Sum(f => f.FleteConIva);

            if (TxtHojaDiasEntregaGral != null) TxtHojaDiasEntregaGral.Text = Math.Max(1, diasEnt).ToString();
            if (TxtHojaDiasVigenciaGral != null) TxtHojaDiasVigenciaGral.Text = Math.Max(1, diasVig).ToString();
            if (TxtHojaFleteGral != null) TxtHojaFleteGral.Text = fleteSuma.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        DgHojaCotizacionProveedor.ItemsSource = filasHoja;
        if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo rSel)
        {
            var filaSel = filasHoja.FirstOrDefault(f => ReferenceEquals(f.RenglonBase, rSel));
            if (filaSel != null)
                DgHojaCotizacionProveedor.SelectedItem = filaSel;
        }

        ActualizarResumenBarraInferiorHoja(filasHoja);
    }

    private void AlModificarCeldaHojaProveedor(RenglonHojaCotizacionProveedor filaEditada) =>
        AlModificarCeldaHojaProveedor(filaEditada, usarProveedorSeleccionado: true);

    private void AlModificarCeldaHojaProveedor(RenglonHojaCotizacionProveedor filaEditada, bool usarProveedorSeleccionado)
    {
        if (_sincronizandoHojaProveedor)
            return;

        var r = filaEditada.RenglonBase;

        if (usarProveedorSeleccionado && CboHojaProveedor?.SelectedItem is Proveedor provHoja && provHoja.Id > 0)
        {
            filaEditada.ProveedorHojaId = provHoja.Id;
            filaEditada.ProveedorHojaNombre = provHoja.NombreMostrar;
        }

        int slotEfectivo = filaEditada.ObtenerSlotEfectivoLectura();
        if (slotEfectivo == 0 && usarProveedorSeleccionado && CboHojaProveedor?.SelectedItem is Proveedor provAsig && provAsig.Id > 0 && filaEditada.PrecioSinIva > 0)
        {
            slotEfectivo = filaEditada.AsegurarSlotParaEscritura();
        }

        if (slotEfectivo >= 1 && slotEfectivo <= 3 && r.NumeroOpcionesVisibles < slotEfectivo)
            r.NumeroOpcionesVisibles = slotEfectivo;

        r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
        SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
        DatabaseService.GuardarCotizacionPartida(r);
        MarcarComparativaGuardada();

        foreach (var ex in r.ProveedoresExtra)
        {
            if (ex.ProveedorId > 0 && ex.SolicitudPartidaId > 0)
                DatabaseService.GuardarProveedorExtraPartida(ex);
        }

        _sincronizandoHojaProveedor = true;
        try
        {
            if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel && ReferenceEquals(sel, r))
            {
                CargarProductoEnTarjetasComparativa(r);
            }

            ActualizarBotonesSlotsHojaProveedor();
            if (DgHojaCotizacionProveedor?.ItemsSource is IList<RenglonHojaCotizacionProveedor> listaActual)
            {
                ActualizarResumenBarraInferiorHoja(listaActual);
            }
            ConstruirMatrizComparativaRapida();
            ActualizarAnalisisPreciosPaso3();
        }
        finally
        {
            _sincronizandoHojaProveedor = false;
        }

        // Recalcular OCs previas sin reconstruir la hoja para no perder el foco de edición tipo Excel
        ActualizarResumenOcsSinDestruirHoja();
    }

    private void ActualizarResumenOcsSinDestruirHoja()
    {
        if (DgPrevisualizacionOcs == null)
            return;

        ActualizarEncabezadosColumnasTablaComparativa();
        DgComparativa?.Items.Refresh();

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
            TxtCompAvanceDecision.Text = $"Avance: {listosParaOc} decididos listos para emitir OC | {pendientesElegir} pendientes de elegir | {yaConOc} ya con OC emitida";

        decimal granTotalOcs = resumenesOc.Sum(x => x.TotalConIva);
        if (TxtResumenGeneralComparativa != null)
            TxtResumenGeneralComparativa.Text = $"{resumenesOc.Count} OC(s) por emitir | Total: {granTotalOcs:C2}";

        if (BtnGenerarOrdenesDesdeComparativa != null)
        {
            BtnGenerarOrdenesDesdeComparativa.IsEnabled = listosParaOc > 0;
            BtnGenerarOrdenesDesdeComparativa.Content = listosParaOc > 0
                ? $"Emitir {resumenesOc.Count} OC(s) ({listosParaOc} Productos Decididos)"
                : (yaConOc == totalProductos && totalProductos > 0 ? "Todas las OCs ya fueron emitidas" : "Elija Ganador en al menos 1 producto");
        }
    }

    private void ActualizarResumenBarraInferiorHoja(IList<RenglonHojaCotizacionProveedor> filas)
    {
        if (TxtResumenHojaProveedorActual == null)
            return;

        int cotizados = filas.Count(f => f.PrecioSinIva > 0);
        decimal subtotalSinIva = filas.Where(f => f.PrecioSinIva > 0).Sum(f => Math.Round(f.Cantidad * f.PrecioSinIva, 2));
        decimal fleteConIva = filas.Sum(f => f.FleteConIva);
        decimal totalConIva = filas.Where(f => f.PrecioSinIva > 0).Sum(f => f.ImporteTotalConIva);
        int adjudicados = filas.Count(f => f.EsGanadorEnEstaOpcion);

        var cols = ObtenerColumnasProveedoresComparativa();
        var colActiva = cols.FirstOrDefault(c => c.indiceColumna == _indiceSlotHojaProveedorActiva);
        string nomProv = CboHojaProveedor?.SelectedItem is Proveedor pSel
                         ? Proveedor.ExtraerNombreComercialCorto(pSel.NombreMostrar)
                         : (!string.IsNullOrWhiteSpace(colActiva.nombreCorto) ? colActiva.nombreCorto : $"Proveedor {_indiceSlotHojaProveedorActiva}");

        TxtResumenHojaProveedorActual.Text =
            $"{nomProv} · Cotizados: {cotizados}/{filas.Count} art. · Adjudicados aquí: {adjudicados} · Subtotal s/IVA: {subtotalSinIva:C2} · Envío c/IVA: {fleteConIva:C2} · Total Hoja c/IVA: {totalConIva:C2}";
    }

    private static void AsignarProveedorASlotEnRenglon(CotizacionRenglonComparativo r, int slot, Proveedor prov)
    {
        if (slot <= 3 && r.NumeroOpcionesVisibles < slot)
            r.NumeroOpcionesVisibles = slot;

        switch (slot)
        {
            case 1:
                r.Proveedor1Id = prov.Id;
                r.Proveedor1Nombre = prov.NombreMostrar;
                break;
            case 2:
                r.Proveedor2Id = prov.Id;
                r.Proveedor2Nombre = prov.NombreMostrar;
                break;
            case 3:
                r.Proveedor3Id = prov.Id;
                r.Proveedor3Nombre = prov.NombreMostrar;
                break;
            default:
                var ex = r.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot || x.ProveedorId == prov.Id);
                if (ex != null)
                {
                    ex.ProveedorId = prov.Id;
                    ex.ProveedorNombre = prov.NombreMostrar;
                }
                break;
        }
    }

    private void CambiarSlotHojaProveedor(int nuevoSlot)
    {
        var cols = ObtenerColumnasProveedoresComparativa();
        _indiceSlotHojaProveedorActiva = Math.Clamp(nuevoSlot, 1, Math.Max(3, cols.Count));
        RefrescarHojaProveedorYAnalisisComparativa();
    }

    private void BtnHojaSlot1_Click(object sender, RoutedEventArgs e) => CambiarSlotHojaProveedor(1);
    private void BtnHojaSlot2_Click(object sender, RoutedEventArgs e) => CambiarSlotHojaProveedor(2);
    private void BtnHojaSlot3_Click(object sender, RoutedEventArgs e) => CambiarSlotHojaProveedor(3);

    private void BtnHojaAgregarSlotProveedor_Click(object sender, RoutedEventArgs e)
    {
        if (_renglonesComparativa.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione primero una Solicitud con artículos para añadir proveedores concursantes.", "Sin artículos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cols = ObtenerColumnasProveedoresComparativa();
        var colVacia = cols.FirstOrDefault(c => c.provId <= 0);
        if (colVacia.indiceColumna > 0)
        {
            _indiceSlotHojaProveedorActiva = colVacia.indiceColumna;
            RefrescarHojaProveedorYAnalisisComparativa();
            if (CboHojaProveedor != null)
                CboHojaProveedor.IsDropDownOpen = true;
            return;
        }

        // Mostrar ventana rápida para elegir qué proveedor abrir en nueva hoja o crear uno nuevo
        var todosProvs = DatabaseService.ObtenerProveedores();
        var usados = cols.Where(c => c.provId > 0).Select(c => c.provId).ToHashSet();
        var disponibles = todosProvs.Where(p => !usados.Contains(p.Id)).ToList();

        if (disponibles.Count == 0)
        {
            // Si todos los proveedores del catálogo ya están en columnas, abrir directo el alta rápida de nuevo proveedor
            BtnNuevoProveedorRapidoComp_Click(sender, e);
            return;
        }

        var win = new Window
        {
            Title = $"Añadir Proveedor {cols.Count + 1} a Hoja Completa",
            Width = 430,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = $"Seleccione el Proveedor Concursante #{cols.Count + 1}",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var cbo = new ComboBox
        {
            ItemsSource = disponibles,
            DisplayMemberPath = "EtiquetaSelectorComparativa",
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(cbo);

        var pnlBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnNuevo = new Wpf.Ui.Controls.Button { Content = "+ Crear Nuevo Proveedor", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 5, 10, 5) };
        var btnCancelar = new System.Windows.Controls.Button { Content = "Cancelar", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 5, 12, 5) };
        var btnAbrir = new Wpf.Ui.Controls.Button { Content = "Abrir Hoja", Appearance = ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5) };

        bool crearNuevo = false;
        btnNuevo.Click += (_, _) => { crearNuevo = true; win.DialogResult = true; };
        btnCancelar.Click += (_, _) => win.DialogResult = false;
        btnAbrir.Click += (_, _) => win.DialogResult = true;

        pnlBtns.Children.Add(btnNuevo);
        pnlBtns.Children.Add(btnCancelar);
        pnlBtns.Children.Add(btnAbrir);
        root.Children.Add(pnlBtns);
        win.Content = root;

        if (win.ShowDialog() == true)
        {
            if (crearNuevo)
            {
                BtnNuevoProveedorRapidoComp_Click(sender, e);
            }
            else if (cbo.SelectedItem is Proveedor pElegido && pElegido.Id > 0)
            {
                ActivarOAbrirHojaDeProveedor(pElegido.Id);
            }
        }
    }

    private void CboHojaProveedor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoHojaProveedor)
            return;

        if (CboHojaProveedor?.SelectedItem is not Proveedor prov || prov.Id <= 0)
            return;

        var cols = ObtenerColumnasProveedoresComparativa();
        var colActual = cols.FirstOrDefault(c => c.indiceColumna == _indiceSlotHojaProveedorActiva);
        int provAnteriorId = colActual.provId;

        // Si el proveedor elegido ya tiene su propia columna en otra pestaña, cambiar a esa pestaña para no duplicarlo
        var colYaExistente = cols.FirstOrDefault(c => c.provId == prov.Id && c.indiceColumna != _indiceSlotHojaProveedorActiva);
        if (colYaExistente.indiceColumna > 0)
        {
            _indiceSlotHojaProveedorActiva = colYaExistente.indiceColumna;
            RefrescarHojaProveedorYAnalisisComparativa();
            return;
        }

        if (provAnteriorId > 0 && _proveedoresExtraHojaManual.Contains(provAnteriorId))
        {
            int idx = _proveedoresExtraHojaManual.IndexOf(provAnteriorId);
            _proveedoresExtraHojaManual[idx] = prov.Id;
        }
        else if (_indiceSlotHojaProveedorActiva > 3 && !_proveedoresExtraHojaManual.Contains(prov.Id))
        {
            _proveedoresExtraHojaManual.Add(prov.Id);
        }

        foreach (var r in _renglonesComparativa)
        {
            if (r.TieneOcGenerada) continue;

            bool modificado = false;
            if (provAnteriorId > 0)
            {
                if (r.Proveedor1Id == provAnteriorId) { r.Proveedor1Id = prov.Id; r.Proveedor1Nombre = prov.NombreMostrar; modificado = true; }
                if (r.Proveedor2Id == provAnteriorId) { r.Proveedor2Id = prov.Id; r.Proveedor2Nombre = prov.NombreMostrar; modificado = true; }
                if (r.Proveedor3Id == provAnteriorId) { r.Proveedor3Id = prov.Id; r.Proveedor3Nombre = prov.NombreMostrar; modificado = true; }
                foreach (var ex in r.ProveedoresExtra.Where(x => x.ProveedorId == provAnteriorId))
                {
                    ex.ProveedorId = prov.Id;
                    ex.ProveedorNombre = prov.NombreMostrar;
                    DatabaseService.GuardarProveedorExtraPartida(ex);
                    modificado = true;
                }
            }
            else if (_indiceSlotHojaProveedorActiva <= 3)
            {
                int slot = _indiceSlotHojaProveedorActiva;
                int idEnSlot = slot switch { 1 => r.Proveedor1Id, 2 => r.Proveedor2Id, 3 => r.Proveedor3Id, _ => 0 };
                decimal prEnSlot = slot switch { 1 => r.PrecioSinIva1, 2 => r.PrecioSinIva2, 3 => r.PrecioSinIva3, _ => 0m };
                if (idEnSlot <= 0 || prEnSlot <= 0)
                {
                    AsignarProveedorASlotEnRenglon(r, slot, prov);
                    modificado = true;
                }
            }

            if (modificado)
            {
                r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
                SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
                DatabaseService.GuardarCotizacionPartida(r);
            }
        }

        if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel)
        {
            CargarProductoEnTarjetasComparativa(sel);
        }

        RecalcularTotalesComparativa();
    }

    private void BtnAplicarCondicionesGeneralesHoja_Click(object sender, RoutedEventArgs e)
    {
        if (_renglonesComparativa.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione primero una Solicitud con artículos para aplicar condiciones de cotización.", "Sin artículos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cols = ObtenerColumnasProveedoresComparativa();
        var colActiva = cols.FirstOrDefault(c => c.indiceColumna == _indiceSlotHojaProveedorActiva);
        var provSel = CboHojaProveedor?.SelectedItem as Proveedor;
        int provIdHoja = provSel?.Id ?? colActiva.provId;
        string provNomHoja = provSel?.NombreMostrar ?? colActiva.nombreCompleto;

        int diasEnt = Math.Max(1, (int)ParseDecimal(TxtHojaDiasEntregaGral?.Text ?? "3", 3m));
        int diasVig = Math.Max(1, (int)ParseDecimal(TxtHojaDiasVigenciaGral?.Text ?? "15", 15m));
        decimal fleteGral = Math.Max(0m, ParseDecimal(TxtHojaFleteGral?.Text ?? "0", 0m));
        string hoyStr = DateTime.Today.ToString("yyyy-MM-dd");

        bool fleteAsignado = false;
        foreach (var r in ArticulosComparativaVisibles)
        {
            if (r.TieneOcGenerada) continue;

            var filaHelper = new RenglonHojaCotizacionProveedor(r, _indiceSlotHojaProveedorActiva, null, provIdHoja, provNomHoja);
            int slot = filaHelper.ObtenerSlotEfectivoLectura();
            if (slot == 0 && _indiceSlotHojaProveedorActiva <= 3 && provSel != null && provSel.Id > 0)
            {
                int sPref = _indiceSlotHojaProveedorActiva;
                int idEnS = sPref switch { 1 => r.Proveedor1Id, 2 => r.Proveedor2Id, 3 => r.Proveedor3Id, _ => 0 };
                decimal prEnS = sPref switch { 1 => r.PrecioSinIva1, 2 => r.PrecioSinIva2, 3 => r.PrecioSinIva3, _ => 0m };
                if (idEnS <= 0 || prEnS <= 0)
                {
                    AsignarProveedorASlotEnRenglon(r, sPref, provSel);
                    slot = sPref;
                }
            }

            if (slot == 0)
                continue;

            bool tienePrecio = filaHelper.PrecioSinIva > 0;
            decimal fleteRenglon = 0m;
            if (!fleteAsignado && fleteGral > 0 && (tienePrecio || ReferenceEquals(r, _renglonesComparativa[0])))
            {
                fleteRenglon = fleteGral;
                fleteAsignado = true;
            }

            if (slot == 1)
            {
                r.DiasEntrega1 = diasEnt;
                r.DiasVigencia1 = diasVig;
                r.FleteConIva1 = fleteRenglon;
                if (string.IsNullOrWhiteSpace(r.FechaCotizacion1)) r.FechaCotizacion1 = hoyStr;
            }
            else if (slot == 2)
            {
                r.DiasEntrega2 = diasEnt;
                r.DiasVigencia2 = diasVig;
                r.FleteConIva2 = fleteRenglon;
                if (string.IsNullOrWhiteSpace(r.FechaCotizacion2)) r.FechaCotizacion2 = hoyStr;
            }
            else if (slot == 3)
            {
                r.DiasEntrega3 = diasEnt;
                r.DiasVigencia3 = diasVig;
                r.FleteConIva3 = fleteRenglon;
                if (string.IsNullOrWhiteSpace(r.FechaCotizacion3)) r.FechaCotizacion3 = hoyStr;
            }
            else if (slot >= 4)
            {
                var ex = r.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null)
                {
                    ex.DiasEntrega = diasEnt;
                    ex.DiasVigencia = diasVig;
                    ex.FleteConIva = fleteRenglon;
                    if (string.IsNullOrWhiteSpace(ex.FechaCotizacion)) ex.FechaCotizacion = hoyStr;
                    DatabaseService.GuardarProveedorExtraPartida(ex);
                }
            }

            r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
            SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
            DatabaseService.GuardarCotizacionPartida(r);
        }

        if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel)
            CargarProductoEnTarjetasComparativa(sel);

        RecalcularTotalesComparativa();
    }

    private void BtnCargarHistoricoProveedorHoja_Click(object sender, RoutedEventArgs e)
    {
        if (_renglonesComparativa.Count == 0)
            return;

        var cols = ObtenerColumnasProveedoresComparativa();
        var colActiva = cols.FirstOrDefault(c => c.indiceColumna == _indiceSlotHojaProveedorActiva);
        int provId = (CboHojaProveedor?.SelectedItem as Proveedor)?.Id ?? colActiva.provId;
        var provObj = (CboHojaProveedor?.ItemsSource as IEnumerable<Proveedor>)?.FirstOrDefault(p => p.Id == provId);

        if (provId <= 0 || provObj == null)
        {
            DialogoFluent.Show(this, "Seleccione primero el Proveedor en el desplegable superior de la hoja para buscar sus precios históricos.", "Seleccione un proveedor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int cargados = 0;
        string hoyStr = DateTime.Today.ToString("yyyy-MM-dd");

        foreach (var r in ArticulosComparativaVisibles)
        {
            if (r.TieneOcGenerada) continue;

            var refs = DatabaseService.ObtenerReferenciasSimilaresParaComparativa(r.ProductoId, r.ProductoNombre, r.Familia);
            var mejorDeEsteProv = refs.FirstOrDefault(x => x.ProveedorId == provId && x.PrecioSinIva > 0);
            if (mejorDeEsteProv == null)
                continue;

            var filaHelper = new RenglonHojaCotizacionProveedor(r, _indiceSlotHojaProveedorActiva, null, provObj.Id, provObj.NombreMostrar);
            int slot = filaHelper.AsegurarSlotParaEscritura();

            if (slot == 1)
            {
                r.PrecioSinIva1 = mejorDeEsteProv.PrecioSinIva;
                r.DiasEntrega1 = mejorDeEsteProv.DiasEntrega > 0 ? mejorDeEsteProv.DiasEntrega : 3;
                r.DiasVigencia1 = mejorDeEsteProv.DiasVigencia > 0 ? mejorDeEsteProv.DiasVigencia : 15;
                r.FechaCotizacion1 = !string.IsNullOrWhiteSpace(mejorDeEsteProv.FechaCotizacion) ? mejorDeEsteProv.FechaCotizacion : hoyStr;
                r.NotasCotizacion1 = mejorDeEsteProv.ReferenciaOrigen;
            }
            else if (slot == 2)
            {
                r.PrecioSinIva2 = mejorDeEsteProv.PrecioSinIva;
                r.DiasEntrega2 = mejorDeEsteProv.DiasEntrega > 0 ? mejorDeEsteProv.DiasEntrega : 3;
                r.DiasVigencia2 = mejorDeEsteProv.DiasVigencia > 0 ? mejorDeEsteProv.DiasVigencia : 15;
                r.FechaCotizacion2 = !string.IsNullOrWhiteSpace(mejorDeEsteProv.FechaCotizacion) ? mejorDeEsteProv.FechaCotizacion : hoyStr;
                r.NotasCotizacion2 = mejorDeEsteProv.ReferenciaOrigen;
            }
            else if (slot == 3)
            {
                r.PrecioSinIva3 = mejorDeEsteProv.PrecioSinIva;
                r.DiasEntrega3 = mejorDeEsteProv.DiasEntrega > 0 ? mejorDeEsteProv.DiasEntrega : 3;
                r.DiasVigencia3 = mejorDeEsteProv.DiasVigencia > 0 ? mejorDeEsteProv.DiasVigencia : 15;
                r.FechaCotizacion3 = !string.IsNullOrWhiteSpace(mejorDeEsteProv.FechaCotizacion) ? mejorDeEsteProv.FechaCotizacion : hoyStr;
                r.NotasCotizacion3 = mejorDeEsteProv.ReferenciaOrigen;
            }
            else if (slot >= 4)
            {
                var ex = r.ProveedoresExtra.FirstOrDefault(x => x.NumeroOpcion == slot);
                if (ex != null)
                {
                    ex.PrecioSinIva = mejorDeEsteProv.PrecioSinIva;
                    ex.DiasEntrega = mejorDeEsteProv.DiasEntrega > 0 ? mejorDeEsteProv.DiasEntrega : 3;
                    ex.DiasVigencia = mejorDeEsteProv.DiasVigencia > 0 ? mejorDeEsteProv.DiasVigencia : 15;
                    ex.FechaCotizacion = !string.IsNullOrWhiteSpace(mejorDeEsteProv.FechaCotizacion) ? mejorDeEsteProv.FechaCotizacion : hoyStr;
                    ex.NotasCotizacion = mejorDeEsteProv.ReferenciaOrigen;
                    DatabaseService.GuardarProveedorExtraPartida(ex);
                }
            }

            r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
            SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
            DatabaseService.GuardarCotizacionPartida(r);
            cargados++;
        }

        if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel)
            CargarProductoEnTarjetasComparativa(sel);

        RecalcularTotalesComparativa();

        string nomCortoProv = Proveedor.ExtraerNombreComercialCorto(provObj.NombreMostrar);
        DialogoFluent.Show(
            this,
            cargados > 0
                ? $"Se precargaron {cargados} precio(s) históricos/vigentes de {nomCortoProv} en esta hoja."
                : $"No se encontraron precios previos registrados con {nomCortoProv} para los artículos de esta solicitud. Puede capturarlos directamente en la tabla.",
            "Carga de Precios por Proveedor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnNuevoProveedorDesdeComp_Click(object sender, RoutedEventArgs e)
    {
        BtnNuevoProveedorRapidoComp_Click(sender, e);
    }

    private void BtnAdjudicarTodoHojaProveedor_Click(object sender, RoutedEventArgs e)
    {
        if (_renglonesComparativa.Count == 0)
            return;

        var cols = ObtenerColumnasProveedoresComparativa();
        var colActiva = cols.FirstOrDefault(c => c.indiceColumna == _indiceSlotHojaProveedorActiva);
        int provIdHoja = (CboHojaProveedor?.SelectedItem as Proveedor)?.Id ?? colActiva.provId;
        int adjudicados = 0;

        foreach (var r in ArticulosComparativaVisibles)
        {
            if (r.TieneOcGenerada) continue;
            var filaHelper = new RenglonHojaCotizacionProveedor(r, _indiceSlotHojaProveedorActiva, null, provIdHoja, colActiva.nombreCompleto);
            int slotEfectivo = filaHelper.ObtenerSlotEfectivoLectura();
            decimal precio = filaHelper.PrecioSinIva;

            if (slotEfectivo > 0 && precio > 0)
            {
                r.ProveedorGanadorIndice = slotEfectivo;
                r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
                SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
                DatabaseService.GuardarCotizacionPartida(r);
                adjudicados++;
            }
        }

        if (adjudicados == 0)
        {
            DialogoFluent.Show(this, "Seleccione un proveedor arriba y capture precios mayores a $0.00 en al menos una partida de esta hoja antes de adjudicar.", "Sin partidas cotizadas en esta hoja", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel)
            CargarProductoEnTarjetasComparativa(sel);

        RecalcularTotalesComparativa();
    }

    private void DgHojaCotizacionProveedor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoHojaProveedor || _cargandoDetalleComparativa)
            return;

        if (DgHojaCotizacionProveedor?.SelectedItem is RenglonHojaCotizacionProveedor fila &&
            DgComparativa != null &&
            !ReferenceEquals(DgComparativa.SelectedItem, fila.RenglonBase))
        {
            DgComparativa.SelectedItem = fila.RenglonBase;
        }
    }

    private void DgMatrizComparativaRapida_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoHojaProveedor || _cargandoDetalleComparativa)
            return;

        if (DgMatrizComparativaRapida?.SelectedItem is FilaMatrizProveedor { Articulo: var r } &&
            DgComparativa != null &&
            !ReferenceEquals(DgComparativa.SelectedItem, r))
        {
            DgComparativa.SelectedItem = r;
        }
    }

    private void CboCompSelectorArticuloDetalle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _cargandoDetalleComparativa || _sincronizandoHojaProveedor)
            return;

        if (CboCompSelectorArticuloDetalle?.SelectedItem is CotizacionRenglonComparativo r &&
            DgComparativa != null &&
            !ReferenceEquals(DgComparativa.SelectedItem, r))
        {
            DgComparativa.SelectedItem = r;
        }
    }

    private void DgProveedoresExtraComp_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Row.Item is not CotizacionProveedorExtra ex ||
                DgComparativa?.SelectedItem is not CotizacionRenglonComparativo r ||
                r.TieneOcGenerada)
                return;

            ex.PrecioSinIva = Math.Max(0m, Math.Round(ex.PrecioSinIva, 2));
            ex.FleteConIva = Math.Max(0m, Math.Round(ex.FleteConIva, 2));
            ex.DiasEntrega = Math.Max(1, ex.DiasEntrega);
            ex.DiasVigencia = Math.Max(1, ex.DiasVigencia);
            if (ex.PrecioSinIva > 0 && string.IsNullOrWhiteSpace(ex.FechaCotizacion))
                ex.FechaCotizacion = DateTime.Today.ToString("yyyy-MM-dd");

            DatabaseService.GuardarProveedorExtraPartida(ex);
            DatabaseService.GuardarCotizacionPartida(r);
            CargarProductoEnTarjetasComparativa(r);
            RecalcularTotalesComparativa();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TcModoCapturaComparativa_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _sincronizandoHojaProveedor)
            return;

        if (ReferenceEquals(e.OriginalSource, TcModoCapturaComparativa))
        {
            ConfirmarEdicionesPendientesComparativa();
            RefrescarHojaProveedorYAnalisisComparativa();
        }
    }

    // --- MATRIZ COMPARATIVA LADO A LADO ---

    private void ConstruirMatrizComparativaRapida()
    {
        if (DgMatrizComparativaRapida != null && CboMatrizProveedor3 != null)
            ConstruirMatrizPorProveedor();
    }

    private void ChkMatrizMostrarTotalConIva_Changed(object sender, RoutedEventArgs e)
    {
        ConstruirMatrizComparativaRapida();
    }

    // --- PASO 3: INTELIGENCIA DE COMPRAS · ANÁLISIS DE PRECIOS, ESCENARIOS Y AHORROS ---

    private void BtnToggleTablaAnalisisPaso3_Click(object sender, RoutedEventArgs e)
    {
        if (DgAnalisisDiferenciasComp == null)
            return;

        DgAnalisisDiferenciasComp.Visibility = DgAnalisisDiferenciasComp.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ActualizarAnalisisPreciosPaso3()
    {
        if (TxtKpiCompActualTotal == null || DgAnalisisDiferenciasComp == null)
            return;

        int totalItems = _renglonesComparativa.Count;
        if (totalItems == 0)
        {
            TxtKpiCompActualTotal.Text = "$0.00";
            TxtKpiCompActualSub.Text = "0 de 0 partidas decididas";
            TxtKpiCompOptimoTotal.Text = "$0.00";
            TxtKpiCompOptimoSub.Text = "Sin partidas en comparativa";
            TxtKpiCompUnicoProvResumen.Text = "Sin cotizaciones";
            TxtKpiCompAhorroTotal.Text = "$0.00 (0.0%)";
            TxtKpiCompAhorroSub.Text = "vs. opción más alta y promedio";
            DgAnalisisDiferenciasComp.ItemsSource = null;
            return;
        }

        var desglose = new List<RenglonAnalisisPrecioPartida>();
        decimal sumaElegida = 0m;
        decimal sumaOptima = 0m;
        decimal sumaMaximaEnDecididas = 0m;
        decimal sumaPromedioEnDecididas = 0m;
        int decididasCount = 0;

        foreach (var r in _renglonesComparativa)
        {
            var opcionesCotizadas = new List<(string nombre, decimal totalConIva)>();
            if (r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0)
                opcionesCotizadas.Add((Proveedor.ExtraerNombreComercialCorto(r.Proveedor1Nombre), r.TotalConFlete1));
            if (r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0)
                opcionesCotizadas.Add((Proveedor.ExtraerNombreComercialCorto(r.Proveedor2Nombre), r.TotalConFlete2));
            if (r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0)
                opcionesCotizadas.Add((Proveedor.ExtraerNombreComercialCorto(r.Proveedor3Nombre), r.TotalConFlete3));
            foreach (var ex in r.ProveedoresExtra)
            {
                if (ex.ProveedorId > 0 && ex.PrecioSinIva > 0)
                {
                    decimal totEx = Math.Round(r.Cantidad * ex.PrecioConIva + ex.FleteConIva, 2);
                    opcionesCotizadas.Add((Proveedor.ExtraerNombreComercialCorto(ex.ProveedorNombre), totEx));
                }
            }

            decimal minTotal = opcionesCotizadas.Count > 0 ? opcionesCotizadas.Min(o => o.totalConIva) : 0m;
            decimal maxTotal = opcionesCotizadas.Count > 0 ? opcionesCotizadas.Max(o => o.totalConIva) : 0m;
            decimal promTotal = opcionesCotizadas.Count > 0 ? Math.Round(opcionesCotizadas.Average(o => o.totalConIva), 2) : 0m;

            sumaOptima += minTotal;

            bool tieneGanador = r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0;
            decimal elegidoTotal = tieneGanador
                ? Math.Round(Math.Round(r.Cantidad * r.PrecioUnitarioGanadorSinIva, 2) * 1.16m + r.FleteGanadorConIva, 2)
                : 0m;
            string provElegido = tieneGanador
                ? Proveedor.ExtraerNombreComercialCorto(r.ProveedorGanadorNombre)
                : "Sin adjudicar";

            decimal ahorroVsMax = 0m;
            decimal pctAhorroVsMax = 0m;
            string diagnostico;

            if (tieneGanador)
            {
                decididasCount++;
                sumaElegida += elegidoTotal;
                sumaMaximaEnDecididas += maxTotal > 0 ? maxTotal : elegidoTotal;
                sumaPromedioEnDecididas += promTotal > 0 ? promTotal : elegidoTotal;

                if (opcionesCotizadas.Count <= 1)
                {
                    diagnostico = "Cotización única (1 opción)";
                }
                else
                {
                    ahorroVsMax = Math.Max(0m, Math.Round(maxTotal - elegidoTotal, 2));
                    pctAhorroVsMax = maxTotal > 0 ? Math.Round((ahorroVsMax / maxTotal) * 100m, 1) : 0m;

                    decimal sobrecostoVsMin = Math.Round(elegidoTotal - minTotal, 2);
                    if (sobrecostoVsMin <= 0.05m)
                    {
                        diagnostico = "✓ Mejor precio elegido";
                    }
                    else
                    {
                        decimal pctSobreMin = minTotal > 0 ? Math.Round((sobrecostoVsMin / minTotal) * 100m, 1) : 0m;
                        diagnostico = $"⚠ +{sobrecostoVsMin:C2} (+{pctSobreMin:0.0}%) vs mejor";
                    }
                }
            }
            else
            {
                diagnostico = opcionesCotizadas.Count > 0
                    ? $"Pendiente elegir ({opcionesCotizadas.Count} op.)"
                    : "Sin precios capturados";
            }

            desglose.Add(new RenglonAnalisisPrecioPartida
            {
                Codigo = r.ProductoCodigo,
                Producto = r.ProductoNombre,
                Cantidad = r.Cantidad,
                OpcionesCotizadas = opcionesCotizadas.Count,
                MejorPrecioTotalConIva = minTotal,
                PeorPrecioTotalConIva = maxTotal,
                PromedioTotalConIva = promTotal,
                ElegidoTotalConIva = elegidoTotal,
                ProveedorElegido = provElegido,
                AhorroVsMaximo = ahorroVsMax,
                PorcentajeAhorroVsMaximo = pctAhorroVsMax,
                DiagnosticoDiferencia = diagnostico
            });
        }

        int numProvsGanadores = _renglonesComparativa
            .Where(r => r.ProveedorGanadorIndice >= 1 && r.ProveedorGanadorId > 0 && r.PrecioUnitarioGanadorSinIva > 0)
            .Select(r => r.ProveedorGanadorId)
            .Distinct()
            .Count();

        // 1. Tarjeta Selección Adjudicada
        TxtKpiCompActualTotal.Text = $"{sumaElegida:C2}";
        TxtKpiCompActualSub.Text = $"{decididasCount}/{totalItems} partidas decididas · {numProvsGanadores} OC(s)";

        // 2. Tarjeta Escenario Mix Óptimo
        TxtKpiCompOptimoTotal.Text = $"{sumaOptima:C2}";
        decimal difContraOptimo = Math.Max(0m, Math.Round(sumaElegida - sumaOptima, 2));
        TxtKpiCompOptimoSub.Text = decididasCount == 0
            ? "Menor precio por ítem combinado"
            : (difContraOptimo <= 0.05m && decididasCount == totalItems
                ? "✓ Selección actual está en el óptimo"
                : $"Potencial adicional: {difContraOptimo:C2}");

        // 3. Tarjeta Consolidar Todo con 1 Solo Proveedor (incluye 4to+ proveedores)
        var lineasProvUnico = new List<string>();
        foreach (var col in ObtenerColumnasProveedoresComparativa().Where(c => c.provId > 0))
        {
            int cots = ContarCotizadosDeProveedor(col.provId);
            if (cots == 0) continue;

            decimal totProv = _renglonesComparativa.Sum(r =>
            {
                if (r.Proveedor1Id == col.provId && r.PrecioSinIva1 > 0) return r.TotalConFlete1;
                if (r.Proveedor2Id == col.provId && r.PrecioSinIva2 > 0) return r.TotalConFlete2;
                if (r.Proveedor3Id == col.provId && r.PrecioSinIva3 > 0) return r.TotalConFlete3;
                var ex = r.ProveedoresExtra.FirstOrDefault(x => x.ProveedorId == col.provId && x.PrecioSinIva > 0);
                return ex != null ? Math.Round(r.Cantidad * ex.PrecioConIva + ex.FleteConIva, 2) : 0m;
            });
            lineasProvUnico.Add($"• {AbreviarNombreProveedorSlot(col.nombreCorto, 18)}: {totProv:C2} ({cots}/{totalItems} art.)");
        }
        TxtKpiCompUnicoProvResumen.Text = lineasProvUnico.Count > 0
            ? string.Join("\n", lineasProvUnico)
            : "Capture precios para comparar";

        // 4. Tarjeta Ahorro Obtenido ($ y %)
        decimal ahorroTotalMax = Math.Max(0m, Math.Round(sumaMaximaEnDecididas - sumaElegida, 2));
        decimal pctTotalMax = sumaMaximaEnDecididas > 0 ? Math.Round((ahorroTotalMax / sumaMaximaEnDecididas) * 100m, 1) : 0m;
        decimal ahorroTotalProm = Math.Max(0m, Math.Round(sumaPromedioEnDecididas - sumaElegida, 2));
        decimal pctTotalProm = sumaPromedioEnDecididas > 0 ? Math.Round((ahorroTotalProm / sumaPromedioEnDecididas) * 100m, 1) : 0m;

        TxtKpiCompAhorroTotal.Text = $"{ahorroTotalMax:C2} ({pctTotalMax:0.0}%)";
        TxtKpiCompAhorroSub.Text = $"vs. máx ({sumaMaximaEnDecididas:C2}) · vs. prom: {ahorroTotalProm:C2} ({pctTotalProm:0.0}%)";

        DgAnalisisDiferenciasComp.ItemsSource = desglose;
    }

    // =========================================================================
    // EXPEDIENTE COMPLETO DE SOLICITUD Y REIMPRESIÓN DE DOCUMENTOS VINCULADOS
    // =========================================================================

    private sealed class RenglonExpedientePartidaSolicitud
    {
        public string Codigo { get; set; } = string.Empty;
        public string Articulo { get; set; } = string.Empty;
        public string CantidadTexto { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string OpcionesCotizadas { get; set; } = string.Empty;
        public string ProveedorGanador { get; set; } = string.Empty;
        public decimal PrecioUnitarioConIva { get; set; }
        public decimal TotalConIva { get; set; }
        public string FolioOc { get; set; } = string.Empty;
    }

    private static bool EsSolicitudFinalizadaOAvanzada(Solicitud? sol)
    {
        if (sol == null || sol.Id <= 0) return false;
        return string.Equals(sol.Estatus, "OC Generada", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sol.Estatus, "Atendida", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sol.Estatus, "Cotización Entregada", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sol.Estatus, "Autorizada para Surtir Stock", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sol.Estatus, "Parcial en OC", StringComparison.OrdinalIgnoreCase);
    }

    private void SeleccionarTabDetalleSolicitudAlAbrir(Solicitud? sol, bool forzarExpediente)
    {
        if (forzarExpediente || EsSolicitudFinalizadaOAvanzada(sol))
        {
            if (TabSolExpediente != null)
                TabSolExpediente.IsSelected = true;
        }
        else
        {
            if (TabSolArticulos != null)
                TabSolArticulos.IsSelected = true;
        }
    }

    private Solicitud? ObtenerSolicitudActualParaExpediente()
    {
        if (_solicitudEditandoId > 0)
        {
            var porId = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == _solicitudEditandoId);
            if (porId != null) return porId;
        }
        if (DgHistorialSolicitudes?.SelectedItem is Solicitud sel && sel.Id > 0)
        {
            return DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == sel.Id) ?? sel;
        }
        return null;
    }

    private List<CotizacionRenglonComparativo> ConstruirRenglonesComparativaDeSolicitud(Solicitud sol)
    {
        var lista = new List<CotizacionRenglonComparativo>();
        var productos = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        var provDict = DatabaseService.ObtenerProveedores().ToDictionary(p => p.Id);

        var articulos = sol.UsaPartidas ? sol.PartidasParaComparativa : sol.Partidas;
        foreach (var part in articulos)
        {
            productos.TryGetValue(part.ProductoId, out var pObj);
            var provsExtra = part.Id > 0 ? DatabaseService.ObtenerProveedoresExtraPartida(part.Id) : new List<CotizacionProveedorExtra>();

            int numOpciones = 1;
            if (provsExtra.Count > 0)
                numOpciones = Math.Max(3, provsExtra.Max(x => x.NumeroOpcion));
            else if (part.Proveedor3Id > 0 || part.PrecioSinIva3 > 0 || part.ProveedorGanadorIndice == 3)
                numOpciones = 3;
            else if (part.Proveedor2Id > 0 || part.PrecioSinIva2 > 0 || part.ProveedorGanadorIndice == 2)
                numOpciones = 2;

            lista.Add(new CotizacionRenglonComparativo
            {
                SolicitudPartidaId = part.Id,
                NombrePartida = part.NombrePartida,
                ProductoId = part.ProductoId,
                ProductoCodigo = part.ProductoCodigo,
                ProductoNombre = part.ProductoNombre,
                DescripcionLibre = part.DescripcionLibre,
                Familia = !string.IsNullOrWhiteSpace(part.Familia) ? part.Familia : (pObj?.Familia ?? string.Empty),
                UnidadCompra = string.IsNullOrWhiteSpace(part.UnidadCompra) ? (pObj?.UnidadCompra ?? "Pieza") : part.UnidadCompra,
                Cantidad = part.CantidadSolicitada,
                StockActualAlmacen = pObj?.StockActual ?? part.StockActualAlmacen,
                DiasVigenciaCotizacion = part.DiasVigenciaCotizacion > 0 ? part.DiasVigenciaCotizacion : 15,
                FechaCotizacion = part.FechaCotizacion,
                Proveedor1Id = part.Proveedor1Id,
                Proveedor1Nombre = provDict.TryGetValue(part.Proveedor1Id, out var p1) ? p1.RazonSocial : "Sin asignar",
                PrecioSinIva1 = part.PrecioSinIva1,
                DiasEntrega1 = part.DiasEntrega1 > 0 ? part.DiasEntrega1 : 2,
                DiasVigencia1 = part.DiasVigencia1 > 0 ? part.DiasVigencia1 : 15,
                FechaCotizacion1 = part.FechaCotizacion1,
                FleteConIva1 = part.FleteConIva1,
                NotasCotizacion1 = part.NotasCotizacion1,
                Proveedor2Id = part.Proveedor2Id,
                Proveedor2Nombre = provDict.TryGetValue(part.Proveedor2Id, out var p2) ? p2.RazonSocial : "Sin asignar",
                PrecioSinIva2 = part.PrecioSinIva2,
                DiasEntrega2 = part.DiasEntrega2 > 0 ? part.DiasEntrega2 : 3,
                DiasVigencia2 = part.DiasVigencia2 > 0 ? part.DiasVigencia2 : 15,
                FechaCotizacion2 = part.FechaCotizacion2,
                FleteConIva2 = part.FleteConIva2,
                NotasCotizacion2 = part.NotasCotizacion2,
                Proveedor3Id = part.Proveedor3Id,
                Proveedor3Nombre = provDict.TryGetValue(part.Proveedor3Id, out var p3) ? p3.RazonSocial : "Sin asignar",
                PrecioSinIva3 = part.PrecioSinIva3,
                DiasEntrega3 = part.DiasEntrega3 > 0 ? part.DiasEntrega3 : 1,
                DiasVigencia3 = part.DiasVigencia3 > 0 ? part.DiasVigencia3 : 15,
                FechaCotizacion3 = part.FechaCotizacion3,
                FleteConIva3 = part.FleteConIva3,
                NotasCotizacion3 = part.NotasCotizacion3,
                ProveedoresExtra = provsExtra,
                ProveedorGanadorIndice = part.ProveedorGanadorIndice,
                NumeroOpcionesVisibles = numOpciones,
                OrdenCompraId = part.OrdenCompraId,
                OrdenCompraFolio = part.OrdenCompraFolio
            });
        }

        return lista;
    }

    private (
        List<CotizacionRenglonComparativo> renglones,
        List<OrdenCompra> ordenes,
        List<EntradaAlmacen> entradas,
        List<FacturaXml> facturas,
        List<SalidaAlmacen> salidas,
        List<Solicitud> anexos,
        List<NotificacionCheckpoint> bitacora) ObtenerDatosExpedienteSolicitud(Solicitud sol)
    {
        var renglones = ConstruirRenglonesComparativaDeSolicitud(sol);

        string folioRaiz = sol.Folio ?? string.Empty;
        int idxAnexo = folioRaiz.IndexOf("-A", StringComparison.OrdinalIgnoreCase);
        if (idxAnexo > 0)
            folioRaiz = folioRaiz[..idxAnexo];

        var todasSolicitudes = DatabaseService.ObtenerSolicitudes();
        var anexos = todasSolicitudes
            .Where(s => s.Id != sol.Id &&
                        !string.IsNullOrWhiteSpace(folioRaiz) &&
                        (string.Equals(s.Folio, folioRaiz, StringComparison.OrdinalIgnoreCase) ||
                         s.Folio.StartsWith($"{folioRaiz}-A", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Id)
            .ToList();

        var idsOcPartidas = sol.Partidas.Where(p => p.OrdenCompraId > 0).Select(p => p.OrdenCompraId).ToHashSet();
        var foliosOcPartidas = sol.Partidas
            .Where(p => !string.IsNullOrWhiteSpace(p.OrdenCompraFolio))
            .Select(p => p.OrdenCompraFolio.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordenes = DatabaseService.ObtenerOrdenesCompra()
            .Where(oc => oc.SolicitudId == sol.Id
                         || (!string.IsNullOrWhiteSpace(sol.Folio) && string.Equals(oc.SolicitudFolio, sol.Folio, StringComparison.OrdinalIgnoreCase))
                         || idsOcPartidas.Contains(oc.Id)
                         || (!string.IsNullOrWhiteSpace(oc.Folio) && foliosOcPartidas.Contains(oc.Folio)))
            .OrderByDescending(oc => oc.Id)
            .ToList();

        var idsOcs = ordenes.Select(o => o.Id).ToHashSet();
        var foliosOcs = ordenes
            .Where(o => !string.IsNullOrWhiteSpace(o.Folio))
            .Select(o => o.Folio.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in foliosOcPartidas) foliosOcs.Add(f);

        var entradas = DatabaseService.ObtenerEntradasAlmacen()
            .Where(ent => (ent.OrdenCompraId.HasValue && idsOcs.Contains(ent.OrdenCompraId.Value))
                          || (!string.IsNullOrWhiteSpace(ent.OrdenCompraFolio) && foliosOcs.Contains(ent.OrdenCompraFolio.Trim()))
                          || (!string.IsNullOrWhiteSpace(sol.Folio) && (ent.Notas ?? string.Empty).Contains(sol.Folio, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(ent => ent.Id)
            .ToList();

        var foliosEntradas = entradas
            .Where(e => !string.IsNullOrWhiteSpace(e.Folio))
            .Select(e => e.Folio.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var facturas = DatabaseService.ObtenerFacturasXml()
            .Where(fac => (fac.OrdenCompraId.HasValue && idsOcs.Contains(fac.OrdenCompraId.Value))
                          || (!string.IsNullOrWhiteSpace(fac.OrdenCompraFolio) && foliosOcs.Contains(fac.OrdenCompraFolio.Trim())))
            .OrderByDescending(fac => fac.Id)
            .ToList();

        var salidas = DatabaseService.ObtenerSalidasAlmacen()
            .Where(sal =>
            {
                string notas = sal.Notas ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sol.Folio) && notas.Contains(sol.Folio, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (foliosOcs.Any(foc => notas.Contains(foc, StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (foliosEntradas.Any(fent => notas.Contains(fent, StringComparison.OrdinalIgnoreCase)))
                    return true;
                return false;
            })
            .OrderByDescending(sal => sal.Id)
            .ToList();

        var foliosSalidas = salidas
            .Where(s => !string.IsNullOrWhiteSpace(s.Folio))
            .Select(s => s.Folio.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var todosFoliosExpediente = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sol.Folio)) todosFoliosExpediente.Add(sol.Folio.Trim());
        foreach (var a in anexos.Where(a => !string.IsNullOrWhiteSpace(a.Folio))) todosFoliosExpediente.Add(a.Folio.Trim());
        foreach (var f in foliosOcs) todosFoliosExpediente.Add(f);
        foreach (var f in foliosEntradas) todosFoliosExpediente.Add(f);
        foreach (var f in foliosSalidas) todosFoliosExpediente.Add(f);

        var bitacora = DatabaseService.ObtenerNotificacionesCheckpoint(null, 300)
            .Where(n => (!string.IsNullOrWhiteSpace(n.FolioReferencia) && todosFoliosExpediente.Contains(n.FolioReferencia.Trim()))
                        || (!string.IsNullOrWhiteSpace(sol.Folio) && (n.Mensaje ?? string.Empty).Contains(sol.Folio, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.Id)
            .ToList();

        return (renglones, ordenes, entradas, facturas, salidas, anexos, bitacora);
    }

    private void CargarExpedienteSolicitudEnEditor(Solicitud? sol)
    {
        if (DgExpedienteSolPartidas == null)
            return;

        if (sol == null || sol.Id <= 0)
        {
            if (TxtExpTituloFolio != null) TxtExpTituloFolio.Text = "Expediente de Solicitud (Guarde primero la solicitud)";
            if (TxtExpSubtituloResumen != null) TxtExpSubtituloResumen.Text = "Una vez guardada la solicitud podrá consultar su expediente completo y reimprimir todos sus documentos.";
            if (TxtExpCard1Principal != null) TxtExpCard1Principal.Text = "En captura";
            if (TxtExpCard1Detalle != null) TxtExpCard1Detalle.Text = "-";
            if (TxtExpCard2Principal != null) TxtExpCard2Principal.Text = "-";
            if (TxtExpCard2Detalle != null) TxtExpCard2Detalle.Text = "-";
            if (TxtExpCard3Principal != null) TxtExpCard3Principal.Text = "$0.00";
            if (TxtExpCard3Detalle != null) TxtExpCard3Detalle.Text = "0 OCs";
            if (TxtExpCard4Principal != null) TxtExpCard4Principal.Text = "0 mov.";
            if (TxtExpCard4Detalle != null) TxtExpCard4Detalle.Text = "-";
            DgExpedienteSolPartidas.ItemsSource = null;
            if (DgExpedienteSolOrdenes != null) DgExpedienteSolOrdenes.ItemsSource = null;
            if (DgExpedienteSolEntradas != null) DgExpedienteSolEntradas.ItemsSource = null;
            if (DgExpedienteSolFacturas != null) DgExpedienteSolFacturas.ItemsSource = null;
            if (DgExpedienteSolSalidas != null) DgExpedienteSolSalidas.ItemsSource = null;
            if (DgExpedienteSolBitacora != null) DgExpedienteSolBitacora.ItemsSource = null;
            return;
        }

        var (renglones, ordenes, entradas, facturas, salidas, anexos, bitacora) = ObtenerDatosExpedienteSolicitud(sol);

        if (TxtExpTituloFolio != null)
            TxtExpTituloFolio.Text = $"Expediente Completo: {sol.Folio} — {sol.ProyectoMostrar} [{sol.Estatus}]";
        if (TxtExpSubtituloResumen != null)
            TxtExpSubtituloResumen.Text = $"Solicitó: {sol.EncargadoNombre} ({sol.DepartamentoNombre}) el {sol.Fecha} · Modalidad: {sol.TipoSolicitud} · {ordenes.Count} OC(s) · {entradas.Count} Entrada(s) · {facturas.Count} Factura(s)/Nota(s) · {salidas.Count} Salida(s)";

        if (TxtExpCard1Principal != null)
            TxtExpCard1Principal.Text = $"{sol.Folio} · {sol.Estatus}";
        if (TxtExpCard1Detalle != null)
            TxtExpCard1Detalle.Text = $"{sol.Fecha} · {sol.DepartamentoNombre}\nSolicitante: {sol.EncargadoNombre}";

        int cotizadas = renglones.Count(r => r.PrecioSinIva1 > 0 || r.PrecioSinIva2 > 0 || r.PrecioSinIva3 > 0 || r.ProveedoresExtra.Any(x => x.PrecioSinIva > 0));
        int adjudicadas = renglones.Count(r => r.ProveedorGanadorId > 0 || r.TieneOcGenerada);
        if (TxtExpCard2Principal != null)
            TxtExpCard2Principal.Text = $"{adjudicadas}/{renglones.Count} adjudicadas ({cotizadas} cot.)";
        if (TxtExpCard2Detalle != null)
            TxtExpCard2Detalle.Text = anexos.Count > 0
                ? $"Anexos vinculados: {string.Join(", ", anexos.Select(a => a.Folio))}"
                : "Sin anexos adicionales · Cuadro comparativo listo para reimprimir";

        decimal totalOcs = ordenes.Sum(o => o.TotalConIva);
        decimal totalAdjudicado = renglones.Where(r => r.ProveedorGanadorId > 0).Sum(r => r.TotalGanadorConIva + r.FleteGanadorConIva);
        decimal montoMostrar = totalOcs > 0 ? totalOcs : totalAdjudicado;
        if (TxtExpCard3Principal != null)
            TxtExpCard3Principal.Text = $"{ordenes.Count} OC(s) · {montoMostrar:C2}";
        if (TxtExpCard3Detalle != null)
            TxtExpCard3Detalle.Text = ordenes.Count > 0
                ? $"Folios: {string.Join(", ", ordenes.Select(o => o.Folio))}\nComprobantes: {facturas.Count} ({facturas.Sum(f => f.Total):C2})"
                : "Aún sin Órdenes de Compra emitidas";

        if (TxtExpCard4Principal != null)
            TxtExpCard4Principal.Text = $"{entradas.Count} Entrada(s) · {salidas.Count} Salida(s)";
        if (TxtExpCard4Detalle != null)
            TxtExpCard4Detalle.Text = entradas.Count > 0 || salidas.Count > 0
                ? $"Recibido en almacén: {entradas.Sum(e => e.Partidas.Sum(p => p.CantidadIngresadaAlmacen)):0.##} u. · Entregado en vales: {salidas.Sum(s => s.TotalUnidadesEntregadas):0.##} u."
                : "Sin recepciones o vales de salida registrados aún";

        var filasPartidas = renglones.Select(r =>
        {
            var opts = new List<string>();
            if (r.Proveedor1Id > 0 && r.PrecioSinIva1 > 0)
                opts.Add($"{Proveedor.ExtraerNombreComercialCorto(r.Proveedor1Nombre)}: {r.PrecioConIva1:C2}");
            if (r.Proveedor2Id > 0 && r.PrecioSinIva2 > 0)
                opts.Add($"{Proveedor.ExtraerNombreComercialCorto(r.Proveedor2Nombre)}: {r.PrecioConIva2:C2}");
            if (r.Proveedor3Id > 0 && r.PrecioSinIva3 > 0)
                opts.Add($"{Proveedor.ExtraerNombreComercialCorto(r.Proveedor3Nombre)}: {r.PrecioConIva3:C2}");
            foreach (var ex in r.ProveedoresExtra.Where(x => x.ProveedorId > 0 && x.PrecioSinIva > 0))
                opts.Add($"{Proveedor.ExtraerNombreComercialCorto(ex.ProveedorNombre)}: {ex.PrecioConIva:C2}");

            return new RenglonExpedientePartidaSolicitud
            {
                Codigo = string.IsNullOrWhiteSpace(r.ProductoCodigo) ? "CONCEPTO" : r.ProductoCodigo,
                Articulo = r.ProductoNombre,
                CantidadTexto = $"{r.Cantidad:0.##}",
                Unidad = r.UnidadCompra,
                OpcionesCotizadas = opts.Count > 0 ? string.Join(" | ", opts) : "Sin cotizar",
                ProveedorGanador = r.ProveedorGanadorId > 0 ? Proveedor.ExtraerNombreComercialCorto(r.ProveedorGanadorNombre) : "Sin adjudicar",
                PrecioUnitarioConIva = r.PrecioUnitarioGanadorConIva,
                TotalConIva = r.TotalGanadorConIva,
                FolioOc = string.IsNullOrWhiteSpace(r.OrdenCompraFolio) ? "Pendiente" : r.OrdenCompraFolio
            };
        }).ToList();

        DgExpedienteSolPartidas.ItemsSource = filasPartidas;
        if (DgExpedienteSolOrdenes != null) DgExpedienteSolOrdenes.ItemsSource = ordenes;
        if (DgExpedienteSolEntradas != null) DgExpedienteSolEntradas.ItemsSource = entradas;
        if (DgExpedienteSolFacturas != null) DgExpedienteSolFacturas.ItemsSource = facturas;
        if (DgExpedienteSolSalidas != null) DgExpedienteSolSalidas.ItemsSource = salidas;
        if (DgExpedienteSolBitacora != null) DgExpedienteSolBitacora.ItemsSource = bitacora;
    }

    private void BtnAbrirExpedienteSolicitudFila_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Solicitud sol })
        {
            DgHistorialSolicitudes.SelectedItem = sol;
            var fresca = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == sol.Id) ?? sol;
            CargarSolicitudEnEditor(fresca);
            MostrarDetalleSolicitud(true);
            SeleccionarTabDetalleSolicitudAlAbrir(fresca, true);
            e.Handled = true;
        }
    }

    private void BtnAbrirExpedienteSolicitudContext_Click(object sender, RoutedEventArgs e)
    {
        if (DgHistorialSolicitudes?.SelectedItem is Solicitud sol)
        {
            var fresca = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == sol.Id) ?? sol;
            CargarSolicitudEnEditor(fresca);
            MostrarDetalleSolicitud(true);
            SeleccionarTabDetalleSolicitudAlAbrir(fresca, true);
        }
    }

    private void BtnVerExpedienteSolicitudDetalle_Click(object sender, RoutedEventArgs e)
    {
        var sol = ObtenerSolicitudActualParaExpediente();
        if (sol != null)
            CargarExpedienteSolicitudEnEditor(sol);
        if (TabSolExpediente != null)
            TabSolExpediente.IsSelected = true;
    }

    private void BtnExpReimprimirComparativa_Click(object sender, RoutedEventArgs e)
    {
        var sol = ObtenerSolicitudActualParaExpediente();
        if (sol == null || sol.Partidas.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione o guarde primero una solicitud con partidas para reimprimir su Cuadro Comparativo.", "Sin partidas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var renglones = ConstruirRenglonesComparativaDeSolicitud(sol);
        BarcodePrintService.MostrarVistaPreviaEImprimirComparativa(this, sol, renglones, soloDescargar: false);
    }

    private void BtnExpDescargarComparativa_Click(object sender, RoutedEventArgs e)
    {
        var sol = ObtenerSolicitudActualParaExpediente();
        if (sol == null || sol.Partidas.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione o guarde primero una solicitud con partidas para descargar su Cuadro Comparativo.", "Sin partidas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var renglones = ConstruirRenglonesComparativaDeSolicitud(sol);
        BarcodePrintService.MostrarVistaPreviaEImprimirComparativa(this, sol, renglones, soloDescargar: true);
    }

    private void BtnExpImprimirExpedienteConsolidado_Click(object sender, RoutedEventArgs e)
    {
        var sol = ObtenerSolicitudActualParaExpediente();
        if (sol == null || sol.Id <= 0)
        {
            DialogoFluent.Show(this, "Guarde o seleccione primero una solicitud para imprimir su Expediente Completo.", "Solicitud no guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var datos = ObtenerDatosExpedienteSolicitud(sol);
        BarcodePrintService.MostrarVistaPreviaEImprimirExpedienteSolicitud(
            this, sol, datos.renglones, datos.ordenes, datos.entradas, datos.facturas, datos.salidas, datos.anexos, soloDescargar: false);
    }

    private void BtnExpDescargarExpedienteConsolidado_Click(object sender, RoutedEventArgs e)
    {
        var sol = ObtenerSolicitudActualParaExpediente();
        if (sol == null || sol.Id <= 0)
        {
            DialogoFluent.Show(this, "Guarde o seleccione primero una solicitud para descargar su Expediente Completo.", "Solicitud no guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var datos = ObtenerDatosExpedienteSolicitud(sol);
        BarcodePrintService.MostrarVistaPreviaEImprimirExpedienteSolicitud(
            this, sol, datos.renglones, datos.ordenes, datos.entradas, datos.facturas, datos.salidas, datos.anexos, soloDescargar: true);
    }

    private void BtnExpReimprimirOc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OrdenCompra oc })
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, oc, soloDescargar: false);
        }
    }

    private void BtnExpDescargarOc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OrdenCompra oc })
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirOrdenCompra(this, oc, soloDescargar: true);
        }
    }

    private void BtnExpIrAOc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OrdenCompra oc })
        {
            CambiarSeccion(4);
            RefrescarOrdenesYEntradas();
            if (DgOrdenesCompra?.ItemsSource is IEnumerable<OrdenCompra> lista)
            {
                var match = lista.FirstOrDefault(x => x.Id == oc.Id);
                if (match != null)
                    DgOrdenesCompra.SelectedItem = match;
            }
            MostrarDetalleOrdenCompra(true);
        }
    }

    private void BtnExpReimprimirEntrada_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EntradaAlmacen ent })
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent, soloDescargar: false);
        }
    }

    private void BtnExpDescargarEntrada_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EntradaAlmacen ent })
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirEntrada(this, ent, soloDescargar: true);
        }
    }

    private void BtnExpReimprimirSalida_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SalidaAlmacen sal })
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, sal, soloDescargar: false);
        }
    }

    private void BtnExpDescargarSalida_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SalidaAlmacen sal })
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirSalida(this, sal, soloDescargar: true);
        }
    }

    // =========================================================================
    // FASE 1: BITÁCORA POR USUARIO, FAMILIAS GENÉRICAS, DIRECTO A OBRA,
    //         REINGRESO DE SOBRANTES Y AJUSTES DE INVENTARIO / MERMA (AJU)
    // =========================================================================

    private void MostrarModalBitacoraUsuarios(Window? ownerWin = null)
    {
        var win = new Window
        {
            Title = "Bitácora de Actividad Nominal por Usuario",
            Width = 940,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = ownerWin ?? this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

        var cboUsuarioFiltro = new ComboBox { Width = 210, Margin = new Thickness(8, 0, 8, 0) };
        cboUsuarioFiltro.Items.Add("Todos");
        foreach (var u in DatabaseService.ObtenerUsuariosSistema(soloActivos: false))
            cboUsuarioFiltro.Items.Add(u.Nombre);
        cboUsuarioFiltro.SelectedIndex = 0;

        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightPanel.Children.Add(new TextBlock { Text = "Filtrar por usuario:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        rightPanel.Children.Add(cboUsuarioFiltro);
        DockPanel.SetDock(rightPanel, Dock.Right);
        topBar.Children.Add(rightPanel);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "Historial de Operaciones por Usuario", FontSize = 16, FontWeight = FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Trazabilidad nominal de quién creó solicitudes, emitió órdenes, recibió mercancía, entregó material o ajustó inventario.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush")
        });
        topBar.Children.Add(titleStack);
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Fecha y Hora", Binding = new System.Windows.Data.Binding("FechaHora"), Width = 130, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Usuario", Binding = new System.Windows.Data.Binding("UsuarioNombre"), Width = 140, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Vista / Rol", Binding = new System.Windows.Data.Binding("RolOperativo"), Width = 95, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Módulo", Binding = new System.Windows.Data.Binding("Modulo"), Width = 125, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Acción", Binding = new System.Windows.Data.Binding("Accion"), Width = 145, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Folio", Binding = new System.Windows.Data.Binding("FolioReferencia"), Width = 120, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Detalle", Binding = new System.Windows.Data.Binding("Detalle"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });

        void RecargarBitacora()
        {
            string f = cboUsuarioFiltro.SelectedItem?.ToString() ?? "Todos";
            dg.ItemsSource = DatabaseService.ObtenerBitacoraUsuarios(null, f == "Todos" ? string.Empty : f, 300);
        }

        cboUsuarioFiltro.SelectionChanged += (_, _) => RecargarBitacora();
        RecargarBitacora();

        root.Children.Add(dg);
        win.Content = root;
        win.ShowDialog();
    }

    private void BtnAgregarPorFamiliaGenericaSolicitud_Click(object sender, RoutedEventArgs e)
    {
        var familias = DatabaseService.ObtenerResumenFamiliasCatalogo();
        if (familias.Count == 0)
        {
            DialogoFluent.Show(this, "Aún no hay familias genéricas registradas en el catálogo.", "Sin familias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = "Solicitar por Familia Genérica (Sin casarse con una marca específica)",
            Width = 760,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock
        {
            Text = "Seleccionar Familia Funcional de Material",
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold
        });
        top.Children.Add(new TextBlock
        {
            Text = "Elija la función o familia genérica (ej. Pegamento para PVC, Tubo Conduit). El sistema sugiere la marca con mejor costo/existencia actual, pero Compras o Almacén podrán intercambiar la marca al cotizar o recoger.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var filterGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var txtFiltro = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Filtrar familia o marca disponible (ej. PVC, cable, guante, disco)...", Margin = new Thickness(0, 0, 8, 0) };
        var txtCant = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Cant. (1)", Text = string.IsNullOrWhiteSpace(TxtSolCantidadAgregar?.Text) ? "1" : TxtSolCantidadAgregar.Text };
        Grid.SetColumn(txtFiltro, 0);
        Grid.SetColumn(txtCant, 1);
        filterGrid.Children.Add(txtFiltro);
        filterGrid.Children.Add(txtCant);
        top.Children.Add(filterGrid);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = familias
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Categoría", Binding = new System.Windows.Data.Binding("CategoriaNombre"), Width = 125, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Familia Genérica", Binding = new System.Windows.Data.Binding("Familia"), Width = 175, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Variantes / Marcas en Catálogo", Binding = new System.Windows.Data.Binding("MarcasResumen"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Stock Total Familia", Binding = new System.Windows.Data.Binding("StockTotalFamilia"), Width = 120, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadBase"), Width = 80, ElementStyle = CeldaRecortable() });
        if (familias.Count > 0) dg.SelectedIndex = 0;

        txtFiltro.TextChanged += (_, _) =>
        {
            string q = txtFiltro.Text?.Trim() ?? string.Empty;
            dg.ItemsSource = string.IsNullOrWhiteSpace(q)
                ? familias
                : familias.Where(f =>
                    f.Familia.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    f.CategoriaNombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    f.MarcasResumen.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (dg.Items.Count > 0) dg.SelectedIndex = 0;
        };

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var chkComoConceptoAbierto = new CheckBox
        {
            Content = "Dejar marca abierta hasta Comparativa (Concepto Genérico [FAMILIA])",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Si se marca, se agrega como concepto genérico de familia; si no, usa el producto representante sugerido de la familia permitiendo cambiar marca después."
        };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnAdd = new Wpf.Ui.Controls.Button { Content = "+ Agregar Familia a Solicitud", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnAdd);
        DockPanel.SetDock(btnPanel, Dock.Right);
        bottom.Children.Add(btnPanel);
        bottom.Children.Add(chkComoConceptoAbierto);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(dg);

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnAdd.Click += (_, _) =>
        {
            if (dg.SelectedItem is not ResumenFamiliaCatalogo) return;
            win.DialogResult = true;
        };
        dg.MouseDoubleClick += (_, _) =>
        {
            if (dg.SelectedItem is ResumenFamiliaCatalogo) win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && dg.SelectedItem is ResumenFamiliaCatalogo famSel)
        {
            decimal cant = ParseDecimal(txtCant.Text, 1m);
            if (cant <= 0) cant = 1m;

            if (chkComoConceptoAbierto.IsChecked == true || famSel.ProductoSugeridoId <= 0)
            {
                _partidasNuevaSolicitud.Add(new SolicitudPartida
                {
                    ProductoId = 0,
                    ProductoCodigo = "FAMILIA",
                    ProductoNombre = $"[FAMILIA: {famSel.Familia}] (Cualquier marca equivalente)",
                    UnidadCompra = famSel.UnidadBase,
                    CantidadSolicitada = cant,
                    StockActualAlmacen = famSel.StockTotalFamilia,
                    IncluirEnCotizacion = true
                });
            }
            else
            {
                var prodRep = DatabaseService.ObtenerProductos().FirstOrDefault(p => p.Id == famSel.ProductoSugeridoId);
                if (prodRep != null)
                {
                    _partidasNuevaSolicitud.Add(new SolicitudPartida
                    {
                        ProductoId = prodRep.Id,
                        ProductoCodigo = prodRep.CodigoInterno,
                        ProductoNombre = $"{prodRep.Nombre} [Familia: {famSel.Familia} — Marca intercambiable]",
                        UnidadCompra = prodRep.UnidadCompra,
                        CantidadSolicitada = cant,
                        StockActualAlmacen = famSel.StockTotalFamilia,
                        IncluirEnCotizacion = true
                    });
                }
            }
            DgPartidasNuevaSolicitud.Items.Refresh();
        }
    }

    private void BtnSustituirProductoEnRecepcionOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra.", "Sin OC seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var partidaSel = DgPartidasRecepcionOc.SelectedItem as OrdenCompraPartida ?? oc.Partidas.FirstOrDefault();
        if (partidaSel == null)
        {
            DialogoFluent.Show(this, "Seleccione en la tabla de recepción la partida cuya marca o producto desea sustituir.", "Seleccione partida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var todosProds = DatabaseService.ObtenerProductos();
        var prodActual = todosProds.FirstOrDefault(p => p.Id == partidaSel.ProductoId);
        string familiaActual = prodActual?.Familia ?? string.Empty;

        var candidatos = todosProds
            .OrderByDescending(p => !string.IsNullOrWhiteSpace(familiaActual) && string.Equals(p.Familia, familiaActual, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Nombre)
            .ToList();

        var win = new Window
        {
            Title = $"Sustituir Marca / Producto Equivalente en {oc.Folio}",
            Width = 680,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock
        {
            Text = $"Partida original: {partidaSel.ProductoCodigo} — {partidaSel.ProductoNombre}",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold
        });
        top.Children.Add(new TextBlock
        {
            Text = "Si al recoger con el proveedor entregaron otra marca o variante equivalente, selecciónela aquí para que el inventario y el kardex reciban el artículo exacto:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var txtBuscar = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Buscar sustituto por nombre, marca, familia o código...",
            Text = familiaActual,
            Margin = new Thickness(0, 0, 0, 6)
        };
        top.Children.Add(txtBuscar);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("CodigoInterno"), Width = 100, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Producto / Marca", Binding = new System.Windows.Data.Binding("Nombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Familia", Binding = new System.Windows.Data.Binding("Familia"), Width = 140, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Stock", Binding = new System.Windows.Data.Binding("StockActual"), Width = 75, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Ubicación", Binding = new System.Windows.Data.Binding("Ubicacion"), Width = 85, ElementStyle = CeldaRecortable() });

        void FiltrarSustitutos()
        {
            string q = txtBuscar.Text?.Trim() ?? string.Empty;
            dg.ItemsSource = string.IsNullOrWhiteSpace(q)
                ? candidatos
                : candidatos.Where(p =>
                    p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.Familia.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.CodigoInterno.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (dg.Items.Count > 0) dg.SelectedIndex = 0;
        }
        txtBuscar.TextChanged += (_, _) => FiltrarSustitutos();
        FiltrarSustitutos();

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var txtMotivo = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Motivo (ej. Proveedor entregó marca equivalente por disponibilidad en mostrador)", Width = 390, HorizontalAlignment = HorizontalAlignment.Left };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnOk = new Wpf.Ui.Controls.Button { Content = "Aplicar Sustitución", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        DockPanel.SetDock(btnPanel, Dock.Right);
        bottom.Children.Add(btnPanel);
        bottom.Children.Add(txtMotivo);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(dg);

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click += (_, _) =>
        {
            if (dg.SelectedItem is not Producto) return;
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && dg.SelectedItem is Producto prodNuevo)
        {
            string motivo = string.IsNullOrWhiteSpace(txtMotivo.Text) ? "Cambio de marca/equivalente al recibir" : txtMotivo.Text.Trim();
            DatabaseService.CambiarProductoEnPartidaOrdenCompra(partidaSel.Id, prodNuevo, motivo);
            CargarTodoElSistema();
            DialogoFluent.Show(this,
                $"La partida se actualizó a '{prodNuevo.CodigoInterno} — {prodNuevo.Nombre}'.\n\nYa puede registrar su recepción en almacén o entrega directa a obra.",
                "Marca / Producto Sustituido",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void BtnRegistrarRecepcionDirectaObraOc_Click(object sender, RoutedEventArgs e)
    {
        if (DgOrdenesCompra.SelectedItem is not OrdenCompra oc)
        {
            DialogoFluent.Show(this, "Seleccione primero una Orden de Compra.", "Sin OC seleccionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (oc.Estatus == "Cancelada")
        {
            DialogoFluent.Show(this, "Esta Orden de Compra está Cancelada.", "OC Cancelada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Si todas las cantidades 'Recibir Hoy' están en 0, precargar automáticamente todo lo pendiente para ahorrar clics
        if (oc.Partidas.All(p => p.CantidadRecibirAhora <= 0m))
        {
            foreach (var p in oc.Partidas)
                p.CantidadRecibirAhora = Math.Max(0m, p.CantidadPendiente);
            DgPartidasRecepcionOc.Items.Refresh();
        }

        var aRecibir = oc.Partidas
            .Where(p => p.CantidadRecibirAhora > 0)
            .Select(p => (p, p.CantidadRecibirAhora))
            .ToList();

        if (aRecibir.Count == 0)
        {
            DialogoFluent.Show(this, "No hay cantidades pendientes por recibir en esta Orden de Compra.", "Sin cantidades", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = $"Recolección con Proveedor y Entrega Directa a Obra — {oc.Folio}",
            Width = 560,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Cerrar Compra con Entrega Directa a Obra / Servicio",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Use esta opción cuando el material se recogió en mostrador del proveedor y se llevó directo al proyecto (sin pisar los anaqueles de Almacén). El sistema registrará la Entrada y su Salida Espejo automática al proyecto, conservando el Stock de Almacén intacto.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = $"Proyecto destino: {oc.ProyectoNombre}", FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 8) });

        root.Children.Add(new TextBlock { Text = "Persona que recogió el material o recibió en obra *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboPersona = new ComboBox
        {
            ItemsSource = DatabaseService.ObtenerEncargados().Select(x => x.Nombre).Distinct().ToList(),
            IsEditable = true,
            Text = !string.IsNullOrWhiteSpace(oc.EncargadoNombre) ? oc.EncargadoNombre : DatabaseService.ObtenerUsuarioActivo().Nombre,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(cboPersona);

        root.Children.Add(new TextBlock { Text = "Folio de Remisión / Factura o Nota de Mostrador", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtRemision = new Wpf.Ui.Controls.TextBox
        {
            Text = TxtFolioRemisionRecepcionOc?.Text ?? string.Empty,
            PlaceholderText = "Ej. REM-9012 / Nota mostrador Home Depot / Servicio en sitio",
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(txtRemision);

        var chkServicio = new CheckBox
        {
            Content = "Es Servicio / Flete / Gasto Directo No Inventariable (no requiere paso por anaquel)",
            IsChecked = false,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(chkServicio);

        root.Children.Add(new TextBlock { Text = "Lugar de entrega para correo al gerente (\"...concluido con entrega en xxx\"):", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtLugarCorreoObra = new Wpf.Ui.Controls.TextBox
        {
            Text = !string.IsNullOrWhiteSpace(oc.ProyectoNombre) ? $"Obra / Proyecto {oc.ProyectoNombre}" : "Directo en Obra",
            PlaceholderText = "Ej. Obra Norte / Planta Mérida / Entregado en sitio",
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(txtLugarCorreoObra);

        var chkNotificarCorreoObra = new CheckBox
        {
            Content = "Enviar correo de conclusión de folio al gerente solicitante",
            IsChecked = true,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(chkNotificarCorreoObra);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnConfirm = new Wpf.Ui.Controls.Button { Content = "Confirmar Entrega Directa a Obra", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnConfirm);
        root.Children.Add(btnPanel);

        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnConfirm.Click += (_, _) => win.DialogResult = true;

        win.Content = root;
        if (win.ShowDialog() != true)
            return;

        string persona = string.IsNullOrWhiteSpace(cboPersona.Text) ? DatabaseService.ObtenerUsuarioActivo().Nombre : cboPersona.Text.Trim();
        string rem = txtRemision.Text?.Trim() ?? string.Empty;
        string obs = TxtNotasRecepcionOc?.Text?.Trim() ?? string.Empty;
        string lugarObraPersonalizado = txtLugarCorreoObra.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(lugarObraPersonalizado) && !lugarObraPersonalizado.Contains(persona, StringComparison.OrdinalIgnoreCase))
            lugarObraPersonalizado = $"{lugarObraPersonalizado} (Recibió: {persona})";

        bool esServ = chkServicio.IsChecked == true;
        var (ent, sal) = DatabaseService.RegistrarRecepcionDirectaAObra(
            oc,
            aRecibir,
            persona,
            rem,
            obs,
            esServ,
            lugarObraPersonalizado,
            chkNotificarCorreoObra.IsChecked == true);

        TxtNotasRecepcionOc?.Clear();
        TxtFolioRemisionRecepcionOc?.Clear();
        CargarTodoElSistema();

        DialogoFluent.Show(this,
            $"Se registró exitosamente la operación directa de {oc.Folio}:\n\n" +
            $"• Folio Entrada Contable: {ent.Folio} ({ent.TipoEntrada})\n" +
            (sal != null ? $"• Folio Salida Espejo a Obra: {sal.Folio} (Entregado a: {persona})\n" : "") +
            $"• Proyecto imputado: {oc.ProyectoNombre}\n" +
            "• El Stock físico de los anaqueles de Almacén se mantuvo intacto (variación neta 0).",
            "Entrega Directa a Obra Registrada",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnReingresoSobranteObra_Click(object sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Reingresar Material Sobrante de Obra a Stock de Almacén",
            Width = 760,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock
        {
            Text = "Reingreso de Sobrante de Obra a Bolsa Libre de Almacén",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        top.Children.Add(new TextBlock
        {
            Text = "Use esta ventana cuando regrese material útil de una obra (incluso si fue comprado directo en mostrador y no tuvo un Vale de Salida SAL previo). El material se sumará al Stock Actual en la ubicación de anaquel indicada.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10)
        });

        var metaGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var spProy = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spProy.Children.Add(new TextBlock { Text = "Proyecto / Obra de donde regresa *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboProy = new ComboBox { ItemsSource = DatabaseService.ObtenerProyectos(), IsEditable = true };
        if (cboProy.Items.Count > 0) cboProy.SelectedIndex = 0;
        spProy.Children.Add(cboProy);

        var spPersona = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        spPersona.Children.Add(new TextBlock { Text = "Persona que entrega el sobrante en Almacén *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboPersona = new ComboBox
        {
            ItemsSource = DatabaseService.ObtenerEncargados().Select(x => x.Nombre).Distinct().ToList(),
            IsEditable = true,
            Text = DatabaseService.ObtenerUsuarioActivo().Nombre
        };
        spPersona.Children.Add(cboPersona);

        Grid.SetColumn(spProy, 0);
        Grid.SetColumn(spPersona, 1);
        metaGrid.Children.Add(spProy);
        metaGrid.Children.Add(spPersona);
        top.Children.Add(metaGrid);

        var addBar = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prods = DatabaseService.ObtenerProductos();
        var cboProd = new ComboBox { ItemsSource = prods, IsEditable = true, Margin = new Thickness(0, 0, 6, 0) };
        var txtCant = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Cant. (1)", Text = "1", Margin = new Thickness(0, 0, 6, 0) };
        var txtUbic = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Anaquel (A-01)", Text = "A-01", Margin = new Thickness(0, 0, 6, 0) };
        var btnAdd = new Wpf.Ui.Controls.Button { Content = "+ Agregar sobrante", Appearance = ControlAppearance.Primary };

        cboProd.SelectionChanged += (_, _) =>
        {
            if (cboProd.SelectedItem is Producto pSel && !string.IsNullOrWhiteSpace(pSel.Ubicacion))
                txtUbic.Text = pSel.Ubicacion;
        };

        Grid.SetColumn(cboProd, 0);
        Grid.SetColumn(txtCant, 1);
        Grid.SetColumn(txtUbic, 2);
        Grid.SetColumn(btnAdd, 3);
        addBar.Children.Add(cboProd);
        addBar.Children.Add(txtCant);
        addBar.Children.Add(txtUbic);
        addBar.Children.Add(btnAdd);
        top.Children.Add(addBar);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var partidas = new ObservableCollection<EntradaPartida>();
        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            ItemsSource = partidas
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("ProductoCodigo"), IsReadOnly = true, Width = 100 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Producto", Binding = new System.Windows.Data.Binding("ProductoNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Cant. a Reingresar", Binding = new System.Windows.Data.Binding("CantidadIngresadaAlmacen"), Width = 130 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Unidad", Binding = new System.Windows.Data.Binding("UnidadAlmacen"), IsReadOnly = true, Width = 90 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Ubicación Anaquel", Binding = new System.Windows.Data.Binding("UbicacionAlmacen"), Width = 130 });

        btnAdd.Click += (_, _) =>
        {
            if (cboProd.SelectedItem is not Producto prod)
            {
                DialogoFluent.Show(win, "Seleccione un artículo del catálogo.", "Artículo requerido", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            decimal cant = ParseDecimal(txtCant.Text, 1m);
            if (cant <= 0) cant = 1m;
            string ubic = string.IsNullOrWhiteSpace(txtUbic.Text) ? prod.Ubicacion : txtUbic.Text.Trim();

            var existente = partidas.FirstOrDefault(x => x.ProductoId == prod.Id);
            if (existente != null)
            {
                existente.CantidadIngresadaAlmacen += cant;
                existente.CantidadCompraRecibida = existente.CantidadIngresadaAlmacen;
                dg.Items.Refresh();
            }
            else
            {
                partidas.Add(new EntradaPartida
                {
                    ProductoId = prod.Id,
                    ProductoCodigo = prod.CodigoInterno,
                    ProductoNombre = prod.Nombre,
                    CantidadCompraRecibida = cant,
                    UnidadCompra = prod.UnidadAlmacen,
                    FactorConversion = 1m,
                    CantidadIngresadaAlmacen = cant,
                    UnidadAlmacen = prod.UnidadAlmacen,
                    UbicacionAlmacen = ubic,
                    CostoUnitarioCompraConIva = prod.UltimoCostoCompra
                });
            }
        };

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var txtNotas = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Observaciones del sobrante (ej. Material nuevo sin usar al cierre de instalación)...", Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Reingresar a Stock", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnSave);
        DockPanel.SetDock(btnPanel, Dock.Right);
        bottom.Children.Add(btnPanel);
        bottom.Children.Add(txtNotas);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(dg);

        EntradaAlmacen? entradaCreada = null;
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnSave.Click += (_, _) =>
        {
            if (partidas.Count == 0)
            {
                DialogoFluent.Show(win, "Agregue al menos un artículo sobrante para reingresar.", "Sin artículos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string proyNombre = (cboProy.SelectedItem as Proyecto)?.Nombre ?? cboProy.Text?.Trim() ?? "Obra General";
            string persona = string.IsNullOrWhiteSpace(cboPersona.Text) ? DatabaseService.ObtenerUsuarioActivo().Nombre : cboPersona.Text.Trim();
            entradaCreada = DatabaseService.RegistrarReingresoSobranteObra(proyNombre, persona, txtNotas.Text?.Trim() ?? string.Empty, partidas.ToList());
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && entradaCreada != null)
        {
            CargarTodoElSistema();
            DialogoFluent.Show(this,
                $"Se registró el reingreso de sobrante con folio {entradaCreada.Folio} y se incrementaron las existencias en Almacén.",
                "Sobrante Reingresado a Stock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void BtnAjusteInventarioMerma_Click(object sender, RoutedEventArgs e)
    {
        var prods = DatabaseService.ObtenerProductos();
        if (prods.Count == 0) return;

        var win = new Window
        {
            Title = "Ajuste Físico de Inventario o Baja por Merma / Daño (AJU-xxxx)",
            Width = 560,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Ajuste Cíclico de Inventario o Registro de Merma (AJU)",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Cuadre las existencias reales de un anaquel tras un conteo físico o dé de baja material dañado/extraviado sin inventar salidas falsas a obra. Quedará auditado con folio AJU-xxxx.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = "Artículo a ajustar *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboProd = new ComboBox
        {
            ItemsSource = prods,
            IsEditable = true,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (_productoEnFicha != null)
            cboProd.SelectedItem = prods.FirstOrDefault(p => p.Id == _productoEnFicha.Id);
        if (cboProd.SelectedItem == null && prods.Count > 0)
            cboProd.SelectedIndex = 0;
        root.Children.Add(cboProd);

        var gridStock = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        gridStock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridStock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridStock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var spActual = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spActual.Children.Add(new TextBlock { Text = "Stock en Sistema", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtStockSistema = new Wpf.Ui.Controls.TextBox { IsReadOnly = true };
        spActual.Children.Add(txtStockSistema);

        var spNuevo = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
        spNuevo.Children.Add(new TextBlock { Text = "Existencia Física Real *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtStockFisico = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Conteo real" };
        spNuevo.Children.Add(txtStockFisico);

        var spUbic = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        spUbic.Children.Add(new TextBlock { Text = "Ubicación Anaquel", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtUbic = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. A-01" };
        spUbic.Children.Add(txtUbic);

        Grid.SetColumn(spActual, 0);
        Grid.SetColumn(spNuevo, 1);
        Grid.SetColumn(spUbic, 2);
        gridStock.Children.Add(spActual);
        gridStock.Children.Add(spNuevo);
        gridStock.Children.Add(spUbic);
        root.Children.Add(gridStock);

        root.Children.Add(new TextBlock { Text = "Tipo de Ajuste *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var cboTipoAjuste = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        cboTipoAjuste.Items.Add("Conteo Cíclico Físico (Cuadre de Anaquel)");
        cboTipoAjuste.Items.Add("Baja por Merma / Material Dañado o Caduco");
        cboTipoAjuste.Items.Add("Baja por Extravío / Faltante en Bodega");
        cboTipoAjuste.Items.Add("Alta por Hallazgo en Inventario Físico");
        cboTipoAjuste.SelectedIndex = 0;
        root.Children.Add(cboTipoAjuste);

        root.Children.Add(new TextBlock { Text = "Motivo / Justificación detallada *", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtMotivo = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Explique la razón de la diferencia física o daño...",
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(txtMotivo);

        void SincronizarProductoSel()
        {
            if (cboProd.SelectedItem is Producto p)
            {
                txtStockSistema.Text = $"{p.StockActual:0.##} {p.UnidadAlmacen}";
                txtStockFisico.Text = p.StockActual.ToString("0.##", CultureInfo.InvariantCulture);
                txtUbic.Text = p.Ubicacion;
            }
        }
        cboProd.SelectionChanged += (_, _) => SincronizarProductoSel();
        SincronizarProductoSel();

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnApply = new Wpf.Ui.Controls.Button { Content = "Aplicar Ajuste (AJU-xxxx)", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnApply);
        root.Children.Add(btnPanel);

        string folioGenerado = string.Empty;
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnApply.Click += (_, _) =>
        {
            if (cboProd.SelectedItem is not Producto pSel)
            {
                DialogoFluent.Show(win, "Seleccione un artículo del catálogo.", "Artículo requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            decimal nuevoStock = ParseDecimal(txtStockFisico.Text, -1m);
            if (nuevoStock < 0m)
            {
                DialogoFluent.Show(win, "Ingrese una existencia física válida (mayor o igual a 0).", "Cantidad inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtMotivo.Text))
            {
                DialogoFluent.Show(win, "Es obligatorio capturar el motivo o justificación del ajuste para la bitácora de auditoría.", "Motivo requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tipo = cboTipoAjuste.SelectedItem?.ToString() ?? "Ajuste Físico";
            string ubicNueva = txtUbic.Text?.Trim() ?? pSel.Ubicacion;
            if (!string.IsNullOrWhiteSpace(ubicNueva) && !string.Equals(pSel.Ubicacion, ubicNueva, StringComparison.OrdinalIgnoreCase))
            {
                pSel.Ubicacion = ubicNueva;
                DatabaseService.GuardarProducto(pSel);
            }
            var entAjuste = DatabaseService.RegistrarAjusteInventarioAlmacen(
                pSel,
                nuevoStock,
                tipo,
                txtMotivo.Text.Trim());
            folioGenerado = entAjuste.Folio;
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(folioGenerado))
        {
            CargarTodoElSistema();
            DialogoFluent.Show(this,
                $"Ajuste de inventario aplicado y auditado con el folio {folioGenerado}.",
                "Ajuste Aplicado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    // =========================================================================
    // FASE 2: MALETINES / KITS DE HERRAMIENTAS (KIT-xxxx) CON SALIDA Y
    //         CHECK-LIST DE REGRESO EN 1 CLIC (OPERACIÓN SIN ALMACENISTA)
    // =========================================================================

    private void BtnAbrirGestorKitsHerramientas_Click(object sender, RoutedEventArgs e)
    {
        MostrarModalGestorKitsHerramientas(null);
    }

    private void BtnCargarKitEnPrestamoActual_Click(object sender, RoutedEventArgs e)
    {
        var kits = DatabaseService.ObtenerKitsHerramientas();
        if (kits.Count == 0)
        {
            MostrarModalGestorKitsHerramientas(null);
            return;
        }

        var win = new Window
        {
            Title = "Despachar o Cargar Maletín / Kit de Herramientas (KIT-xxxx) en 1 Clic",
            Width = 760,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock
        {
            Text = "Seleccionar Maletín / Kit para Salida Rápida",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        top.Children.Add(new TextBlock
        {
            Text = "Elija un maletín armado (KIT-xxxx). Puede cargar sus herramientas a la lista actual de préstamo o emitir el Vale de Préstamo (PRE-xxxx) en un solo clic:",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var formRapido = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        formRapido.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formRapido.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formRapido.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var spDep = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        spDep.Children.Add(new TextBlock { Text = "Depto. que Aprueba", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var cboDep = new ComboBox { ItemsSource = DatabaseService.ObtenerDepartamentos() };
        if (CboPreDepartamento?.SelectedItem is Departamento dPre)
            cboDep.SelectedItem = cboDep.Items.Cast<Departamento>().FirstOrDefault(x => x.Id == dPre.Id);
        if (cboDep.SelectedItem == null && cboDep.Items.Count > 0) cboDep.SelectedIndex = 0;
        spDep.Children.Add(cboDep);

        var spPer = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
        spPer.Children.Add(new TextBlock { Text = "Persona que lleva el Maletín *", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var cboPer = new ComboBox
        {
            ItemsSource = DatabaseService.ObtenerEncargados().Select(x => x.Nombre).Distinct().ToList(),
            IsEditable = true,
            Text = !string.IsNullOrWhiteSpace(CboPrePersonaRecibe?.Text) ? CboPrePersonaRecibe.Text : DatabaseService.ObtenerUsuarioActivo().Nombre
        };
        spPer.Children.Add(cboPer);

        var spProy = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        spProy.Children.Add(new TextBlock { Text = "Proyecto / Obra Destino", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var cboProy = new ComboBox { ItemsSource = DatabaseService.ObtenerProyectos() };
        if (CboPreProyecto?.SelectedItem is Proyecto pPre)
            cboProy.SelectedItem = cboProy.Items.Cast<Proyecto>().FirstOrDefault(x => x.Id == pPre.Id);
        if (cboProy.SelectedItem == null && cboProy.Items.Count > 0) cboProy.SelectedIndex = 0;
        spProy.Children.Add(cboProy);

        Grid.SetColumn(spDep, 0);
        Grid.SetColumn(spPer, 1);
        Grid.SetColumn(spProy, 2);
        formRapido.Children.Add(spDep);
        formRapido.Children.Add(spPer);
        formRapido.Children.Add(spProy);
        top.Children.Add(formRapido);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = kits
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Código Kit", Binding = new System.Windows.Data.Binding("CodigoKit"), Width = 95, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Nombre del Maletín", Binding = new System.Windows.Data.Binding("Nombre"), Width = 220, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Disponibles", Binding = new System.Windows.Data.Binding("DisponibilidadTexto"), Width = 90, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Estatus", Binding = new System.Windows.Data.Binding("Estatus"), Width = 135, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Herramientas Incluidas", Binding = new System.Windows.Data.Binding("ResumenHerramientas"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });
        if (kits.Count > 0) dg.SelectedIndex = 0;

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var btnAdminKits = new System.Windows.Controls.Button { Content = "Editar / Armar Maletines...", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 0) };
        var btnSoloCargar = new Wpf.Ui.Controls.Button { Content = "Solo Cargar a Lista", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 0) };
        var btnDespachar1Clic = new Wpf.Ui.Controls.Button { Content = "Despachar Maletín en 1 Clic (PRE)", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        rightBtns.Children.Add(btnCancel);
        rightBtns.Children.Add(btnSoloCargar);
        rightBtns.Children.Add(btnDespachar1Clic);
        DockPanel.SetDock(rightBtns, Dock.Right);
        bottom.Children.Add(rightBtns);
        bottom.Children.Add(btnAdminKits);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(dg);

        int modoElegido = 0; // 1 = Solo cargar a lista, 2 = Despachar en 1 clic, 3 = Abrir gestor
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnAdminKits.Click += (_, _) => { modoElegido = 3; win.DialogResult = true; };
        btnSoloCargar.Click += (_, _) =>
        {
            if (dg.SelectedItem is not KitHerramienta) return;
            modoElegido = 1;
            win.DialogResult = true;
        };
        btnDespachar1Clic.Click += (_, _) =>
        {
            if (dg.SelectedItem is not KitHerramienta) return;
            modoElegido = 2;
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() != true) return;

        var kitSel = dg.SelectedItem as KitHerramienta;
        if (modoElegido == 3)
        {
            MostrarModalGestorKitsHerramientas(kitSel?.Id);
            return;
        }

        if (kitSel == null) return;

        if (modoElegido == 1)
        {
            var todasHer = DatabaseService.ObtenerHerramientasIndividuales().ToDictionary(h => h.Id);
            int agregadas = 0;
            foreach (var part in kitSel.Partidas)
            {
                if (todasHer.TryGetValue(part.HerramientaActivoId, out var hObj) &&
                    string.Equals(hObj.Estado, "Disponible", StringComparison.OrdinalIgnoreCase))
                {
                    AgregarHerramientaAListaPrestamo(hObj);
                    agregadas++;
                }
            }
            if (agregadas == 0)
                DialogoFluent.Show(this, $"Ninguna de las herramientas del maletín {kitSel.CodigoKit} figura como Disponible en este momento.", "Sin herramientas disponibles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (modoElegido == 2)
        {
            var dep = cboDep.SelectedItem as Departamento ?? DatabaseService.ObtenerDepartamentos().FirstOrDefault();
            var proy = cboProy.SelectedItem as Proyecto;
            string persona = string.IsNullOrWhiteSpace(cboPer.Text) ? DatabaseService.ObtenerUsuarioActivo().Nombre : cboPer.Text.Trim();
            int depId = dep?.Id ?? 1;
            int proyId = proy?.Id ?? DatabaseService.ObtenerProyectoStockGeneralId();
            int encId = DatabaseService.ObtenerEncargados(depId).FirstOrDefault()?.Id ?? 1;
            string fechaComp = (DpPreFechaCompromiso?.SelectedDate ?? DateTime.Today.AddDays(7)).ToString("yyyy-MM-dd");

            var prestamo = DatabaseService.DespacharKitHerramientaEnUnClic(kitSel.Id, depId, encId, proyId, persona, fechaComp);
            if (prestamo == null)
            {
                DialogoFluent.Show(this, $"El maletín {kitSel.CodigoKit} no tiene herramientas disponibles para despachar.", "Maletín ya en obra o vacío", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CargarTodoElSistema();
            BarcodePrintService.MostrarVistaPreviaEImprimirPrestamo(this, prestamo);
        }
    }

    private void BtnChecklistRegresoKitRapido_Click(object sender, RoutedEventArgs e)
    {
        var kits = DatabaseService.ObtenerKitsHerramientas();
        if (kits.Count == 0)
        {
            DialogoFluent.Show(this, "No hay maletines registrados.", "Sin maletines", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var kitEnObra = kits.FirstOrDefault(k => k.EnPrestamoCount > 0) ?? kits.First();
        MostrarModalChecklistRegresoKit(kitEnObra);
    }

    private void MostrarModalChecklistRegresoKit(KitHerramienta kitInicial, Window? ownerWin = null)
    {
        var kits = DatabaseService.ObtenerKitsHerramientas();
        var win = new Window
        {
            Title = $"Check-list Visual de Regreso de Maletín / Kit — {kitInicial.CodigoKit}",
            Width = 820,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = ownerWin ?? this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var cboKitSelector = new ComboBox
        {
            Width = 310,
            ItemsSource = kits,
            SelectedItem = kits.FirstOrDefault(k => k.Id == kitInicial.Id) ?? kits.FirstOrDefault()
        };
        var rightSel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightSel.Children.Add(new TextBlock { Text = "Maletín a revisar:", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        rightSel.Children.Add(cboKitSelector);
        DockPanel.SetDock(rightSel, Dock.Right);
        headerDock.Children.Add(rightSel);
        headerDock.Children.Add(new TextBlock
        {
            Text = "Check-list de Regreso de Maletín en 1 Clic",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        top.Children.Add(headerDock);

        var txtSubtitulo = new TextBlock
        {
            Text = "Desmarque únicamente la herramienta que NO haya regresado hoy (si se quedó en obra) o cambie su estado si regresó dañada/extraviada. Todo lo marcado como 'Bueno (Completo)' quedará Disponible de inmediato.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        top.Children.Add(txtSubtitulo);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false
        };
        dg.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Regresó OK",
            Binding = new System.Windows.Data.Binding("RegresoOk") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 90
        });
        dg.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("CodigoIndividual"), IsReadOnly = true, Width = 85 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Herramienta", Binding = new System.Windows.Data.Binding("HerramientaNombre"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Estado Actual", Binding = new System.Windows.Data.Binding("EstadoActual"), IsReadOnly = true, Width = 105 });

        var colEstadoRetorno = new DataGridComboBoxColumn
        {
            Header = "Resultado Revisión",
            SelectedItemBinding = new System.Windows.Data.Binding("EstadoRetornoElegido") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            ItemsSource = new[]
            {
                "Bueno (Completo)",
                "Se queda en obra (Continúa préstamo)",
                "Incompleto / Falta Pieza",
                "Dañado / Mantenimiento",
                "Extraviado / Baja"
            },
            Width = 205
        };
        dg.Columns.Add(colEstadoRetorno);
        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "Observaciones de la pieza",
            Binding = new System.Windows.Data.Binding("ObservacionesRetorno") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 160
        });

        var partidasRevision = new ObservableCollection<KitHerramientaPartida>();
        dg.ItemsSource = partidasRevision;

        void CargarPartidasDeKitSeleccionado()
        {
            partidasRevision.Clear();
            if (cboKitSelector.SelectedItem is KitHerramienta kSel)
            {
                foreach (var p in kSel.Partidas)
                {
                    p.RegresoOk = true;
                    p.EstadoRetornoElegido = "Bueno (Completo)";
                    partidasRevision.Add(p);
                }
            }
        }
        cboKitSelector.SelectionChanged += (_, _) => CargarPartidasDeKitSeleccionado();
        CargarPartidasDeKitSeleccionado();

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var txtObsGen = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Notas generales de la revisión del maletín...", Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnTodoOk = new System.Windows.Controls.Button { Content = "Marcar Todo OK", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 0) };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 0) };
        var btnConfirmar = new Wpf.Ui.Controls.Button { Content = "Confirmar Check-list de Regreso", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        rightBtns.Children.Add(btnTodoOk);
        rightBtns.Children.Add(btnCancel);
        rightBtns.Children.Add(btnConfirmar);
        DockPanel.SetDock(rightBtns, Dock.Right);
        bottom.Children.Add(rightBtns);
        bottom.Children.Add(txtObsGen);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(dg);

        btnTodoOk.Click += (_, _) =>
        {
            foreach (var p in partidasRevision)
                p.RegresoOk = true;
            dg.Items.Refresh();
        };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        (int ok, int siguen, int inc) resultado = (0, 0, 0);
        string codKitRevisado = kitInicial.CodigoKit;

        btnConfirmar.Click += (_, _) =>
        {
            if (cboKitSelector.SelectedItem is not KitHerramienta kSel) return;
            codKitRevisado = kSel.CodigoKit;
            resultado = DatabaseService.RegistrarChecklistRegresoKit(kSel.Id, partidasRevision.ToList(), txtObsGen.Text?.Trim() ?? string.Empty);
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true)
        {
            CargarTodoElSistema();
            DialogoFluent.Show(this,
                $"Check-list del maletín {codKitRevisado} aplicado:\n\n" +
                $"• Devueltas en buen estado (Disponibles): {resultado.ok}\n" +
                $"• Continúan en obra (En préstamo): {resultado.siguen}\n" +
                $"• Con incidencia (Dañadas/Extraviadas): {resultado.inc}",
                "Check-list de Maletín Completado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void MostrarModalGestorKitsHerramientas(int? kitInicialId = null)
    {
        var win = new Window
        {
            Title = "Maletines y Kits de Herramientas Operativos (KIT-xxxx) — Gestión y Despacho en 1 Clic",
            Width = 980,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });

        // Panel izquierdo: Lista de Kits + Alta/Edición de Kit
        var leftDock = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var leftTop = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var btnNuevoKit = new Wpf.Ui.Controls.Button { Content = "+ Nuevo Maletín", FontSize = 11.5, Padding = new Thickness(10, 4, 10, 4) };
        DockPanel.SetDock(btnNuevoKit, Dock.Right);
        leftTop.Children.Add(btnNuevoKit);
        leftTop.Children.Add(new TextBlock { Text = "Maletines / Kits (KIT-xxxx)", FontSize = 15.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(leftTop, Dock.Top);
        leftDock.Children.Add(leftTop);

        var bdrEditKit = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var spEdit = new StackPanel();
        spEdit.Children.Add(new TextBlock { Text = "Datos del Maletín Seleccionado", FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) });

        var g1 = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var txtCodigoKit = new Wpf.Ui.Controls.TextBox { PlaceholderText = "KIT-0001", FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 6, 0) };
        var txtNombreKit = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Nombre (ej. Maletín Eléctrico Cuadrilla 1)" };
        Grid.SetColumn(txtCodigoKit, 0);
        Grid.SetColumn(txtNombreKit, 1);
        g1.Children.Add(txtCodigoKit);
        g1.Children.Add(txtNombreKit);
        spEdit.Children.Add(g1);

        var g2 = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        var txtEspKit = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Especialidad (Eléctrica, Mecánica...)", Margin = new Thickness(0, 0, 6, 0) };
        var txtUbiKit = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ubicación (GABINETE-1)" };
        Grid.SetColumn(txtEspKit, 0);
        Grid.SetColumn(txtUbiKit, 1);
        g2.Children.Add(txtEspKit);
        g2.Children.Add(txtUbiKit);
        spEdit.Children.Add(g2);

        var gBtnsKit = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnEliminarKit = new System.Windows.Controls.Button { Content = "Eliminar Kit", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var btnGuardarKit = new Wpf.Ui.Controls.Button { Content = "Guardar Datos del Kit", Appearance = ControlAppearance.Primary, Padding = new Thickness(12, 4, 12, 4) };
        gBtnsKit.Children.Add(btnEliminarKit);
        gBtnsKit.Children.Add(btnGuardarKit);
        spEdit.Children.Add(gBtnsKit);
        bdrEditKit.Child = spEdit;
        DockPanel.SetDock(bdrEditKit, Dock.Bottom);
        leftDock.Children.Add(bdrEditKit);

        var dgKits = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        dgKits.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("CodigoKit"), Width = 85, ElementStyle = CeldaRecortable() });
        dgKits.Columns.Add(new DataGridTextColumn { Header = "Maletín", Binding = new System.Windows.Data.Binding("Nombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });
        dgKits.Columns.Add(new DataGridTextColumn { Header = "Piezas", Binding = new System.Windows.Data.Binding("DisponibilidadTexto"), Width = 75, ElementStyle = CeldaRecortable() });
        dgKits.Columns.Add(new DataGridTextColumn { Header = "Estatus / Resguardo", Binding = new System.Windows.Data.Binding("ResguardoMostrar"), Width = 145, ElementStyle = CeldaRecortable() });
        leftDock.Children.Add(dgKits);

        // Panel derecho: Herramientas dentro del Kit + Botones de Salida 1 Clic y Check-list Regreso
        var rightDock = new DockPanel { Margin = new Thickness(10, 0, 0, 0) };
        var rightTop = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var txtTituloContenido = new TextBlock { Text = "Herramientas asignadas al Maletín", FontSize = 15.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        rightTop.Children.Add(txtTituloContenido);

        var addHerGrid = new Grid();
        addHerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addHerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addHerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cboHerAgregar = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 6, 0) };
        var btnAgregarHerAKit = new Wpf.Ui.Controls.Button { Content = "+ Meter al Maletín", Appearance = ControlAppearance.Primary, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var btnQuitarHerDeKit = new System.Windows.Controls.Button { Content = "Sacar Seleccionada", Padding = new Thickness(10, 4, 10, 4) };
        Grid.SetColumn(cboHerAgregar, 0);
        Grid.SetColumn(btnAgregarHerAKit, 1);
        Grid.SetColumn(btnQuitarHerDeKit, 2);
        addHerGrid.Children.Add(cboHerAgregar);
        addHerGrid.Children.Add(btnAgregarHerAKit);
        addHerGrid.Children.Add(btnQuitarHerDeKit);
        rightTop.Children.Add(addHerGrid);
        DockPanel.SetDock(rightTop, Dock.Top);
        rightDock.Children.Add(rightTop);

        var rightBottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var btnChecklistReturn = new Wpf.Ui.Controls.Button { Content = "Check-list de Regreso (1 Clic)", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnIrAPrestarKit = new Wpf.Ui.Controls.Button { Content = "Prestar / Despachar este Maletín", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        rightBottom.Children.Add(btnChecklistReturn);
        rightBottom.Children.Add(btnIrAPrestarKit);
        DockPanel.SetDock(rightBottom, Dock.Bottom);
        rightDock.Children.Add(rightBottom);

        var dgPartidasKit = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        dgPartidasKit.Columns.Add(new DataGridTextColumn { Header = "Código HER", Binding = new System.Windows.Data.Binding("CodigoIndividual"), Width = 90, ElementStyle = CeldaRecortable() });
        dgPartidasKit.Columns.Add(new DataGridTextColumn { Header = "Herramienta", Binding = new System.Windows.Data.Binding("HerramientaNombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });
        dgPartidasKit.Columns.Add(new DataGridTextColumn { Header = "Marca / Modelo", Binding = new System.Windows.Data.Binding("MarcaModelo"), Width = 125, ElementStyle = CeldaRecortable() });
        dgPartidasKit.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new System.Windows.Data.Binding("EstadoActual"), Width = 95, ElementStyle = CeldaRecortable() });
        rightDock.Children.Add(dgPartidasKit);

        Grid.SetColumn(leftDock, 0);
        Grid.SetColumn(rightDock, 1);
        root.Children.Add(leftDock);
        root.Children.Add(rightDock);

        int kitEditandoId = 0;

        void RefrescarVistaKits(int? selId = null)
        {
            var listaKits = DatabaseService.ObtenerKitsHerramientas();
            dgKits.ItemsSource = listaKits;
            cboHerAgregar.ItemsSource = DatabaseService.ObtenerHerramientasIndividuales();

            int target = selId ?? kitEditandoId;
            var sel = listaKits.FirstOrDefault(k => k.Id == target) ?? listaKits.FirstOrDefault();
            if (sel != null)
                dgKits.SelectedItem = sel;
        }

        dgKits.SelectionChanged += (_, _) =>
        {
            if (dgKits.SelectedItem is KitHerramienta k)
            {
                kitEditandoId = k.Id;
                txtCodigoKit.Text = k.CodigoKit;
                txtNombreKit.Text = k.Nombre;
                txtEspKit.Text = k.Especialidad;
                txtUbiKit.Text = k.Ubicacion;
                txtTituloContenido.Text = $"Contenido de {k.CodigoKit} — {k.Nombre} ({k.Partidas.Count} piezas)";
                dgPartidasKit.ItemsSource = k.Partidas;
            }
        };

        btnNuevoKit.Click += (_, _) =>
        {
            kitEditandoId = 0;
            dgKits.SelectedIndex = -1;
            txtCodigoKit.Text = DatabaseService.GenerarSiguienteCodigoKit();
            txtNombreKit.Text = string.Empty;
            txtEspKit.Text = "General";
            txtUbiKit.Text = "GABINETE-1";
            dgPartidasKit.ItemsSource = null;
            txtNombreKit.Focus();
        };

        btnGuardarKit.Click += (_, _) =>
        {
            int idGuardado = DatabaseService.GuardarKitHerramienta(new KitHerramienta
            {
                Id = kitEditandoId,
                CodigoKit = txtCodigoKit.Text,
                Nombre = txtNombreKit.Text,
                Especialidad = txtEspKit.Text,
                Ubicacion = txtUbiKit.Text
            });
            RefrescarVistaKits(idGuardado);
            CargarTodoElSistema();
        };

        btnEliminarKit.Click += (_, _) =>
        {
            if (dgKits.SelectedItem is not KitHerramienta kSel) return;
            if (DialogoFluent.Show(win, $"¿Eliminar el maletín {kSel.CodigoKit} ({kSel.Nombre})? Sus herramientas individuales se conservarán en el catálogo.", "Eliminar Maletín", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                DatabaseService.EliminarKitHerramienta(kSel.Id);
                kitEditandoId = 0;
                RefrescarVistaKits();
            }
        };

        btnAgregarHerAKit.Click += (_, _) =>
        {
            if (dgKits.SelectedItem is not KitHerramienta kSel)
            {
                DialogoFluent.Show(win, "Seleccione o guarde primero un maletín a la izquierda.", "Seleccione maletín", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (cboHerAgregar.SelectedItem is not HerramientaActivo hSel)
            {
                DialogoFluent.Show(win, "Seleccione una herramienta individual (HER-xxxx) del combo para meterla al maletín.", "Seleccione herramienta", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DatabaseService.AgregarHerramientaAKit(kSel.Id, hSel.Id);
            RefrescarVistaKits(kSel.Id);
        };

        btnQuitarHerDeKit.Click += (_, _) =>
        {
            if (dgKits.SelectedItem is not KitHerramienta kSel || dgPartidasKit.SelectedItem is not KitHerramientaPartida pSel)
                return;
            DatabaseService.QuitarHerramientaDeKit(kSel.Id, pSel.HerramientaActivoId);
            RefrescarVistaKits(kSel.Id);
        };

        btnChecklistReturn.Click += (_, _) =>
        {
            if (dgKits.SelectedItem is not KitHerramienta kSel) return;
            MostrarModalChecklistRegresoKit(kSel, win);
            RefrescarVistaKits(kSel.Id);
        };

        btnIrAPrestarKit.Click += (_, _) =>
        {
            if (dgKits.SelectedItem is not KitHerramienta kSel) return;
            var todasHer = DatabaseService.ObtenerHerramientasIndividuales().ToDictionary(h => h.Id);
            int agregadas = 0;
            foreach (var part in kSel.Partidas)
            {
                if (todasHer.TryGetValue(part.HerramientaActivoId, out var hObj) &&
                    string.Equals(hObj.Estado, "Disponible", StringComparison.OrdinalIgnoreCase))
                {
                    AgregarHerramientaAListaPrestamo(hObj);
                    agregadas++;
                }
            }
            win.Close();
            CambiarSeccion(5);
            if (agregadas == 0)
            {
                DialogoFluent.Show(this, $"Las herramientas de {kSel.CodigoKit} ya están en préstamo. Puede usar 'Check-list Regreso de Kit' cuando vuelvan de obra.", "Maletín en obra", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        RefrescarVistaKits(kitInicialId);
        win.Content = root;
        win.ShowDialog();
    }

    // =========================================================================
    // FASE 3: FLOTILLA DE CAMIONETAS (VEH-xxxx), COSTO POR KILÓMETRO (CPK),
    //         GARANTÍAS VIGENTES Y COSTO TOTAL DE PROPIEDAD (TCO) DE EQUIPOS
    // =========================================================================

    private void BtnAbrirFlotillaYGarantias_Click(object sender, RoutedEventArgs e)
    {
        CambiarSeccion(9);
    }

    private void BtnAbrirGarantiasEquipos_Click(object sender, RoutedEventArgs e)
    {
        MostrarModalFlotillaYGarantias(1, null);
    }

    // =========================================================================
    // MÓDULO PRINCIPAL DE FLOTILLA (TAB 9): KPIS, MODALES DEDICADOS E IMPRESIÓN
    // =========================================================================

    private void RefrescarModuloFlotilla(int? vehiculoSeleccionarId = null)
    {
        if (DgFlotillaPrincipal == null) return;

        int? idPrevio = vehiculoSeleccionarId ?? (DgFlotillaPrincipal.SelectedItem as VehiculoFlotilla)?.Id;
        var vehiculos = DatabaseService.ObtenerVehiculosFlotilla();
        DgFlotillaPrincipal.ItemsSource = vehiculos;

        int totalKmRecorridos = vehiculos.Sum(v => v.KilometrosRecorridosPeriodo);
        decimal totalCombustible = vehiculos.Sum(v => v.GastoCombustibleAcumulado);
        decimal totalLitros = vehiculos.Sum(v => v.LitrosCombustibleAcumulados);
        decimal totalMantOtros = vehiculos.Sum(v => v.GastoMantenimientoAcumulado + v.GastoOtrosAcumulado);
        decimal totalGeneral = totalCombustible + totalMantOtros;

        decimal cpkPromedio = totalKmRecorridos > 0 ? Math.Round(totalGeneral / totalKmRecorridos, 2) : 0m;
        decimal rendPromedio = totalLitros > 0 && totalKmRecorridos > 0 ? Math.Round(totalKmRecorridos / totalLitros, 2) : 0m;

        if (TxtKpiFlotillaCpk != null)
            TxtKpiFlotillaCpk.Text = cpkPromedio > 0 ? $"${cpkPromedio:0.00} / km" : "S/D";
        if (TxtKpiFlotillaCpkSub != null)
            TxtKpiFlotillaCpkSub.Text = $"{totalKmRecorridos:N0} km recorridos en periodo";

        if (TxtKpiFlotillaCombustible != null)
            TxtKpiFlotillaCombustible.Text = $"{totalCombustible:C2}";
        if (TxtKpiFlotillaRendimientoSub != null)
            TxtKpiFlotillaRendimientoSub.Text = $"{(rendPromedio > 0 ? $"{rendPromedio:0.1} km/L prom." : "Sin rend.")} | {totalLitros:0.#} L cargados";

        if (TxtKpiFlotillaMantenimiento != null)
            TxtKpiFlotillaMantenimiento.Text = $"{totalMantOtros:C2}";
        if (TxtKpiFlotillaMantenimientoSub != null)
            TxtKpiFlotillaMantenimientoSub.Text = $"Gasto total acumulado: {totalGeneral:C2}";

        int activas = vehiculos.Count(v => v.Estatus.Contains("Activo", StringComparison.OrdinalIgnoreCase));
        int odometroTotal = vehiculos.Sum(v => v.KilometrajeActual);
        if (TxtKpiFlotillaUnidades != null)
            TxtKpiFlotillaUnidades.Text = $"{activas} de {vehiculos.Count} activas";
        if (TxtKpiFlotillaUnidadesSub != null)
            TxtKpiFlotillaUnidadesSub.Text = $"Odómetro acumulado: {odometroTotal:N0} km";

        var proxVeh = vehiculos
            .OrderBy(v => v.KmRestantesServicio)
            .FirstOrDefault();
        int enAlertaCount = vehiculos.Count(v => v.RequiereServicioPronto);
        if (TxtKpiFlotillaAlertasServicio != null)
        {
            TxtKpiFlotillaAlertasServicio.Text = enAlertaCount > 0
                ? $"{enAlertaCount} por atender"
                : (proxVeh != null ? $"{proxVeh.CodigoVehiculo} ({proxVeh.KmRestantesServicio:N0} km)" : "Al día");
        }
        if (TxtKpiFlotillaAlertasServicioSub != null)
        {
            TxtKpiFlotillaAlertasServicioSub.Text = proxVeh != null
                ? $"{proxVeh.CodigoVehiculo}: {proxVeh.EstimacionProximoServicioFechaTexto}"
                : "Sin unidades registradas";
        }

        var sel = idPrevio.HasValue
            ? vehiculos.FirstOrDefault(v => v.Id == idPrevio.Value) ?? vehiculos.FirstOrDefault()
            : vehiculos.FirstOrDefault();
        if (sel != null)
        {
            DgFlotillaPrincipal.SelectedItem = sel;
            ActualizarDetalleUnidadFlotillaSeleccionada(sel);
        }
        else
        {
            if (DgFlotillaGastosUnidad != null) DgFlotillaGastosUnidad.ItemsSource = null;
            if (DgFlotillaRevisionesUnidad != null) DgFlotillaRevisionesUnidad.ItemsSource = null;
        }
    }

    private void ActualizarDetalleUnidadFlotillaSeleccionada(VehiculoFlotilla v)
    {
        if (TxtTituloBitacoraGastosFlotilla != null)
        {
            TxtTituloBitacoraGastosFlotilla.Text =
                $"{v.CodigoVehiculo} — {v.MarcaModelo} ({v.Placas}) | CPK: {v.CostoPorKilometroTexto} | Rend: {v.RendimientoTexto} | Próx. Servicio: {v.EstimacionProximoServicioFechaTexto}";
        }
        if (DgFlotillaGastosUnidad != null)
            DgFlotillaGastosUnidad.ItemsSource = v.HistorialGastos;
        if (DgFlotillaRevisionesUnidad != null)
            DgFlotillaRevisionesUnidad.ItemsSource = v.HistorialRevisiones;
    }

    private void DgFlotillaPrincipal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgFlotillaPrincipal?.SelectedItem is VehiculoFlotilla v)
            ActualizarDetalleUnidadFlotillaSeleccionada(v);
    }

    private void DgFlotillaPrincipal_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DgFlotillaPrincipal?.SelectedItem is VehiculoFlotilla v)
            MostrarModalEquipoFlotilla(v);
    }

    private void BtnModalNuevaCamionetaFlotilla_Click(object sender, RoutedEventArgs e)
    {
        MostrarModalEquipoFlotilla(null);
    }

    private void BtnEditarCamionetaSeleccionadaFlotilla_Click(object sender, RoutedEventArgs e)
    {
        if (DgFlotillaPrincipal?.SelectedItem is VehiculoFlotilla v)
            MostrarModalEquipoFlotilla(v);
        else
            MostrarModalEquipoFlotilla(null);
    }

    private void BtnEliminarCamionetaSeleccionadaFlotilla_Click(object sender, RoutedEventArgs e)
    {
        if (DgFlotillaPrincipal?.SelectedItem is not VehiculoFlotilla vSel)
        {
            DialogoFluent.Show(this, "Seleccione primero una unidad de la tabla superior para eliminarla.", "Seleccionar unidad", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (DialogoFluent.Show(this, $"¿Eliminar la unidad {vSel.CodigoVehiculo} ({vSel.MarcaModelo}) junto con sus registros de gastos y revisiones?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes)
        {
            DatabaseService.EliminarVehiculoFlotilla(vSel.Id);
            CargarTodoElSistema();
            RefrescarModuloFlotilla();
        }
    }

    private void BtnEliminarGastoSeleccionadoFlotilla_Click(object sender, RoutedEventArgs e)
    {
        if (DgFlotillaGastosUnidad?.SelectedItem is not RegistroGastoVehiculo gSel)
        {
            DialogoFluent.Show(this, "Seleccione un registro en la bitácora de gastos para eliminarlo.", "Seleccionar gasto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (DialogoFluent.Show(this, $"¿Eliminar el registro de '{gSel.TipoGasto}' del {gSel.Fecha} por {gSel.MontoConIva:C2}?", "Eliminar gasto", MessageBoxButton.YesNo, MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
        {
            int vehId = gSel.VehiculoId;
            DatabaseService.EliminarGastoVehiculo(gSel.Id);
            CargarTodoElSistema();
            RefrescarModuloFlotilla(vehId);
        }
    }

    private void BtnEliminarRevisionSemanalFlotilla_Click(object sender, RoutedEventArgs e)
    {
        if (DgFlotillaRevisionesUnidad?.SelectedItem is not RevisionSemanalVehiculo revSel)
        {
            DialogoFluent.Show(this, "Seleccione una revisión semanal de la tabla derecha para eliminarla.", "Seleccionar revisión", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (DialogoFluent.Show(this, $"¿Eliminar la revisión semanal {revSel.FolioRevision} del {revSel.Fecha}?", "Eliminar revisión", MessageBoxButton.YesNo, MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
        {
            int vehId = revSel.VehiculoId;
            DatabaseService.EliminarRevisionSemanalVehiculo(revSel.Id);
            CargarTodoElSistema();
            RefrescarModuloFlotilla(vehId);
        }
    }

    private void BtnModalCargaGasolinaFlotilla_Click(object sender, RoutedEventArgs e)
    {
        var vSel = DgFlotillaPrincipal?.SelectedItem as VehiculoFlotilla;
        MostrarModalCargaGasolinaFlotilla(vSel);
    }

    private void BtnModalGastoTallerFlotilla_Click(object sender, RoutedEventArgs e)
    {
        var vSel = DgFlotillaPrincipal?.SelectedItem as VehiculoFlotilla;
        MostrarModalGastoNoGasolinaFlotilla(vSel);
    }

    private void BtnModalChecklistSemanalFlotilla_Click(object sender, RoutedEventArgs e)
    {
        var vSel = DgFlotillaPrincipal?.SelectedItem as VehiculoFlotilla;
        MostrarModalChecklistSemanalFlotilla(vSel);
    }

    private void BtnImprimirChecklistSemanalFlotilla_Click(object sender, RoutedEventArgs e)
    {
        var vSel = DgFlotillaPrincipal?.SelectedItem as VehiculoFlotilla
                   ?? DatabaseService.ObtenerVehiculosFlotilla().FirstOrDefault();
        if (vSel == null)
        {
            DialogoFluent.Show(this, "Primero registre una camioneta en la flotilla.", "Sin unidades", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ultRev = vSel.HistorialRevisiones.FirstOrDefault();
        BarcodePrintService.MostrarVistaPreviaEImprimirRevisionSemanalVehiculo(this, vSel, ultRev);
    }

    private void BtnReimprimirRevisionSemanalFlotilla_Click(object sender, RoutedEventArgs e)
    {
        if (DgFlotillaPrincipal?.SelectedItem is not VehiculoFlotilla vSel) return;
        var rev = DgFlotillaRevisionesUnidad?.SelectedItem as RevisionSemanalVehiculo
                  ?? vSel.HistorialRevisiones.FirstOrDefault();
        BarcodePrintService.MostrarVistaPreviaEImprimirRevisionSemanalVehiculo(this, vSel, rev);
    }

    private void DgFlotillaRevisionesUnidad_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DgFlotillaPrincipal?.SelectedItem is VehiculoFlotilla vSel &&
            DgFlotillaRevisionesUnidad?.SelectedItem is RevisionSemanalVehiculo rev)
        {
            BarcodePrintService.MostrarVistaPreviaEImprimirRevisionSemanalVehiculo(this, vSel, rev);
        }
    }

    // -------------------------------------------------------------------------
    // MODAL 1: CREAR / EDITAR EQUIPO O CAMIONETA DE FLOTILLA (CON CONFIGURACIÓN DE INTERVALO DE SERVICIO)
    // -------------------------------------------------------------------------
    private void MostrarModalEquipoFlotilla(VehiculoFlotilla? existente)
    {
        bool esEdicion = existente != null && existente.Id > 0;
        var win = new Window
        {
            Title = esEdicion ? $"Editar Camioneta / Equipo ({existente!.CodigoVehiculo})" : "Registrar Nueva Camioneta / Equipo en Flotilla",
            Width = 620,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = esEdicion ? $"Configuración y Ficha Técnica — {existente!.CodigoVehiculo}" : "Alta de Nueva Unidad / Camioneta en Flotilla",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Configure el kilometraje actual y cada cuántos kilómetros requiere servicio preventivo para estimar automáticamente la fecha del siguiente mantenimiento según su consumo y rodaje.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var g1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtCodigo = new Wpf.Ui.Controls.TextBox { Text = esEdicion ? existente!.CodigoVehiculo : DatabaseService.GenerarSiguienteCodigoVehiculo(), FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 6, 0) };
        var txtPlacas = new Wpf.Ui.Controls.TextBox { Text = existente?.Placas ?? "", PlaceholderText = "Placas (ej. JKL-4821)", Margin = new Thickness(0, 0, 6, 0) };
        var txtAnio = new Wpf.Ui.Controls.TextBox { Text = (existente?.Anio ?? DateTime.Today.Year).ToString(), PlaceholderText = "Año", Margin = new Thickness(0, 0, 6, 0) };
        var cboEstatus = new ComboBox();
        cboEstatus.Items.Add("Activo en Operación");
        cboEstatus.Items.Add("En Taller / Servicio");
        cboEstatus.Items.Add("Fuera de Servicio");
        cboEstatus.SelectedItem = existente?.Estatus ?? "Activo en Operación";
        if (cboEstatus.SelectedIndex < 0) cboEstatus.SelectedIndex = 0;

        var fCod = CrearCampoConEtiqueta("Código Interno", txtCodigo); Grid.SetColumn(fCod, 0); g1.Children.Add(fCod);
        var fPla = CrearCampoConEtiqueta("Placas", txtPlacas); Grid.SetColumn(fPla, 1); g1.Children.Add(fPla);
        var fAni = CrearCampoConEtiqueta("Año Modelo", txtAnio); Grid.SetColumn(fAni, 2); g1.Children.Add(fAni);
        var fEst = CrearCampoConEtiqueta("Estatus Operativo", cboEstatus); Grid.SetColumn(fEst, 3); g1.Children.Add(fEst);
        root.Children.Add(g1);

        var g2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var txtMarcaModelo = new Wpf.Ui.Controls.TextBox { Text = existente?.MarcaModelo ?? "", PlaceholderText = "Ej. Nissan NP300 Estacas / Hilux / Tornado", Margin = new Thickness(0, 0, 6, 0) };
        var txtResponsable = new Wpf.Ui.Controls.TextBox { Text = existente?.ResponsableHabitual ?? "", PlaceholderText = "Conductor o Cuadrilla asignada" };
        var fMod = CrearCampoConEtiqueta("Marca, Línea y Tipo de Unidad", txtMarcaModelo); Grid.SetColumn(fMod, 0); g2.Children.Add(fMod);
        var fRes = CrearCampoConEtiqueta("Conductor / Cuadrilla Habitual", txtResponsable); Grid.SetColumn(fRes, 1); g2.Children.Add(fRes);
        root.Children.Add(g2);

        var g3 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtKmInicial = new Wpf.Ui.Controls.TextBox { Text = (existente?.KilometrajeInicial ?? 0).ToString(), Margin = new Thickness(0, 0, 6, 0) };
        var txtKmActual = new Wpf.Ui.Controls.TextBox { Text = (existente?.KilometrajeActual ?? 0).ToString(), Margin = new Thickness(0, 0, 6, 0) };
        var txtIntervaloKm = new Wpf.Ui.Controls.TextBox { Text = (existente?.IntervaloEfectivoKm ?? 10000).ToString(), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Cada cuántos kilómetros se realiza el servicio preventivo (ej. 5000, 10000, 15000)" };
        var txtProxServKm = new Wpf.Ui.Controls.TextBox { Text = (existente?.MetaServicioCalculadaKm ?? 10000).ToString(), ToolTip = "Kilometraje meta para el siguiente mantenimiento" };

        var fKmi = CrearCampoConEtiqueta("Km Inicial Base", txtKmInicial); Grid.SetColumn(fKmi, 0); g3.Children.Add(fKmi);
        var fKma = CrearCampoConEtiqueta("Odómetro Actual (km)", txtKmActual); Grid.SetColumn(fKma, 1); g3.Children.Add(fKma);
        var fInt = CrearCampoConEtiqueta("Servicio cada (km)", txtIntervaloKm); Grid.SetColumn(fInt, 2); g3.Children.Add(fInt);
        var fPrx = CrearCampoConEtiqueta("Próximo Servicio (km)", txtProxServKm); Grid.SetColumn(fPrx, 3); g3.Children.Add(fPrx);
        root.Children.Add(g3);

        void RecalcularMetaServicioSugerida()
        {
            if (int.TryParse(txtKmActual.Text, out int kma) && int.TryParse(txtIntervaloKm.Text, out int interv) && interv >= 1000)
            {
                int baseServ = existente?.UltimoServicioKm > 0 ? existente.UltimoServicioKm : kma;
                int sugerido = baseServ + interv;
                if (sugerido <= kma)
                    sugerido = kma + interv;
                txtProxServKm.Text = sugerido.ToString();
            }
        }
        txtIntervaloKm.TextChanged += (_, _) => RecalcularMetaServicioSugerida();

        var g4 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cboCombustible = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
        cboCombustible.Items.Add("Gasolina Regular");
        cboCombustible.Items.Add("Gasolina Premium");
        cboCombustible.Items.Add("Diésel");
        cboCombustible.SelectedItem = existente?.TipoCombustible ?? "Gasolina Regular";
        if (cboCombustible.SelectedIndex < 0) cboCombustible.SelectedIndex = 0;

        var txtCapTanque = new Wpf.Ui.Controls.TextBox { Text = (existente?.CapacidadTanqueLitros ?? 60m).ToString("0.#", CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 6, 0) };
        var txtVigSeguro = new Wpf.Ui.Controls.TextBox { Text = string.IsNullOrWhiteSpace(existente?.VigenciaSeguro) ? DateTime.Today.AddYears(1).ToString("yyyy-MM-dd") : existente!.VigenciaSeguro, PlaceholderText = "yyyy-MM-dd" };

        var fTcb = CrearCampoConEtiqueta("Tipo de Combustible", cboCombustible); Grid.SetColumn(fTcb, 0); g4.Children.Add(fTcb);
        var fCap = CrearCampoConEtiqueta("Capacidad Tanque (Litros)", txtCapTanque); Grid.SetColumn(fCap, 1); g4.Children.Add(fCap);
        var fSeg = CrearCampoConEtiqueta("Vigencia Póliza Seguro", txtVigSeguro); Grid.SetColumn(fSeg, 2); g4.Children.Add(fSeg);
        root.Children.Add(g4);

        var txtNotas = new Wpf.Ui.Controls.TextBox
        {
            Text = existente?.Notas ?? "",
            PlaceholderText = "Notas adicionales (póliza, número de serie VIN, equipamiento, tarjeta de circulación)...",
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(CrearCampoConEtiqueta("Notas y Observaciones de la Unidad", txtNotas));

        var pnlBtns = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (esEdicion)
        {
            var btnEliminar = new Wpf.Ui.Controls.Button
            {
                Content = "Eliminar Unidad",
                Padding = new Thickness(12, 6, 12, 6)
            };
            btnEliminar.Click += (_, _) =>
            {
                if (DialogoFluent.Show(win, $"¿Eliminar la unidad {existente!.CodigoVehiculo} ({existente.MarcaModelo}) y su bitácora?", "Confirmar baja", MessageBoxButton.YesNo, MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes)
                {
                    DatabaseService.EliminarVehiculoFlotilla(existente.Id);
                    win.DialogResult = true;
                }
            };
            DockPanel.SetDock(btnEliminar, Dock.Left);
            pnlBtns.Children.Add(btnEliminar);
        }

        var spRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Wpf.Ui.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = esEdicion ? "Guardar Cambios" : "Crear Unidad en Flotilla", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        int idGuardado = existente?.Id ?? 0;
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtMarcaModelo.Text))
            {
                DialogoFluent.Show(win, "Indique la marca y modelo de la camioneta.", "Dato requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int.TryParse(txtAnio.Text, out int anio);
            int.TryParse(txtKmInicial.Text, out int kmi);
            int.TryParse(txtKmActual.Text, out int kma);
            int.TryParse(txtIntervaloKm.Text, out int interv);
            int.TryParse(txtProxServKm.Text, out int kmProx);
            decimal capTanque = ParseDecimal(txtCapTanque.Text, 60m);

            idGuardado = DatabaseService.GuardarVehiculoFlotilla(new VehiculoFlotilla
            {
                Id = existente?.Id ?? 0,
                CodigoVehiculo = txtCodigo.Text,
                Placas = txtPlacas.Text,
                MarcaModelo = txtMarcaModelo.Text,
                Anio = anio > 1990 ? anio : DateTime.Today.Year,
                ResponsableHabitual = txtResponsable.Text,
                KilometrajeInicial = kmi,
                KilometrajeActual = Math.Max(kmi, kma),
                IntervaloServicioKm = interv > 0 ? interv : 10000,
                UltimoServicioKm = existente?.UltimoServicioKm > 0 ? existente.UltimoServicioKm : kmi,
                ProximoServicioKm = kmProx > 0 ? kmProx : (Math.Max(kmi, kma) + (interv > 0 ? interv : 10000)),
                CapacidadTanqueLitros = capTanque,
                TipoCombustible = cboCombustible.SelectedItem?.ToString() ?? "Gasolina Regular",
                VigenciaSeguro = txtVigSeguro.Text,
                Estatus = cboEstatus.SelectedItem?.ToString() ?? "Activo en Operación",
                Notas = txtNotas.Text
            });
            win.DialogResult = true;
        };
        spRight.Children.Add(btnCancel);
        spRight.Children.Add(btnSave);
        pnlBtns.Children.Add(spRight);
        root.Children.Add(pnlBtns);

        win.Content = root;
        if (win.ShowDialog() == true)
        {
            CargarTodoElSistema();
            RefrescarModuloFlotilla(idGuardado);
            MostrarToastDiscreto(esEdicion ? "Datos de la unidad actualizados." : "Nueva unidad agregada a la flotilla.", "Flotilla Vehicular");
        }
    }

    // -------------------------------------------------------------------------
    // MODAL 2: REGISTRAR CARGA DE GASOLINA / COMBUSTIBLE (CON LITROS, RENDIMIENTO Y ESTIMACIÓN DE SERVICIO)
    // -------------------------------------------------------------------------
    private void MostrarModalCargaGasolinaFlotilla(VehiculoFlotilla? preseleccionado)
    {
        var vehiculos = DatabaseService.ObtenerVehiculosFlotilla();
        if (vehiculos.Count == 0)
        {
            DialogoFluent.Show(this, "Primero dé de alta una camioneta en la flotilla.", "Sin unidades", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = "Registrar Carga de Gasolina / Combustible y Odómetro",
            Width = 580,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Registro de Carga de Gasolina / Combustible",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Capture los litros cargados, importe y kilometraje actual en tablero. El sistema actualizará el Costo por Kilómetro (CPK), rendimiento km/L y la estimación del siguiente servicio.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var cboVehiculo = new ComboBox { ItemsSource = vehiculos, Margin = new Thickness(0, 0, 0, 8) };
        cboVehiculo.SelectedItem = preseleccionado != null
            ? vehiculos.FirstOrDefault(v => v.Id == preseleccionado.Id) ?? vehiculos[0]
            : vehiculos[0];
        root.Children.Add(CrearCampoConEtiqueta("Camioneta / Unidad", cboVehiculo));

        var g1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtFecha = new Wpf.Ui.Controls.TextBox { Text = DateTime.Today.ToString("yyyy-MM-dd"), Margin = new Thickness(0, 0, 6, 0) };
        var txtKmActual = new Wpf.Ui.Controls.TextBox { Text = ((cboVehiculo.SelectedItem as VehiculoFlotilla)?.KilometrajeActual ?? 0).ToString(), Margin = new Thickness(0, 0, 6, 0) };
        var chkTanqueLleno = new CheckBox { Content = "Tanque Lleno", IsChecked = true, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 0, 6) };

        var fFec = CrearCampoConEtiqueta("Fecha de Carga", txtFecha); Grid.SetColumn(fFec, 0); g1.Children.Add(fFec);
        var fKm = CrearCampoConEtiqueta("Odómetro en Tablero (km)", txtKmActual); Grid.SetColumn(fKm, 1); g1.Children.Add(fKm);
        Grid.SetColumn(chkTanqueLleno, 2); g1.Children.Add(chkTanqueLleno);
        root.Children.Add(g1);

        var g2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtLitros = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. 52.5", Margin = new Thickness(0, 0, 6, 0) };
        var txtPrecioLitro = new Wpf.Ui.Controls.TextBox { Text = "24.80", Margin = new Thickness(0, 0, 6, 0) };
        var txtMontoTotal = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Total $ c/IVA" };

        var fLit = CrearCampoConEtiqueta("Litros Cargados (L)", txtLitros); Grid.SetColumn(fLit, 0); g2.Children.Add(fLit);
        var fPre = CrearCampoConEtiqueta("Precio por Litro ($)", txtPrecioLitro); Grid.SetColumn(fPre, 1); g2.Children.Add(fPre);
        var fMon = CrearCampoConEtiqueta("Importe Total ($ c/IVA)", txtMontoTotal); Grid.SetColumn(fMon, 2); g2.Children.Add(fMon);
        root.Children.Add(g2);

        bool calculandoComb = false;
        var lblPronosticoServicio = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("AccentTextFillColorPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        };

        void ActualizarCalculoCombustible()
        {
            if (calculandoComb) return;
            calculandoComb = true;
            try
            {
                decimal lit = ParseDecimal(txtLitros.Text, 0m);
                decimal pL = ParseDecimal(txtPrecioLitro.Text, 0m);
                if (lit > 0 && pL > 0)
                {
                    txtMontoTotal.Text = Math.Round(lit * pL, 2).ToString("0.00", CultureInfo.InvariantCulture);
                }

                if (cboVehiculo.SelectedItem is VehiculoFlotilla v)
                {
                    int.TryParse(txtKmActual.Text, out int kmNuevo);
                    int kmEf = Math.Max(v.KilometrajeActual, kmNuevo);
                    int rest = v.MetaServicioCalculadaKm - kmEf;
                    int deltaHoy = Math.Max(0, kmNuevo - v.KilometrajeActual);
                    string rendCarga = (deltaHoy > 0 && lit > 0) ? $" | Rendimiento tramo: {(deltaHoy / lit):0.1} km/L" : "";
                    lblPronosticoServicio.Text = rest <= 0
                        ? $"¡Atención! Con {kmEf:N0} km el servicio preventivo ya está vencido (Meta: {v.MetaServicioCalculadaKm:N0} km){rendCarga}."
                        : $"Faltan {rest:N0} km para el próximo servicio ({v.MetaServicioCalculadaKm:N0} km, cada {v.IntervaloEfectivoKm:N0} km) — Estimado: {v.EstimacionProximoServicioFechaTexto}{rendCarga}";
                }
            }
            finally
            {
                calculandoComb = false;
            }
        }

        txtLitros.TextChanged += (_, _) => ActualizarCalculoCombustible();
        txtPrecioLitro.TextChanged += (_, _) => ActualizarCalculoCombustible();
        txtKmActual.TextChanged += (_, _) => ActualizarCalculoCombustible();
        cboVehiculo.SelectionChanged += (_, _) =>
        {
            if (cboVehiculo.SelectedItem is VehiculoFlotilla v)
            {
                txtKmActual.Text = v.KilometrajeActual.ToString();
                ActualizarCalculoCombustible();
            }
        };
        ActualizarCalculoCombustible();

        var g3 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        g3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var txtGasolinera = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ej. Estación Pemex / Mobil / Oxxo Gas", Margin = new Thickness(0, 0, 6, 0) };
        var txtTicketFactura = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Folio Ticket o Factura CFDI" };
        var fGas = CrearCampoConEtiqueta("Gasolinera / Estación", txtGasolinera); Grid.SetColumn(fGas, 0); g3.Children.Add(fGas);
        var fFol = CrearCampoConEtiqueta("Folio Ticket / Factura", txtTicketFactura); Grid.SetColumn(fFol, 1); g3.Children.Add(fFol);
        root.Children.Add(g3);

        var txtNotas = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Ruta o proyecto atendido (ej. Ruta Obras Norte / Recolección proveedores)...", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(CrearCampoConEtiqueta("Observaciones / Ruta", txtNotas));
        root.Children.Add(lblPronosticoServicio);

        var spBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        var btnCancel = new Wpf.Ui.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Guardar Carga de Gasolina", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        int idVehGuardado = 0;
        btnSave.Click += (_, _) =>
        {
            if (cboVehiculo.SelectedItem is not VehiculoFlotilla vSel) return;
            decimal monto = ParseDecimal(txtMontoTotal.Text, 0m);
            decimal litros = ParseDecimal(txtLitros.Text, 0m);
            decimal precioL = ParseDecimal(txtPrecioLitro.Text, 0m);
            if (monto <= 0m && litros > 0m && precioL > 0m)
                monto = Math.Round(litros * precioL, 2);

            if (monto <= 0m)
            {
                DialogoFluent.Show(win, "Ingrese el importe total o los litros cargados.", "Importe requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int.TryParse(txtKmActual.Text, out int kmReg);
            idVehGuardado = vSel.Id;

            DatabaseService.RegistrarGastoVehiculo(new RegistroGastoVehiculo
            {
                VehiculoId = vSel.Id,
                Fecha = txtFecha.Text,
                TipoGasto = "Gasolina / Combustible",
                KilometrajeRegistro = kmReg > 0 ? kmReg : vSel.KilometrajeActual,
                LitrosCargados = litros,
                PrecioPorLitro = precioL,
                TanqueLleno = chkTanqueLleno.IsChecked == true,
                MontoConIva = monto,
                ProveedorTaller = txtGasolinera.Text,
                FolioReferencia = txtTicketFactura.Text,
                Descripcion = string.IsNullOrWhiteSpace(txtNotas.Text) ? $"Carga de combustible ({litros:0.##} L)" : txtNotas.Text
            });
            win.DialogResult = true;
        };
        spBtns.Children.Add(btnCancel);
        spBtns.Children.Add(btnSave);
        root.Children.Add(spBtns);

        win.Content = root;
        if (win.ShowDialog() == true)
        {
            CargarTodoElSistema();
            RefrescarModuloFlotilla(idVehGuardado);
        }
    }

    // -------------------------------------------------------------------------
    // MODAL 3: REGISTRAR GASTO QUE NO SEA GASOLINA (MANTENIMIENTO, TALLER, LLANTAS, SEGURO, PEAJES)
    // -------------------------------------------------------------------------
    private void MostrarModalGastoNoGasolinaFlotilla(VehiculoFlotilla? preseleccionado)
    {
        var vehiculos = DatabaseService.ObtenerVehiculosFlotilla();
        if (vehiculos.Count == 0)
        {
            DialogoFluent.Show(this, "Primero dé de alta una camioneta en la flotilla.", "Sin unidades", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = "Registrar Gasto de Taller, Mantenimiento, Llantas o Peajes",
            Width = 580,
            Height = 510,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Registro de Gasto Vehicular (Taller, Mantenimiento, Refacciones u Otros)",
            FontSize = 15.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Si registra un 'Mantenimiento Preventivo', el sistema actualizará automáticamente la meta del siguiente servicio sumando el intervalo configurado de la unidad.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var cboVehiculo = new ComboBox { ItemsSource = vehiculos, Margin = new Thickness(0, 0, 0, 8) };
        cboVehiculo.SelectedItem = preseleccionado != null
            ? vehiculos.FirstOrDefault(v => v.Id == preseleccionado.Id) ?? vehiculos[0]
            : vehiculos[0];
        root.Children.Add(CrearCampoConEtiqueta("Camioneta / Unidad", cboVehiculo));

        var g1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cboCategoria = new ComboBox { Margin = new Thickness(0, 0, 6, 0), SelectedIndex = 0 };
        cboCategoria.Items.Add("Mantenimiento Preventivo (Afinación / Aceite)");
        cboCategoria.Items.Add("Reparación Mecánica / Taller");
        cboCategoria.Items.Add("Llantas, Alineación y Balanceo");
        cboCategoria.Items.Add("Batería / Sistema Eléctrico / Refacciones");
        cboCategoria.Items.Add("Seguro / Verificación / Tenencia");
        cboCategoria.Items.Add("Casetas / Peajes / Estacionamiento");

        var txtFecha = new Wpf.Ui.Controls.TextBox { Text = DateTime.Today.ToString("yyyy-MM-dd"), Margin = new Thickness(0, 0, 6, 0) };
        var txtKmActual = new Wpf.Ui.Controls.TextBox { Text = ((cboVehiculo.SelectedItem as VehiculoFlotilla)?.KilometrajeActual ?? 0).ToString() };

        var fCat = CrearCampoConEtiqueta("Rubro del Gasto", cboCategoria); Grid.SetColumn(fCat, 0); g1.Children.Add(fCat);
        var fFec = CrearCampoConEtiqueta("Fecha", txtFecha); Grid.SetColumn(fFec, 1); g1.Children.Add(fFec);
        var fKm = CrearCampoConEtiqueta("Odómetro en el Servicio (km)", txtKmActual); Grid.SetColumn(fKm, 2); g1.Children.Add(fKm);
        root.Children.Add(g1);

        var g2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtMonto = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Importe $ c/IVA", Margin = new Thickness(0, 0, 6, 0) };
        var txtProveedorTaller = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Nombre del Taller / Proveedor", Margin = new Thickness(0, 0, 6, 0) };
        var txtFolioRef = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Factura XML / OC / Nota" };

        var fMon = CrearCampoConEtiqueta("Monto con IVA ($)", txtMonto); Grid.SetColumn(fMon, 0); g2.Children.Add(fMon);
        var fPrv = CrearCampoConEtiqueta("Taller / Proveedor / Aseguradora", txtProveedorTaller); Grid.SetColumn(fPrv, 1); g2.Children.Add(fPrv);
        var fFol = CrearCampoConEtiqueta("Folio Factura / Nota", txtFolioRef); Grid.SetColumn(fFol, 2); g2.Children.Add(fFol);
        root.Children.Add(g2);

        cboVehiculo.SelectionChanged += (_, _) =>
        {
            if (cboVehiculo.SelectedItem is VehiculoFlotilla v)
                txtKmActual.Text = v.KilometrajeActual.ToString();
        };

        var txtDescripcion = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Detalle de trabajos realizados o refacciones instaladas (ej. Cambio de aceite sintético, filtros y balatas delanteras)...",
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(CrearCampoConEtiqueta("Descripción Detallada del Servicio o Gasto", txtDescripcion));

        var spBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Wpf.Ui.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Registrar Gasto", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        int idVehGuardado = 0;
        btnSave.Click += (_, _) =>
        {
            if (cboVehiculo.SelectedItem is not VehiculoFlotilla vSel) return;
            decimal monto = ParseDecimal(txtMonto.Text, 0m);
            if (monto <= 0m)
            {
                DialogoFluent.Show(win, "Ingrese un monto válido mayor a 0.", "Monto requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int.TryParse(txtKmActual.Text, out int kmReg);
            idVehGuardado = vSel.Id;

            DatabaseService.RegistrarGastoVehiculo(new RegistroGastoVehiculo
            {
                VehiculoId = vSel.Id,
                Fecha = txtFecha.Text,
                TipoGasto = cboCategoria.SelectedItem?.ToString() ?? "Reparación Mecánica / Taller",
                KilometrajeRegistro = kmReg > 0 ? kmReg : vSel.KilometrajeActual,
                MontoConIva = monto,
                ProveedorTaller = txtProveedorTaller.Text,
                FolioReferencia = txtFolioRef.Text,
                Descripcion = string.IsNullOrWhiteSpace(txtDescripcion.Text) ? (cboCategoria.SelectedItem?.ToString() ?? "Gasto vehicular") : txtDescripcion.Text
            });
            win.DialogResult = true;
        };
        spBtns.Children.Add(btnCancel);
        spBtns.Children.Add(btnSave);
        root.Children.Add(spBtns);

        win.Content = root;
        if (win.ShowDialog() == true)
        {
            CargarTodoElSistema();
            RefrescarModuloFlotilla(idVehGuardado);
            MostrarToastDiscreto("Gasto vehicular registrado y Costo por Km (CPK) recalculado.", "Taller y Mantenimiento");
        }
    }

    // -------------------------------------------------------------------------
    // MODAL 4: REGISTRO SEMANAL DE ESTADO DE CAMIONETA + KILOMETRAJE + IMPRIMIBLE CARTA
    // -------------------------------------------------------------------------
    private void MostrarModalChecklistSemanalFlotilla(VehiculoFlotilla? preseleccionado)
    {
        var vehiculos = DatabaseService.ObtenerVehiculosFlotilla();
        if (vehiculos.Count == 0)
        {
            DialogoFluent.Show(this, "Primero dé de alta una camioneta en la flotilla.", "Sin unidades", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new Window
        {
            Title = "Check-list Semanal de Estado de Camioneta y Registro de Odómetro",
            Width = 620,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Registro Semanal de Estado Físico y Kilometraje de Camioneta",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Capture el odómetro semanal y el estado físico de la unidad. Al guardar podrá generar la Hoja Tamaño Carta Imprimible con código de barras para firma del responsable.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var cboVehiculo = new ComboBox { ItemsSource = vehiculos, Margin = new Thickness(0, 0, 0, 8) };
        cboVehiculo.SelectedItem = preseleccionado != null
            ? vehiculos.FirstOrDefault(v => v.Id == preseleccionado.Id) ?? vehiculos[0]
            : vehiculos[0];
        root.Children.Add(CrearCampoConEtiqueta("Camioneta Inspeccionada", cboVehiculo));

        var g1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        var vInit = cboVehiculo.SelectedItem as VehiculoFlotilla;
        var txtKmSemanal = new Wpf.Ui.Controls.TextBox { Text = (vInit?.KilometrajeActual ?? 0).ToString(), Margin = new Thickness(0, 0, 6, 0) };
        var cboNivelTanque = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
        cboNivelTanque.Items.Add("Tanque Lleno");
        cboNivelTanque.Items.Add("3/4 Tanque");
        cboNivelTanque.Items.Add("1/2 Tanque");
        cboNivelTanque.Items.Add("1/4 Tanque");
        cboNivelTanque.Items.Add("Reserva (Cargar hoy)");
        cboNivelTanque.SelectedIndex = 1;

        var txtInspector = new Wpf.Ui.Controls.TextBox { Text = vInit?.ResponsableHabitual ?? "", PlaceholderText = "Conductor / Responsable" };

        var fKm = CrearCampoConEtiqueta("Odómetro Semanal (km)", txtKmSemanal); Grid.SetColumn(fKm, 0); g1.Children.Add(fKm);
        var fTan = CrearCampoConEtiqueta("Nivel de Combustible", cboNivelTanque); Grid.SetColumn(fTan, 1); g1.Children.Add(fTan);
        var fIns = CrearCampoConEtiqueta("Conductor / Inspector", txtInspector); Grid.SetColumn(fIns, 2); g1.Children.Add(fIns);
        root.Children.Add(g1);

        var g2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ComboBox CrearComboEstadoCheck(Thickness margin)
        {
            var cb = new ComboBox { Margin = margin };
            cb.Items.Add("OK");
            cb.Items.Add("Atención Pronto");
            cb.Items.Add("Urgente / Dañado");
            cb.SelectedIndex = 0;
            return cb;
        }

        var cboLlantas = CrearComboEstadoCheck(new Thickness(0, 0, 6, 0));
        var cboFluidos = CrearComboEstadoCheck(new Thickness(0, 0, 6, 0));
        var cboFrenosLuces = CrearComboEstadoCheck(new Thickness(0, 0, 6, 0));
        var cboCarroceria = CrearComboEstadoCheck(new Thickness(0));

        var fLla = CrearCampoConEtiqueta("Llantas y Refacción", cboLlantas); Grid.SetColumn(fLla, 0); g2.Children.Add(fLla);
        var fFlu = CrearCampoConEtiqueta("Aceite y Fluidos", cboFluidos); Grid.SetColumn(fFlu, 1); g2.Children.Add(fFlu);
        var fFre = CrearCampoConEtiqueta("Frenos y Luces", cboFrenosLuces); Grid.SetColumn(fFre, 2); g2.Children.Add(fFre);
        var fCar = CrearCampoConEtiqueta("Carrocería y Orden", cboCarroceria); Grid.SetColumn(fCar, 3); g2.Children.Add(fCar);
        root.Children.Add(g2);

        var lblEstimacionServicio = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tk("AccentTextFillColorPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        };

        void ActualizarPronosticoChecklist()
        {
            if (cboVehiculo.SelectedItem is VehiculoFlotilla v)
            {
                int.TryParse(txtKmSemanal.Text, out int kmCap);
                int kmEf = Math.Max(v.KilometrajeActual, kmCap);
                int rest = v.MetaServicioCalculadaKm - kmEf;
                lblEstimacionServicio.Text = rest <= 0
                    ? $"ALERTA DE MANTENIMIENTO: Con {kmEf:N0} km el servicio preventivo ya está vencido por {Math.Abs(rest):N0} km (Meta: {v.MetaServicioCalculadaKm:N0} km)."
                    : $"Con este kilometraje ({kmEf:N0} km), faltan {rest:N0} km para el próximo servicio (configurado cada {v.IntervaloEfectivoKm:N0} km). Fecha estimada según ritmo de uso: {v.EstimacionProximoServicioFechaTexto}.";
            }
        }

        cboVehiculo.SelectionChanged += (_, _) =>
        {
            if (cboVehiculo.SelectedItem is VehiculoFlotilla v)
            {
                txtKmSemanal.Text = v.KilometrajeActual.ToString();
                txtInspector.Text = v.ResponsableHabitual;
                ActualizarPronosticoChecklist();
            }
        };
        txtKmSemanal.TextChanged += (_, _) => ActualizarPronosticoChecklist();
        ActualizarPronosticoChecklist();

        var txtObs = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Observaciones semanales (ej. Llanta trasera derecha baja de presión, parabrisa limpio, extintor vigente)...",
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(CrearCampoConEtiqueta("Observaciones / Hallazgos de la Revisión", txtObs));
        root.Children.Add(lblEstimacionServicio);

        var chkImprimirAlGuardar = new CheckBox
        {
            Content = "Abrir vista previa e imprimir Formato Semanal Tamaño Carta al guardar",
            IsChecked = true,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(chkImprimirAlGuardar);

        var spBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Wpf.Ui.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnSave = new Wpf.Ui.Controls.Button { Content = "Guardar Revisión Semanal", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        btnCancel.Click += (_, _) => win.DialogResult = false;

        int idVehGuardado = 0;
        RevisionSemanalVehiculo? revCreada = null;
        btnSave.Click += (_, _) =>
        {
            if (cboVehiculo.SelectedItem is not VehiculoFlotilla vSel) return;
            int.TryParse(txtKmSemanal.Text, out int kmReg);
            idVehGuardado = vSel.Id;

            revCreada = DatabaseService.RegistrarRevisionSemanalVehiculo(new RevisionSemanalVehiculo
            {
                VehiculoId = vSel.Id,
                Fecha = DateTime.Today.ToString("yyyy-MM-dd"),
                ResponsableInspector = txtInspector.Text,
                KilometrajeRegistrado = kmReg > 0 ? kmReg : vSel.KilometrajeActual,
                NivelCombustible = cboNivelTanque.SelectedItem?.ToString() ?? "3/4 Tanque",
                EstadoLlantas = cboLlantas.SelectedItem?.ToString() ?? "OK",
                EstadoNivelesFluidos = cboFluidos.SelectedItem?.ToString() ?? "OK",
                EstadoFrenosLuces = cboFrenosLuces.SelectedItem?.ToString() ?? "OK",
                EstadoCarroceriaLimpieza = cboCarroceria.SelectedItem?.ToString() ?? "OK",
                Observaciones = txtObs.Text
            });
            win.DialogResult = true;
        };
        spBtns.Children.Add(btnCancel);
        spBtns.Children.Add(btnSave);
        root.Children.Add(spBtns);

        win.Content = root;
        if (win.ShowDialog() == true)
        {
            CargarTodoElSistema();
            RefrescarModuloFlotilla(idVehGuardado);
            MostrarToastDiscreto($"Check-list semanal {revCreada?.FolioRevision} guardado y odómetro actualizado.", "Revisión Semanal");
            if (chkImprimirAlGuardar.IsChecked == true && idVehGuardado > 0)
            {
                var vehActualizado = DatabaseService.ObtenerVehiculosFlotilla().FirstOrDefault(v => v.Id == idVehGuardado);
                if (vehActualizado != null)
                {
                    BarcodePrintService.MostrarVistaPreviaEImprimirRevisionSemanalVehiculo(this, vehActualizado, revCreada);
                }
            }
        }
    }

    private StackPanel CrearCampoConEtiqueta(string etiqueta, UIElement control)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = etiqueta,
            FontSize = 11.2,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        });
        sp.Children.Add(control);
        return sp;
    }

    private void MostrarModalFlotillaYGarantias(int tabInicial = 0, int? vehiculoInicialId = null)
    {
        if (tabInicial == 0)
        {
            CambiarSeccion(9);
            RefrescarModuloFlotilla(vehiculoInicialId);
            return;
        }

        var win = new Window
        {
            Title = "Control de Garantías con Proveedor y Costo de Mantenimiento (TCO) de Herramientas (HER-xxxx)",
            Width = 1020,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var dockGar = new DockPanel { Margin = new Thickness(14) };

        var topGar = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        topGar.Children.Add(new TextBlock
        {
            Text = "Control de Garantías con Proveedor y Costo Total de Propiedad (TCO) de Herramientas (HER-xxxx)",
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold
        });
        topGar.Children.Add(new TextBlock
        {
            Text = "Antes de comprar una herramienta nueva o pagar reparación, verifique aquí si sigue Dentro de Garantía. Cuando el gasto acumulado en reparaciones alcanza el 50% de su costo de adquisición, el sistema emite alerta de reemplazo [REEMPLAZAR >=50%].",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var bdrEditGar = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
        var gridEditGar = new Grid();
        gridEditGar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        gridEditGar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        gridEditGar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        gridEditGar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        gridEditGar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridEditGar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txtGarFechaCompra = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Compra (yyyy-MM-dd)", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Fecha de compra de la herramienta" };
        var txtGarFechaFin = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Fin Garantía (yyyy-MM-dd)", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Fecha límite de garantía con proveedor/fabricante" };
        var txtGarCostoAdq = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Costo Compra $", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Valor de adquisición con IVA" };
        var txtGarSumarMant = new Wpf.Ui.Controls.TextBox { PlaceholderText = "+ Reparación $ (0)", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Monto de reparación o servicio a sumar al historial TCO" };
        var txtGarMotivo = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Nota de garantía o servicio (ej. Cambio de carbones / Garantía en Bosch)...", Margin = new Thickness(0, 0, 6, 0) };
        var btnGuardarGar = new Wpf.Ui.Controls.Button { Content = "Actualizar Garantía / TCO", Appearance = ControlAppearance.Primary, Padding = new Thickness(12, 5, 12, 5) };

        Grid.SetColumn(txtGarFechaCompra, 0);
        Grid.SetColumn(txtGarFechaFin, 1);
        Grid.SetColumn(txtGarCostoAdq, 2);
        Grid.SetColumn(txtGarSumarMant, 3);
        Grid.SetColumn(txtGarMotivo, 4);
        Grid.SetColumn(btnGuardarGar, 5);
        gridEditGar.Children.Add(txtGarFechaCompra);
        gridEditGar.Children.Add(txtGarFechaFin);
        gridEditGar.Children.Add(txtGarCostoAdq);
        gridEditGar.Children.Add(txtGarSumarMant);
        gridEditGar.Children.Add(txtGarMotivo);
        gridEditGar.Children.Add(btnGuardarGar);
        bdrEditGar.Child = gridEditGar;
        topGar.Children.Add(bdrEditGar);

        DockPanel.SetDock(topGar, Dock.Top);
        dockGar.Children.Add(topGar);

        var dgGarantias = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        dgGarantias.Columns.Add(new DataGridTextColumn { Header = "Código HER", Binding = new System.Windows.Data.Binding("CodigoIndividual"), Width = 90, ElementStyle = CeldaRecortable() });
        dgGarantias.Columns.Add(new DataGridTextColumn { Header = "Herramienta / Equipo", Binding = new System.Windows.Data.Binding("ProductoNombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });
        dgGarantias.Columns.Add(new DataGridTextColumn { Header = "Marca / Serie", Binding = new System.Windows.Data.Binding("MarcaModelo"), Width = 140, ElementStyle = CeldaRecortable() });
        dgGarantias.Columns.Add(new DataGridTextColumn { Header = "Estado Físico", Binding = new System.Windows.Data.Binding("Estado"), Width = 105, ElementStyle = CeldaRecortable() });
        dgGarantias.Columns.Add(new DataGridTextColumn { Header = "Vigencia Garantía", Binding = new System.Windows.Data.Binding("EstadoGarantiaTexto"), Width = 185, ElementStyle = CeldaRecortable() });
        dgGarantias.Columns.Add(new DataGridTextColumn { Header = "Costo Total (TCO) y Alerta 50%", Binding = new System.Windows.Data.Binding("ResumenTcoTexto"), Width = 240, ElementStyle = CeldaRecortable() });
        dockGar.Children.Add(dgGarantias);

        void RefrescarGarantiasModal(int? selHerId = null)
        {
            var herList = DatabaseService.ObtenerHerramientas();
            dgGarantias.ItemsSource = herList;
            var sel = selHerId.HasValue ? herList.FirstOrDefault(h => h.Id == selHerId.Value) : herList.FirstOrDefault();
            if (sel != null)
                dgGarantias.SelectedItem = sel;
        }

        dgGarantias.SelectionChanged += (_, _) =>
        {
            if (dgGarantias.SelectedItem is HerramientaActivo h)
            {
                txtGarFechaCompra.Text = string.IsNullOrWhiteSpace(h.FechaCompra) ? DateTime.Today.ToString("yyyy-MM-dd") : h.FechaCompra;
                txtGarFechaFin.Text = string.IsNullOrWhiteSpace(h.FechaFinGarantia) ? DateTime.Today.AddYears(1).ToString("yyyy-MM-dd") : h.FechaFinGarantia;
                txtGarCostoAdq.Text = h.CostoAdquisicion.ToString("0.##", CultureInfo.InvariantCulture);
                txtGarSumarMant.Text = "0";
                txtGarMotivo.Text = h.NotasEstado;
            }
        };

        btnGuardarGar.Click += (_, _) =>
        {
            if (dgGarantias.SelectedItem is not HerramientaActivo hSel) return;
            decimal costoAdq = ParseDecimal(txtGarCostoAdq.Text, hSel.CostoAdquisicion);
            decimal sumarMant = ParseDecimal(txtGarSumarMant.Text, 0m);
            DatabaseService.RegistrarMantenimientoGarantiaHerramienta(
                hSel.Id,
                txtGarFechaCompra.Text,
                txtGarFechaFin.Text,
                costoAdq,
                sumarMant,
                txtGarMotivo.Text);
            RefrescarGarantiasModal(hSel.Id);
            CargarTodoElSistema();
            ActualizarBadgeNotificacionesCheckpoints();
            MostrarToastDiscreto($"Se actualizó el registro TCO/Garantía de {hSel.CodigoIndividual}.", "Garantías y TCO");
        };

        RefrescarGarantiasModal();

        win.Content = dockGar;
        win.ShowDialog();
    }

    // ==================== FASE 4: TOAST DISCRETO, MENÚS CONTEXTUALES, KRALJIC, PPV Y RESPALDO ZIP ====================

    private System.Windows.Threading.DispatcherTimer? _timerToastDiscreto;
    private List<ItemMatrizKraljic> _cacheKraljicFase4 = new();

    public void MostrarToastDiscreto(string mensaje, string? titulo = null, bool esAlerta = false)
    {
        if (BdrToastNotificacion == null || TxtToastMensaje == null || TxtToastTitulo == null)
            return;

        TxtToastTitulo.Text = string.IsNullOrWhiteSpace(titulo)
            ? (esAlerta ? "Aviso Operativo" : "Operación completada")
            : titulo;
        TxtToastMensaje.Text = mensaje ?? string.Empty;

        var colorBorde = esAlerta
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
        BdrToastNotificacion.BorderBrush = colorBorde;
        TxtToastTitulo.Foreground = colorBorde;
        BdrToastNotificacion.Visibility = Visibility.Visible;

        if (_timerToastDiscreto == null)
        {
            _timerToastDiscreto = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.8)
            };
            _timerToastDiscreto.Tick += (_, _) =>
            {
                _timerToastDiscreto.Stop();
                if (BdrToastNotificacion != null)
                    BdrToastNotificacion.Visibility = Visibility.Collapsed;
            };
        }

        _timerToastDiscreto.Stop();
        _timerToastDiscreto.Start();
    }

    private void BdrToastNotificacion_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _timerToastDiscreto?.Stop();
        if (BdrToastNotificacion != null)
            BdrToastNotificacion.Visibility = Visibility.Collapsed;
    }

    private void RefrescarEconomiaIndustrialFase4()
    {
        try
        {
            _cacheKraljicFase4 = DatabaseService.ObtenerAnalisisMatrizKraljic();
            FiltrarTablaKraljicFase4();

            var ppvList = DatabaseService.ObtenerAnalisisPpvEInflacionInterna();
            if (DgAnalisisPpv != null)
                DgAnalisisPpv.ItemsSource = ppvList;

            var audList = DatabaseService.ObtenerAuditoriaConsumoYMermaPorCuadrilla();
            if (DgAuditoriaCuadrillas != null)
                DgAuditoriaCuadrillas.ItemsSource = audList;

            // Actualizar KPIs Superiores de Economía Industrial
            int estrategicosOCuello = _cacheKraljicFase4.Count(x =>
                x.CuadranteKraljic.StartsWith("Estratégico", StringComparison.OrdinalIgnoreCase) ||
                x.CuadranteKraljic.StartsWith("Cuello", StringComparison.OrdinalIgnoreCase));
            int rutinarios = _cacheKraljicFase4.Count(x =>
                x.CuadranteKraljic.StartsWith("Rutinario", StringComparison.OrdinalIgnoreCase));

            if (TxtKpiEconKraljicRiesgo != null)
                TxtKpiEconKraljicRiesgo.Text = $"{estrategicosOCuello} insumos críticos";
            if (TxtKpiEconKraljicSub != null)
                TxtKpiEconKraljicSub.Text = $"{rutinarios} rutinarios automatizables";

            decimal ppvNeto = ppvList.Sum(x => x.AhorroOSobreprecioTotalPpv);
            if (TxtKpiEconPpvNeto != null)
            {
                TxtKpiEconPpvNeto.Text = ppvNeto >= 0m ? $"+{ppvNeto:C2} (Ahorro)" : $"{ppvNeto:C2} (Alza)";
            }
            if (TxtKpiEconPpvSub != null)
                TxtKpiEconPpvSub.Text = $"{ppvList.Count} partidas analizadas vs. promedio";

            int famInflacion = ppvList
                .Where(x => x.VariacionUnitariaPct >= 10m)
                .Select(x => x.Familia)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (TxtKpiEconInflacion != null)
                TxtKpiEconInflacion.Text = $"{famInflacion} familia(s) en alerta";
            if (TxtKpiEconInflacionSub != null)
                TxtKpiEconInflacionSub.Text = "Variaciones >= +10% vs. histórico";

            int totalHerActivas = audList.Sum(x => x.HerramientasEnResguardoActivo);
            int totalIncidencias = audList.Sum(x => x.IncidenciasOMermasRegistradas);
            if (TxtKpiEconAgencia != null)
                TxtKpiEconAgencia.Text = $"{audList.Count} responsables";
            if (TxtKpiEconAgenciaSub != null)
                TxtKpiEconAgenciaSub.Text = $"{totalHerActivas} herram. activas | {totalIncidencias} incidencias";
        }
        catch { }
    }

    private void FiltrarTablaKraljicFase4()
    {
        if (DgAnalisisKraljic == null) return;
        string filtro = (CboFiltroCuadranteKraljic?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Todos";
        if (string.IsNullOrWhiteSpace(filtro) || filtro.StartsWith("Todos", StringComparison.OrdinalIgnoreCase))
        {
            DgAnalisisKraljic.ItemsSource = _cacheKraljicFase4;
        }
        else
        {
            DgAnalisisKraljic.ItemsSource = _cacheKraljicFase4
                .Where(x => x.CuadranteKraljic.Equals(filtro, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void CboFiltroCuadranteKraljic_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        FiltrarTablaKraljicFase4();
    }

    private void BtnRefrescarEconomiaIndustrial_Click(object sender, RoutedEventArgs e)
    {
        RefrescarEconomiaIndustrialFase4();
        MostrarToastDiscreto("Indicadores de Kraljic, Variación de Precio (PPV) y Auditoría de Cuadrillas actualizados.", "Economía Industrial");
    }

    private void BtnAbrirEconomiaKraljic_Click(object sender, RoutedEventArgs e)
    {
        CambiarSeccion(7);
        if (TcSubAnalisis != null && TcSubAnalisis.Items.Count >= 5)
            TcSubAnalisis.SelectedIndex = 4;
        if (TcEconomiaIndustrial != null)
            TcEconomiaIndustrial.SelectedIndex = 0;
        RefrescarEconomiaIndustrialFase4();
        MostrarToastDiscreto("Mostrando Matriz Estratégica de Kraljic.", "Segmentación de Insumos");
    }

    private void BtnAbrirEconomiaPpv_Click(object sender, RoutedEventArgs e)
    {
        CambiarSeccion(7);
        if (TcSubAnalisis != null && TcSubAnalisis.Items.Count >= 5)
            TcSubAnalisis.SelectedIndex = 4;
        if (TcEconomiaIndustrial != null)
            TcEconomiaIndustrial.SelectedIndex = 1;
        RefrescarEconomiaIndustrialFase4();
        MostrarToastDiscreto("Mostrando Variación de Precio de Compra (PPV) e Inflación Interna.", "Auditoría de Precios");
    }

    private void BtnAbrirAuditoriaCuadrillas_Click(object sender, RoutedEventArgs e)
    {
        CambiarSeccion(7);
        if (TcSubAnalisis != null && TcSubAnalisis.Items.Count >= 5)
            TcSubAnalisis.SelectedIndex = 4;
        if (TcEconomiaIndustrial != null)
            TcEconomiaIndustrial.SelectedIndex = 2;
        RefrescarEconomiaIndustrialFase4();
        MostrarToastDiscreto("Mostrando Auditoría de Consumo, Sobrantes y Resguardo por Cuadrilla.", "Control de Agencia");
    }

    private void BtnAutoAdjudicarRutinariosKraljic_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud?.SelectedItem is not Solicitud sol)
        {
            DialogoFluent.Show(this, "Seleccione primero una Solicitud de Compra en el Cuadro Comparativo.", "Solicitud requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (adjudicados, montoConIva) = DatabaseService.AutoAdjudicarRutinariosMejorPrecioHistorico(sol.Id);
        if (adjudicados <= 0)
        {
            MostrarToastDiscreto("No se encontraron partidas rutinarias con precio histórico para auto-adjudicar en esta solicitud.", "Auto-Cotización Kraljic", esAlerta: true);
            return;
        }

        CargarTodoElSistema();
        CboCompSolicitud.SelectedItem = DatabaseService.ObtenerSolicitudes().FirstOrDefault(s => s.Id == sol.Id);
        MostrarToastDiscreto($"Se auto-cotizaron y adjudicaron {adjudicados} partida(s) con su mejor precio histórico ({montoConIva:C2} c/IVA).", "Auto-Cotización Kraljic");
    }

    private void BtnExportarFlotillaCsv_Click(object sender, RoutedEventArgs e)
    {
        var unidades = DatabaseService.ObtenerVehiculosFlotilla();
        if (unidades.Count == 0)
        {
            MostrarToastDiscreto("No hay camionetas registradas en la flotilla para exportar.", "Flotilla", esAlerta: true);
            return;
        }

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar Resumen de Flotilla y Costo por Kilómetro (CSV)",
            Filter = "Archivo CSV (*.csv)|*.csv",
            FileName = $"Flotilla_CPK_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (sfd.ShowDialog() != true) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Codigo,Unidad,Placas,Responsable,KmInicial,KmActual,KmRecorridos,LitrosAcumulados,RendimientoKmL,GastoGasolina,GastoTaller,GastoTotal,CostoPorKm,ProximoServicio");
        foreach (var v in unidades)
        {
            sb.AppendLine($"\"{v.CodigoVehiculo}\",\"{v.MarcaModelo.Replace("\"", "\"\"")}\",\"{v.Placas}\",\"{v.ResponsableHabitual.Replace("\"", "\"\"")}\",{v.KilometrajeInicial},{v.KilometrajeActual},{v.KilometrosRecorridosPeriodo},{v.LitrosCombustibleAcumulados:0.##},{v.RendimientoKmPorLitro:0.##},{v.GastoCombustibleAcumulado:0.00},{v.GastoMantenimientoAcumulado + v.GastoOtrosAcumulado:0.00},{v.GastoTotalAcumulado:0.00},{v.CostoPorKilometro:0.00},\"{v.AlertaServicioTexto.Replace("\"", "\"\"")}\"");
        }

        System.IO.File.WriteAllText(sfd.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
        MostrarToastDiscreto($"Reporte CSV de flotilla guardado en: {System.IO.Path.GetFileName(sfd.FileName)}", "Exportación Flotilla");
    }

    private void BtnModalRespaldoZip_Click(object sender, RoutedEventArgs e)
    {
        MostrarModalRespaldoIntegralZip();
    }

    private void MostrarModalRespaldoIntegralZip()
    {
        var win = new Window
        {
            Title = "Respaldo y Restauración Integral del Sistema (.ZIP)",
            Width = 640,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ApplicationBackgroundBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Respaldo Integral en 1 Clic (Base de Datos + Imágenes + Facturas XML)",
            FontSize = 16.5,
            FontWeight = FontWeights.Bold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Empaqueta en un solo archivo .ZIP la base de datos SQLite (con checkpoint WAL seguro), las fotografías del catálogo y las facturas XML CFDI.",
            FontSize = 11.8,
            Opacity = 0.76,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14)
        });

        var bdrCrear = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlElevationBorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spCrear = new StackPanel();
        spCrear.Children.Add(new TextBlock { Text = "1. Generar Respaldo Completo (.ZIP)", FontWeight = FontWeights.Bold, FontSize = 13.5 });
        spCrear.Children.Add(new TextBlock
        {
            Text = $"Carpeta automática de respaldos: {DatabaseService.BackupsFolder}",
            FontSize = 11,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10)
        });

        var wpBotonesCrear = new WrapPanel();
        var btnRespaldoRapido = new Wpf.Ui.Controls.Button
        {
            Content = "Crear Respaldo .ZIP en Carpeta Local",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnExportarZip = new Wpf.Ui.Controls.Button
        {
            Content = "Guardar Respaldo .ZIP en otra ubicación...",
            Padding = new Thickness(12, 6, 12, 6)
        };
        wpBotonesCrear.Children.Add(btnRespaldoRapido);
        wpBotonesCrear.Children.Add(btnExportarZip);
        spCrear.Children.Add(wpBotonesCrear);
        bdrCrear.Child = spCrear;
        root.Children.Add(bdrCrear);

        var bdrRestaurar = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlElevationBorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var spRestaurar = new StackPanel();
        spRestaurar.Children.Add(new TextBlock { Text = "2. Restaurar desde un Archivo .ZIP", FontWeight = FontWeights.Bold, FontSize = 13.5 });
        spRestaurar.Children.Add(new TextBlock
        {
            Text = "Antes de restaurar, el sistema genera automáticamente un respaldo preventivo del estado actual.",
            FontSize = 11,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10)
        });
        var btnRestaurarZip = new Wpf.Ui.Controls.Button
        {
            Content = "Seleccionar Archivo .ZIP y Restaurar Sistema...",
            Padding = new Thickness(12, 6, 12, 6)
        };
        spRestaurar.Children.Add(btnRestaurarZip);
        bdrRestaurar.Child = spRestaurar;
        root.Children.Add(bdrRestaurar);

        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = "Cerrar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 6, 16, 6)
        };
        btnCerrar.Click += (_, _) => win.Close();
        root.Children.Add(btnCerrar);

        btnRespaldoRapido.Click += (_, _) =>
        {
            try
            {
                string zipPath = DatabaseService.CrearRespaldoIntegralZip();
                win.Close();
                MostrarToastDiscreto($"Respaldo integral creado: {System.IO.Path.GetFileName(zipPath)}", "Respaldo .ZIP Exitoso");
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"Error al crear el respaldo ZIP:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        btnExportarZip.Click += (_, _) =>
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar Respaldo Integral (.ZIP)",
                Filter = "Archivo ZIP de Respaldo (*.zip)|*.zip",
                FileName = $"RespaldoIntegral_Lebaref_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
            };
            if (sfd.ShowDialog() != true) return;
            try
            {
                string zipPath = DatabaseService.CrearRespaldoIntegralZip(sfd.FileName);
                win.Close();
                MostrarToastDiscreto($"Respaldo exportado en: {zipPath}", "Respaldo .ZIP Exportado");
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"Error al exportar el respaldo ZIP:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        btnRestaurarZip.Click += (_, _) =>
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar Respaldo Integral (.ZIP) para Restaurar",
                Filter = "Archivo ZIP de Respaldo (*.zip)|*.zip",
                InitialDirectory = System.IO.Directory.Exists(DatabaseService.BackupsFolder) ? DatabaseService.BackupsFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (ofd.ShowDialog() != true) return;

            var conf = DialogoFluent.Show(
                win,
                $"¿Está seguro de restaurar la base de datos, imágenes y XML desde el archivo:\n{System.IO.Path.GetFileName(ofd.FileName)}?\n\nSe creará un respaldo preventivo automático antes de reemplazar los datos actuales.",
                "Confirmar Restauración Integral",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (conf != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                DatabaseService.RestaurarRespaldoIntegralZip(ofd.FileName);
                CargarTodoElSistema();
                RefrescarModuloFlotilla();
                RefrescarEconomiaIndustrialFase4();
                win.Close();
                MostrarToastDiscreto($"Sistema restaurado exitosamente desde {System.IO.Path.GetFileName(ofd.FileName)}.", "Restauración Completada");
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"No fue posible restaurar el archivo ZIP:\n{ex.Message}", "Error al restaurar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        win.Content = root;
        win.ShowDialog();
    }

    // =========================================================================
    // NOTIFICACIONES POR CORREO A GERENTES (GMAIL SMTP -> CORREO CORPORATIVO)
    // =========================================================================

    private void BtnAbrirConfigNotificacionesCorreo_Click(object sender, RoutedEventArgs e)
    {
        MostrarModalConfiguracionYBitacoraCorreoGerentes();
    }

    private void MostrarModalConfiguracionYBitacoraCorreoGerentes()
    {
        var cfg = DatabaseService.ObtenerConfiguracionSmtpCorreo();

        var win = new Window
        {
            Title = "Notificaciones por Correo a Gerentes (Gmail SMTP → Correo Corporativo)",
            Width = 1040,
            Height = 680,
            MaxWidth = SystemParameters.WorkArea.Width - 20,
            MaxHeight = SystemParameters.WorkArea.Height - 20,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Notificaciones Automáticas por Correo a Gerentes sobre Cambios de Estado de Cotizaciones",
            FontSize = 15.5,
            FontWeight = FontWeights.Bold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Envía avisos automáticos desde su cuenta Gmail hacia el correo corporativo registrado de cada Gerente / Solicitante en las 5 etapas del ciclo de compra.",
            FontSize = 11.5,
            Opacity = 0.78,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(365) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(bodyGrid, 1);
        root.Children.Add(bodyGrid);

        // Panel Izquierdo: Configuración Gmail SMTP y Envío Manual
        var bdrLeft = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 10, 0)
        };
        var svLeft = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var spLeft = new StackPanel();

        spLeft.Children.Add(new TextBlock
        {
            Text = "1. Cuenta Remitente (Gmail SMTP)",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var chkActivo = new CheckBox
        {
            Content = "Activar envío automático en los 5 cambios de estado",
            IsChecked = cfg.Activo,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        spLeft.Children.Add(chkActivo);

        spLeft.Children.Add(new TextBlock { Text = "Correo Gmail Remitente:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtGmail = new Wpf.Ui.Controls.TextBox { Text = cfg.CorreoRemitenteGmail, PlaceholderText = "ej. compras.lebaref@gmail.com", Margin = new Thickness(0, 0, 0, 6) };
        spLeft.Children.Add(txtGmail);

        spLeft.Children.Add(new TextBlock { Text = "Contraseña de Aplicación de Google (16 letras):", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtPassApp = new System.Windows.Controls.PasswordBox { Password = cfg.ContrasenaAplicacionGmail, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4) };
        spLeft.Children.Add(txtPassApp);

        var txtEstadoPass = new TextBlock
        {
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        void ActualizarEstadoPasswordUI()
        {
            string pLimpia = (txtPassApp.Password ?? string.Empty).Replace(" ", "").Trim();
            if (string.IsNullOrEmpty(pLimpia))
            {
                txtEstadoPass.Text = "Pendiente: Pegue aquí los 16 caracteres generados por Google.";
                txtEstadoPass.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            }
            else if (pLimpia.Length != 16 || pLimpia.Any(c => !char.IsLetter(c)))
            {
                txtEstadoPass.Text = $"⚠️ Atención: La clave actual tiene {pLimpia.Length} caracteres (contiene números o símbolos). Parece la contraseña normal de su correo, la cual Google bloquea. Debe usar una Contraseña de Aplicación de 16 letras.";
                txtEstadoPass.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
            else
            {
                txtEstadoPass.Text = "✓ Formato válido: 16 letras de Contraseña de Aplicación detectadas.";
                txtEstadoPass.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
        }
        txtPassApp.PasswordChanged += (_, _) => ActualizarEstadoPasswordUI();
        ActualizarEstadoPasswordUI();
        spLeft.Children.Add(txtEstadoPass);

        var btnAbrirGoogleAppPass = new System.Windows.Controls.Button
        {
            Content = "🔑 Abrir Google para generar Contraseña de 16 letras",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 8)
        };
        btnAbrirGoogleAppPass.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://myaccount.google.com/apppasswords")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"Abra en su navegador:\nhttps://myaccount.google.com/apppasswords\n\n({ex.Message})", "Generar Contraseña de Aplicación", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        spLeft.Children.Add(btnAbrirGoogleAppPass);

        spLeft.Children.Add(new TextBlock { Text = "Nombre visible del remitente:", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtNombreRem = new Wpf.Ui.Controls.TextBox { Text = cfg.NombreMostrarRemitente, Margin = new Thickness(0, 0, 0, 6) };
        spLeft.Children.Add(txtNombreRem);

        spLeft.Children.Add(new TextBlock { Text = "Correo Corporativo en Copia Fija (CC Opcional):", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var txtCopiaGerencia = new Wpf.Ui.Controls.TextBox { Text = cfg.CorreoCopiaFijaGerencia, PlaceholderText = "ej. direccion@empresa.com (opcional)", Margin = new Thickness(0, 0, 0, 8) };
        spLeft.Children.Add(txtCopiaGerencia);

        var ugSmtp = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        var spHost = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        spHost.Children.Add(new TextBlock { Text = "Servidor SMTP:", FontSize = 11 });
        var txtHost = new Wpf.Ui.Controls.TextBox { Text = cfg.ServidorSmtp };
        spHost.Children.Add(txtHost);
        ugSmtp.Children.Add(spHost);

        var spPort = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        spPort.Children.Add(new TextBlock { Text = "Puerto SSL/TLS:", FontSize = 11 });
        var txtPort = new Wpf.Ui.Controls.TextBox { Text = cfg.PuertoSmtp.ToString() };
        spPort.Children.Add(txtPort);
        ugSmtp.Children.Add(spPort);
        spLeft.Children.Add(ugSmtp);

        spLeft.Children.Add(new TextBlock
        {
            Text = "2. Tiempos Aproximados por Defecto",
            FontWeight = FontWeights.Bold,
            FontSize = 12.5,
            Margin = new Thickness(0, 4, 0, 4)
        });

        var ugTiempos = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
        var spT1 = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        spT1.Children.Add(new TextBlock { Text = "Al crear cotización:", FontSize = 11 });
        var txtTiempoReg = new Wpf.Ui.Controls.TextBox { Text = cfg.TiempoAproxRegistroDefault, PlaceholderText = "24 a 48 hrs" };
        spT1.Children.Add(txtTiempoReg);
        ugTiempos.Children.Add(spT1);

        var spT2 = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        spT2.Children.Add(new TextBlock { Text = "En comparativa:", FontSize = 11 });
        var txtTiempoComp = new Wpf.Ui.Controls.TextBox { Text = cfg.TiempoAproxComparativaDefault, PlaceholderText = "2 a 4 días hábiles" };
        spT2.Children.Add(txtTiempoComp);
        ugTiempos.Children.Add(spT2);
        spLeft.Children.Add(ugTiempos);

        ConfiguracionSmtpCorreo LeerConfigActualUI() => new()
        {
            Activo = chkActivo.IsChecked == true,
            CorreoRemitenteGmail = txtGmail.Text?.Trim() ?? string.Empty,
            ContrasenaAplicacionGmail = txtPassApp.Password?.Replace(" ", "").Trim() ?? string.Empty,
            NombreMostrarRemitente = string.IsNullOrWhiteSpace(txtNombreRem.Text) ? "Compras y Almacén Lebaref" : txtNombreRem.Text.Trim(),
            CorreoCopiaFijaGerencia = txtCopiaGerencia.Text?.Trim() ?? string.Empty,
            ServidorSmtp = string.IsNullOrWhiteSpace(txtHost.Text) ? "smtp.gmail.com" : txtHost.Text.Trim(),
            PuertoSmtp = int.TryParse(txtPort.Text, out int p) && p > 0 ? p : 587,
            UsarSsl = true,
            TiempoAproxRegistroDefault = string.IsNullOrWhiteSpace(txtTiempoReg.Text) ? "24 a 48 hrs" : txtTiempoReg.Text.Trim(),
            TiempoAproxComparativaDefault = string.IsNullOrWhiteSpace(txtTiempoComp.Text) ? "2 a 4 días hábiles" : txtTiempoComp.Text.Trim()
        };

        var btnGuardarCfg = new Wpf.Ui.Controls.Button
        {
            Content = "Guardar Configuración Gmail",
            Appearance = ControlAppearance.Primary,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6)
        };
        spLeft.Children.Add(btnGuardarCfg);

        var btnProbarSmtp = new System.Windows.Controls.Button
        {
            Content = "Enviar Correo de Prueba Ahora",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 12)
        };
        spLeft.Children.Add(btnProbarSmtp);

        // Sección 3: Enviar / Actualizar aviso manual de un folio con tiempo aprox personalizado
        spLeft.Children.Add(new Border
        {
            Height = 1,
            Background = Tk("ControlStrokeColorDefaultBrush"),
            Margin = new Thickness(0, 2, 0, 10)
        });
        spLeft.Children.Add(new TextBlock
        {
            Text = "3. Enviar Aviso Manual con Tiempo/Entrega Personalizado",
            FontWeight = FontWeights.Bold,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var solicitudes = DatabaseService.ObtenerSolicitudes();
        var cboSolManual = new ComboBox
        {
            ItemsSource = solicitudes,
            SelectedIndex = solicitudes.Count > 0 ? 0 : -1,
            Margin = new Thickness(0, 0, 0, 6)
        };
        spLeft.Children.Add(cboSolManual);

        var cboEtapaManual = new ComboBox { Margin = new Thickness(0, 0, 0, 6), SelectedIndex = 0 };
        cboEtapaManual.Items.Add(new ComboBoxItem { Content = "1. Cuando se crea (Tu cotización folio xxx registrada...)", Tag = "1_CREADA" });
        cboEtapaManual.Items.Add(new ComboBoxItem { Content = "2. Pasa a cuadro comparativo (Recibiendo avances...)", Tag = "2_COMPARATIVA" });
        cboEtapaManual.Items.Add(new ComboBoxItem { Content = "3. Se emiten órdenes de compra (Esperando pago)", Tag = "3_OC_EMITIDA" });
        cboEtapaManual.Items.Add(new ComboBoxItem { Content = "4. Pago realizado (Ya fue pagada, tiempo aprox...)", Tag = "4_PAGADA" });
        cboEtapaManual.Items.Add(new ComboBoxItem { Content = "5. Material recibido (Concluido con entrega en...)", Tag = "5_CONCLUIDA" });
        spLeft.Children.Add(cboEtapaManual);

        var txtDatoPersonalizado = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Tiempo aprox o lugar de entrega (ej. 3 días hábiles / Obra Norte)",
            Margin = new Thickness(0, 0, 0, 6)
        };
        spLeft.Children.Add(txtDatoPersonalizado);

        var btnEnviarManual = new System.Windows.Controls.Button
        {
            Content = "Generar y Enviar Notificación de esta Etapa",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 5, 10, 5)
        };
        spLeft.Children.Add(btnEnviarManual);

        svLeft.Content = spLeft;
        bdrLeft.Child = svLeft;
        Grid.SetColumn(bdrLeft, 0);
        bodyGrid.Children.Add(bdrLeft);

        // Panel Derecho: Bitácora de Correos Enviados y Vista Previa
        var bdrRight = new Border
        {
            Background = Tk("ControlFillColorDefaultBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        var dpRight = new DockPanel();

        var topRight = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(topRight, Dock.Top);
        var btnReenviarSel = new Wpf.Ui.Controls.Button
        {
            Content = "Reenviar Seleccionado (Gmail)",
            Appearance = ControlAppearance.Primary,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(btnReenviarSel, Dock.Right);
        topRight.Children.Add(btnReenviarSel);

        var btnRefrescarBit = new System.Windows.Controls.Button
        {
            Content = "Actualizar",
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(btnRefrescarBit, Dock.Right);
        topRight.Children.Add(btnRefrescarBit);

        var txtFiltroBit = new Wpf.Ui.Controls.TextBox
        {
            Width = 180,
            PlaceholderText = "Filtrar folio o gerente...",
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(txtFiltroBit, Dock.Right);
        topRight.Children.Add(txtFiltroBit);

        topRight.Children.Add(new TextBlock
        {
            Text = "Bitácora de Notificaciones a Gerentes",
            FontWeight = FontWeights.Bold,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        dpRight.Children.Add(topRight);

        var bdrDetalleMsj = new Border
        {
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(bdrDetalleMsj, Dock.Bottom);
        var txtDetalleSel = new TextBlock
        {
            Text = "Seleccione un registro de la bitácora para ver el mensaje completo y el resultado de entrega SMTP.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        };
        bdrDetalleMsj.Child = txtDetalleSel;
        dpRight.Children.Add(bdrDetalleMsj);

        var dgBitacora = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Single
        };
        dgBitacora.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Fecha / Hora", Binding = new System.Windows.Data.Binding("FechaHora"), Width = 125 });
        dgBitacora.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Folio", Binding = new System.Windows.Data.Binding("SolicitudFolio"), Width = 105 });
        dgBitacora.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Gerente", Binding = new System.Windows.Data.Binding("GerenteNombre"), Width = 130 });
        dgBitacora.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Correo Corporativo", Binding = new System.Windows.Data.Binding("CorreoDestino"), Width = 155 });
        dgBitacora.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Mensaje Enviado", Binding = new System.Windows.Data.Binding("MensajePrincipal"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dgBitacora.Columns.Add(new DataGridTextColumn { ElementStyle = CeldaRecortable(), Header = "Estado", Binding = new System.Windows.Data.Binding("EstadoEnvio"), Width = 135 });

        dpRight.Children.Add(dgBitacora);
        bdrRight.Child = dpRight;
        Grid.SetColumn(bdrRight, 1);
        bodyGrid.Children.Add(bdrRight);

        void CargarTablaBitacora()
        {
            dgBitacora.ItemsSource = DatabaseService.ObtenerNotificacionesCorreo(txtFiltroBit.Text);
        }

        txtFiltroBit.TextChanged += (_, _) => CargarTablaBitacora();
        btnRefrescarBit.Click += (_, _) => CargarTablaBitacora();

        dgBitacora.SelectionChanged += (_, _) =>
        {
            if (dgBitacora.SelectedItem is RegistroNotificacionCorreo reg)
            {
                txtDetalleSel.Text =
                    $"[{reg.EtapaNombre}] — Para: {reg.GerenteNombre} <{(string.IsNullOrWhiteSpace(reg.CorreoDestino) ? "Sin correo asignado en Directorios" : reg.CorreoDestino)}>\n" +
                    $"Mensaje: \"{reg.MensajePrincipal}\"\n" +
                    $"Estado: {reg.EstadoEnvio} | {reg.DetalleResultado}";
            }
        };

        void HandlerBitacoraLive(RegistroNotificacionCorreo _)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(CargarTablaBitacora));
            }
            catch
            {
                // Ignorar si ventana ya cerró
            }
        }
        DatabaseService.OnNotificacionCorreoProcesada += HandlerBitacoraLive;
        win.Closed += (_, _) => DatabaseService.OnNotificacionCorreoProcesada -= HandlerBitacoraLive;

        btnGuardarCfg.Click += (_, _) =>
        {
            var nuevaCfg = LeerConfigActualUI();
            DatabaseService.GuardarConfiguracionSmtpCorreo(nuevaCfg);
            string pLimpia = nuevaCfg.ContrasenaAplicacionGmail;
            if (nuevaCfg.ServidorSmtp.Contains("gmail", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(pLimpia) &&
                (pLimpia.Length != 16 || pLimpia.Any(c => !char.IsLetter(c))))
            {
                DialogoFluent.Show(
                    win,
                    $"Configuración guardada, pero atención:\n\nLa contraseña ingresada tiene {pLimpia.Length} caracteres (contiene números o signos) y corresponde a una contraseña normal.\n\nGoogle bloquea las contraseñas normales en SMTP (Error 5.7.8 BadCredentials). Para enviar desde {nuevaCfg.CorreoRemitenteGmail}, haga clic en el botón '🔑 Abrir Google para generar Contraseña de 16 letras' y pegue los 16 caracteres que le dará Google.",
                    "Se requiere Contraseña de Aplicación de 16 letras",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            MostrarToastDiscreto("Configuración Gmail SMTP guardada correctamente.", "Correo Configurado");
        };

        btnProbarSmtp.Click += async (_, _) =>
        {
            var nuevaCfg = LeerConfigActualUI();
            DatabaseService.GuardarConfiguracionSmtpCorreo(nuevaCfg);
            btnProbarSmtp.IsEnabled = false;
            btnProbarSmtp.Content = "Enviando prueba por Gmail...";
            try
            {
                string destPrueba = !string.IsNullOrWhiteSpace(nuevaCfg.CorreoCopiaFijaGerencia)
                    ? nuevaCfg.CorreoCopiaFijaGerencia
                    : nuevaCfg.CorreoRemitenteGmail;
                var (ok, msj) = await DatabaseService.ProbarConexionSmtpGmailAsync(nuevaCfg, destPrueba);
                CargarTablaBitacora();
                DialogoFluent.Show(
                    win,
                    ok
                        ? $"Correo de prueba enviado exitosamente desde {nuevaCfg.CorreoRemitenteGmail} hacia {destPrueba}.\n\nDetalle: {msj}"
                        : $"No se pudo enviar el correo de prueba:\n\n{msj}",
                    ok ? "Prueba Gmail Exitosa" : "Aviso de Configuración SMTP",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            finally
            {
                btnProbarSmtp.IsEnabled = true;
                btnProbarSmtp.Content = "Enviar Correo de Prueba Ahora";
            }
        };

        btnEnviarManual.Click += (_, _) =>
        {
            if (cboSolManual.SelectedItem is not Solicitud solSel)
            {
                DialogoFluent.Show(win, "Seleccione una solicitud / cotización de la lista.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DatabaseService.GuardarConfiguracionSmtpCorreo(LeerConfigActualUI());
            string etapaTag = (cboEtapaManual.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1_CREADA";
            string customTxt = txtDatoPersonalizado.Text?.Trim() ?? string.Empty;

            DatabaseService.NotificarCambioEstadoCotizacionGerente(
                solSel.Id,
                null,
                etapaTag,
                etapaTag == "5_CONCLUIDA" ? null : customTxt,
                etapaTag == "5_CONCLUIDA" ? customTxt : null);

            CargarTablaBitacora();
        };

        btnReenviarSel.Click += async (_, _) =>
        {
            if (dgBitacora.SelectedItem is not RegistroNotificacionCorreo regSel)
            {
                DialogoFluent.Show(win, "Seleccione un registro de la bitácora para reenviarlo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var nuevaCfg = LeerConfigActualUI();
            DatabaseService.GuardarConfiguracionSmtpCorreo(nuevaCfg);

            // Si el gerente ya tiene correo actualizado en Directorios, actualizarlo antes de reenviar
            if (string.IsNullOrWhiteSpace(regSel.CorreoDestino))
            {
                var encMatch = DatabaseService.ObtenerEncargados()
                    .FirstOrDefault(e => string.Equals(e.Nombre, regSel.GerenteNombre, StringComparison.OrdinalIgnoreCase));
                if (encMatch != null && !string.IsNullOrWhiteSpace(encMatch.Correo))
                    regSel.CorreoDestino = encMatch.Correo.Trim();
            }

            btnReenviarSel.IsEnabled = false;
            try
            {
                var (ok, msj) = await DatabaseService.EnviarNotificacionCorreoSmtpInternoAsync(regSel, nuevaCfg);
                CargarTablaBitacora();
                DialogoFluent.Show(
                    win,
                    ok ? $"Notificación reenviada con éxito.\n{msj}" : $"No se pudo enviar:\n{msj}",
                    ok ? "Reenviado" : "Error al Reenviar",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            finally
            {
                btnReenviarSel.IsEnabled = true;
            }
        };

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = "Cerrar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 6, 18, 6)
        };
        DockPanel.SetDock(btnCerrar, Dock.Right);
        btnCerrar.Click += (_, _) => win.Close();
        footer.Children.Add(btnCerrar);
        footer.Children.Add(new TextBlock
        {
            Text = "Tip: Asigne el correo corporativo de cada gerente en '8. Directorios → Personal y Departamentos → Correo Corporativo'.",
            FontSize = 11.5,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        CargarTablaBitacora();
        win.Content = root;
        win.ShowDialog();
    }

    // =========================================================================
    // BANDEJA DE CORREOS ENTRANTES (IMAP GMAIL -> SOLICITUD AUTOMÁTICA) Y PLANTILLA
    // =========================================================================

    private void BtnAbrirBandejaCorreosSolicitud_Click(object sender, RoutedEventArgs e)
    {
        MostrarModalBandejaCorreosSolicitud();
    }

    private async void BtnEnviarPlantillaCorreoAGerentes_Click(object sender, RoutedEventArgs e)
    {
        var cfg = DatabaseService.ObtenerConfiguracionSmtpCorreo();
        if (!cfg.TieneCredencialesCompletas)
        {
            DialogoFluent.Show(
                this,
                "Primero configure la cuenta Gmail y su Contraseña de Aplicación de 16 letras en 'Configurar Correo Gmail (Gerentes)'.",
                "Configuración Requerida",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            MostrarModalConfiguracionYBitacoraCorreoGerentes();
            return;
        }

        var encargadosConCorreo = DatabaseService.ObtenerEncargados()
            .Where(x => !string.IsNullOrWhiteSpace(x.Correo) && x.Correo.Contains('@'))
            .ToList();

        string listaDestinos = encargadosConCorreo.Count > 0
            ? string.Join("\n", encargadosConCorreo.Select(x => $"• {x.Nombre} ({x.DepartamentoNombre}) → {x.Correo}"))
            : $"• {cfg.CorreoCopiaFijaGerencia} (Correo general de gerencia configurado)";

        var resp = DialogoFluent.Show(
            this,
            $"Se enviará por correo el archivo 'Plantilla_Solicitud_Compras_Lebaref.csv' junto con las instrucciones para que los gerentes puedan enviar sus solicitudes (con asunto SOLICITUD o pegando su lista tipo WhatsApp) a:\n\n{listaDestinos}\n\n¿Desea enviar la plantilla ahora?",
            "Enviar Plantilla de Solicitud a Gerentes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp != MessageBoxResult.Yes) return;

        if (sender is System.Windows.Controls.Button btn) btn.IsEnabled = false;
        try
        {
            var (exito, detalle) = await DatabaseService.EnviarPlantillaSolicitudAGerentesPorCorreoAsync();
            DialogoFluent.Show(
                this,
                detalle,
                exito ? "Plantilla Enviada por Correo" : "Aviso al Enviar Plantilla",
                MessageBoxButton.OK,
                exito ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            if (sender is System.Windows.Controls.Button btnRestore) btnRestore.IsEnabled = true;
        }
    }

    private void MostrarModalBandejaCorreosSolicitud()
    {
        var cfg = DatabaseService.ObtenerConfiguracionSmtpCorreo();

        var win = new FluentWindow
        {
            Title = "Bandeja de Correos Entrantes (Solicitudes por Correo / IMAP Gmail)",
            Width = 1120,
            Height = 760,
            MinWidth = 920,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ExtendsContentIntoTitleBar = true
        };

        var root = new Grid { Margin = new Thickness(18, 12, 18, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new TitleBar
        {
            Title = "Bandeja Anti-Traspapeleo y Creación Automática de Solicitudes por Correo (Gmail IMAP)",
            ShowMaximize = true,
            ShowMinimize = false
        };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        // Panel superior de reglas y acciones rápidas
        var cardReglas = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 10),
            BorderThickness = new Thickness(1)
        };
        cardReglas.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        cardReglas.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        var stackTop = new StackPanel();

        var filaReglas1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var chkImapActivo = new CheckBox
        {
            Content = "Revisar buzón Gmail automáticamente (cada 2 min)",
            IsChecked = cfg.ImapRecepcionActiva,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0)
        };
        var chkAutoCrearAsunto = new CheckBox
        {
            Content = "Auto-crear Solicitud (SOL-xxxx) si el Asunto contiene:",
            IsChecked = cfg.ImapAutoCrearPorAsunto,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var txtPalabrasClave = new System.Windows.Controls.TextBox
        {
            Text = string.IsNullOrWhiteSpace(cfg.PalabrasClaveAsuntoImap)
                ? "SOLICITUD, COTIZAR, COTIZACION, PEDIDO, REQUERIMIENTO"
                : cfg.PalabrasClaveAsuntoImap,
            Width = 340,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Palabras clave separadas por coma. Si el asunto del correo contiene cualquiera de estas palabras y tiene partidas válidas, se creará la Solicitud automáticamente."
        };
        var btnGuardarReglas = new Wpf.Ui.Controls.Button
        {
            Content = "Guardar Reglas",
            Appearance = ControlAppearance.Secondary,
            Padding = new Thickness(12, 4, 12, 4)
        };

        filaReglas1.Children.Add(chkImapActivo);
        filaReglas1.Children.Add(chkAutoCrearAsunto);
        filaReglas1.Children.Add(txtPalabrasClave);
        filaReglas1.Children.Add(btnGuardarReglas);
        stackTop.Children.Add(filaReglas1);

        var filaAcciones = new DockPanel { LastChildFill = false };
        var btnSincronizarAhora = new Wpf.Ui.Controls.Button
        {
            Content = "🔄 Revisar Buzón Gmail Ahora",
            Appearance = ControlAppearance.Primary,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 10, 0)
        };
        var cboFiltroBandeja = new ComboBox
        {
            Width = 210,
            SelectedIndex = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        cboFiltroBandeja.Items.Add(new ComboBoxItem { Content = "Todos los correos recibidos", Tag = "TODOS" });
        cboFiltroBandeja.Items.Add(new ComboBoxItem { Content = "En Bandeja (Por Revisar)", Tag = "PENDIENTES" });
        cboFiltroBandeja.Items.Add(new ComboBoxItem { Content = "Convertidos en Solicitud", Tag = "PROCESADOS" });

        var txtEstadoSync = new TextBlock
        {
            Text = $"Cuenta vinculada: {cfg.CorreoRemitenteGmail}",
            FontSize = 11.5,
            Opacity = 0.78,
            VerticalAlignment = VerticalAlignment.Center
        };

        var btnEnviarPlantillaGerentes = new Wpf.Ui.Controls.Button
        {
            Content = "📤 Enviar Plantilla a Gerentes por Correo",
            Appearance = ControlAppearance.Secondary,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        var btnDescargarPlantillaLocal = new Wpf.Ui.Controls.Button
        {
            Content = "📄 Descargar Plantilla CSV",
            Appearance = ControlAppearance.Secondary,
            Padding = new Thickness(12, 6, 12, 6)
        };

        DockPanel.SetDock(btnSincronizarAhora, Dock.Left);
        DockPanel.SetDock(cboFiltroBandeja, Dock.Left);
        DockPanel.SetDock(txtEstadoSync, Dock.Left);
        DockPanel.SetDock(btnDescargarPlantillaLocal, Dock.Right);
        DockPanel.SetDock(btnEnviarPlantillaGerentes, Dock.Right);

        filaAcciones.Children.Add(btnSincronizarAhora);
        filaAcciones.Children.Add(cboFiltroBandeja);
        filaAcciones.Children.Add(txtEstadoSync);
        filaAcciones.Children.Add(btnDescargarPlantillaLocal);
        filaAcciones.Children.Add(btnEnviarPlantillaGerentes);
        stackTop.Children.Add(filaAcciones);

        cardReglas.Child = stackTop;
        Grid.SetRow(cardReglas, 1);
        root.Children.Add(cardReglas);

        // Cuerpo dividido: Izquierda lista de correos, Derecha previsualización y conversión en 1 clic
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dgCorreos = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeight = 34
        };
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Fecha", Binding = new System.Windows.Data.Binding("FechaCorreo"), Width = 122 });
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Remitente", Binding = new System.Windows.Data.Binding("RemitenteNombre"), Width = 140 });
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Depto", Binding = new System.Windows.Data.Binding("DepartamentoDetectadoNombre"), Width = 105 });
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Asunto del Correo", Binding = new System.Windows.Data.Binding("Asunto"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Ítems", Binding = new System.Windows.Data.Binding("PartidasDetectadasCount"), Width = 55 });
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new System.Windows.Data.Binding("EstadoBandeja"), Width = 150 });
        dgCorreos.Columns.Add(new DataGridTextColumn { Header = "Folio SOL", Binding = new System.Windows.Data.Binding("SolicitudCreadaFolio"), Width = 85 });

        Grid.SetColumn(dgCorreos, 0);
        bodyGrid.Children.Add(dgCorreos);

        // Panel derecho: Detalle del correo seleccionado y conversión rápida
        var cardDetalle = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            BorderThickness = new Thickness(1)
        };
        cardDetalle.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        cardDetalle.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        var gridDetalle = new Grid();
        gridDetalle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridDetalle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridDetalle.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        gridDetalle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridDetalle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var txtTituloDetalle = new TextBlock
        {
            Text = "Seleccione un correo de la lista para revisar sus partidas",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(txtTituloDetalle, 0);
        gridDetalle.Children.Add(txtTituloDetalle);

        var txtSubDetalle = new TextBlock
        {
            Text = "Puede editar la lista de materiales antes de convertirla en Solicitud (formato WhatsApp o plantilla CSV).",
            FontSize = 11.5,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(txtSubDetalle, 1);
        gridDetalle.Children.Add(txtSubDetalle);

        var txtCuerpoEditable = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(txtCuerpoEditable, 2);
        gridDetalle.Children.Add(txtCuerpoEditable);

        // Selectores de Departamento, Solicitante y Proyecto para asignar al crear la solicitud
        var gridMetadatos = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        gridMetadatos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridMetadatos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        gridMetadatos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridMetadatos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridMetadatos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var stackDepto = new StackPanel();
        stackDepto.Children.Add(new TextBlock { Text = "Departamento:", FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
        var cboDeptoCorreo = new ComboBox { DisplayMemberPath = "Nombre", SelectedValuePath = "Id", ItemsSource = DatabaseService.ObtenerDepartamentos() };
        stackDepto.Children.Add(cboDeptoCorreo);
        Grid.SetRow(stackDepto, 0);
        Grid.SetColumn(stackDepto, 0);
        gridMetadatos.Children.Add(stackDepto);

        var stackEncargado = new StackPanel();
        stackEncargado.Children.Add(new TextBlock { Text = "Gerente / Solicitante:", FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
        var cboEncargadoCorreo = new ComboBox { DisplayMemberPath = "Nombre", SelectedValuePath = "Id", ItemsSource = DatabaseService.ObtenerEncargados() };
        stackEncargado.Children.Add(cboEncargadoCorreo);
        Grid.SetRow(stackEncargado, 0);
        Grid.SetColumn(stackEncargado, 2);
        gridMetadatos.Children.Add(stackEncargado);

        var stackProy = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stackProy.Children.Add(new TextBlock { Text = "Proyecto / Destino (Opcional):", FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) });
        var cboProyectoCorreo = new ComboBox { DisplayMemberPath = "Nombre", SelectedValuePath = "Id", ItemsSource = DatabaseService.ObtenerProyectos() };
        stackProy.Children.Add(cboProyectoCorreo);
        Grid.SetRow(stackProy, 1);
        Grid.SetColumn(stackProy, 0);
        Grid.SetColumnSpan(stackProy, 3);
        gridMetadatos.Children.Add(stackProy);

        Grid.SetRow(gridMetadatos, 3);
        gridDetalle.Children.Add(gridMetadatos);

        var stackBotonesDetalle = new WrapPanel();
        var btnConvertirEnSol = new Wpf.Ui.Controls.Button
        {
            Content = "✓ Crear Solicitud (SOL-xxxx) con este Correo",
            Appearance = ControlAppearance.Primary,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 6),
            IsEnabled = false
        };
        var btnAbrirSolExistente = new Wpf.Ui.Controls.Button
        {
            Content = "📂 Ir a Solicitud Creada",
            Appearance = ControlAppearance.Secondary,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 6),
            Visibility = Visibility.Collapsed
        };
        var btnDescartarCorreo = new Wpf.Ui.Controls.Button
        {
            Content = "✕ Descartar",
            Appearance = ControlAppearance.Secondary,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6),
            IsEnabled = false
        };
        stackBotonesDetalle.Children.Add(btnConvertirEnSol);
        stackBotonesDetalle.Children.Add(btnAbrirSolExistente);
        stackBotonesDetalle.Children.Add(btnDescartarCorreo);

        Grid.SetRow(stackBotonesDetalle, 4);
        gridDetalle.Children.Add(stackBotonesDetalle);

        cardDetalle.Child = gridDetalle;
        Grid.SetColumn(cardDetalle, 2);
        bodyGrid.Children.Add(cardDetalle);

        Grid.SetRow(bodyGrid, 2);
        root.Children.Add(bodyGrid);

        void GuardarReglasImapUI()
        {
            var c = DatabaseService.ObtenerConfiguracionSmtpCorreo();
            c.ImapRecepcionActiva = chkImapActivo.IsChecked == true;
            c.ImapAutoCrearPorAsunto = chkAutoCrearAsunto.IsChecked == true;
            c.PalabrasClaveAsuntoImap = string.IsNullOrWhiteSpace(txtPalabrasClave.Text)
                ? "SOLICITUD, COTIZAR, COTIZACION, PEDIDO, REQUERIMIENTO"
                : txtPalabrasClave.Text.Trim();
            DatabaseService.GuardarConfiguracionSmtpCorreo(c);
        }

        void CargarListaBandeja()
        {
            string tagFiltro = (cboFiltroBandeja.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TODOS";
            var lista = DatabaseService.ObtenerCorreosEntrantesSolicitud(tagFiltro);
            dgCorreos.ItemsSource = lista;
            ActualizarBadgeBandejaCorreosSolicitud();
            if (lista.Count > 0 && dgCorreos.SelectedItem == null)
                dgCorreos.SelectedIndex = 0;
        }

        btnGuardarReglas.Click += (_, _) =>
        {
            GuardarReglasImapUI();
            MostrarToastDiscreto("Reglas de detección por asunto e IMAP guardadas.", "Reglas Guardadas");
        };

        cboFiltroBandeja.SelectionChanged += (_, _) => CargarListaBandeja();

        dgCorreos.SelectionChanged += (_, _) =>
        {
            if (dgCorreos.SelectedItem is not CorreoEntranteSolicitud sel)
            {
                txtTituloDetalle.Text = "Seleccione un correo de la lista para revisar sus partidas";
                txtSubDetalle.Text = "Puede editar la lista de materiales antes de convertirla en Solicitud.";
                txtCuerpoEditable.Text = string.Empty;
                btnConvertirEnSol.IsEnabled = false;
                btnDescartarCorreo.IsEnabled = false;
                btnAbrirSolExistente.Visibility = Visibility.Collapsed;
                return;
            }

            txtTituloDetalle.Text = $"{sel.Asunto}";
            txtSubDetalle.Text = $"De: {sel.RemitenteNombre} <{sel.RemitenteCorreo}> · Fecha: {sel.FechaCorreo} · Estado: {sel.EstadoBandeja}";
            txtCuerpoEditable.Text = !string.IsNullOrWhiteSpace(sel.ContenidoAdjuntoCsv)
                ? sel.ContenidoAdjuntoCsv
                : sel.CuerpoTexto;

            cboDeptoCorreo.SelectedValue = sel.DepartamentoDetectadoId;
            cboEncargadoCorreo.SelectedValue = sel.EncargadoDetectadoId;
            cboProyectoCorreo.SelectedValue = sel.ProyectoDetectadoId;

            bool yaTieneSol = sel.SolicitudCreadaId > 0;
            btnConvertirEnSol.IsEnabled = !yaTieneSol;
            btnConvertirEnSol.Content = yaTieneSol
                ? $"✓ Ya convertido ({sel.SolicitudCreadaFolio})"
                : "✓ Crear Solicitud (SOL-xxxx) con este Correo";
            btnDescartarCorreo.IsEnabled = !yaTieneSol && !string.Equals(sel.EstadoBandeja, "Descartado", StringComparison.OrdinalIgnoreCase);

            if (yaTieneSol)
            {
                btnAbrirSolExistente.Visibility = Visibility.Visible;
                btnAbrirSolExistente.Content = $"📂 Abrir {sel.SolicitudCreadaFolio}";
            }
            else
            {
                btnAbrirSolExistente.Visibility = Visibility.Collapsed;
            }
        };

        btnSincronizarAhora.Click += async (_, _) =>
        {
            GuardarReglasImapUI();
            btnSincronizarAhora.IsEnabled = false;
            btnSincronizarAhora.Content = "Conectando a Gmail IMAP...";
            txtEstadoSync.Text = "Revisando buzón de entrada en imap.gmail.com:993...";
            try
            {
                var (ok, msj, autoCreadas, enBandeja) = await DatabaseService.SincronizarCorreosEntrantesImapAsync();
                txtEstadoSync.Text = msj;
                CargarListaBandeja();
                if (autoCreadas > 0)
                    RefrescarSolicitudes();

                if (!ok)
                {
                    DialogoFluent.Show(win, msj, "Aviso de Conexión IMAP", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                btnSincronizarAhora.IsEnabled = true;
                btnSincronizarAhora.Content = "🔄 Revisar Buzón Gmail Ahora";
            }
        };

        btnConvertirEnSol.Click += (_, _) =>
        {
            if (dgCorreos.SelectedItem is not CorreoEntranteSolicitud sel) return;

            int? deptoId = cboDeptoCorreo.SelectedValue as int?;
            int? encId = cboEncargadoCorreo.SelectedValue as int?;
            int? proyId = cboProyectoCorreo.SelectedValue as int?;

            var solCreada = DatabaseService.ConvertirCorreoBandejaEnSolicitud(
                sel.Id,
                deptoId,
                encId,
                proyId,
                txtCuerpoEditable.Text);

            if (solCreada == null)
            {
                DialogoFluent.Show(win, "No se detectaron partidas válidas en el correo o texto editado. Verifique que tenga al menos un renglón con cantidad y descripción.", "No se pudo convertir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefrescarSolicitudes(solCreada.Id);
            CargarListaBandeja();
            DialogoFluent.Show(
                win,
                $"Se generó exitosamente la solicitud {solCreada.Folio} con {solCreada.TotalSkus} partida(s).\n\nAdemás se envió acuse automático por correo al solicitante con su folio {solCreada.Folio}.",
                $"Solicitud {solCreada.Folio} Creada",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };

        btnAbrirSolExistente.Click += (_, _) =>
        {
            if (dgCorreos.SelectedItem is not CorreoEntranteSolicitud sel || sel.SolicitudCreadaId <= 0) return;
            int idSol = sel.SolicitudCreadaId;
            win.Close();
            AppMediator.Instance.Publicar(new NavegarModuloMessage(ModuloSistema.Solicitudes));
            RefrescarSolicitudes(idSol);
        };

        btnDescartarCorreo.Click += (_, _) =>
        {
            if (dgCorreos.SelectedItem is not CorreoEntranteSolicitud sel) return;
            DatabaseService.DescartarCorreoEntranteSolicitud(sel.Id);
            CargarListaBandeja();
        };

        btnEnviarPlantillaGerentes.Click += (_, _) => BtnEnviarPlantillaCorreoAGerentes_Click(btnEnviarPlantillaGerentes, new RoutedEventArgs());
        btnDescargarPlantillaLocal.Click += (_, _) => BtnExportarPlantillaSolicitud_Click(btnDescargarPlantillaLocal, new RoutedEventArgs());

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var btnCerrar = new System.Windows.Controls.Button
        {
            Content = "Cerrar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 6, 18, 6)
        };
        DockPanel.SetDock(btnCerrar, Dock.Right);
        btnCerrar.Click += (_, _) => win.Close();
        footer.Children.Add(btnCerrar);
        footer.Children.Add(new TextBlock
        {
            Text = "Tip: Los gerentes pueden adjuntar la plantilla CSV o escribir su lista estilo WhatsApp (ej. '10 pza Guantes de nitrilo') en el cuerpo del correo.",
            FontSize = 11.5,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        CargarListaBandeja();
        win.Content = root;
        win.ShowDialog();
    }

    // =========================================================================
    // ENVIAR COMPARATIVA POR CORREO (TABLA EN EL CUERPO + PDF ADJUNTO)
    // =========================================================================

    private void BtnEnviarComparativaPorCorreo_Click(object sender, RoutedEventArgs e)
    {
        if (CboCompSolicitud.SelectedItem is not Solicitud sol || _renglonesComparativa.Count == 0)
        {
            DialogoFluent.Show(this, "Seleccione primero una solicitud con artículos para enviar su Cuadro Comparativo.", "Sin solicitud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cfg = DatabaseService.ObtenerConfiguracionSmtpCorreo();
        if (!cfg.TieneCredencialesCompletas)
        {
            DialogoFluent.Show(this, "Primero configure la cuenta Gmail y su Contraseña de Aplicación de 16 letras.", "Configuración Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            MostrarModalConfiguracionYBitacoraCorreoGerentes();
            return;
        }

        // Guardar lo capturado antes de generar el documento
        SincronizarRenglonActualDesdeTarjetas();
        DatabaseService.GuardarBorradorComparativa(sol.Id, _renglonesComparativa.ToList(), sol.Folio);
        var renglones = _renglonesComparativa.ToList();

        // Destinatario sugerido: correo del gerente solicitante
        var enc = DatabaseService.ObtenerEncargados().FirstOrDefault(x => x.Id == sol.EncargadoId);
        string correoGerente = enc?.Correo?.Trim() ?? string.Empty;

        var win = new Window
        {
            Title = $"Enviar Comparativa {sol.Folio} por Correo",
            Width = 620,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            Background = Tk("ApplicationBackgroundBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        root.Children.Add(new TextBlock { Text = $"Enviar Cuadro Comparativo {sol.Folio}", FontSize = 17, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = $"{sol.ProyectoMostrar} · {renglones.Count} partida(s) · Desde: {cfg.CorreoRemitenteGmail}",
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 2, 0, 14),
            TextWrapping = TextWrapping.Wrap
        });

        TextBlock Etiqueta(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) };

        root.Children.Add(Etiqueta("Para (separe varios correos con coma):"));
        var txtPara = new System.Windows.Controls.TextBox { Text = correoGerente, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(txtPara);

        root.Children.Add(Etiqueta("CC (opcional):"));
        var txtCc = new System.Windows.Controls.TextBox { Text = cfg.CorreoCopiaFijaGerencia ?? string.Empty, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(txtCc);

        root.Children.Add(Etiqueta("Asunto:"));
        var txtAsunto = new System.Windows.Controls.TextBox
        {
            Text = $"Cuadro Comparativo de Cotizaciones {sol.Folio} — {sol.ProyectoMostrar}",
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(txtAsunto);

        root.Children.Add(Etiqueta("Mensaje:"));
        string saludo = enc != null ? $"Hola {enc.Nombre}," : "Hola,";
        var txtMensaje = new System.Windows.Controls.TextBox
        {
            Text = $"{saludo}\n\nTe comparto el cuadro comparativo de cotizaciones de la solicitud {sol.Folio} con los precios de los proveedores concursantes, para tu revisión.\n\nQuedo atento a tus comentarios.",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(txtMensaje);

        var chkTabla = new CheckBox { Content = "Incluir la tabla comparativa dentro del cuerpo del correo", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        var chkPdf = new CheckBox { Content = $"Adjuntar PDF (Comparativa_{sol.Folio}.pdf, igual al impreso)", IsChecked = true, Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(chkTabla);
        root.Children.Add(chkPdf);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancelar = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnEnviar = new Wpf.Ui.Controls.Button { Content = "✉ Enviar Correo", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };
        botones.Children.Add(btnCancelar);
        botones.Children.Add(btnEnviar);
        root.Children.Add(botones);

        btnCancelar.Click += (_, _) => win.Close();
        btnEnviar.Click += async (_, _) =>
        {
            if (chkTabla.IsChecked != true && chkPdf.IsChecked != true)
            {
                DialogoFluent.Show(win, "Elija al menos una opción: tabla en el cuerpo o PDF adjunto.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtPara.Text) || !txtPara.Text.Contains('@'))
            {
                DialogoFluent.Show(win, "Indique al menos un correo destinatario.", "Falta destinatario", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btnEnviar.IsEnabled = false;
            btnEnviar.Content = "Enviando...";
            try
            {
                string html = string.Empty;
                byte[]? pdf = null;

                if (chkTabla.IsChecked == true)
                    html = BarcodePrintService.GenerarHtmlTablasDocumento(BarcodePrintService.ConstruirHojaComparativa(sol, renglones));
                if (chkPdf.IsChecked == true)
                    pdf = BarcodePrintService.GenerarPdfBytes(BarcodePrintService.ConstruirHojaComparativa(sol, renglones));

                var (ok, msj) = await DatabaseService.EnviarComparativaPorCorreoAsync(
                    txtPara.Text,
                    txtCc.Text,
                    txtAsunto.Text.Trim(),
                    txtMensaje.Text,
                    html,
                    pdf,
                    $"Comparativa_{sol.Folio}.pdf");

                if (ok)
                {
                    win.Close();
                    MostrarToastDiscreto(msj, $"Comparativa {sol.Folio} enviada");
                }
                else
                {
                    DialogoFluent.Show(win, $"No se pudo enviar el correo:\n\n{msj}", "Error al enviar", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                DialogoFluent.Show(win, $"No se pudo generar o enviar la comparativa:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnEnviar.IsEnabled = true;
                btnEnviar.Content = "✉ Enviar Correo";
            }
        };

        win.Content = root;
        win.ShowDialog();
    }
}
