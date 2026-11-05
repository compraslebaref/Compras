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
                ? $"{col.indiceColumna} · {AbreviarNombreProveedorSlot(col.nombreCorto, 20)} ({cot}/{tot})"
                : $"Proveedor {col.indiceColumna} ({cot}/{tot})";
            btn.ToolTip = col.provId > 0
                ? $"Opción {col.indiceColumna}: {col.nombreCorto} ({cot} de {tot} cotizados)"
                : $"Opción {col.indiceColumna} (Sin proveedor asignado)";
            btn.Appearance = _indiceSlotHojaProveedorActiva == col.indiceColumna
                ? Wpf.Ui.Controls.ControlAppearance.Primary
                : Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        ConfigurarBotonSlot(BtnHojaSlot1, cols[0]);
        ConfigurarBotonSlot(BtnHojaSlot2, cols[1]);
        ConfigurarBotonSlot(BtnHojaSlot3, cols[2]);

        if (WpBotonesSlotsExtraHoja != null)
        {
            WpBotonesSlotsExtraHoja.Children.Clear();
            for (int i = 3; i < cols.Count; i++)
            {
                var colExtra = cols[i];
                var btnEx = new Wpf.Ui.Controls.Button
                {
                    FontSize = 11.5,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 2, 6, 2),
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

        var filasHoja = _renglonesComparativa
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

    private void AlModificarCeldaHojaProveedor(RenglonHojaCotizacionProveedor filaEditada)
    {
        if (_sincronizandoHojaProveedor)
            return;

        var r = filaEditada.RenglonBase;

        if (CboHojaProveedor?.SelectedItem is Proveedor provHoja && provHoja.Id > 0)
        {
            filaEditada.ProveedorHojaId = provHoja.Id;
            filaEditada.ProveedorHojaNombre = provHoja.NombreMostrar;
        }

        int slotEfectivo = filaEditada.ObtenerSlotEfectivoLectura();
        if (slotEfectivo == 0 && CboHojaProveedor?.SelectedItem is Proveedor provAsig && provAsig.Id > 0 && filaEditada.PrecioSinIva > 0)
        {
            slotEfectivo = filaEditada.AsegurarSlotParaEscritura();
        }

        if (slotEfectivo >= 1 && slotEfectivo <= 3 && r.NumeroOpcionesVisibles < slotEfectivo)
            r.NumeroOpcionesVisibles = slotEfectivo;

        r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
        SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
        DatabaseService.GuardarCotizacionPartida(r);

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
        foreach (var r in _renglonesComparativa)
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

        foreach (var r in _renglonesComparativa)
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

        foreach (var r in _renglonesComparativa)
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

        if (DgMatrizComparativaRapida?.SelectedItem is CotizacionRenglonComparativo r &&
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
            RefrescarHojaProveedorYAnalisisComparativa();
        }
    }

    // --- MATRIZ COMPARATIVA LADO A LADO ---

    private void ConstruirMatrizComparativaRapida()
    {
        if (DgMatrizComparativaRapida == null)
            return;

        var p1 = ObtenerProveedorPredominanteSlot(1);
        var p2 = ObtenerProveedorPredominanteSlot(2);
        var p3 = ObtenerProveedorPredominanteSlot(3);
        int extraCount = _renglonesComparativa.SelectMany(r => r.ProveedoresExtra).Select(x => x.ProveedorId).Where(id => id > 0).Distinct().Count();

        if (ColMatrizProv1 != null) ColMatrizProv1.Header = $"P.U. {AbreviarNombreProveedorSlot(p1.nombreCorto, 15)}";
        if (ColMatrizProv2 != null) ColMatrizProv2.Header = $"P.U. {AbreviarNombreProveedorSlot(p2.nombreCorto, 15)}";
        if (ColMatrizProv3 != null) ColMatrizProv3.Header = extraCount > 0
            ? $"P.U. {AbreviarNombreProveedorSlot(p3.nombreCorto, 12)} (+{extraCount})"
            : $"P.U. {AbreviarNombreProveedorSlot(p3.nombreCorto, 15)}";

        var visTotal = ChkMatrizMostrarTotalConIva?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (ColMatrizTotal1 != null)
        {
            ColMatrizTotal1.Header = $"Total {AbreviarNombreProveedorSlot(p1.nombreCorto, 11)}";
            ColMatrizTotal1.Visibility = visTotal;
        }
        if (ColMatrizTotal2 != null)
        {
            ColMatrizTotal2.Header = $"Total {AbreviarNombreProveedorSlot(p2.nombreCorto, 11)}";
            ColMatrizTotal2.Visibility = visTotal;
        }
        if (ColMatrizTotal3 != null)
        {
            ColMatrizTotal3.Header = $"Total {AbreviarNombreProveedorSlot(p3.nombreCorto, 11)}";
            ColMatrizTotal3.Visibility = visTotal;
        }

        if (!ReferenceEquals(DgMatrizComparativaRapida.ItemsSource, _renglonesComparativa))
        {
            DgMatrizComparativaRapida.ItemsSource = _renglonesComparativa;
        }
        else
        {
            DgMatrizComparativaRapida.Items.Refresh();
        }
    }

    private void DgMatrizComparativaRapida_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Row.Item is not CotizacionRenglonComparativo r || r.TieneOcGenerada)
                return;

            var proveedores = CboCompProv1?.ItemsSource as IList<Proveedor> ?? DatabaseService.ObtenerProveedores();
            string hoyStr = DateTime.Today.ToString("yyyy-MM-dd");

            if (r.PrecioSinIva1 > 0)
            {
                if (r.Proveedor1Id <= 0)
                {
                    var pred1 = ObtenerProveedorPredominanteSlot(1);
                    var pObj = proveedores.FirstOrDefault(p => p.Id == pred1.provId);
                    if (pObj != null) AsignarProveedorASlotEnRenglon(r, 1, pObj);
                }
                if (string.IsNullOrWhiteSpace(r.FechaCotizacion1)) r.FechaCotizacion1 = hoyStr;
            }
            if (r.PrecioSinIva2 > 0)
            {
                if (r.NumeroOpcionesVisibles < 2) r.NumeroOpcionesVisibles = 2;
                if (r.Proveedor2Id <= 0)
                {
                    var pred2 = ObtenerProveedorPredominanteSlot(2);
                    var pObj = proveedores.FirstOrDefault(p => p.Id == pred2.provId);
                    if (pObj != null) AsignarProveedorASlotEnRenglon(r, 2, pObj);
                }
                if (string.IsNullOrWhiteSpace(r.FechaCotizacion2)) r.FechaCotizacion2 = hoyStr;
            }
            if (r.PrecioSinIva3 > 0)
            {
                if (r.NumeroOpcionesVisibles < 3) r.NumeroOpcionesVisibles = 3;
                if (r.Proveedor3Id <= 0)
                {
                    var pred3 = ObtenerProveedorPredominanteSlot(3);
                    var pObj = proveedores.FirstOrDefault(p => p.Id == pred3.provId);
                    if (pObj != null) AsignarProveedorASlotEnRenglon(r, 3, pObj);
                }
                if (string.IsNullOrWhiteSpace(r.FechaCotizacion3)) r.FechaCotizacion3 = hoyStr;
            }

            r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
            SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
            DatabaseService.GuardarCotizacionPartida(r);
            if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel && ReferenceEquals(sel, r))
                CargarProductoEnTarjetasComparativa(r);

            RecalcularTotalesComparativa();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ElegirGanadorDesdeMatriz(object sender, int slot)
    {
        if (sender is not FrameworkElement { Tag: CotizacionRenglonComparativo r } || r.TieneOcGenerada)
            return;

        int provId = slot switch { 1 => r.Proveedor1Id, 2 => r.Proveedor2Id, 3 => r.Proveedor3Id, _ => 0 };
        decimal precio = slot switch { 1 => r.PrecioSinIva1, 2 => r.PrecioSinIva2, 3 => r.PrecioSinIva3, _ => 0m };

        if (provId <= 0 || precio <= 0)
        {
            DialogoFluent.Show(this, $"Asigne primero un proveedor y un precio mayor a $0.00 en la Opción {slot} antes de adjudicar.", "Opción incompleta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        r.ProveedorGanadorIndice = slot;
        r.UnidadCompra = DatabaseService.NormalizarUnidadMedida(r.UnidadCompra);
        SincronizarUnidadEnEditorSolicitudSiAbierta(r.SolicitudPartidaId, r.UnidadCompra);
        DatabaseService.GuardarCotizacionPartida(r);
        if (DgComparativa?.SelectedItem is CotizacionRenglonComparativo sel && ReferenceEquals(sel, r))
            CargarProductoEnTarjetasComparativa(r);

        RecalcularTotalesComparativa();
    }

    private void ChkMatrizMostrarTotalConIva_Changed(object sender, RoutedEventArgs e)
    {
        ConstruirMatrizComparativaRapida();
    }

    private void BtnMatrizElegirOp1_Click(object sender, RoutedEventArgs e) => ElegirGanadorDesdeMatriz(sender, 1);
    private void BtnMatrizElegirOp2_Click(object sender, RoutedEventArgs e) => ElegirGanadorDesdeMatriz(sender, 2);
    private void BtnMatrizElegirOp3_Click(object sender, RoutedEventArgs e) => ElegirGanadorDesdeMatriz(sender, 3);

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

        foreach (var part in sol.Partidas)
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
            TxtExpTituloFolio.Text = $"📁 Expediente Completo: {sol.Folio} — {sol.ProyectoMostrar} [{sol.Estatus}]";
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
}
