using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lebaref.Windows
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(17, 42, 67);
        private static readonly Color Blue = Color.FromArgb(20, 87, 133);
        private static readonly Color Pale = Color.FromArgb(240, 246, 250);

        private readonly TextBox user = new TextBox { AccessibleName = "Usuario", Width = 308, MaxLength = 140 };
        private readonly TextBox password = new TextBox { AccessibleName = "Contraseña", Width = 308, UseSystemPasswordChar = true, MaxLength = 256 };
        private readonly Button login = Action("Entrar");
        private readonly Button logout = Action("Cerrar sesión");
        private readonly Button reload = Action("Actualizar");
        private readonly Button open = Action("Ver detalle");
        private readonly Button compare = Action("Comparativa");
        private readonly ComboBox operation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 245, AccessibleName = "Operación" };
        private readonly Button execute = Action("Abrir operación");
        private readonly Button export = Action("Exportar CSV");
        private readonly Button label = Action("Etiqueta");
        private readonly TextBox codeSearch = new TextBox { Width = 310, AccessibleName = "Código exacto de herramienta, vehículo o kit" };
        private readonly Button findCode = Action("Buscar código");
        private readonly Label codeCaption = new Label { Text = "Código exacto:", AutoSize = true, Margin = new Padding(0, 9, 6, 0) };
        private readonly ComboBox module = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 236, AccessibleName = "Documentos" };
        private readonly DataGridView documents = Grid();
        private readonly DataGridView detail = Grid();
        private readonly Label identity = new Label { AutoSize = true, ForeColor = Color.White, Anchor = AnchorStyles.Right };
        private readonly Label status = new Label { AutoSize = false, Dock = DockStyle.Fill, AccessibleName = "Estado", TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label selected = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Panel loginPage = new Panel { Dock = DockStyle.Fill, BackColor = Pale };
        private readonly TableLayoutPanel workPage = new TableLayoutPanel { Dock = DockStyle.Fill, Visible = false };
        private ErpClient erp;
        private bool busy;

        private static readonly string[] DocTypes = { "Material Request", "Purchase Order", "Purchase Receipt", "Request for Quotation", "Supplier Quotation", "Stock Entry", "Bin", "Supplier", "Item", "Project", "Warehouse", "Lebaref CFDI", "Lebaref Herramienta", "Lebaref Prestamo", "Lebaref Retorno", "Lebaref Vehiculo", "Lebaref Gasto Vehiculo", "Lebaref Revision Vehiculo", "Lebaref Estado Herramienta", "Lebaref Estado Vehiculo", "Lebaref Kit", "Lebaref Composicion Kit", "Lebaref Garantia Herramienta", "Lebaref Mantenimiento Herramienta", "Lebaref Anulacion Mantenimiento" };
        private static readonly string[] Actions = { "solicitud", "peticion", "oferta", "autorizar", "recibir", "entregar", "devolver", "cfdi", "articulo", "proveedor", "obra", "almacen", "herramienta", "prestamo", "retorno_herramienta", "estado_herramienta", "vehiculo", "gasto_vehiculo", "revision_vehiculo", "estado_vehiculo", "kit", "composicion_kit", "prestamo_kit", "garantia_herramienta", "mantenimiento_herramienta", "anular_mantenimiento" };
        private static readonly string[] ActionLabels = { "Nueva solicitud de material", "Pedir cotización de la solicitud", "Registrar oferta de la petición", "Autorizar orden seleccionada", "Recibir orden seleccionada", "Entregar materiales a obra", "Devolver sobrante de entrega", "Archivar CFDI de recepción", "Nuevo artículo", "Nuevo proveedor", "Nueva obra", "Nuevo almacén", "Nueva herramienta individual", "Nuevo préstamo de herramientas", "Retornar herramientas del préstamo", "Cambiar estado de herramienta", "Nuevo vehículo", "Registrar gasto del vehículo", "Revisar vehículo", "Estado / seguro del vehículo", "Nuevo kit de herramientas", "Cambiar contenido del kit", "Prestar herramientas del kit", "Compra y garantía de herramienta", "Registrar mantenimiento", "Anular mantenimiento seleccionado" };
        private static readonly int[] SourceModule = { -1, 0, 3, 1, 1, -1, 5, 2, -1, -1, -1, -1, -1, -1, 13, 12, -1, 15, 15, 15, -1, 20, 20, 12, 12, 23 };
        private static readonly Dictionary<string, string> StatusNames = new Dictionary<string, string>
        {
            { "Draft", "Borrador" }, { "Submitted", "Enviado" }, { "Pending", "Pendiente" },
            { "Partially Ordered", "Ordenado parcialmente" }, { "Ordered", "Ordenado" },
            { "Partially Received", "Recibido parcialmente" }, { "Received", "Recibido" },
            { "To Receive and Bill", "Por recibir y facturar" }, { "To Receive", "Por recibir" },
            { "To Bill", "Por facturar" }, { "Completed", "Completado" },
            { "Cancelled", "Cancelado" }, { "Stopped", "Detenido" }, { "Closed", "Cerrado" },
            { "Return Issued", "Devuelto" }, { "On Hold", "En espera" },
        };

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public MainForm()
        {
            Text = "Lebaref | Compras y Almacén";
            ClientSize = new Size(1000, 650);
            MinimumSize = new Size(840, 570);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Navy, ColumnCount = 2, Padding = new Padding(22, 0, 22, 0), Margin = Padding.Empty };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(new Label { Text = "Lebaref  /  Compras y Almacén", Font = new Font("Segoe UI", 17F, FontStyle.Bold), ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            top.Controls.Add(identity, 1, 0);

            var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            pages.Controls.Add(workPage);
            pages.Controls.Add(loginPage);
            status.Padding = new Padding(22, 0, 22, 0);
            status.ForeColor = Navy;
            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(pages, 0, 1);
            layout.Controls.Add(status, 0, 2);
            Controls.Add(layout);
            SetupLogin();
            SetupWorkspace();

            login.Click += async (sender, args) => await SignIn();
            logout.Click += async (sender, args) => await SignOut();
            reload.Click += async (sender, args) => await Run(LoadList);
            open.Click += async (sender, args) => await ShowDetail();
            compare.Click += async (sender, args) => await ShowComparison();
            execute.Click += async (sender, args) => await ShowOperation();
            export.Click += (sender, args) => ExportList();
            label.Click += async (sender, args) => await ShowLabel();
            findCode.Click += async (sender, args) => await SearchCode();
            codeSearch.KeyDown += async (sender, args) => { if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; await SearchCode(); } };
            documents.CellDoubleClick += async (sender, args) => { if (args.RowIndex >= 0) await ShowDetail(); };
            module.SelectedIndexChanged += async (sender, args) => { if (erp != null) await Run(LoadList); };
            FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
            FormClosed += (sender, args) => { if (erp != null) erp.Dispose(); };
            AcceptButton = login;
            status.Text = "Entorno de pruebas en esta PC.";
        }

        private void SetupLogin()
        {
            var centering = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            centering.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            centering.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            centering.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            centering.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            centering.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            centering.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var card = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 24, 28, 22), BackColor = Color.White };
            card.Controls.Add(new Label { Text = "Iniciar sesión", AutoSize = true, Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = Navy, Margin = new Padding(3, 0, 3, 14) });
            card.Controls.Add(new Label { Text = "Usuario", AutoSize = true });
            card.Controls.Add(user);
            card.Controls.Add(new Label { Text = "Contraseña", AutoSize = true, Margin = new Padding(3, 10, 3, 0) });
            card.Controls.Add(password);
            login.Margin = new Padding(3, 16, 3, 0);
            login.BackColor = Blue;
            login.ForeColor = Color.White;
            card.Controls.Add(login);
            centering.Controls.Add(card, 1, 1);
            loginPage.Controls.Add(centering);
        }

        private void SetupWorkspace()
        {
            workPage.Padding = new Padding(20, 14, 20, 0);
            workPage.ColumnCount = 1;
            workPage.RowCount = 4;
            workPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            workPage.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
            workPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            workPage.RowStyles.Add(new RowStyle(SizeType.Percent, 46));

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, AutoScroll = true };
            module.Items.AddRange(new object[] { "Solicitudes de material", "Órdenes de compra", "Recepciones de compra", "Peticiones de cotización", "Ofertas de proveedores", "Movimientos de almacén", "Existencias", "Proveedores", "Artículos", "Obras", "Almacenes", "CFDI archivados", "Herramientas individuales", "Préstamos de herramientas", "Retornos de herramientas", "Flotilla", "Gastos de vehículos", "Revisiones de vehículos", "Historial de herramientas", "Historial de vehículos", "Kits de herramientas", "Cambios de kits", "Garantías de herramientas", "Mantenimientos de herramientas", "Anulaciones de mantenimiento" });
            module.SelectedIndex = 0;
            module.Margin = new Padding(0, 7, 10, 0);
            toolbar.Controls.AddRange(new Control[] { module, reload, open, compare, logout });
            toolbar.SetFlowBreak(logout, true);
            operation.Items.AddRange(ActionLabels); operation.SelectedIndex = 0;
            operation.Margin = new Padding(0, 7, 10, 0);
            operation.DropDownWidth = 380;
            toolbar.Controls.AddRange(new Control[] { operation, execute, export, label });
            toolbar.SetFlowBreak(label, true);
            codeSearch.Margin = new Padding(0, 7, 8, 0);
            toolbar.Controls.AddRange(new Control[] { codeCaption, codeSearch, findCode });

            documents.AccessibleName = "Documentos de compras";
            detail.AccessibleName = "Renglones del documento";
            Columns(documents, "Folio", "Fecha", "Proveedor", "Estado");
            Columns(detail, "Artículo", "Cantidad", "Unidad", "Obra", "Partida de obra");
            selected.ForeColor = Navy;
            workPage.Controls.Add(toolbar, 0, 0);
            workPage.Controls.Add(documents, 0, 1);
            workPage.Controls.Add(selected, 0, 2);
            workPage.Controls.Add(detail, 0, 3);
        }

        private async Task SignIn()
        {
            var name = user.Text.Trim();
            if (String.IsNullOrWhiteSpace(name) || password.Text.Length == 0)
            {
                status.Text = "Escribe tu usuario y contraseña.";
                return;
            }
            await Run(async () =>
            {
                var client = new ErpClient();
                try { identity.Text = await client.SignIn(name, password.Text); }
                catch { client.Dispose(); throw; }
                finally { password.Clear(); }
                erp = client;
                loginPage.Hide();
                workPage.Show();
                workPage.BringToFront();
                AcceptButton = null;
                await LoadList();
            });
        }

        private async Task SignOut()
        {
            await Run(async () =>
            {
                try { await erp.SignOut(); }
                finally
                {
                    erp.Dispose();
                    erp = null;
                    identity.Text = String.Empty;
                    documents.Rows.Clear();
                    detail.Rows.Clear();
                    workPage.Hide();
                    loginPage.Show();
                    loginPage.BringToFront();
                    AcceptButton = login;
                    user.Focus();
                }
                status.Text = "Sesión cerrada.";
            });
        }

        private async Task LoadList()
        {
            var doctype = DocTypes[module.SelectedIndex];
            var isRequest = doctype == "Material Request";
            compare.Visible = isRequest;
            label.Visible = module.SelectedIndex == 12 || module.SelectedIndex == 15 || module.SelectedIndex == 20;
            codeCaption.Visible = codeSearch.Visible = findCode.Visible = label.Visible;
            workPage.RowStyles[0].Height = label.Visible ? 138 : 96;
            codeSearch.Clear();
            if (module.SelectedIndex >= 6) { await LoadCatalog(doctype); return; }
            var date = doctype == "Purchase Receipt" ? "posting_date" : "transaction_date";
            if (doctype == "Stock Entry") date = "posting_date";
            bool noSupplier = isRequest || doctype == "Request for Quotation" || doctype == "Stock Entry";
            var fields = noSupplier
                ? new[] { "name", date, "status" }
                : new[] { "name", date, "supplier_name", "status" };
            if (doctype == "Stock Entry") fields = new[] { "name", date, "stock_entry_type", "docstatus" };
            documents.Columns.Clear(); Columns(documents, "Folio", "Fecha", "Proveedor", "Estado");
            documents.Rows.Clear();
            detail.Rows.Clear();
            selected.Text = "Selecciona un documento para ver sus renglones.";
            documents.Columns[2].Visible = !noSupplier;
            var rows = await erp.List(doctype, fields);
            foreach (var row in rows)
            {
                string statusName;
                var statusKey = doctype == "Stock Entry" ? (ErpClient.Value(row, "docstatus") == "1" ? "Submitted" : (ErpClient.Value(row, "docstatus") == "2" ? "Cancelled" : "Draft")) : ErpClient.Value(row, "status");
                if (!StatusNames.TryGetValue(statusKey, out statusName)) statusName = statusKey;
                DateTime parsedDate;
                var dateValue = ErpClient.Value(row, date);
                if (DateTime.TryParseExact(dateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                    dateValue = parsedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                documents.Rows.Add(ErpClient.Value(row, "name"), dateValue, ErpClient.Value(row, "supplier_name"), statusName);
            }
            status.Text = rows.Count == 0 ? "No hay documentos visibles para esta cuenta." : rows.Count + " documentos recientes.";
        }

        private async Task ShowDetail()
        {
            if (documents.CurrentRow == null) return;
            if (module.SelectedIndex >= 12)
            {
                var name = Convert.ToString(documents.CurrentRow.Cells[0].Value);
                await Run(() => LoadEquipmentDetail(name));
                return;
            }
            if (module.SelectedIndex >= 6) { status.Text = "Los datos del catálogo se muestran en la tabla. Puedes exportarlos."; return; }
            var folio = Convert.ToString(documents.CurrentRow.Cells[0].Value);
            await Run(async () =>
            {
                detail.Rows.Clear();
                detail.Columns.Clear(); Columns(detail, "Artículo", "Cantidad", "Unidad", "Obra", "Partida de obra");
                selected.Text = folio;
                var doc = await erp.Detail(DocTypes[module.SelectedIndex], folio);
                var rows = ErpClient.Rows(doc, "items");
                foreach (var row in rows)
                    detail.Rows.Add(ErpClient.Value(row, "item_name"), ErpClient.Value(row, "qty"), ErpClient.Value(row, "uom"),
                        !String.IsNullOrEmpty(ErpClient.Value(row, "project")) ? ErpClient.Value(row, "project") : (!String.IsNullOrEmpty(ErpClient.Value(row, "custom_lebaref_obra")) ? ErpClient.Value(row, "custom_lebaref_obra") : ErpClient.Value(doc, "project")), ErpClient.Value(row, "custom_lebaref_partida"));
                selected.Text = folio + "   |   " + rows.Count + " renglones";
                status.Text = "Documento cargado.";
            });
        }

        private async Task LoadCatalog(string doctype)
        {
            string[] fields, labels;
            switch (doctype)
            {
                case "Bin": fields = new[] { "item_code", "warehouse", "actual_qty", "reserved_qty", "ordered_qty" }; labels = new[] { "Artículo", "Almacén", "Existencia", "Reservado", "Por recibir" }; break;
                case "Supplier": fields = new[] { "name", "supplier_name", "tax_id" }; labels = new[] { "Código", "Proveedor", "RFC" }; break;
                case "Item": fields = new[] { "name", "item_name", "stock_uom", "is_stock_item" }; labels = new[] { "Código", "Artículo", "Unidad", "Control de inventario" }; break;
                case "Project": fields = new[] { "name", "project_name", "status" }; labels = new[] { "Código", "Obra", "Estado" }; break;
                case "Warehouse": fields = new[] { "name", "company", "is_group" }; labels = new[] { "Almacén", "Empresa", "Es agrupador" }; break;
                case "Lebaref Herramienta": fields = new[] { "name", "articulo", "estado", "responsable", "obra", "prestamo" }; labels = new[] { "Código", "Artículo", "Estado", "Responsable", "Obra", "Préstamo" }; break;
                case "Lebaref Prestamo": fields = new[] { "name", "fecha", "responsable", "obra", "compromiso", "estado" }; labels = new[] { "Folio", "Salida", "Responsable", "Obra", "Compromiso", "Estado" }; break;
                case "Lebaref Retorno": fields = new[] { "name", "prestamo", "fecha", "recibe" }; labels = new[] { "Folio", "Préstamo", "Fecha", "Recibe" }; break;
                case "Lebaref Vehiculo": fields = new[] { "name", "placas", "responsable", "km_actual", "proximo_servicio", "seguro", "estado" }; labels = new[] { "Código", "Placas", "Responsable", "Kilometraje", "Servicio (km)", "Seguro hasta", "Estado" }; break;
                case "Lebaref Gasto Vehiculo": fields = new[] { "name", "vehiculo", "fecha", "tipo", "monto", "moneda", "referencia" }; labels = new[] { "Folio", "Vehículo", "Fecha", "Tipo", "Importe", "Moneda", "Referencia" }; break;
                case "Lebaref Revision Vehiculo": fields = new[] { "name", "vehiculo", "fecha", "inspector", "km" }; labels = new[] { "Folio", "Vehículo", "Fecha", "Inspector", "Kilometraje" }; break;
                case "Lebaref Estado Herramienta": fields = new[] { "name", "herramienta", "fecha", "estado_anterior", "estado", "motivo" }; labels = new[] { "Folio", "Herramienta", "Fecha", "Anterior", "Nuevo estado", "Motivo" }; break;
                case "Lebaref Estado Vehiculo": fields = new[] { "name", "vehiculo", "fecha", "estado_anterior", "estado", "motivo" }; labels = new[] { "Folio", "Vehículo", "Fecha", "Anterior", "Nuevo estado", "Motivo" }; break;
                case "Lebaref Kit": fields = new[] { "name", "nombre", "company", "ubicacion", "ultimo_prestamo" }; labels = new[] { "Código", "Nombre", "Empresa", "Ubicación", "Último préstamo" }; break;
                case "Lebaref Composicion Kit": fields = new[] { "name", "kit", "fecha", "motivo" }; labels = new[] { "Folio", "Kit", "Fecha", "Motivo" }; break;
                case "Lebaref Garantia Herramienta": fields = new[] { "name", "herramienta", "fecha_compra", "fin_garantia", "costo_adquisicion", "moneda" }; labels = new[] { "Folio", "Herramienta", "Compra", "Garantía hasta", "Costo", "Moneda" }; break;
                case "Lebaref Mantenimiento Herramienta": fields = new[] { "name", "herramienta", "fecha", "monto", "moneda", "anulado" }; labels = new[] { "Folio", "Herramienta", "Fecha", "Costo", "Moneda", "Anulado (1=sí)" }; break;
                case "Lebaref Anulacion Mantenimiento": fields = new[] { "name", "mantenimiento", "herramienta", "fecha", "motivo" }; labels = new[] { "Folio", "Mantenimiento", "Herramienta", "Fecha", "Motivo" }; break;
                default: fields = new[] { "name", "recepcion", "proveedor", "moneda", "total" }; labels = new[] { "UUID", "Recepción", "Proveedor", "Moneda", "Total XML" }; break;
            }
            documents.Rows.Clear(); documents.Columns.Clear(); detail.Rows.Clear(); Columns(documents, labels);
            selected.Text = "Catálogo / consulta";
            var rows = await erp.List(doctype, fields);
            foreach (var row in rows) documents.Rows.Add(System.Linq.Enumerable.Select(fields, f => (object)ErpClient.Value(row, f)).ToArray());
            status.Text = rows.Count + " registros recientes (máximo 100 por consulta).";
        }

        private async Task ShowOperation()
        {
            int index = operation.SelectedIndex;
            int source = SourceModule[index];
            if (source >= 0 && (module.SelectedIndex != source || documents.CurrentRow == null))
            {
                status.Text = "Selecciona primero un documento en «" + module.Items[source] + "»."; return;
            }
            string folio = source < 0 ? "" : Convert.ToString(documents.CurrentRow.Cells[0].Value);
            await Run(async () => {
                var schema = await erp.Schema(Actions[index], folio);
                using (var dialog = new OperationForm(erp, Actions[index], folio, ActionLabels[index], schema))
                {
                    dialog.ShowDialog(this);
                    if (dialog.Result == null) { status.Text = "Operación cerrada."; return; }
                    int target = Array.IndexOf(DocTypes, ErpClient.Value(dialog.Result, "tipo"));
                    if (target >= 0) module.SelectedIndex = target;
                    await LoadList();
                    status.Text = "Guardado en ERPNext: " + ErpClient.Value(dialog.Result, "folio");
                }
            });
        }

        private async Task LoadEquipmentDetail(string name)
        {
            var result = await erp.EquipmentDetail(DocTypes[module.SelectedIndex], name);
            detail.Rows.Clear(); detail.Columns.Clear(); Columns(detail, "Dato / evento", "Valor / estado", "Detalle");
            foreach (var row in ErpClient.Rows(result, "rows")) detail.Rows.Add(ErpClient.Value(row, "campo"), ErpClient.Value(row, "valor"), ErpClient.Value(row, "detalle"));
            selected.Text = name + " | Detalle e historial";
            status.Text = "Historial consultado.";
        }

        private async Task SearchCode()
        {
            if (module.SelectedIndex != 12 && module.SelectedIndex != 15 && module.SelectedIndex != 20) return;
            string code = codeSearch.Text.Trim();
            if (code.Length == 0) { status.Text = "Escribe, pega o escanea un código y pulsa Enter."; return; }
            await Run(async () => {
                documents.Rows.Clear(); detail.Rows.Clear(); selected.Text = "Buscando código…";
                var doc = await erp.Detail(DocTypes[module.SelectedIndex], code);
                string[] fields, headers;
                if (module.SelectedIndex == 12) { fields = new[] { "name", "articulo", "estado", "responsable", "obra", "prestamo" }; headers = new[] { "Código", "Artículo", "Estado", "Responsable", "Obra", "Préstamo" }; }
                else if (module.SelectedIndex == 15) { fields = new[] { "name", "placas", "responsable", "km_actual", "proximo_servicio", "seguro", "estado" }; headers = new[] { "Código", "Placas", "Responsable", "Kilometraje", "Servicio (km)", "Seguro hasta", "Estado" }; }
                else { fields = new[] { "name", "nombre", "company", "ubicacion", "ultimo_prestamo" }; headers = new[] { "Código", "Nombre", "Empresa", "Ubicación", "Último préstamo" }; }
                documents.Rows.Clear(); documents.Columns.Clear(); Columns(documents, headers);
                documents.Rows.Add(fields.Select(f => (object)ErpClient.Value(doc, f)).ToArray());
                await LoadEquipmentDetail(ErpClient.Value(doc, "name"));
                status.Text = "Código encontrado. Actualizar vuelve al listado reciente.";
            });
        }

        private async Task ShowLabel()
        {
            if (documents.CurrentRow == null || (module.SelectedIndex != 12 && module.SelectedIndex != 15 && module.SelectedIndex != 20)) return;
            string folio = Convert.ToString(documents.CurrentRow.Cells[0].Value);
            await Run(async () => {
                var doc = await erp.Detail(DocTypes[module.SelectedIndex], folio);
                string kind = module.SelectedIndex == 12 ? "HERRAMIENTA" : (module.SelectedIndex == 15 ? "VEHÍCULO" : "KIT");
                string description = module.SelectedIndex == 20 ? ErpClient.Value(doc, "nombre") : ErpClient.Value(doc, "marca_modelo");
                string extra = module.SelectedIndex == 12 ? "Serie: " + ErpClient.Value(doc, "serie") : (module.SelectedIndex == 15 ? "Placas: " + ErpClient.Value(doc, "placas") : ErpClient.Value(doc, "ubicacion"));
                using (var dialog = new LabelForm(folio, kind, description, extra)) dialog.ShowDialog(this);
                status.Text = "Vista de etiqueta cerrada.";
            });
        }

        private void ExportList()
        {
            using (var picker = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Lebaref-consulta.csv" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try {
                    var lines = new System.Collections.Generic.List<string>();
                    var cols = System.Linq.Enumerable.Where(System.Linq.Enumerable.Cast<DataGridViewColumn>(documents.Columns), c => c.Visible);
                    Func<string, string> csv = value => "\"" + ((value.StartsWith("=") || value.StartsWith("+") || value.StartsWith("-") || value.StartsWith("@") || value.StartsWith("\t") || value.StartsWith("\r")) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
                    lines.Add(String.Join(",", System.Linq.Enumerable.Select(cols, c => csv(c.HeaderText))));
                    foreach (DataGridViewRow row in documents.Rows) lines.Add(String.Join(",", System.Linq.Enumerable.Select(cols, c => csv(Convert.ToString(row.Cells[c.Index].Value)))));
                    System.IO.File.WriteAllLines(picker.FileName, lines, new System.Text.UTF8Encoding(true));
                    status.Text = "Consulta exportada (solo los registros mostrados).";
                } catch (Exception error) { status.Text = "No se pudo guardar: " + error.Message; }
            }
        }

        private async Task ShowComparison()
        {
            if (documents.CurrentRow == null || module.SelectedIndex != 0) return;
            var folio = Convert.ToString(documents.CurrentRow.Cells[0].Value);
            await Run(async () =>
            {
                var data = await erp.Compare(folio);
                using (var dialog = new ComparisonForm(erp, folio, data))
                {
                    dialog.ShowDialog(this);
                    if (dialog.Orders == null)
                    {
                        status.Text = "Comparativa cerrada.";
                        return;
                    }
                    module.SelectedIndex = 1;
                    await LoadList();
                    status.Text = "Órdenes en borrador: " + String.Join(", ",
                        System.Linq.Enumerable.Select(dialog.Orders, row => ErpClient.Value(row, "folio")));
                }
            });
        }

        private async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            SetEnabled(false);
            status.Text = "Consultando ERPNext…";
            try { await action(); }
            catch (ErpException error) { status.Text = error.Message; }
            catch (TaskCanceledException) { status.Text = "ERPNext tardó demasiado. Inténtalo otra vez."; }
            catch (HttpRequestException) { status.Text = "No se pudo conectar al servidor local. Inicia ERPNext de pruebas."; }
            catch (Exception) { status.Text = "No se pudo leer la respuesta de ERPNext. Inténtalo otra vez."; }
            finally { busy = false; SetEnabled(true); }
        }

        private void SetEnabled(bool enabled)
        {
            UseWaitCursor = !enabled;
            user.Enabled = password.Enabled = login.Enabled = enabled;
            module.Enabled = documents.Enabled = reload.Enabled = open.Enabled = compare.Enabled = logout.Enabled = enabled;
            operation.Enabled = execute.Enabled = export.Enabled = label.Enabled = enabled;
            codeSearch.Enabled = findCode.Enabled = enabled;
        }

        internal static Button Action(string label)
        {
            return new Button { Text = label, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat,
                Padding = new Padding(9, 0, 9, 0), BackColor = Color.White, ForeColor = Navy };
        }

        internal static DataGridView Grid()
        {
            return new DataGridView {
                Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowTemplate = { Height = 32 },
                AlternatingRowsDefaultCellStyle = { BackColor = Pale }
            };
        }

        internal static void Columns(DataGridView grid, params string[] headers)
        {
            foreach (var header in headers)
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable });
        }
    }
}
