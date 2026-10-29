// Объекты, которые GoLabel рисует сам (GDI+) и шлёт растром Q.
// Источник: Image.PrintMyToString/ReDraw/Draw/LoadImage_From_Base64 (QLabelSDK/Image.cs:787, :571,
// :405, :597), ImageAccess.RotateImage (ImageAccess.cs:235), Ellipse.PrintMyToString(x, y)
// (Ellipse.cs:473), GraphicShape.iBackground_color (GraphicShape.cs:171).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Linq;
using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
public sealed class GdiRasterRenderer : IRasterRenderer
{
    public void Render(RasterRequest request, EzplWriter output)
    {
        switch (request.Shape.TypeName)
        {
            case "Image":
                RenderImage((EzpxRasterShape)request.Shape, request.OffsetX, request.OffsetY, output);
                break;
            case "BarCode":
                RenderBarCode((EzpxBarCode)request.Shape, request.OffsetX, request.OffsetY, request.Job, output);
                break;
            case "GdiWindowTextBox":
                RenderGdiTextBox((EzpxRasterShape)request.Shape, request.OffsetX, request.OffsetY, request.Job, output);
                break;
            case "WindowTextBox":
                RenderWindowTextBox((EzpxRasterShape)request.Shape, request.OffsetX, request.OffsetY, request.Job, output);
                break;
            case "WindowText":
            {
                var shape = (EzpxRasterShape)request.Shape;
                string data = request.Job.FillPrintingData(shape, bformat: false);
                var (bitmap, px, py) = WindowTextRaster.Render(shape, data, request.Job.Dpi);
                using (bitmap)
                {
                    Raster1Bit.WriteQ(output, (int)Math.Round(request.OffsetX, 0) + px, (int)Math.Round(request.OffsetY, 0) + py, bitmap);
                }
                break;
            }
            case "Ellipse":
                RenderEllipse((EzpxRasterShape)request.Shape, request.OffsetX, request.OffsetY, output);
                break;
            default:
                throw new NotSupportedException($"Объект {request.Shape.TypeName} растром ещё не перенесён.");
        }
    }

    /// <summary>
    /// Image (LoadToDevice = false). Картинка — из BitmapCmd (base64): файла из FileName на
    /// печатающей машине нет, GoLabel берёт ту же копию из шаблона. Перед печатью GoLabel рисует
    /// объект на экран (Image.Draw — из LinkedVar.UpdateLinkVariableData), и там
    /// CurrentImage.MakeTransparent(White): белые точки становятся прозрачными, а это меняет
    /// бикубическое масштабирование на краях — повторяем.
    /// </summary>
    private static void RenderImage(EzpxRasterShape shape, float x, float y, EzplWriter output)
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
        using var canvas = new Bitmap((int)rect.Width, (int)rect.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(current, new RectangleF(0f, 0f, rect.Width, rect.Height));
        }
        Raster1Bit.DitherDefault(canvas);
        Raster1Bit.WriteQ(output, (int)Math.Round(x, 0) + shape.XInPixel, (int)Math.Round(y, 0) + shape.YInPixel, canvas);
    }

    /// <summary>Ellipse.PrintMyToString(x, y): заливка штриховкой и контур пером, без сглаживания.</summary>
    private static void RenderEllipse(EzpxRasterShape shape, float x, float y, EzplWriter output)
    {
        var e = shape.Element!;
        float stroke = XmlConvert.ToSingle(e.Element("Stroke")?.Value ?? "0");
        var style = Enum.Parse<HatchStyle>((string?)e.Attribute("Style") ?? "Cross");
        bool fill = (string?)e.Attribute("bFill") is not { } f || XmlConvert.ToBoolean(f);
        bool drawStroke = (string?)e.Attribute("bStroke") is not { } s || XmlConvert.ToBoolean(s);
        // iBackground_color: 0 — чёрный фон штриховки, иначе белый (конструктор — белый).
        uint background = e.Element("iBackground_color") is { } bg ? XmlConvert.ToUInt32(bg.Value) : uint.MaxValue;
        var dash = Enum.Parse<DashStyle>(e.Element("DashStyle")?.Value ?? "Solid");

        using var canvas = new Bitmap((int)shape.Width, (int)shape.Height, PixelFormat.Format24bppRgb);
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
        Raster1Bit.WriteQ(output, (int)(x + shape.X), (int)(y + shape.Y), canvas);
    }

    /// <summary>GdiWindowTextBox.PrintMyToString(x, y).</summary>
    private static void RenderGdiTextBox(EzpxRasterShape shape, float x, float y, EzplJob job, EzplWriter output)
    {
        string data = job.FillPrintingData(shape, bformat: false);
        using var box = new TextBoxRaster(shape, job.Dpi).Render(data);
        Raster1Bit.WriteQ(output, (int)Math.Round(x, 0) + shape.XInPixel, (int)Math.Round(y, 0) + shape.YInPixel, box);
    }

    /// <summary>WindowTextBox.PrintMyToString(x, y), без поворота.</summary>
    private static void RenderWindowTextBox(EzpxRasterShape shape, float x, float y, EzplJob job, EzplWriter output)
    {
        job.FillPrintingData(shape, bformat: true);
        string data = job.FillPrintingData(shape, bformat: false);
        // WindowTextBox.Draw (её зовёт LinkedVar.UpdateLinkVariableData перед печатью) рисует при
        // QLabel.ZoomRate = 0.1 (в командном режиме GoLabel его не меняет) и ставит ширину рамки
        // (int)(W · 0.1) / 0.1: эталон gkv-avto-product — 428 → 420.
        const float zoom = 0.1f;
        float drawnWidth = (float)(int)((float)(int)shape.BoundRect.Width * zoom) / zoom;
        using var text = new TextBoxRaster(shape, job.Dpi, window: true).Render(data, (int)drawnWidth);
        Raster1Bit.WriteQ(output, (int)Math.Round(x, 0) + shape.XInPixel, (int)Math.Round(y, 0) + shape.YInPixel, text);
    }

    /// <summary>BarCode.PrintBmpBarcode: пустые данные — ничего не печатается.</summary>
    private static void RenderBarCode(EzpxBarCode b, float x, float y, EzplJob job, EzplWriter output)
    {
        string data = job.FillPrintingData(b, bformat: false);
        if (data == " ")
        {
            return;
        }
        using var bitmap = Code39Raster.Render(b, data);
        Raster1Bit.WriteQ(output, b.XInPixel + (int)x, b.YInPixel + (int)y, bitmap);
    }

    private static bool Bool(XElement? e) => e != null && XmlConvert.ToBoolean(e.Value);
}
