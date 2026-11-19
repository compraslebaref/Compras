using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lebaref.Windows
{
    internal sealed class OperationForm : Form
    {
        private readonly ErpClient client;
        private readonly string action, folio, token = Guid.NewGuid().ToString();
        private readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>();
        private readonly HashSet<string> files = new HashSet<string>();
        private readonly DataGridView grid = MainForm.Grid();
        private readonly Button save = MainForm.Action("Guardar y confirmar");
        private readonly Label status = new Label { Dock = DockStyle.Fill, AutoSize = false };
        private bool busy;
        private Dictionary<string, object> pending;
        public Dictionary<string, object> Result { get; private set; }

        public OperationForm(ErpClient client, string action, string folio, string title, Dictionary<string, object> schema)
        {
            this.client = client; this.action = action; this.folio = folio;
            Text = "Lebaref | " + title + (folio.Length > 0 ? " | " + folio : "");
            ClientSize = new Size(1060, 690); MinimumSize = new Size(900, 600);
            Font = new Font("Segoe UI", 10F); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(new Label { Text = ErpClient.Value(schema, "note"), Dock = DockStyle.Fill }, 0, 0);
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var field in ErpClient.Rows(schema, "fields"))
            {
                string name = ErpClient.Value(field, "name"), kind = ErpClient.Value(field, "kind");
                Control input;
                var choices = Choices(field);
                if (choices != null)
                {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    combo.Items.AddRange(choices);
                    if (combo.Items.Count > 0) { int initial = combo.FindStringExact(ErpClient.Value(field, "value")); combo.SelectedIndex = initial >= 0 ? initial : 0; }
                    input = combo;
                }
                else if (kind == "date")
                {
                    var date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
                    DateTime value; if (DateTime.TryParse(ErpClient.Value(field, "value"), out value)) date.Value = value;
                    input = date;
                }
                else input = new TextBox { Text = ErpClient.Value(field, "value"), Multiline = kind == "multiline", Height = kind == "multiline" ? 70 : 28 };
                input.Dock = DockStyle.Fill; input.AccessibleName = ErpClient.Value(field, "label");
                inputs.Add(name, input);
                int index = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                fields.Controls.Add(new Label { Text = ErpClient.Value(field, "label"), AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, index);
                if (kind == "file")
                {
                    files.Add(name); ((TextBox)input).ReadOnly = true;
                    var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 38 };
                    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
                    var browse = MainForm.Action("Elegir XML");
                    browse.Click += (s, e) => { using (var picker = new OpenFileDialog { Filter = "CFDI XML (*.xml)|*.xml", CheckFileExists = true }) { if (picker.ShowDialog(this) == DialogResult.OK) input.Text = picker.FileName; } };
                    panel.Controls.Add(input, 0, 0); panel.Controls.Add(browse, 1, 0); fields.Controls.Add(panel, 1, index);
                }
                else fields.Controls.Add(input, 1, index);
            }
            var fieldScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            fieldScroll.Controls.Add(fields);
            layout.RowStyles[1] = new RowStyle(SizeType.Absolute, Math.Min(300, Math.Max(0, fields.RowCount * 42 + 30)));
            layout.Controls.Add(fieldScroll, 0, 1);
            grid.ReadOnly = false; grid.AllowUserToAddRows = Flag(schema, "add_rows"); grid.AllowUserToDeleteRows = grid.AllowUserToAddRows;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            foreach (var column in ErpClient.Rows(schema, "columns"))
            {
                var choices = Choices(column); DataGridViewColumn c;
                if (choices == null) c = new DataGridViewTextBoxColumn();
                else { var combo = new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat }; combo.Items.Add(""); combo.Items.AddRange(choices.Where(x => Convert.ToString(x) != "").ToArray()); c = combo; }
                c.Name = ErpClient.Value(column, "name"); c.HeaderText = ErpClient.Value(column, "label"); c.ReadOnly = !Flag(column, "editable");
                c.SortMode = DataGridViewColumnSortMode.NotSortable; if (c.Name == "id") c.Visible = false;
                grid.Columns.Add(c);
            }
            foreach (var row in ErpClient.Rows(schema, "rows"))
                grid.Rows.Add(grid.Columns.Cast<DataGridViewColumn>().Select(c => (object)ErpClient.Value(row, c.Name)).ToArray());
            if (grid.Columns.Count == 0)
            {
                grid.Visible = false;
                layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
                layout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0);
            }
            grid.DataError += (s, e) => { e.ThrowException = false; status.Text = "Selecciona un valor válido del catálogo."; };
            layout.Controls.Add(grid, 0, 2); layout.Controls.Add(status, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var close = MainForm.Action("Cerrar"); close.Click += (s, e) => Close();
            actions.Controls.Add(save); actions.Controls.Add(close); layout.Controls.Add(actions, 0, 4); Controls.Add(layout);
            save.Click += async (s, e) => await Save();
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
        }

        private async Task Save()
        {
            if (busy) return;
            busy = true; save.Enabled = false;
            try
            {
                if (pending == null)
                {
                    grid.EndEdit(); pending = new Dictionary<string, object>();
                    foreach (var pair in inputs)
                    {
                        if (files.Contains(pair.Key))
                        {
                            if (!File.Exists(pair.Value.Text) || new FileInfo(pair.Value.Text).Length > 2000000) throw new ErpException("Selecciona un XML de hasta 2 MB.");
                            pending[pair.Key] = File.ReadAllText(pair.Value.Text);
                        }
                        else pending[pair.Key] = pair.Value is DateTimePicker ? ((DateTimePicker)pair.Value).Value.ToString("yyyy-MM-dd") : pair.Value.Text.Trim();
                    }
                    var rows = new List<Dictionary<string, object>>();
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (row.IsNewRow) continue;
                        var values = new Dictionary<string, object>();
                        foreach (DataGridViewColumn c in grid.Columns) values[c.Name] = Convert.ToString(row.Cells[c.Index].Value).Trim();
                        rows.Add(values);
                    }
                    pending["rows"] = rows;
                }
                status.Text = "Guardando…";
                Result = await client.Execute(action, folio, token, pending);
                DialogResult = DialogResult.OK; busy = false; Close();
            }
            catch (TaskCanceledException) { LockRetry(); }
            catch (HttpRequestException) { LockRetry(); }
            catch (Exception error) { pending = null; status.Text = error.Message; }
            finally { busy = false; save.Enabled = true; }
        }

        private void LockRetry()
        {
            grid.Enabled = false; foreach (var input in inputs.Values) input.Enabled = false;
            status.Text = "No llegó confirmación. Pulsa Reintentar para consultar/guardar el mismo intento sin duplicarlo. No abras otra captura hasta resolverlo.";
            save.Text = "Reintentar mismo envío";
        }
        private static bool Flag(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && Convert.ToBoolean(v); }
        private static object[] Choices(Dictionary<string, object> d) { object v; return d.TryGetValue("choices", out v) && v != null ? ((IEnumerable)v).Cast<object>().ToArray() : null; }
    }
}
