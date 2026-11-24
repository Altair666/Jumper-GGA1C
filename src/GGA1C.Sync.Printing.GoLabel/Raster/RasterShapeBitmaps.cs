// Источник растров объектов: ровно то, что GdiRasterRenderer шлёт принтером командой Q.
// Вынесено из GdiRasterRenderer (2026-10-28, П12.1), чтобы превью редактора (LabelPreview)
// рисовалось тем же кодом, что печать — иначе превью и принтер разойдутся.
// Источники GoLabel: Image.PrintMyToString/ReDraw/Draw/LoadImage_From_Base64 (QLabelSDK/Image.cs:787,
// :571, :405, :597), ImageAccess.RotateImage (ImageAccess.cs:235), Ellipse.PrintMyToString(x, y)
// (Ellipse.cs:473), WindowText.PrintMyToString (WindowText.cs:1252), BarCode.PrintBmpBarcode.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Linq;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
internal static class RasterShapeBitmaps
{
    /// <summary>
    /// Image (LoadToDevice = false). Картинка — из BitmapCmd (base64): файла из FileName на
    /// печатающей машине нет, GoLabel берёт ту же копию из шаблона. Перед печатью GoLabel рисует
    /// объект на экран (Image.Draw — из LinkedVar.UpdateLinkVariableData), и там
    /// CurrentImage.MakeTransparent(White): белые точки становятся прозрачными, а это меняет
    /// бикубическое масштабирование на краях — повторяем.
    /// </summary>
    public static Bitmap ImageBitmap(EzpxRasterShape shape)
    {
        var e = shape.Element!;
        if (Bool(e.Element("LoadToDevice")))
        {
            throw new NotSupportedException("Картинка из памяти принтера (LoadToDevice) не перенесена.");
        }
        if ((e.Element("DitherType")?.Value ?? "Default") != "Default"
            || XmlConvert.ToSingle(e.Element("Angle")?.Value ?? "0") != 0f
            || (e.Element("RotationFlip")?.Value ?? "RotateNoneFlipNone") != "RotateNoneFlipNone"
            || Bool(e.Element("Binverse"))
            || Bool(e.Element("FixedAspectRatio")))
        {
            throw new NotSupportedException("Картинка с поворотом, инверсией, сохранением пропорций или растрированием не перенесена.");
        }
        string base64 = e.Element("BitmapCmd")?.Value ?? throw new InvalidOperationException("У картинки нет BitmapCmd.");

        using var stream = new MemoryStream(Convert.FromBase64String(base64));
        using var image = new Bitmap(stream);
        // RotateImage(angle 0): new Bitmap(image) — копия 32 бит ARGB; затем MakeTransparent(White).
        using var current = new Bitmap(image);
        current.MakeTransparent(Color.White);

        var rect = shape.BoundRect;
        var canvas = new Bitmap((int)rect.Width, (int)rect.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(current, new RectangleF(0f, 0f, rect.Width, rect.Height));
        }
        Raster1Bit.DitherDefault(canvas);
        return canvas;
    }

    /// <summary>Ellipse.PrintMyToString(x, y): заливка штриховкой и контур пером, без сглаживания.</summary>
    public static Bitmap EllipseBitmap(EzpxRasterShape shape)
    {
        var e = shape.Element!;
        float stroke = XmlConvert.ToSingle(e.Element("Stroke")?.Value ?? "0");
        var style = Enum.Parse<HatchStyle>((string?)e.Attribute("Style") ?? "Cross");
        bool fill = (string?)e.Attribute("bFill") is not { } f || XmlConvert.ToBoolean(f);
        bool drawStroke = (string?)e.Attribute("bStroke") is not { } s || XmlConvert.ToBoolean(s);
        // iBackground_color: 0 — чёрный фон штриховки, иначе белый (конструктор — белый).
        uint background = e.Element("iBackground_color") is { } bg ? XmlConvert.ToUInt32(bg.Value) : uint.MaxValue;
        var dash = Enum.Parse<DashStyle>(e.Element("DashStyle")?.Value ?? "Solid");

        var canvas = new Bitmap((int)shape.Width, (int)shape.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var rect = new RectangleF(0f + stroke / 2f, 0f + stroke / 2f, shape.Width - stroke, shape.Height - stroke);
            using var brush = new HatchBrush(style, Color.Black, background != 0 ? Color.White : Color.Black);
            using var pen = new Pen(Color.Black, stroke) { DashStyle = dash };
            if (fill)
            {
                g.FillEllipse(brush, rect);
            }
            if (drawStroke)
            {
                g.DrawEllipse(pen, rect);
            }
        }
        return canvas;
    }

    /// <summary>WindowText: растр надписи и точка привязки (см. WindowTextRaster.Render).</summary>
    public static (Bitmap Bitmap, int X, int Y) WindowTextBitmap(EzpxRasterShape shape, string dispData, float dpi)
    {
        return WindowTextRaster.Render(shape, dispData, dpi);
    }

    /// <summary>GdiWindowTextBox.PrintMyToString(x, y).</summary>
    public static Bitmap GdiTextBoxBitmap(EzpxRasterShape shape, string data, float dpi)
    {
        return new TextBoxRaster(shape, dpi).Render(data);
    }

    /// <summary>WindowTextBox.PrintMyToString(x, y), без поворота. Ширина рамки —
    /// (int)(W · 0.1) / 0.1: эталон gkv-avto-product — 428 → 420.</summary>
    public static Bitmap WindowTextBoxBitmap(EzpxRasterShape shape, string data, float dpi)
    {
        const float zoom = 0.1f;
        float drawnWidth = (float)(int)((float)(int)shape.BoundRect.Width * zoom) / zoom;
        return new TextBoxRaster(shape, dpi, window: true).Render(data, (int)drawnWidth);
    }

    /// <summary>BarCode.PrintBmpBarcode: пустые данные — ничего не печатается (null).</summary>
    public static Bitmap? BarCodeBitmap(EzpxBarCode b, string data)
    {
        if (data == " ")
        {
            return null;
        }
        return Code39Raster.Render(b, data);
    }

    private static bool Bool(XElement? e) => e != null && XmlConvert.ToBoolean(e.Value);
}
