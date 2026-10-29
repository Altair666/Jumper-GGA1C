// Объекты, которые уходят командой принтера: Text (A…/AT…/V…) и BarCode встроенным шрифтом (B…).
// Источник: Text.MyToStringEZPL/MyToStringEZPL_GetCommand/MeasureAdjustPositionEZPL/
// Encoding_SendCommand/DrawInternalTTFont (QLabelSDK/Text.cs:3633, :3728, :690, :3978, :2190),
// Graphic.PrintTimeDateEZPL/GetTimeDateEZPL (Graphic.cs:2475), BarCode.MyToStringEZPL/
// GetBarcodeCommand_EZPL (BarCode.cs:3061).
using System.Text;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using GGA1C.Sync.Printing.GoLabel.Fonts;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

public sealed partial class EzplJob
{
    private static readonly string[] FontIdSymbol =
    [
        "", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S",
        "T", "U", "V", "W", "X", "Y", "Z", "1", "2", "3", "4", "5",
    ];

    private static readonly string[] FontEncodeSymbol =
    [
        "0", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S",
        "T", "U", "V", "W", "X", "Y", "Z", "1", "2", "3", "4", "5",
    ];

    private static readonly string[] FontTypeSymbol = ["A", "AT", "V", "AZ1", "AZ2", "AZ3", "AZ4", "AZ5", "AZ6", "AZ7", "AZ8", "AZ"];

    private static readonly string[] BarCodeSymbol =
    [
        "A", "A2", "A3", "A4", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "N2", "O", "P",
        "Q", "Q2", "R", "S", "T", "U", "7", "V", "W", "X", "Z", "1", "2", "3", "4", "Y", "Y2", "Y3", "Y4", "001",
        "S1", "050", "N4", "N5", "A7", "QI", "A8", "A5", "A6", "N6", "002", "Q", "E2",
    ];

    // Text.advX/advY — шаг строки; GoLabel считает его при отрисовке (Draw) и хранит в объекте.
    private readonly Dictionary<EzpxText, (int X, int Y)> _lineAdvance = [];

    /// <summary>Text.PrintMyToStringEZPL: команда, затем Graphic.PrintTimeDateEZPL, затем байты.</summary>
    private void PrintText(EzpxText t, float x, float y, EzplWriter output)
    {
        string cmd = TextCommand(t, x, y);
        if (!string.IsNullOrEmpty(t.DateFormat))
        {
            output.WriteLine("D" + t.DateFormat);
        }
        if (!string.IsNullOrEmpty(t.TimeFormat))
        {
            output.WriteLine("T" + t.TimeFormat);
        }
        bool utf8 = t.IsUTF8 && t.FontType is FontSource.TrueType_Font or FontSource.BuiltIn_Bitmap_Font or FontSource.DownLoad_Bitmap_Font;
        if (!utf8)
        {
            throw new NotSupportedException("Текст принтера не в UTF-8 (кодировка ANSI) не перенесён.");
        }
        output.WriteBytes(Encoding.UTF8.GetBytes(cmd));
        output.WriteLine("");
    }

    /// <summary>Text.MyToStringEZPL(x, y).</summary>
    private string TextCommand(EzpxText t, float x, float y)
    {
        if (t.UseBlock)
        {
            throw new NotSupportedException("Текст принтера в блоке (bUseBlock) не перенесён.");
        }
        string printdata = FillPrintingData(t, bformat: t.UsePrinterClock);
        bool measure = !(t.Direction < Orientation.Angle0_Vertical && t.TextAlign is TextAlignment.Center or TextAlignment.Right);
        if (t.UsePrinterClock)
        {
            // GoLabel подставляет ещё раз без формата — только ради замера (и побочных эффектов).
            FillPrintingData(t, bformat: false, measureOnly: true);
        }
        if (measure)
        {
            MeasureAdjustPosition(t);
        }
        int posX = t.XInPixel + (int)Math.Round(x, 0);
        int posY = t.YInPixel + (int)Math.Round(y, 0);
        return TextCommandLines(t, posX, posY, printdata);
    }

    /// <summary>
    /// Text.MeasureAdjustPositionEZPL: точка привязки по BoundRect. Для выравнивания по левому
    /// краю размер текста не нужен; по центру и вправо GoLabel меряет строку шрифтом — не перенесено.
    /// </summary>
    private static void MeasureAdjustPosition(EzpxText t)
    {
        if (t.Alignment != TextAlignment.Left)
        {
            throw new NotSupportedException($"Текст принтера с выравниванием {t.Alignment} (нужен замер строки) не перенесён.");
        }
        var r = t.BoundRect;
        switch (t.Direction)
        {
            case Orientation.Angle0:
                t.X = r.Left;
                t.Y = r.Top;
                if (t.X < 0f)
                {
                    t.X = 0f;
                }
                break;
            case Orientation.Angle90:
            case Orientation.Angle90_Vertical:
                t.X = r.Right;
                t.Y = r.Top;
                break;
            case Orientation.Angle180:
                t.X = r.Right;
                t.Y = r.Bottom;
                if (t.X < 0f)
                {
                    t.X = 0f;
                }
                break;
            case Orientation.Angle180_Vertical:
                t.X = r.Right;
                t.Y = r.Bottom;
                break;
            case Orientation.Angle270:
            case Orientation.Angle270_Vertical:
                t.X = r.Left;
                t.Y = r.Bottom;
                break;
            case Orientation.Angle0_Vertical:
                t.X = r.Left;
                t.Y = r.Top;
                break;
        }
    }

    /// <summary>Text.MyToStringEZPL_GetCommand: по команде на каждую непустую строку.</summary>
    private string TextCommandLines(EzpxText t, int posX, int posY, string printdata)
    {
        float dotsPerPoint = Dpi / 72f;
        var sb = new StringBuilder();
        int x = posX;
        int y = posY;
        int lineNo = 0;
        using var reader = new StringReader(printdata);
        while (reader.Peek() > -1)
        {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                lineNo++;
                continue;
            }
            if (lineNo > 0)
            {
                sb.Append("\r\n");
                var (advX, advY) = LineAdvance(t);
                x = posX + lineNo * advX;
                y = posY + lineNo * advY;
            }
            if (t.Name.Length > 0 && t.Name[0] == 'A')
            {
                bool selfAlign = SelfAligns(t);
                var (ax, ay) = selfAlign ? AlignedStart(t, x, y, line, dotsPerPoint) : (x, y);
                AppendTextCommand(sb, t, ax, ay, line, dotsPerPoint, alignSuffix: !selfAlign);
            }
            lineNo++;
        }
        return sb.ToString();
    }

    private static void AppendTextCommand(StringBuilder sb, EzpxText t, int x, int y, string line, float dotsPerPoint, bool alignSuffix)
    {
        string common = "," + x + "," + y + ",";
        switch (t.FontType)
        {
            case FontSource.TrueType_Font:
            {
                // Get_FontFactor_PrintTTF = 1 без блока со сжатием.
                int height = (int)Math.Round(dotsPerPoint * t.FontHeight * 1f, 0);
                int width = (int)Math.Round(dotsPerPoint * t.FontWidth * 1f, 0);
                string style = (t.Bold ? "B" : "") + (t.Italic ? "T" : "") + (t.Strikeout ? "S" : "") + (t.Underline ? "U" : "");
                sb.Append("AT" + FontIdSymbol[(int)t.FontId] + common + width + "," + height + "," + Fmt.F(t.TextSpace) + "," + (int)t.Direction + style);
                AppendFlags(sb, t, utf8Flag: true, alignSuffix);
                sb.Append("," + FontEncodeSymbol[(int)t.Encoding] + ",0," + line);
                break;
            }
            case FontSource.BuiltIn_Bitmap_Font:
            case FontSource.DownLoad_Bitmap_Font:
            {
                string id = t.FontId != FontIDs.Default ? FontIdSymbol[(int)t.FontId] : "D";
                string head = t.FontType == FontSource.BuiltIn_Bitmap_Font ? t.Name : "V";
                sb.Append(head + id + common + (int)t.FontWidth + "," + (int)t.FontHeight + "," + Fmt.F(t.TextSpace) + "," + (int)t.Direction);
                AppendFlags(sb, t, utf8Flag: true, alignSuffix);
                sb.Append("," + line);
                break;
            }
            default:
                sb.Append(FontTypeSymbol[(int)t.FontType] + common + (int)t.FontWidth + "," + (int)t.FontHeight + "," + Fmt.F(t.TextSpace) + "," + (int)t.Direction);
                AppendFlags(sb, t, utf8Flag: false, alignSuffix);
                sb.Append("," + line);
                break;
        }
    }

    private static void AppendFlags(StringBuilder sb, EzpxText t, bool utf8Flag, bool alignSuffix)
    {
        if (t.IsInverse)
        {
            sb.Append('I');
        }
        if (utf8Flag && t.IsUTF8)
        {
            sb.Append('E');
        }
        if (t.IsCheckDigit)
        {
            sb.Append('M');
        }
        if (alignSuffix && t.Direction < Orientation.Angle0_Vertical)
        {
            if (t.TextAlign == TextAlignment.Center)
            {
                sb.Append("P2");
            }
            else if (t.TextAlign == TextAlignment.Right)
            {
                sb.Append("P3");
            }
        }
    }

    /// <summary>
    /// БАГ-3, вариант А: выравнивает ли порт текст сам. GoLabel шлёт для центра/вправо суффикс
    /// P2/P3 и точку выравнивания вместо начала строки; ZX430i (прошивка v2.110) суффикс
    /// игнорирует и печатает от точки как от начала. Пока принтер не понимает выравнивание
    /// (<see cref="PrintValues.PrinterAlignsText"/> = false), порт считает начало строки сам и
    /// суффикс не шлёт.
    /// </summary>
    private bool SelfAligns(EzpxText t) =>
        !_values.PrinterAlignsText
        && t.Direction < Orientation.Angle0_Vertical
        && t.TextAlign is TextAlignment.Center or TextAlignment.Right;

    /// <summary>
    /// Начало строки по точке выравнивания (x, y) и ширине строки шрифтом принтера: по центру —
    /// на полширины назад по направлению письма, вправо — на всю ширину. Направление письма:
    /// 0° — +x, 90° — +y, 180° — −x, 270° — −y (как шаг строк и раскладка GoLabel).
    /// </summary>
    private (int X, int Y) AlignedStart(EzpxText t, int x, int y, string line, float dotsPerPoint)
    {
        float width = LineWidth(t, line, dotsPerPoint);
        int back = t.TextAlign == TextAlignment.Center
            ? (int)Math.Round(width / 2f, 0, MidpointRounding.AwayFromZero)
            : (int)Math.Round(width, 0, MidpointRounding.AwayFromZero);
        return t.Direction switch
        {
            Orientation.Angle0 => (x - back, y),
            Orientation.Angle90 => (x, y - back),
            Orientation.Angle180 => (x + back, y),
            _ => (x, y + back),
        };
    }

    /// <summary>Ширина строки в точках так, как её напечатает принтер (см. PrinterTextWidth).</summary>
    private float LineWidth(EzpxText t, string line, float dotsPerPoint)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("Выравнивание текста принтера портом считается шрифтами Windows (GDI+).");
        }
        switch (t.FontType)
        {
            case FontSource.TrueType_Font:
            {
                // Те же размеры, что уходят в команду AT: высота и ширина знака в точках.
                float height = (float)Math.Round(dotsPerPoint * t.FontHeight, 0);
                float width = (float)Math.Round(dotsPerPoint * t.FontWidth, 0);
                var style = (t.Bold ? System.Drawing.FontStyle.Bold : 0) | (t.Italic ? System.Drawing.FontStyle.Italic : 0);
                return PrinterTextWidth.Measure(t.FontFamily, style, height, height > 0 ? width / height : 1f, t.TextSpace, line);
            }
            case FontSource.BuiltIn_Bitmap_Font:
            {
                var (family, pixels) = BuiltInScreenFont(t.FontId);
                return PrinterTextWidth.Measure(family, 0, pixels, (int)t.FontWidth, t.TextSpace, line);
            }
            default:
                throw new NotSupportedException($"Выравнивание портом для шрифта {t.FontType} не перенесено (принтер без P2/P3).");
        }
    }

    /// <summary>
    /// Шаг строки — Text.advX/advY, которые GoLabel считает при отрисовке:
    /// <list type="bullet">
    /// <item>TrueType_Font (DrawInternalTTFont): Font.Height шрифта Windows из FontCmd размером
    /// round(dpi/72 · FontHeight) точек;</item>
    /// <item>BuiltIn_Bitmap_Font (DrawInternalBMPFontEZPL): (int)(Font.Height · FontHeight), где
    /// шрифт — экранный образ встроенного шрифта принтера из таблиц IBfonts/IBfonts300
    /// (Text.cs:3114–3155), FontHeight — множитель высоты.</item>
    /// </list>
    /// </summary>
    private (int X, int Y) LineAdvance(EzpxText t)
    {
        if (_lineAdvance.TryGetValue(t, out var cached))
        {
            return cached;
        }
        int h;
        if (t.FontType == FontSource.BuiltIn_Bitmap_Font)
        {
            var (family, pixels) = BuiltInScreenFont(t.FontId);
            h = (int)(GdiPlusFonts.FontHeight(family, 0, pixels) * t.FontHeight);
        }
        else if (t.FontType == FontSource.TrueType_Font && t.FontId is not (FontIDs.Default or >= FontIDs.BuiltIn_1) && t.BTrueType)
        {
            int emPixels = (int)Math.Round(Dpi / 72f * t.FontHeight, 0);
            int style = (t.Bold ? 1 : 0) | (t.Italic ? 2 : 0) | (t.Underline ? 4 : 0) | (t.Strikeout ? 8 : 0);
            h = GdiPlusFonts.FontHeight(t.FontFamily, style, emPixels);
        }
        else
        {
            throw new NotSupportedException("Многострочный текст этим шрифтом принтера (шаг строки) не перенесён.");
        }
        (int X, int Y) advance = t.Direction switch
        {
            Orientation.Angle0 => (0, h),
            Orientation.Angle90 => (-h, 0),
            Orientation.Angle180 => (0, -h),
            Orientation.Angle270 => (h, 0),
            _ => throw new NotSupportedException("Многострочный вертикальный текст принтера не перенесён."),
        };
        _lineAdvance[t] = advance;
        return advance;
    }

    /// <summary>
    /// Экранный образ встроенного шрифта принтера (Text.cs: G6P…G36P, G16x26; IBfonts — до 250 dpi,
    /// FontId не дальше I; IBfonts300 — иначе). Шрифт — пиксельный (GraphicsUnit.Pixel), обычный.
    /// </summary>
    private (string Family, float Pixels) BuiltInScreenFont(FontIDs id)
    {
        bool lowDpi = Dpi < 250f;
        int index = (int)id;
        if (lowDpi && index > (int)FontIDs.I)
        {
            index = (int)FontIDs.I;
        }
        return index switch
        {
            0 or 4 => ("Arial", 33f),
            1 => ("Arial", 16f),
            2 => ("Arial", 22f),
            3 => ("Arial", 27f),
            5 => ("Arial", 38f),
            6 => ("Arial", 50f),
            7 => ("Arial", 66f),
            8 => ("Arial", 83f),
            9 => lowDpi ? ("Courier New", 26f) : ("Arial", 100f),
            10 => ("Courier New", 26f),
            // GoLabel здесь падает (индекс вне таблицы из 11 шрифтов).
            _ => throw new NotSupportedException($"Встроенный шрифт {id}: в GoLabel нет его экранного образа."),
        };
    }

    /// <summary>BarCode.PrintMyToString → Graphic.PrintMyToString(x, y) для штрихкода командой.</summary>
    private void PrintBarCode(EzpxBarCode b, float x, float y, EzplWriter output)
    {
        if (!string.IsNullOrEmpty(b.DateFormat))
        {
            output.WriteLine("D" + b.DateFormat);
        }
        if (!string.IsNullOrEmpty(b.TimeFormat))
        {
            output.WriteLine("T" + b.TimeFormat);
        }
        output.WriteLine(BarCodeCommand(b, x, y));
    }

    /// <summary>BarCode.MyToStringEZPL(x, y) и GetBarcodeCommand_EZPL.</summary>
    private string BarCodeCommand(EzpxBarCode b, float x, float y)
    {
        string data = FillPrintingData(b, bformat: false);
        int posX = b.XInPixel + (int)Math.Round(x, 0);
        int posY = b.YInPixel + (int)Math.Round(y, 0);
        if (data == "" || data == " ")
        {
            return "";
        }
        switch (b.Symbology)
        {
            case BarCodeType.Code39Extended or BarCodeType.Code39 or BarCodeType.Code93 or BarCodeType.Code128Auto
                or BarCodeType.I2of5 or BarCodeType.Codabar or BarCodeType.Code39Start or BarCodeType.Logmars:
                break;
            default:
                throw new NotSupportedException($"Штрихкод {b.Symbology} командой не перенесён.");
        }
        string timeDate = (!string.IsNullOrEmpty(b.DateFormat) ? "D" + b.DateFormat + "\r\n" : "")
            + (!string.IsNullOrEmpty(b.TimeFormat) ? "T" + b.TimeFormat + "\r\n" : "");
        return timeDate + b.Name + BarCodeSymbol[(int)b.Symbology] + "," + posX + "," + posY + "," + b.NarrowInPixel + ","
            + b.WidthInPixel + "," + b.HeightInPixel + "," + (int)b.Direction + "," + (int)b.CaptionAlignment + "," + data;
    }
}
