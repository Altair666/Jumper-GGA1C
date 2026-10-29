// Текст шрифтом Windows в рамке — GdiWindowTextBox (GoLabel рисует GDI+ и шлёт растром Q).
// Источник: GdiWindowTextBox.PrintMyToString(x, y)/GetRender2Bmp/myDraw2BmpTTFont/myDrawString/
// Cal_FontSize/Cal_PosY/WrapText/WrapTextHeight (QLabelSDK/GdiWindowTextBox.cs:2093, :887, :748,
// :685, :1012, :1066, :391, :544).
// Сглаживание: GoLabel его не задаёт (SystemDefault — ClearType сеанса стенда); порт задаёт
// ClearTypeGridFit явно — и для замеров, и для отрисовки (p5-plan.md, «Главный вывод»).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
internal sealed class TextBoxRaster
{
    public const TextRenderingHint Hint = TextRenderingHint.ClearTypeGridFit;

    private readonly string _family;
    private readonly float _size;
    private readonly FontStyle _style;
    private readonly TextAlignment _alignment;
    private readonly string _lineSpacing;
    private int _lineHeight;
    private readonly int _multiple;
    private readonly int _dotBeforePara;
    private readonly int _dotAfterPara;
    private readonly bool _autoFontResize;
    private readonly bool _wordWrap;
    private readonly RectF _rect;
    private readonly Orientation _direction;
    private readonly float _dpi;
    private readonly bool _window;

    /// <param name="window">WindowTextBox (WindowTextBox.cs): формат GenericDefault + Trimming.Word,
    /// без подбора размера и без рамки — картинка текста уходит как есть.</param>
    public TextBoxRaster(EzpxRasterShape shape, float dpi, bool window = false)
    {
        var e = shape.Element!;
        _dpi = dpi;
        _window = window;
        _rect = shape.BoundRect;
        _direction = shape.Direction;
        _alignment = shape.Alignment;
        string fontCmd = ((string?)e.Attribute("FontCmd") ?? "Microsoft Sans Serif,24").TrimEnd('\r', '\n');
        string[] parts = fontCmd.Split(',');
        _family = parts[0];
        _size = XmlConvert.ToSingle(parts[1].Trim());
        string style = parts.Length > 2 ? parts[2] : "";
        _style = (FontStyle)((style.Contains('B') ? 1 : 0) | (style.Contains('I') ? 2 : 0) | (style.Contains('U') ? 4 : 0) | (style.Contains('S') ? 8 : 0));
        _lineSpacing = (string?)e.Attribute("LineSpacing") ?? "Single";
        _lineHeight = Int(e.Attribute("LineHeight"), 0);
        _multiple = Int(e.Attribute("Multiple"), 10);
        _dotBeforePara = Int(e.Attribute("DotBeforePara"), 0);
        _dotAfterPara = Int(e.Attribute("DotAfterPara"), 0);
        _autoFontResize = !window && Bool(e.Attribute("AutoFontResize"));
        _wordWrap = (e.Element("WrapMode")?.Value ?? "WordWrap") == "WordWrap";
        if (Bool(e.Attribute("bSpaceCropping")) || Bool(e.Attribute("IsInverse")) || _alignment == TextAlignment.Distributed
            || (string?)e.Attribute("FontScript") is { } script && script != "Default" || e.Element("BTrueType") is { } tt && !XmlConvert.ToBoolean(tt.Value)
            || e.Attribute("BTrueType") is { } ta && !XmlConvert.ToBoolean(ta.Value) || Bool(e.Element("SplitDataByAI")))
        {
            throw new NotSupportedException("GdiWindowTextBox с обрезкой полей, инверсией, распределением, письмом справа налево или не-TrueType шрифтом не перенесён.");
        }
        if (_direction != Orientation.Angle0)
        {
            throw new NotSupportedException("Повёрнутый GdiWindowTextBox не перенесён.");
        }
        Bounding = (string?)e.Attribute("BoundingAlignment") ?? "Top";
    }

    /// <summary>BoundingAlignment: None/Top, Center, Bottom.</summary>
    public string Bounding { get; }

    /// <summary>Готовая картинка рамки (bitmap2 в PrintMyToString), 32 бит ARGB, вне текста — прозрачная.</summary>
    public Bitmap Render(string dispData, int? widthOverride = null)
    {
        int width = widthOverride ?? (int)_rect.Width;
        if (_window)
        {
            return GetRender2Bmp(width, dispData, out _);
        }
        using var text = GetRender2Bmp(width, dispData, out float textHeight);
        var box = new Bitmap((int)_rect.Width, (int)_rect.Height);
        using var g = Graphics.FromImage(box);
        switch (Bounding)
        {
            case "None" or "Top":
                g.DrawImage(text, new Rectangle(0, 0, box.Width, box.Height), new Rectangle(0, 0, box.Width, box.Height), GraphicsUnit.Pixel);
                break;
            case "Center":
            {
                int shift = (int)((textHeight - _rect.Height) / 2f);
                Draw(g, text, box, textHeight, shift);
                break;
            }
            case "Bottom":
            {
                int shift = (int)(textHeight - _rect.Height);
                Draw(g, text, box, textHeight, shift);
                break;
            }
            default:
                throw new NotSupportedException($"BoundingAlignment {Bounding} не перенесён.");
        }
        return box;
    }

    private void Draw(System.Drawing.Graphics g, Bitmap text, Bitmap box, float textHeight, int shift)
    {
        if (textHeight > _rect.Height)
        {
            g.DrawImage(text, new Rectangle(0, 0, box.Width, box.Height), new Rectangle(0, shift, box.Width, box.Height), GraphicsUnit.Pixel);
        }
        else
        {
            g.DrawImage(text, new Rectangle(0, -shift, box.Width, (int)textHeight), new Rectangle(0, 0, box.Width, (int)textHeight), GraphicsUnit.Pixel);
        }
    }

    /// <summary>GetRender2Bmp(width): высота по переносам строк, затем отрисовка.</summary>
    private Bitmap GetRender2Bmp(int width, string dispData, out float height)
    {
        using var probe = new Bitmap(100, 100, PixelFormat.Format24bppRgb);
        using var g = NewGraphics(probe, clear: false);
        float fontSize = _size * (_dpi / g.DpiX) + 0.5f;
        var font = new Font(_family, fontSize, _style);
        using var format = Format();
        _lineHeight = LineHeightFor(font);
        int total = 0;
        foreach (string paragraph in dispData.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            int lines = WrapTextHeight(font, paragraph, width, g, format);
            while (lines-- > 0)
            {
                total += total == 0 ? _dotBeforePara : 0;
                total += _lineHeight;
            }
            total += _dotBeforePara == _dotAfterPara ? _dotBeforePara : Math.Max(_dotBeforePara, _dotAfterPara);
        }
        if (_autoFontResize)
        {
            total = CalFontSize(g, format, ref font, width, dispData);
        }
        font.Dispose();
        height = total;
        var bitmap = new Bitmap(Math.Max(width, 1), Math.Max(total, 1), PixelFormat.Format24bppRgb);
        using var draw = NewGraphics(bitmap, clear: true);
        Draw2BmpTTFont(draw, width, dispData);
        height = bitmap.Height;
        return bitmap;
    }

    /// <summary>myDraw2BmpTTFont.</summary>
    private void Draw2BmpTTFont(System.Drawing.Graphics g, int width, string dispData)
    {
        float fontSize = _size * (_dpi / g.DpiX) + 0.5f;
        var font = new Font(_family, fontSize, _style);
        using var format = Format();
        int x = _alignment switch
        {
            TextAlignment.Center => width / 2,
            TextAlignment.Right => width,
            _ => 0,
        };
        _lineHeight = LineHeightFor(font);
        if (font.Italic && _alignment == TextAlignment.Right)
        {
            x -= (int)(font.Size / 2f);
        }
        int yPos = 0;
        if (_autoFontResize)
        {
            CalFontSize(g, format, ref font, width, dispData);
        }
        foreach (string paragraph in dispData.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            DrawParagraph(g, WrapText(font, paragraph, width, g, format), font, x, ref yPos, format);
            yPos += _dotBeforePara == _dotAfterPara ? _dotBeforePara : Math.Max(_dotBeforePara, _dotAfterPara);
        }
        font.Dispose();
    }

    /// <summary>myDrawString.</summary>
    private void DrawParagraph(System.Drawing.Graphics g, string paragraph, Font font, int x, ref int yPos, StringFormat format)
    {
        foreach (string s in paragraph.Split('\n'))
        {
            yPos += yPos == 0 ? _dotBeforePara : 0;
            int y = _lineSpacing switch
            {
                "Single" or "SingleHalf" or "Double" => yPos + (_lineHeight - font.Height) / 2,
                "ExactSingle" => yPos + (_lineHeight - font.Height),
                "Exact" => yPos + Math.Max(_lineHeight - font.Height, 0),
                _ => yPos,
            };
            g.DrawString(s, font, Brushes.Black, x, y, format);
            yPos += _lineHeight;
        }
    }

    private int LineHeightFor(Font font) => _lineSpacing switch
    {
        "Double" => font.Height * 2,
        "Exact" => _lineHeight <= 0 ? font.Height : _lineHeight,
        "ExactSingle" => _lineHeight < font.Height ? font.Height : _lineHeight,
        "Single" => font.Height,
        "SingleHalf" => font.Height * 15 / 10,
        "Multiple" => font.Height * _multiple / 10,
        "UpperCase" => (int)font.Size + 1,
        _ => _lineHeight,
    };

    /// <summary>Cal_FontSize: подбор размера шрифта по высоте рамки, шаг 1 пт.</summary>
    private int CalFontSize(System.Drawing.Graphics g, StringFormat format, ref Font font, int width, string dispData)
    {
        string[] lines = dispData.Split(["\r\n", "\n"], StringSplitOptions.None);
        int total = CalPosY(lines, g, format, font, width);
        bool found = false;
        float limit = _rect.Height;
        if (total > limit)
        {
            for (float size = font.Size - 1f; size >= 2f; size--)
            {
                using var candidate = new Font(font.Name, size, font.Style);
                total = CalPosY(lines, g, format, candidate, width);
                if (total < limit)
                {
                    found = true;
                    Replace(ref font, size);
                    break;
                }
            }
            if (!found)
            {
                Replace(ref font, 2f);
            }
        }
        else
        {
            int previous = 0;
            for (float size = font.Size + 1f; size < 72f; size++)
            {
                using var candidate = new Font(font.Name, size, font.Style);
                total = CalPosY(lines, g, format, candidate, width);
                if (total > limit)
                {
                    if (previous != 0)
                    {
                        total = previous;
                    }
                    found = true;
                    Replace(ref font, size - 1f);
                    break;
                }
                previous = total;
            }
            if (!found)
            {
                Replace(ref font, 72f);
            }
        }
        _lineHeight = LineHeightFor(font);
        return total;
    }

    private static void Replace(ref Font font, float size)
    {
        var next = new Font(font.Name, size, font.Style);
        font.Dispose();
        font = next;
    }

    /// <summary>Cal_PosY (bHeadingSpace = false). Меняет межстрочный, как в GoLabel.</summary>
    private int CalPosY(string[] lines, System.Drawing.Graphics g, StringFormat format, Font font, int width)
    {
        _lineHeight = LineHeightFor(font);
        int total = 0;
        foreach (string text in lines)
        {
            if (total != 0)
            {
                total += _dotBeforePara;
            }
            total += _lineHeight * WrapTextHeight(font, text, width, g, format);
            total += _dotAfterPara;
        }
        return total;
    }

    private static float StringWidth(Font font, string text, System.Drawing.Graphics g, StringFormat format) =>
        g.MeasureString(text, font, 32767, format).Width;

    /// <summary>WrapText: перенос по словам (или по знакам), строки через «\r\n», в конце строк пробел.</summary>
    private string WrapText(Font font, string text, float lineWidth, System.Drawing.Graphics g, StringFormat format)
    {
        string[] words = _wordWrap ? text.Split(' ') : [text];
        float space = StringWidth(font, "H H", g, format) - StringWidth(font, "HH", g, format);
        float rest = lineWidth;
        var sb = new StringBuilder();
        string line = string.Empty;
        foreach (string word in words)
        {
            float w = StringWidth(font, word, g, format);
            if (w > rest)
            {
                if (line == string.Empty)
                {
                    foreach (char c in word)
                    {
                        line += c;
                        if (StringWidth(font, line, g, format) > lineWidth)
                        {
                            sb.Append(line.Substring(0, line.Length - 1)).Append("\r\n");
                            line = string.Empty + c;
                        }
                    }
                    if (line != string.Empty)
                    {
                        line += " ";
                        rest = lineWidth - StringWidth(font, line, g, format);
                    }
                    continue;
                }
                sb.Append(line).Append("\r\n");
                line = string.Empty;
                rest = lineWidth - w - space;
                if (rest > 0f)
                {
                    line = line + word + " ";
                    continue;
                }
                rest = lineWidth;
                foreach (char c in word)
                {
                    line += c;
                    if (StringWidth(font, line, g, format) > lineWidth)
                    {
                        sb.Append(line.Substring(0, line.Length - 1)).Append("\r\n");
                        line = string.Empty + c;
                    }
                }
                if (line != string.Empty)
                {
                    line += " ";
                    rest = lineWidth - StringWidth(font, line, g, format);
                }
            }
            else
            {
                rest -= w + space;
                line = line + word + " ";
            }
        }
        if (line != string.Empty)
        {
            sb.Append(line);
        }
        return sb.ToString();
    }

    /// <summary>WrapTextHeight: число строк после переноса (всегда по пробелам).</summary>
    private static int WrapTextHeight(Font font, string text, float lineWidth, System.Drawing.Graphics g, StringFormat format)
    {
        float space = StringWidth(font, "H H", g, format) - StringWidth(font, "HH", g, format);
        float rest = lineWidth;
        string line = string.Empty;
        int count = 0;
        foreach (string word in text.Split(' '))
        {
            float w = StringWidth(font, word, g, format);
            if (w > rest)
            {
                if (line == string.Empty)
                {
                    foreach (char c in word)
                    {
                        line += c;
                        if (StringWidth(font, line, g, format) > lineWidth)
                        {
                            count++;
                            line = string.Empty + c;
                        }
                    }
                    if (line != string.Empty)
                    {
                        line += " ";
                        rest = lineWidth - StringWidth(font, line, g, format);
                    }
                    continue;
                }
                count++;
                line = string.Empty;
                rest = lineWidth - w - space;
                if (rest > 0f)
                {
                    line = line + word + " ";
                    continue;
                }
                rest = lineWidth;
                foreach (char c in word)
                {
                    line += c;
                    if (StringWidth(font, line, g, format) > lineWidth)
                    {
                        count++;
                        line = string.Empty + c;
                    }
                }
                if (line != string.Empty)
                {
                    line += " ";
                    rest = lineWidth - StringWidth(font, line, g, format);
                }
            }
            else
            {
                rest -= w + space;
                line = line + word + " ";
            }
        }
        if (line != string.Empty)
        {
            count++;
        }
        return count;
    }

    private StringFormat Format()
    {
        var format = new StringFormat(_window ? StringFormat.GenericDefault : StringFormat.GenericTypographic)
        {
            Trimming = _window ? StringTrimming.Word : StringFormat.GenericTypographic.Trimming,
            Alignment = _alignment switch
            {
                TextAlignment.Center => StringAlignment.Center,
                TextAlignment.Right => StringAlignment.Far,
                _ => StringAlignment.Near,
            },
        };
        return format;
    }

    /// <summary>Graphic.GetGraphics + явное сглаживание текста.</summary>
    private static System.Drawing.Graphics NewGraphics(Bitmap bitmap, bool clear)
    {
        var g = System.Drawing.Graphics.FromImage(bitmap);
        if (clear)
        {
            g.Clear(Color.White);
        }
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = Hint;
        return g;
    }

    private static int Int(XAttribute? a, int fallback) => a == null ? fallback : XmlConvert.ToInt32(a.Value);

    private static bool Bool(XObject? a) => a switch
    {
        XAttribute at => XmlConvert.ToBoolean(at.Value),
        XElement el => XmlConvert.ToBoolean(el.Value),
        _ => false,
    };
}
