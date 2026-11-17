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
        private readonly Button open = Action("Ver renglones");
        private readonly Button compare = Action("Comparativa");
        private readonly ComboBox operation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 245, AccessibleName = "Operación" };
        private readonly Button execute = Action("Abrir operación");
        private readonly Button export = Action("Exportar CSV");
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

        private static readonly string[] DocTypes = { "Material Request", "Purchase Order", "Purchase Receipt", "Request for Quotation", "Supplier Quotation", "Stock Entry", "Bin", "Supplier", "Item", "Project", "Warehouse", "Lebaref CFDI" };
        private static readonly string[] Actions = { "solicitud", "peticion", "oferta", "autorizar", "recibir", "entregar", "devolver", "cfdi", "articulo", "proveedor", "obra", "almacen" };
        private static readonly string[] ActionLabels = { "Nueva solicitud de material", "Pedir cotización de la solicitud", "Registrar oferta de la petición", "Autorizar orden seleccionada", "Recibir orden seleccionada", "Entregar materiales a obra", "Devolver sobrante de entrega", "Archivar CFDI de recepción", "Nuevo artículo", "Nuevo proveedor", "Nueva obra", "Nuevo almacén" };
        private static readonly int[] SourceModule = { -1, 0, 3, 1, 1, -1, 5, 2, -1, -1, -1, -1 };
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
            module.Items.AddRange(new object[] { "Solicitudes de material", "Órdenes de compra", "Recepciones de compra", "Peticiones de cotización", "Ofertas de proveedores", "Movimientos de almacén", "Existencias", "Proveedores", "Artículos", "Obras", "Almacenes", "CFDI archivados" });
            module.SelectedIndex = 0;
            module.Margin = new Padding(0, 7, 10, 0);
            toolbar.Controls.AddRange(new Control[] { module, reload, open, compare, logout });
            toolbar.SetFlowBreak(logout, true);
            operation.Items.AddRange(ActionLabels); operation.SelectedIndex = 0;
            operation.Margin = new Padding(0, 7, 10, 0);
            toolbar.Controls.AddRange(new Control[] { operation, execute, export });

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
            if (module.SelectedIndex >= 6) { status.Text = "Los datos del catálogo se muestran en la tabla. Puedes exportarlos."; return; }
            var folio = Convert.ToString(documents.CurrentRow.Cells[0].Value);
            await Run(async () =>
            {
                detail.Rows.Clear();
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
            operation.Enabled = execute.Enabled = export.Enabled = enabled;
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
