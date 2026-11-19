using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Lebaref.Windows;
class LabelCheck
{
    [STAThread] static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        string[] codes = { "HER-0001", "KIT-DEMO-20261119", "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234", "HER /01.$+_%" };
        for (int i = 0; i < codes.Length; i++)
            using (var bitmap = BarcodeLabel.Render(codes[i], "HERRAMIENTA / KIT", "Etiqueta de verificación", "Identificador exacto"))
                bitmap.Save(Path.Combine(args[0], "etiqueta-" + i + ".png"), ImageFormat.Png);
        foreach (string invalid in new[] { "", "Á-01", new string('A',31), "HER\n001" })
        {
            try { using(var b = BarcodeLabel.Render(invalid, "", "", "")) { } throw new Exception("Código inválido aceptado"); }
            catch(ErpException) { }
        }
        File.WriteAllLines(Path.Combine(args[0], "codigos.txt"), codes);
        Console.WriteLine("Etiquetas y validación de identificadores generadas; sin impresión física.");
    }
}
