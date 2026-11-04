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
}
