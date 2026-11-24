// Превью макета для веб-редактора (П12.1): все объекты шаблона на одном битовом поле
// размером в наклейку. Растровые объекты (текст Windows, текстовые поля, штрихкоды-картинки,
// картинки, эллипсы) рисуются тем же кодом, что и печать — RasterShapeBitmaps, поэтому они
// на превью выглядят ровно так, как уйдут на принтер. Объекты-команды (текст шрифтом
// принтера, штрихкод командой, рамки, линии) принтер рисует сам — на превью их рисует GDI
// по тем же координатам, как предпросмотр самого GoLabel: положение точное, начертание близкое.
//
// Порядок отрисовки — как в задании (EzplJob.PrintNormalCommandEZPL): с конца списка, то есть
// первый объект списка ложится последним и перекрывает остальные. Переменные и счётчики не
// подставляются (решение пользователя 2026-10-29): редактор показывает макет с их кодами.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using GGA1C.Sync.Printing.GoLabel.Raster;

namespace GGA1C.Sync.Printing.GoLabel.Preview;

[SupportedOSPlatform("windows")]
public static class LabelPreview
{
    private static readonly Color Paper = Color.White;

    public static Bitmap Render(EzpxTemplate template)
    {
        var setup = template.Setup;
        int width = Math.Max(1, setup.WidthInPixel);
        int height = Math.Max(1, setup.HeightInPixel);

        var canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Paper);
            g.SmoothingMode = SmoothingMode.None;

            var shapes = template.Label.Shapes;
            for (int k = shapes.Count - 1; k >= 0; k--)
            {
                DrawShape(g, shapes[k], setup.Dpi);
            }
        }
        return canvas;
    }

    /// <summary>
    /// Растр одного объекта тем же кодом, что печать — для веб-редактора: растровые объекты
    /// (текст Windows, текстовые поля, логотипы, штрихкоды-картинки, эллипсы) вставляются в
    /// векторную канву картинками. Векторные (текст принтера, рамки, линии) — null: их рисует
    /// браузер. Растровые размеры — в точках принтера.
    /// </summary>
    public static (byte[] Png, int WidthDots, int HeightDots)? RenderShapePng(EzpxShape shape, float dpi)
    {
        if (shape is not EzpxRasterShape raster)
        {
            return null;
        }

        string data = shape is EzpxGraphic graphic ? graphic.Data : "";

        Bitmap? bitmap = shape.TypeName switch
        {
            "Image" => RasterShapeBitmaps.ImageBitmap(raster),
            "WindowText" => RasterShapeBitmaps.WindowTextBitmap(raster, data, dpi).Bitmap,
            "WindowTextBox" => RasterShapeBitmaps.WindowTextBoxBitmap(raster, data, dpi),
            "GdiWindowTextBox" => RasterShapeBitmaps.GdiTextBoxBitmap(raster, data, dpi),
            "Ellipse" => RasterShapeBitmaps.EllipseBitmap(raster),
            "BarCode" => rasterBarcode(shape, data),
            _ => null,
        };

        if (bitmap == null)
        {
            return null;
        }

        using (bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return (stream.ToArray(), bitmap.Width, bitmap.Height);
        }

        static Bitmap? rasterBarcode(EzpxShape shape, string data)
        {
            if (shape is not EzpxBarCode barcode || !barcode.IsRaster)
            {
                return null;
            }
            return data == " " ? null : RasterShapeBitmaps.BarCodeBitmap(barcode, data);
        }
    }

    /// <summary>Превью в PNG — картинку отдают в браузер.</summary>
    public static byte[] RenderPng(EzpxTemplate template)
    {
        using var bitmap = Render(template);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void DrawShape(Graphics g, EzpxShape shape, float dpi)
    {
        int x = shape.XInPixel;
        int y = shape.YInPixel;
        var data = shape is EzpxGraphic graphic ? graphic.Data : "";
        bool hidden = !shape.IsPrint;

        try
        {
            switch (shape)
            {
                case EzpxText text:
                    DrawPrinterText(g, text, x, y, hidden);
                    break;
                case EzpxBarCode barcode:
                    DrawBarCode(g, barcode, x, y, data, dpi, hidden);
                    break;
                case EzpxRectangle rect:
                    DrawFrame(g, x, y, (int)Math.Round(rect.Width, 0), (int)Math.Round(rect.Height, 0),
                        (int)Math.Round(rect.VStroke, 0), hidden, dashed: false);
                    break;
                case EzpxLine line:
                    DrawFrame(g, x, y, (int)Math.Round(line.Width, 0), (int)Math.Round(line.Height, 0), 1,
                        hidden, dashed: false);
                    break;
                case EzpxRasterShape raster:
                    DrawRaster(g, raster, x, y, data, dpi, hidden);
                    break;
                default:
                    DrawFrame(g, x, y, 60, 24, 1, hidden, dashed: true, caption: shape.TypeName);
                    break;
            }
        }
        catch (NotSupportedException)
        {
            // То, что порт не перенёс, показываем рамкой-заглушкой: редактор должен открывать
            // любой шаблон, а не только полностью переносимый.
            DrawFrame(g, x, y, 80, 28, 1, hidden, dashed: true, caption: shape.TypeName);
        }
        catch (InvalidOperationException)
        {
            DrawFrame(g, x, y, 80, 28, 1, hidden, dashed: true, caption: shape.TypeName);
        }
    }

    private static void DrawRaster(Graphics g, EzpxRasterShape shape, int x, int y, string data,
        float dpi, bool hidden)
    {
        using Bitmap? bitmap = shape.TypeName switch
        {
            "Image" => RasterShapeBitmaps.ImageBitmap(shape),
            "WindowText" => RasterShapeBitmaps.WindowTextBitmap(shape, data, dpi).Bitmap,
            "WindowTextBox" => RasterShapeBitmaps.WindowTextBoxBitmap(shape, data, dpi),
            "GdiWindowTextBox" => RasterShapeBitmaps.GdiTextBoxBitmap(shape, data, dpi),
            "Ellipse" => RasterShapeBitmaps.EllipseBitmap(shape),
            _ => null,
        };

        if (bitmap == null || hidden)
        {
            // Скрытый объект рисуем серой рамкой: он виден в редакторе, но не печатается.
            DrawFrame(g, x, y, bitmap?.Width ?? 60, bitmap?.Height ?? 24, 1, hidden,
                dashed: true, caption: hidden ? shape.TypeName : null);
            return;
        }

        g.DrawImage(bitmap, new Rectangle(x, y, bitmap.Width, bitmap.Height));
    }

    private static void DrawPrinterText(Graphics g, EzpxText text, int x, int y, bool hidden)
    {
        // Приближение, как в предпросмотре GoLabel: принтер нарисует своим шрифтом. Размер —
        // высота знака в точках (FontHeight в мм · точек в мм), семья из FontCmd, иначе базовая.
        float heightPx = Math.Clamp(text.FontHeight * 8f, 8f, 300f);
        string family = string.IsNullOrWhiteSpace(text.FontFamily) ? "Microsoft Sans Serif" : text.FontFamily;
        var style = (text.Bold ? FontStyle.Bold : FontStyle.Regular)
            | (text.Italic ? FontStyle.Italic : FontStyle.Regular);

        if (hidden)
        {
            var size = g.MeasureString(text.Data, new Font(family, heightPx, style, GraphicsUnit.Pixel));
            DrawFrame(g, x, y, (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 1,
                hidden: true, dashed: true, caption: "скрыт");
            return;
        }

        try
        {
            using var font = new Font(family, heightPx, style, GraphicsUnit.Pixel);
            g.DrawString(text.Data, font, Brushes.Black, x, y);
        }
        catch (ArgumentException)
        {
            // Семьи нет на машине (шрифт GoLabel) — рисуем тем, что есть.
            using var font = new Font("Microsoft Sans Serif", heightPx, style, GraphicsUnit.Pixel);
            g.DrawString(text.Data, font, Brushes.Black, x, y);
        }
    }

    private static void DrawBarCode(Graphics g, EzpxBarCode barcode, int x, int y, string data,
        float dpi, bool hidden)
    {
        if (barcode.IsRaster)
        {
            using var bitmap = RasterShapeBitmaps.BarCodeBitmap(barcode, data);
            if (bitmap == null)
            {
                return;
            }
            if (hidden)
            {
                DrawFrame(g, x, y, bitmap.Width, bitmap.Height, 1, hidden: true, dashed: true, caption: "скрыт");
                return;
            }
            g.DrawImage(bitmap, new Rectangle(x, y, bitmap.Width, bitmap.Height));
            return;
        }

        // Командный штрихкод рисует принтер. Для Code39 есть наш кодер — им и рисуем; остальное —
        // рамка с данными: точное начертание принтера превью не даёт, положение и размер — даёт.
        bool code39Family = barcode.Symbology is BarCodeType.Code39 or BarCodeType.Code39Extended;
        if (code39Family)
        {
            try
            {
                using var bitmap = RasterShapeBitmaps.BarCodeBitmap(barcode, data);
                if (bitmap != null)
                {
                    if (hidden)
                    {
                        DrawFrame(g, x, y, bitmap.Width, bitmap.Height, 1, hidden: true, dashed: true, caption: "скрыт");
                        return;
                    }
                    g.DrawImage(bitmap, new Rectangle(x, y, bitmap.Width, bitmap.Height));
                    return;
                }
            }
            catch (NotSupportedException)
            {
                // ниже — рамка
            }
        }

        int width = Math.Max(40, data.Length * barcode.NarrowInPixel * 3);
        if (hidden)
        {
            DrawFrame(g, x, y, width, barcode.HeightInPixel, 1, hidden: true, dashed: true, caption: "скрыт");
            return;
        }
        g.DrawRectangle(Pens.Black, x, y, width, barcode.HeightInPixel);
        using var small = new Font("Microsoft Sans Serif", 8f, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString(data, small, Brushes.Black, x + 2, y + 2);
    }

    private static void DrawFrame(Graphics g, int x, int y, int width, int height, int stroke,
        bool hidden, bool dashed, string? caption = null)
    {
        stroke = Math.Clamp(stroke, 1, 16);
        width = Math.Max(width, stroke);
        height = Math.Max(height, stroke);
        var color = hidden ? Color.Silver : dashed ? Color.Red : Color.Black;

        using var pen = new Pen(color, stroke);
        if (dashed)
        {
            pen.DashStyle = DashStyle.Dash;
        }
        g.DrawRectangle(pen, x, y, width - stroke, height - stroke);

        if (caption != null)
        {
            using var font = new Font("Microsoft Sans Serif", 8f, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString(caption, font, new SolidBrush(color), x + 2, y + 2);
        }
    }
}
