using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lebaref.Windows
{
    internal sealed class ComparisonForm : Form
    {
        private readonly ErpClient client;
        private readonly string folio;
        private readonly bool canCreate;
        private readonly DataGridView items = MainForm.Grid();
        private readonly DataGridView offers = MainForm.Grid();
        private readonly Button choose = MainForm.Action("Elegir oferta");
        private readonly Button clear = MainForm.Action("Quitar elección");
        private readonly Button generate = MainForm.Action("Generar órdenes en borrador");
        private readonly Label note = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Dictionary<string, string> selection = new Dictionary<string, string>();
        private bool busy;

        public IList<Dictionary<string, object>> Orders { get; private set; }

        public ComparisonForm(ErpClient client, string folio, Dictionary<string, object> data)
        {
            this.client = client;
            this.folio = folio;
            canCreate = Flag(data, "puede_generar");
            Text = "Comparativa Lebaref | " + folio;
            ClientSize = new Size(1000, 620);
            MinimumSize = new Size(850, 520);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(18),
                BackColor = Color.White, AutoScroll = true
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            items.AccessibleName = "Renglones de la solicitud";
            offers.AccessibleName = "Ofertas por renglón";
            MainForm.Columns(items, "Artículo", "Cantidad", "Obra", "Partida", "Proveedor elegido");
            MainForm.Columns(offers, "Proveedor", "Cotización", "Precio unitario sin impuestos", "Entrega");
            items.Columns[4].FillWeight = 145;
            items.Columns[1].FillWeight = 70;
            items.Columns[2].FillWeight = 75;
            items.Columns[3].FillWeight = 75;
            offers.Columns[0].FillWeight = 165;
            offers.Columns[3].FillWeight = 65;
            layout.Controls.Add(Heading("Materiales de la solicitud"), 0, 0);
            layout.Controls.Add(items, 0, 1);
            layout.Controls.Add(Heading("Ofertas del material seleccionado"), 0, 2);
            layout.Controls.Add(offers, 0, 3);
            layout.Controls.Add(Actions(choose, clear), 0, 4);
            layout.Controls.Add(note, 0, 5);
            var close = MainForm.Action("Cerrar");
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            layout.Controls.Add(Actions(generate, close), 0, 6);
            Controls.Add(layout);

            foreach (var row in ErpClient.Rows(data, "renglones"))
            {
                var index = items.Rows.Add(ErpClient.Value(row, "articulo"),
                    ErpClient.Value(row, "cantidad") + " " + ErpClient.Value(row, "unidad"),
                    ErpClient.Value(row, "obra"), ErpClient.Value(row, "partida"),
                    Flag(row, "ordenado") ? "Orden activa" : String.Empty);
                items.Rows[index].Tag = row;
            }
            if (items.Rows.Count > 0) items.CurrentCell = items.Rows[0].Cells[0];
            items.SelectionChanged += (sender, args) => ShowOffers();
            offers.SelectionChanged += (sender, args) => UpdateButtons();
            offers.CellDoubleClick += (sender, args) => { if (args.RowIndex >= 0) ChooseOffer(); };
            choose.Click += (sender, args) => ChooseOffer();
            clear.Click += (sender, args) => ClearChoice();
            generate.Click += async (sender, args) => await Generate();
            FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
            close.Click += (sender, args) => Close();
            ShowOffers();
        }

        private void ShowOffers()
        {
            offers.Rows.Clear();
            var item = Current(items);
            if (item != null && !Flag(item, "ordenado"))
            {
                string selectedOffer;
                selection.TryGetValue(ErpClient.Value(item, "id"), out selectedOffer);
                foreach (var offer in ErpClient.Rows(item, "opciones"))
                {
                    var days = ErpClient.Value(offer, "entrega");
                    var index = offers.Rows.Add(ErpClient.Value(offer, "proveedor"),
                        ErpClient.Value(offer, "cotizacion"), UnitPrice(offer),
                        String.IsNullOrEmpty(days) || days == "0" ? "Sin plazo indicado" : days + " días");
                    var line = offers.Rows[index];
                    line.Tag = offer;
                    line.Cells[0].ToolTipText = ErpClient.Value(offer, "proveedor");
                    if (ErpClient.Value(offer, "id") == selectedOffer)
                        offers.CurrentCell = line.Cells[0];
                }
            }
            UpdateButtons();
            ShowStatus();
        }

        private void ShowStatus()
        {
            var item = Current(items);
            if (item == null) note.Text = "La solicitud no tiene materiales.";
            else if (!canCreate) note.Text = "Tu cuenta puede consultar las ofertas.";
            else if (Flag(item, "ordenado")) note.Text = "Este material ya tiene una orden activa.";
            else if (offers.Rows.Count == 0) note.Text = "No hay cotizaciones vigentes por la cantidad solicitada.";
            else if (selection.Count == 0) note.Text = "Elige una oferta para cada material que quieras comprar.";
            else note.Text = selection.Count == 1 ? "1 material con proveedor elegido."
                : selection.Count + " materiales con proveedor elegido.";
        }

        private void ChooseOffer()
        {
            if (!choose.Enabled) return;
            var item = Current(items);
            var offer = Current(offers);
            selection[ErpClient.Value(item, "id")] = ErpClient.Value(offer, "id");
            items.CurrentRow.Cells[4].Value = ErpClient.Value(offer, "proveedor");
            items.CurrentRow.Cells[4].ToolTipText = ErpClient.Value(offer, "proveedor");
            UpdateButtons();
            ShowStatus();
        }

        private void ClearChoice()
        {
            var item = Current(items);
            if (item == null) return;
            selection.Remove(ErpClient.Value(item, "id"));
            items.CurrentRow.Cells[4].Value = String.Empty;
            items.CurrentRow.Cells[4].ToolTipText = String.Empty;
            UpdateButtons();
            ShowStatus();
        }

        private async Task Generate()
        {
            if (!generate.Enabled) return;
            busy = true;
            items.Enabled = offers.Enabled = false;
            UpdateButtons();
            note.Text = "Generando órdenes en borrador…";
            try { Orders = await client.CreateDraftOrders(folio, selection); }
            catch (ErpException error) { note.Text = error.Message; }
            catch (TaskCanceledException) { note.Text = "ERPNext tardó demasiado. Cierra y actualiza la solicitud antes de volver a intentar."; }
            catch (HttpRequestException) { note.Text = "Se perdió la conexión. Actualiza la solicitud antes de volver a intentar."; }
            catch (Exception) { note.Text = "No se pudo leer la respuesta. Actualiza la solicitud antes de volver a intentar."; }
            finally
            {
                busy = false;
                items.Enabled = offers.Enabled = true;
                UpdateButtons();
            }
            if (Orders != null) Close();
        }

        private void UpdateButtons()
        {
            var item = Current(items);
            choose.Enabled = canCreate && !busy && item != null && Current(offers) != null;
            clear.Enabled = canCreate && !busy && item != null && selection.ContainsKey(ErpClient.Value(item, "id"));
            generate.Enabled = canCreate && !busy && selection.Count > 0;
        }

        private static Dictionary<string, object> Current(DataGridView grid)
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Dictionary<string, object>;
        }

        private static bool Flag(Dictionary<string, object> row, string key)
        {
            return String.Equals(ErpClient.Value(row, key), "True", StringComparison.OrdinalIgnoreCase);
        }

        private static string UnitPrice(Dictionary<string, object> offer)
        {
            decimal value;
            var amount = ErpClient.Value(offer, "precio");
            if (Decimal.TryParse(amount, out value))
                amount = value.ToString("0.00##", CultureInfo.GetCultureInfo("es-MX"));
            return amount + " " + ErpClient.Value(offer, "moneda");
        }

        private static Label Heading(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft };
        }

        private static FlowLayoutPanel Actions(params Control[] controls)
        {
            var result = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            result.Controls.AddRange(controls);
            return result;
        }
    }
}
