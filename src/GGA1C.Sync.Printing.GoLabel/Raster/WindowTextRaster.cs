// Текст шрифтом Windows одной надписью — WindowText (GoLabel рисует GDI+ и шлёт растром Q).
// Источник: WindowText.PrintMyToString(x, y)/GetRender2Bmp/Draw2BmpTTFont/MeasureAdjustPosition
// (QLabelSDK/WindowText.cs:1252, :492, :266, :1070), ImageAccess.GraphicsRotation (:437).
// Ветка без GDI (UseGDI = false: TrueType, интервал между знаками 0, Windows 10+).
// Сглаживание задаём явно, как в TextBoxRaster (p5-plan.md, «Главный вывод»).
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Linq;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
internal static class WindowTextRaster
{
    /// <summary>
    /// Растр надписи и точка привязки (левый верхний угол растра в точках) — как
    /// PrintMyToString: num/num2 после MeasureAdjustPosition и поворота.
    /// </summary>
    public static (Bitmap Bitmap, int X, int Y) Render(EzpxRasterShape shape, string dispData, float dpi)
    {
        var e = shape.Element!;
        string fontCmd = ((string?)e.Attribute("FontCmd") ?? "Microsoft Sans Serif,24").TrimEnd('\r', '\n');
        string[] parts = fontCmd.Split(',');
        string family = parts[0];
        float points = XmlConvert.ToSingle(parts[1].Trim());
        string styleText = parts.Length > 2 ? parts[2] : "";
        var style = (FontStyle)((styleText.Contains('B') ? 1 : 0) | (styleText.Contains('I') ? 2 : 0)
            | (styleText.Contains('U') ? 4 : 0) | (styleText.Contains('S') ? 8 : 0));
        float fontHeight = Float(e.Attribute("FontHeight"), 1000f);
        float fontWidth = Float(e.Attribute("FontWidth"), 1000f);
        if (Bool(e.Attribute("bSpaceCropping")) || Bool(e.Element("IsInverse")) || Bool(e.Attribute("IsInverse"))
            || Int(e.Element("VerticalKerningOffset")) != 0 || Int(e.Element("CharacterSpacingRate")) != 0
            || Int(e.Attribute("TextSpace")) != 0
            || e.Element("BTrueType") is { } tt && !XmlConvert.ToBoolean(tt.Value)
            || (string?)e.Attribute("FontScript") is { } script && script != "Default"
            || Float(e.Element("Angle"), 0f) != 0f)
        {
            throw new NotSupportedException("WindowText с обрезкой полей, инверсией, интервалами, поворотом на угол, письмом справа налево или не-TrueType шрифтом не перенесён.");
        }
        var direction = shape.Direction;
        if (direction > Orientation.Angle270)
        {
            throw new NotSupportedException("Вертикальный WindowText (по знаку в столбик) не перенесён.");
        }
        if (dispData == "")
        {
            dispData = " ";
        }

        // GetRender2Bmp: замер на картинке 100×100 (96 dpi), шрифт — размер · dpi принтера / 96.
        using var probe = new Bitmap(100, 100, PixelFormat.Format24bppRgb);
        using var measure = Graphics.FromImage(probe);
        measure.TextRenderingHint = TextBoxRaster.Hint;
        using var font = new Font(family, points * (dpi / measure.DpiX), style);
        // GoLabel берёт StringFormat.GenericDefault и ПЕРЕПИСЫВАЕТ ему флаги на флаги
        // GenericTypographic (общий объект GDI+ на весь процесс). Здесь — своя копия с теми же
        // флагами, без порчи общего.
        using var typographic = new StringFormat(StringFormat.GenericTypographic);
        using var defaultWithTypoFlags = new StringFormat(StringFormat.GenericDefault)
        {
            FormatFlags = typographic.FormatFlags,
            Alignment = typographic.Alignment,
        };
        SizeF size = measure.MeasureString(dispData, font, 32767, defaultWithTypoFlags);
        if (font.Italic)
        {
            // sizeV.Width/3 — ширина самого широкого знака × число строк.
            float widest = 1f;
            foreach (char c in dispData.Where(c => c != '\n'))
            {
                widest = Math.Max(widest, measure.MeasureString(c.ToString(), font, 32767, typographic).Width);
            }
            int lines = dispData.Count(c => c == '\n') + 1;
            size.Width += widest * lines / 3f;
        }
        size.Width = size.Width * fontWidth / fontHeight;
        int width = (int)size.Width > 0 ? (int)(size.Width + 0.99) : 10;
        int height = (int)size.Height > 0 ? (int)(size.Height + 1.5) : 10;

        var render = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(render))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextBoxRaster.Hint;
            using var format = new StringFormat(StringFormat.GenericTypographic);
            var alignment = shape.Alignment;
            int num3 = (int)(size.Width + 0.99);
            int anchor = 0;
            switch (alignment)
            {
                case TextAlignment.Center:
                    format.Alignment = StringAlignment.Center;
                    anchor = num3 / 2;
                    break;
                case TextAlignment.Right:
                    format.Alignment = StringAlignment.Far;
                    anchor = num3;
                    break;
                default:
                    format.Alignment = StringAlignment.Near;
                    break;
            }
            g.ScaleTransform(fontWidth / fontHeight, 1f);
            using var drawFont = new Font(family, points * (dpi / g.DpiX), style);
            g.DrawString(dispData, drawFont, Brushes.Black, anchor, 0f, format);
        }

        // MeasureAdjustPosition(width): точка по BoundRect и выравниванию.
        var r = shape.BoundRect;
        float x, y;
        switch (direction)
        {
            case Orientation.Angle0:
                x = shape.Alignment switch
                {
                    TextAlignment.Center => r.Left + (r.Width - width) / 2f,
                    TextAlignment.Right => r.Left + (r.Width - width),
                    _ => r.Left,
                };
                y = r.Top;
                x = Math.Max(x, 0f);
                break;
            case Orientation.Angle90:
                x = r.Right;
                y = shape.Alignment switch
                {
                    TextAlignment.Center => r.Top + (r.Height - width) / 2f,
                    TextAlignment.Right => r.Top + (r.Height - width),
                    _ => r.Top,
                };
                break;
            case Orientation.Angle180:
                x = shape.Alignment switch
                {
                    TextAlignment.Center => r.Right - (r.Width - width) / 2f,
                    TextAlignment.Right => r.Right - (r.Width - width),
                    _ => r.Right,
                };
                y = r.Bottom;
                x = Math.Max(x, 0f);
                break;
            default:
                x = r.Left;
                y = shape.Alignment switch
                {
                    TextAlignment.Center => r.Bottom - (r.Height - width) / 2f,
                    TextAlignment.Right => r.Bottom - (r.Height - width),
                    _ => r.Bottom,
                };
                break;
        }
        int px = (int)Math.Round(x, 0);
        int py = (int)Math.Round(y, 0);
        if (direction == Orientation.Angle0)
        {
            return (render, px, py);
        }

        // Поворот: картинка 24 бит на белом, ImageAccess.GraphicsRotation, затем растр.
        int angle = direction switch { Orientation.Angle90 => 90, Orientation.Angle180 => 180, _ => 270 };
        var rotated = angle == 180
            ? new Bitmap(width, height, PixelFormat.Format24bppRgb)
            : new Bitmap(height, width, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(rotated))
        {
            g.Clear(Color.White);
            switch (angle)
            {
                case 90:
                    g.TranslateTransform(height, 0f);
                    g.RotateTransform(90f);
                    break;
                case 180:
                    g.TranslateTransform(width, height);
                    g.RotateTransform(180f);
                    break;
                default:
                    g.TranslateTransform(0f, width);
                    g.RotateTransform(270f);
                    break;
            }
            g.DrawImage(render, 0, 0);
        }
        render.Dispose();
        return angle switch
        {
            90 => (rotated, px - rotated.Width, py),
            180 => (rotated, px - rotated.Width, py - rotated.Height),
            _ => (rotated, px, py - rotated.Height),
        };
    }

    private static bool Bool(XAttribute? a) => a != null && XmlConvert.ToBoolean(a.Value);

    private static bool Bool(XElement? e) => e != null && XmlConvert.ToBoolean(e.Value);

    private static int Int(XAttribute? a) => a == null ? 0 : XmlConvert.ToInt32(a.Value);

    private static int Int(XElement? e) => e == null ? 0 : XmlConvert.ToInt32(e.Value);

    private static float Float(XAttribute? a, float fallback) => a == null ? fallback : XmlConvert.ToSingle(a.Value);

    private static float Float(XElement? e, float fallback) => e == null ? fallback : XmlConvert.ToSingle(e.Value);
}
