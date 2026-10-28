// Картинка → команда Q. Источник: ImageAccess.AnyBitmapTo1Bits (QLabelSDK/ImageAccess.cs:122),
// ImageAccess.DitherImage (:332), PrintJob.SendBitmapToPrinterEZPL (PrintJob.cs:2148).
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GGA1C.Sync.Printing.GoLabel.Ezpl;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
internal static class Raster1Bit
{
    /// <summary>
    /// Порог чёрной точки AnyBitmapTo1Bits при Graphic.FontThick = Default (в user.config GoLabel
    /// стенда значения нет): зелёный канал ≤ 192.
    /// </summary>
    private const byte Threshold = 192;

    /// <summary>
    /// AnyBitmapTo1Bits + SendBitmapToPrinterEZPL(x, y, bmp, Invese = 1): ширина добивается до
    /// кратной 8 белым, чёрная точка = 1, строка — (ширина + 7) / 8 байт, после растра CR LF.
    /// </summary>
    public static void WriteQ(EzplWriter output, int x, int y, Bitmap source)
    {
        if (x < 0 || y < 0)
        {
            throw new NotSupportedException("Растр с отрицательной координатой (Crop1bppIndexed) не перенесён.");
        }
        int width = (source.Width + 7) / 8 * 8;
        int height = source.Height;
        int bytesPerRow = width / 8;
        byte[] rows = new byte[bytesPerRow * height];
        byte[] pixels = Read24(source, out int stride);
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < source.Width; col++)
            {
                byte green = pixels[row * stride + col * 3 + 1];
                if (green <= Threshold)
                {
                    rows[row * bytesPerRow + col / 8] |= (byte)(0x80 >> (col % 8));
                }
            }
        }
        output.Write($"Q{x},{y},{bytesPerRow},{height}\n");
        output.WriteBytes(rows);
        output.Write("\r\n");
    }

    /// <summary>ImageAccess.DitherImage для Dither_Type = 0 (Default): порог яркости 127.5.</summary>
    public static void DitherDefault(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int size = data.Stride * bitmap.Height;
            byte[] pixels = new byte[size];
            Marshal.Copy(data.Scan0, pixels, 0, size);
            for (int row = 0; row < bitmap.Height; row++)
            {
                int p = row * data.Stride;
                for (int col = 0; col < bitmap.Width; col++, p += 3)
                {
                    float luma = 0.114f * (float)(int)pixels[p] + 0.587f * (float)(int)pixels[p + 1] + 0.299f * (float)(int)pixels[p + 2] + 0.5f;
                    byte v = (double)luma >= 127.5 ? (byte)255 : (byte)0;
                    pixels[p] = v;
                    pixels[p + 1] = v;
                    pixels[p + 2] = v;
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, size);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// Пиксели как 24 бит BGR. AnyBitmapTo1Bits сначала рисует картинку на белом 24-битном
    /// холсте 1:1 — для 24-битной картинки это те же пиксели.
    /// </summary>
    private static byte[] Read24(Bitmap source, out int stride)
    {
        using var canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.DrawImage(source, 0, 0, source.Width, source.Height);
        }
        var data = canvas.LockBits(new Rectangle(0, 0, canvas.Width, canvas.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            stride = data.Stride;
            byte[] pixels = new byte[data.Stride * canvas.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            canvas.UnlockBits(data);
        }
    }
}
