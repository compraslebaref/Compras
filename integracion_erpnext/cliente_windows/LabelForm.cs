using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Lebaref.Windows
{
    internal static class BarcodeLabel
    {
        // Code 128 B: table reused from the original Lebaref BarcodePrintService.
        private static readonly string[] Patterns = { "11011001100","11001101100","11001100110","10010011000","10010001100","10001001100","10011001000","10011000100","10001100100","11001001000","11001000100","11000100100","10110011100","10011011100","10011001110","10111001100","10011101100","10011100110","11001110010","11001011100","11001001110","11011100100","11001110100","11101101110","11101001100","11100101100","11100100110","11101100100","11100110100","11100110010","11011011000","11011000110","11000110110","10100011000","10001011000","10001000110","10110001000","10001101000","10001100010","11010001000","11000101000","11000100010","10110111000","10110001110","10001101110","10111011000","10111000110","10001110110","11101110110","11010001110","11000101110","11011101000","11011100010","11011101110","11101011000","11101000110","11100010110","11101101000","11101100010","11100011010","11101111010","11001000010","11110001010","10100110000","10100001100","10010110000","10010000110","10000101100","10000100110","10110010000","10110000100","10011010000","10011000010","10000110100","10000110010","11000010010","11001010000","11110111010","11000010100","10001111010","10100111100","10010111100","10010011110","10111100100","10011110100","10011110010","11110100100","11110010100","11110010010","11011011110","11011110110","11110110110","10101111000","10100011110","10001011110","10111101000","10111100010","11110101000","11110100010","10111011110","10111101110","11101011110","11110101110","11010000100","11010010000","11010011100","1100011101011" };

        public static Bitmap Render(string code, string kind, string description, string extra)
        {
            if (String.IsNullOrWhiteSpace(code) || code.Length > 30 || code.Any(c => c < 32 || c > 126))
                throw new ErpException("La etiqueta admite un código exacto de 1 a 30 caracteres ASCII imprimibles. No se sustituyen caracteres del identificador.");
            var values = new List<int> { 104 };
            int checksum = 104;
            for (int i = 0; i < code.Length; i++) { int value = code[i] - 32; values.Add(value); checksum += value * (i + 1); }
            values.Add(checksum % 103); values.Add(106);
            string bits = String.Concat(values.Select(v => Patterns[v]));
            const int module = 3;
            int width = (bits.Length + 20) * module;
            var bitmap = new Bitmap(1200, 450, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(300, 300);
            using (var g = Graphics.FromImage(bitmap))
            using (var title = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var human = new Font("Consolas", 37, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 25, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.Clear(Color.White); g.SmoothingMode = SmoothingMode.None;
                g.DrawString("LEBAREF  |  " + kind, title, Brushes.Black, new RectangleF(20, 15, 1160, 50), format);
                g.DrawString(description ?? "", small, Brushes.Black, new RectangleF(20, 65, 1160, 40), format);
                int left = (1200 - width) / 2 + 10 * module;
                for (int i = 0; i < bits.Length; i++)
                    if (bits[i] == '1') g.FillRectangle(Brushes.Black, left + i * module, 120, module, 180);
                g.DrawString(code, human, Brushes.Black, new RectangleF(20, 315, 1160, 55), format);
                g.DrawString(extra ?? "", small, Brushes.Black, new RectangleF(20, 380, 1160, 45), format);
            }
            return bitmap;
        }
    }

    internal sealed class LabelForm : Form
    {
        private readonly Bitmap bitmap;
        private readonly string code;
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        public LabelForm(string code, string kind, string description, string extra)
        {
            this.code = code; bitmap = BarcodeLabel.Render(code, kind, description, extra);
            Text = "Lebaref | Etiqueta " + code; ClientSize = new Size(900, 440); MinimumSize = new Size(720, 390);
            Font = new Font("Segoe UI", 10F); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = bitmap, BackColor = Color.White }, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var save = MainForm.Action("Guardar PNG"); var print = MainForm.Action("Imprimir…"); var close = MainForm.Action("Cerrar");
            save.Click += (s, e) => Save(); print.Click += (s, e) => Print(); close.Click += (s, e) => Close();
            actions.Controls.AddRange(new Control[] { save, print, close }); layout.Controls.Add(actions, 0, 1); layout.Controls.Add(status, 0, 2);
            status.Text = "Code 128 · 300 ppp · 10.16 × 3.81 cm. Comprueba lectura y tamaño con tu impresora.";
            Controls.Add(layout); FormClosed += (s, e) => bitmap.Dispose();
        }

        private void Save()
        {
            using (var dialog = new SaveFileDialog { Filter = "Imagen PNG (*.png)|*.png", FileName = "Etiqueta-" + new string(code.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()) + ".png" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { bitmap.Save(dialog.FileName, ImageFormat.Png); status.Text = "Etiqueta guardada."; }
                catch (Exception error) { status.Text = "No se pudo guardar: " + error.Message; }
            }
        }

        private void Print()
        {
            try
            {
                using (var document = new PrintDocument())
                using (var dialog = new PrintDialog { UseEXDialog = true, Document = document })
                {
                    document.DocumentName = "Lebaref etiqueta " + code;
                    document.DefaultPageSettings.Margins = new Margins(25, 25, 25, 25);
                    document.PrintPage += (s, e) => {
                        if (e.MarginBounds.Width < 400 || e.MarginBounds.Height < 150)
                            throw new ErpException("El área imprimible debe medir al menos 10.16 × 3.81 cm. Ajusta el papel y los márgenes.");
                        e.Graphics.DrawImage(bitmap, new Rectangle(e.MarginBounds.X, e.MarginBounds.Y, 400, 150));
                        e.HasMorePages = false;
                    };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    document.Print(); status.Text = "Etiqueta enviada a la impresora seleccionada.";
                }
            }
            catch (Exception error) { status.Text = "No se pudo imprimir: " + error.Message; }
        }
    }
}
