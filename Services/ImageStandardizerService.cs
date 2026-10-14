using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SistemaComprasAlmacen.Data;

namespace SistemaComprasAlmacen.Services;

public static class ImageStandardizerService
{
    private const int TargetSize = 400;

    public static string? GuardarDesdePortapapeles(string codigoProducto)
    {
        if (!Clipboard.ContainsImage())
            return null;

        var bmp = Clipboard.GetImage();
        if (bmp == null)
            return null;

        return EstandarizarYGuardarBitmap(bmp, codigoProducto);
    }

    public static string? GuardarDesdeArchivo(string rutaOrigen, string codigoProducto)
    {
        if (!File.Exists(rutaOrigen))
            return null;

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(rutaOrigen, UriKind.Absolute);
        bi.EndInit();
        bi.Freeze();

        return EstandarizarYGuardarBitmap(bi, codigoProducto);
    }

    private static string EstandarizarYGuardarBitmap(BitmapSource source, string codigoProducto)
    {
        Directory.CreateDirectory(DatabaseService.ImagesFolder);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // Fondo blanco limpio para mantener estándar visual tipo tienda online
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, TargetSize, TargetSize));

            double scale = Math.Min((TargetSize - 24.0) / source.PixelWidth, (TargetSize - 24.0) / source.PixelHeight);
            double drawW = source.PixelWidth * scale;
            double drawH = source.PixelHeight * scale;
            double offsetX = (TargetSize - drawW) / 2.0;
            double offsetY = (TargetSize - drawH) / 2.0;

            dc.DrawImage(source, new Rect(offsetX, offsetY, drawW, drawH));
        }

        var rtb = new RenderTargetBitmap(TargetSize, TargetSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();

        string safeCode = string.IsNullOrWhiteSpace(codigoProducto) ? $"IMG_{DateTime.Now:yyyyMMddHHmmss}" : codigoProducto.Replace("/", "_").Replace("\\", "_");
        string destPath = Path.Combine(DatabaseService.ImagesFolder, $"{safeCode}_{DateTime.Now:HHmmss}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(destPath))
        {
            encoder.Save(fs);
        }

        return destPath;
    }

    public static ImageSource? CargarImagenSegura(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta))
            return null;

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(ruta, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
