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

    private void InicializarRolYCheckpoints()
    {
        string rolGuardado = DatabaseService.ObtenerRolOperativoActivo();
        AplicarRolOperativo(rolGuardado, navegarSiPestanaOculta: false);
        ActualizarBadgeNotificacionesCheckpoints();
    }

    private void AplicarRolOperativo(string rol, bool navegarSiPestanaOculta = true)
    {
        _rolOperativoActual = string.Equals(rol, "Almacén", StringComparison.OrdinalIgnoreCase)
            ? "Almacén"
            : "Compras";

        DatabaseService.GuardarRolOperativoActivo(_rolOperativoActual);

        bool esAlmacen = EsModoAlmacen;
        var visPrecios = esAlmacen ? Visibility.Collapsed : Visibility.Visible;

        // Actualizar recurso dinámico global para ocultar/mostrar costos en tarjetas y paneles
        Application.Current.Resources["VisibilidadPreciosPerfil"] = visPrecios;

        if (BtnRolOperativo != null)
        {
            BtnRolOperativo.Content = $"Rol: {_rolOperativoActual}";
            BtnRolOperativo.Appearance = esAlmacen ? ControlAppearance.Secondary : ControlAppearance.Primary;
        }

        // Columnas de precios en DataGrids (no participan en el árbol visual directo de DynamicResource)
        if (ColDashboardOcTotal != null) ColDashboardOcTotal.Visibility = visPrecios;
        if (ColOrdenesTotal != null) ColOrdenesTotal.Visibility = visPrecios;

        // Menús superiores y navegación lateral según perfil
        if (MenuComprasTop != null) MenuComprasTop.Visibility = visPrecios;
        if (MenuReportesTop != null) MenuReportesTop.Visibility = visPrecios;

        if (NavComparativa != null) NavComparativa.Visibility = visPrecios;
        if (NavFacturasXml != null) NavFacturasXml.Visibility = visPrecios;
        if (NavAnalisis != null) NavAnalisis.Visibility = visPrecios;

        if (NavOrdenes != null && !_sidebarColapsado)
            NavOrdenes.Content = esAlmacen ? "Recepción OC" : "Órdenes de Compra";

        // Ajustar KPI Grid en Inicio (4 KPIs en Almacén, 5 en Compras)
        if (UgKpisInicio != null && ActualWidth >= 1100)
            UgKpisInicio.Columns = esAlmacen ? 4 : 5;

        // Si estaba en una pestaña exclusiva de Compras (Comparativa=3, Facturas=6, Análisis=7), redirigir a Inicio
        if (navegarSiPestanaOculta && esAlmacen && MainTabControl != null)
        {
            int idx = MainTabControl.SelectedIndex;
            if (idx is 3 or 6 or 7)
                CambiarSeccion(0);
        }

        // Refrescar ficha de producto si hay uno cargado para ocultar/mostrar costos
        if (_productoEnFicha != null)
            ActualizarFichaProducto(_productoEnFicha);

        ActualizarBadgeNotificacionesCheckpoints();
    }

    private void BtnRolOperativo_Click(object sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Seleccionar Rol Operativo en el Sistema",
            Width = 520,
            Height = 435,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Cambiar Vista Operativa por Perfil",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Seleccione con qué perfil está operando hoy. Cada perfil ajusta las etapas visibles, las notificaciones de checkpoints y la privacidad de costos.",
            FontSize = 12,
            Foreground = Tk("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var rbCompras = new RadioButton
        {
            IsChecked = !EsModoAlmacen,
            Margin = new Thickness(0, 0, 0, 10),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Compras (Abastecimiento y Control Administrativo)", FontWeight = FontWeights.SemiBold, FontSize = 13 },
                    new TextBlock
                    {
                        Text = "Aprueba solicitudes, cotiza en Comparativa, elige proveedores ganadores, emite OC, registra Vo.Bo./Pagos, finiquita parciales y sube Facturas XML. Visibilidad total de precios.",
                        FontSize = 11.5,
                        Foreground = Tk("TextFillColorSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 430
                    }
                }
            }
        };

        var rbAlmacen = new RadioButton
        {
            IsChecked = EsModoAlmacen,
            Margin = new Thickness(0, 0, 0, 14),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Almacén (Recepción, Bodega, Salidas y Herramientas)", FontWeight = FontWeights.SemiBold, FontSize = 13 },
                    new TextBlock
                    {
                        Text = "Recibe mercancía (con o sin OC previa), captura folios de remisión/factura impresa, ubica en anaqueles, despacha Salidas (SAL) desde Solicitud u OC y controla Préstamos (PRE). Sin precios ni costos visibles.",
                        FontSize = 11.5,
                        Foreground = Tk("TextFillColorSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 430
                    }
                }
            }
        };

        root.Children.Add(rbCompras);
        root.Children.Add(rbAlmacen);

        var bdrFuturos = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(6),
            Background = Tk("SubtleFillColorSecondaryBrush"),
            BorderBrush = Tk("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 16),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Agentes externos hoy (Perfiles preparados para notificaciones futuras):",
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 4)
                    },
                    new TextBlock
                    {
                        Text = "• Solicitante / Proyectos: Recibe aviso al aprobarse su solicitud, al generarse OC y al llegar su material a Almacén.\n" +
                               "• Dirección: Recibe aviso cuando Compras emite una OC pendiente de Vo.Bo.\n" +
                               "• Finanzas: Recibe aviso de OC autorizadas para pago y facturas CFDI XML vinculadas.",
                        FontSize = 11,
                        Foreground = Tk("TextFillColorSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        root.Children.Add(bdrFuturos);

        var pnlBotones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancelar = new System.Windows.Controls.Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnAplicar = new Wpf.Ui.Controls.Button { Content = "Aplicar Rol", Appearance = ControlAppearance.Primary, Padding = new Thickness(16, 6, 16, 6) };

        btnCancelar.Click += (_, _) => win.DialogResult = false;
        btnAplicar.Click += (_, _) =>
        {
            string nuevoRol = rbAlmacen.IsChecked == true ? "Almacén" : "Compras";
            AplicarRolOperativo(nuevoRol);
            win.DialogResult = true;
        };

        pnlBotones.Children.Add(btnCancelar);
        pnlBotones.Children.Add(btnAplicar);
        root.Children.Add(pnlBotones);

        win.Content = root;
        win.ShowDialog();
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
        var win = new Window
        {
            Title = $"Checkpoints y Notificaciones de Etapa — Rol actual: {_rolOperativoActual}",
            Width = 920,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Tk("ControlFillColorDefaultBrush"),
            Foreground = Tk("TextFillColorPrimaryBrush")
        };

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var cboFiltroRol = new ComboBox { Width = 220, Margin = new Thickness(8, 0, 8, 0) };
        cboFiltroRol.Items.Add($"Para mi rol ({_rolOperativoActual})");
        cboFiltroRol.Items.Add("Todos los roles (Vista completa)");
        cboFiltroRol.Items.Add("Compras");
        cboFiltroRol.Items.Add("Almacén");
        cboFiltroRol.Items.Add("Solicitante");
        cboFiltroRol.Items.Add("Dirección");
        cboFiltroRol.Items.Add("Finanzas");
        cboFiltroRol.SelectedIndex = 0;

        var btnMarcarLeidas = new System.Windows.Controls.Button
        {
            Content = "Marcar leídos",
            Padding = new Thickness(12, 5, 12, 5)
        };

        var rightBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightBar.Children.Add(new TextBlock { Text = "Ver avisos de:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        rightBar.Children.Add(cboFiltroRol);
        rightBar.Children.Add(btnMarcarLeidas);
        DockPanel.SetDock(rightBar, Dock.Right);
        header.Children.Add(rightBar);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "Historial de Checkpoints y Transiciones de Etapa", FontSize = 16, FontWeight = FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Registro de cada cambio de etapa (CP-1 a CP-8) y qué perfiles reciben notificación hoy y a futuro.",
            FontSize = 11.5,
            Foreground = Tk("TextFillColorSecondaryBrush")
        });
        header.Children.Add(titleStack);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var dg = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false
        };
        dg.Columns.Add(new DataGridTextColumn { Header = "Fecha", Binding = new System.Windows.Data.Binding("FechaHora"), Width = 125, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "CP", Binding = new System.Windows.Data.Binding("CheckpointCodigo"), Width = 70, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Etapa", Binding = new System.Windows.Data.Binding("Etapa"), Width = 135, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Folio", Binding = new System.Windows.Data.Binding("FolioReferencia"), Width = 120, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Transición de Estatus", Binding = new System.Windows.Data.Binding("TransicionTexto"), Width = 190, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Notifica a", Binding = new System.Windows.Data.Binding("RolesDestino"), Width = 140, ElementStyle = CeldaRecortable() });
        dg.Columns.Add(new DataGridTextColumn { Header = "Detalle", Binding = new System.Windows.Data.Binding("Mensaje"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = CeldaRecortable() });

        void RecargarTabla()
        {
            string sel = cboFiltroRol.SelectedIndex switch
            {
                0 => _rolOperativoActual,
                1 => "Todos",
                _ => cboFiltroRol.SelectedItem?.ToString() ?? "Todos"
            };
            dg.ItemsSource = DatabaseService.ObtenerNotificacionesCheckpoint(sel, 120);
        }

        cboFiltroRol.SelectionChanged += (_, _) => RecargarTabla();
        btnMarcarLeidas.Click += (_, _) =>
        {
            DatabaseService.MarcarNotificacionesLeidas(_rolOperativoActual);
            ActualizarBadgeNotificacionesCheckpoints();
            RecargarTabla();
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

        CargarOrdenCompraEnSalidaPos(oc);
        CambiarSeccion(5);
    }

    private void CargarOrdenCompraEnSalidaPos(OrdenCompra oc)
    {
        var prodsDict = (_cacheProductos.Count > 0 ? _cacheProductos : DatabaseService.ObtenerProductos()).ToDictionary(p => p.Id);
        _partidasSalidaPos.Clear();

        foreach (var part in oc.Partidas)
        {
            if (part.ProductoId > 0 && prodsDict.TryGetValue(part.ProductoId, out var prod))
            {
                decimal cantBase = part.CantidadRecibida > 0 ? part.CantidadRecibida : part.CantidadPedida;
                decimal factor = part.FactorConversion > 0 ? part.FactorConversion : (prod.FactorConversion > 0 ? prod.FactorConversion : 1m);
                decimal cantAlmacen = Math.Round(cantBase * factor, 2);
                if (cantAlmacen > 0 && prod.StockActual > 0)
                    AgregarProductoAValeSalidaPos(prod, Math.Min(cantAlmacen, prod.StockActual));
            }
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
        _partidasSalidaPos.Clear();

        int agregados = 0;
        foreach (var part in sol.Partidas)
        {
            if (part.ProductoId > 0 && prodsDict.TryGetValue(part.ProductoId, out var prod) && prod.StockActual > 0)
            {
                decimal factor = prod.FactorConversion > 0 ? prod.FactorConversion : 1m;
                decimal cantRequerida = Math.Round(part.CantidadSolicitada * factor, 2);
                decimal cantEntregar = Math.Min(cantRequerida, prod.StockActual);
                if (cantEntregar > 0)
                {
                    AgregarProductoAValeSalidaPos(prod, cantEntregar);
                    agregados++;
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
            DialogoFluent.Show(this,
                $"Ninguno de los artículos de la solicitud {sol.Folio} tiene stock disponible (> 0) en este momento en Almacén.",
                "Sin stock disponible para surtir",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
            .Where(r => r.Proveedor2Id > 0 && !string.IsNullOrWhiteSpace(r.Proveedor2Nombre) && r.Proveedor2Nombre != "Sin asignar")
            .Select(r => Proveedor.ExtraerNombreComercialCorto(r.Proveedor2Nombre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var provs3 = _renglonesComparativa
            .Where(r => r.Proveedor3Id > 0 && !string.IsNullOrWhiteSpace(r.Proveedor3Nombre) && r.Proveedor3Nombre != "Sin asignar")
            .Select(r => Proveedor.ExtraerNombreComercialCorto(r.Proveedor3Nombre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ColCompTablaProv1.Header = provs1.Count == 1
            ? provs1[0]
            : (provs1.Count > 1 ? string.Join(" / ", provs1.Take(2)) : "Proveedor 1");

        ColCompTablaProv2.Header = provs2.Count == 1
            ? provs2[0]
            : (provs2.Count > 1 ? string.Join(" / ", provs2.Take(2)) : "Proveedor 2");

        ColCompTablaProv3.Header = provs3.Count == 1
            ? provs3[0]
            : (provs3.Count > 1 ? string.Join(" / ", provs3.Take(2)) : "Proveedor 3");

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

            nuevoProvId = DatabaseService.GuardarProveedor(new Proveedor
            {
                NombreComercial = nomCom,
                RazonSocial = razon,
                Rfc = rfc,
                Telefono = (txtTel.Text ?? string.Empty).Trim(),
                DiasEntregaPromedio = dias,
                CondicionesPago = "Contado"
            });
            win.DialogResult = true;
        };

        win.Content = root;
        if (win.ShowDialog() == true && nuevoProvId > 0)
        {
            var provs = DatabaseService.ObtenerProveedores();
            if (CboCompProv1 != null) CboCompProv1.ItemsSource = provs;
            if (CboCompProv2 != null) CboCompProv2.ItemsSource = provs;
            if (CboCompProv3 != null) CboCompProv3.ItemsSource = provs;
            if (DgDirProveedores != null) DgDirProveedores.ItemsSource = provs;

            var nuevoProv = provs.FirstOrDefault(p => p.Id == nuevoProvId);
            if (nuevoProv != null && DgComparativa.SelectedItem is CotizacionRenglonComparativo r && !r.TieneOcGenerada)
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
            r.UltimoCostoCompraConIva = Math.Round(prodVinculado.UltimoCostoCompra * 1.16m, 2);

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
                UltimoCostoCompra = costoSugeridoSinIva,
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
            string fProg = string.IsNullOrWhiteSpace(oc.FechaProgramadaPago) ? DateTime.Today.ToString("yyyy-MM-dd") : oc.FechaProgramadaPago;
            DatabaseService.ActualizarEstatusYCondicionOrdenCompra(oc.Id, "Pagada / Lista para Almacén", oc.CondicionPago, fProg);
            RegistrarTransicionCheckpoint("Etapa 3: Pago Finanzas", "CP-4A", oc.Folio, oc.Estatus, "Pagada / Lista para Almacén", "Compras, Almacén", $"Se confirmó pago de {oc.Folio} ({oc.ProveedorNombre}); lista para recepción en Almacén.");
            CargarTodoElSistema(conservarPosicionCatalogo: true);
            return;
        }

        // Si ya está lista para recibir en almacén, ir directo a Recepción con esa OC seleccionada
        ProcesarCodigoEscaneado(oc.Folio);
    }

    private void BtnDashboardOcCerrarPendiente_Click(object sender, RoutedEventArgs e)
    {
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
        if (TabDashboardCheckpointsCompras != null)
        {
            TabDashboardCheckpointsCompras.Header = $"Solicitudes, XML y Vigencias ({pendientes.Count})";
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
}
