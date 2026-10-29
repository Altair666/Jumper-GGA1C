// Модель шаблона .ezpx — то, что GoLabel получает XmlSerializer'ом в PrintJob/QLabel/Setup/Layout
// и классах объектов (QLabelSDK: PrintJob.cs, QLabel.cs, Setup.cs, Layout.cs, GraphicShape.cs,
// Graphic.cs, Text.cs, BarCode.cs, QRectangle.cs, Line.cs). Перенесены только поля, нужные для
// сборки задания; значения по умолчанию — из конструкторов этих классов (для отсутствующих в
// файле элементов и атрибутов XmlSerializer оставляет значение конструктора).
// Модель изменяемая: сборка задания, как и GoLabel, по ходу правит счётчики и переменные.
// Один экземпляр — на одно задание; общего состояния между заданиями нет.
using System.Xml.Linq;

namespace GGA1C.Sync.Printing.GoLabel.Ezpx;

public sealed class EzpxTemplate
{
    public bool GraphicMode { get; set; }
    public int FormatVersion { get; set; }
    public string PrinterModel { get; set; } = "";
    public string PrinterLanguage { get; set; } = "EZPL";
    public EzpxLabel Label { get; set; } = new();
    public EzpxSetup Setup { get; set; } = new();
}

/// <summary>QLabel: наклейка — объекты, счётчики, переменные, формат даты.</summary>
public sealed class EzpxLabel
{
    /// <summary>QLabel.MaxSerial, QLabel.MaxVariable, QLabel.MaxField.</summary>
    public const int MaxSerial = 50;
    public const int MaxVariable = 100;
    public const int MaxField = 99;

    /// <summary>QLabel.LinkedVariable: последние 10 переменных — связанные.</summary>
    public const int LinkedVariable = 10;

    public string?[] SerialFormat { get; set; } = new string?[MaxSerial];
    public int[] SerialLeadingCode { get; set; } = new int[MaxSerial];
    public string?[] SerialCustomSequence { get; set; } = new string?[MaxSerial];
    public bool[] SerialSpecialCarry { get; set; } = new bool[MaxSerial];
    public string?[] VariableFormat { get; set; } = new string?[MaxVariable];
    public List<string> VariableOpFormat { get; set; } = [];
    public string? DateFormat { get; set; }
    public string? TimeFormat { get; set; }
    public string DataBaseFormat { get; set; } = "None";
    public List<EzpxShape> Shapes { get; set; } = [];
}

/// <summary>Setup: размер наклейки и настройки принтера (шапка задания).</summary>
public sealed class EzpxSetup
{
    public bool InfinityPrint { get; set; }
    public float LabelLength { get; set; } = 50f;
    public float LabelWidth { get; set; } = 50f;
    public int LeftMargin { get; set; }
    public int TopMargin { get; set; }
    public int LabelType { get; set; } = 1;
    public float GapLength { get; set; } = 3f;
    public float FeedLength { get; set; } = 10f;
    public char ZSign { get; set; } = '+';
    public float BlackMark { get; set; }
    public float Position { get; set; }
    public int Speed { get; set; } = 20;
    public int Copy { get; set; } = 1;
    public int Stripper { get; set; }
    /// <summary>Значение атрибута; см. <see cref="LabelsPerCut"/>.</summary>
    public int LabelsPerCutRaw { get; set; }
    public bool DoubleCut_Enable { get; set; }
    public int DoubleCut_OffsetLen { get; set; }
    public int DoubleCut_FirstCutMode { get; set; } = 1;
    public int Rotate180 { get; set; } = 300;
    public float Stop { get; set; } = 12f;
    public int Darkness { get; set; } = 5;
    public int Number { get; set; } = 1;
    public bool CutDataBase { get; set; }
    public bool BatchCut { get; set; }
    public bool PartialCut { get; set; }
    public bool FullCutLast { get; set; }
    public PageDirection PageDirection { get; set; }
    public int PrintMode { get; set; } = 1;
    public bool UsePrinterRFIDCfg { get; set; } = true;
    public int DrawMode { get; set; }
    public Unit UnitType { get; set; } = Unit.Mm;
    public float Dpi { get; set; } = 200f;
    public EzpxLayout Layout { get; set; } = new();

    /// <summary>Setup.LabelsPerCut: при отрезе по базе или пачкой — 32767.</summary>
    public int LabelsPerCut => CutDataBase || BatchCut ? 32767 : LabelsPerCutRaw;

    // Setup._WidthInPixel и др.: размеры в точках принтера.
    public int WidthInPixel => (int)Math.Round(Units.ConvertFactor(Unit.Mm, Dpi) * LabelWidth, 0);
    public int HeightInPixel => (int)Math.Round(Units.ConvertFactor(UnitType, Dpi) * LabelLength, 0);
    public int HorGapInPixel => (int)Math.Round(Units.ConvertFactor(Unit.Mm, Dpi) * Layout.HorGap, 0);
    public int VerGapInPixel => (int)Math.Round(Units.ConvertFactor(Unit.Mm, Dpi) * Layout.VerGap, 0);
}

/// <summary>Layout: несколько наклеек на одной странице («в ряд»).</summary>
public sealed class EzpxLayout
{
    public int Shape { get; set; }
    public AcrossType AcrossType { get; set; } = AcrossType.Copied;
    public PageDirection PageDirection { get; set; } = PageDirection.Portrait;
    public int HorAcross { get; set; } = 1;
    public int VerAcross { get; set; } = 1;
    public float HorGap { get; set; }
    public float VerGap { get; set; }
    public int LabelMode { get; set; }
    public int BottomMargin { get; set; }
    public int RightMargin { get; set; }

    /// <summary>Layout.Number: сколько разных наклеек (со своими значениями счётчиков) на странице.</summary>
    public int Number => AcrossType switch
    {
        AcrossType.Copied => 1,
        AcrossType.LeftToRight => HorAcross * VerAcross,
        AcrossType.TopToButtom => HorAcross * VerAcross,
        _ => 1,
    };

    /// <summary>Layout[number]: смещение наклейки number на странице, точки. Layout.cs:71.</summary>
    public (float X, float Y) Offset(int number, EzpxSetup owner)
    {
        if (number < 0 || number >= HorAcross * VerAcross)
        {
            return (0f, 0f);
        }
        int col;
        int row;
        if (PageDirection == PageDirection.Portrait)
        {
            if (AcrossType == AcrossType.LeftToRight)
            {
                col = number % HorAcross;
                row = number / HorAcross;
                row = VerAcross - row - 1;
            }
            else
            {
                row = number % VerAcross;
                col = number / VerAcross;
            }
        }
        else
        {
            if (number == 0)
            {
                return (0f, 0f);
            }
            if (AcrossType == AcrossType.LeftToRight)
            {
                col = number % HorAcross;
                row = number / HorAcross;
            }
            else
            {
                row = number % VerAcross;
                col = number / VerAcross;
            }
        }
        float width = (float)(owner.WidthInPixel - (owner.LeftMargin + RightMargin) - owner.HorGapInPixel * (HorAcross - 1)) / (float)HorAcross;
        float height = (float)(owner.HeightInPixel - BottomMargin - owner.VerGapInPixel * (VerAcross - 1)) / (float)VerAcross;
        int x = (int)Math.Round((float)col * (width + (float)owner.HorGapInPixel), 0);
        int y = (int)Math.Round((float)row * (height + (float)owner.VerGapInPixel), 0);
        return (x, y);
    }
}

/// <summary>Прямоугольник с плавающей точкой — как System.Drawing.RectangleF (Right/Bottom — сумма float).</summary>
public struct RectF
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public RectF(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public readonly float Left => X;
    public readonly float Top => Y;
    public readonly float Right => X + Width;
    public readonly float Bottom => Y + Height;
}

/// <summary>GraphicShape: общее у всех объектов наклейки.</summary>
public abstract class EzpxShape
{
    /// <summary>Значение xsi:type — имя класса QLabelSDK.</summary>
    public string TypeName { get; set; } = "";
    public string Name { get; set; } = "None";
    public float X { get; set; } = 4f;
    public float Y { get; set; } = 4f;
    public Orientation Direction { get; set; }
    public TextAlignment Alignment { get; set; }
    public bool IsPrint { get; set; } = true;
    public PageAlignment PageAlignment { get; set; }

    /// <summary>Исходный элемент — для объектов, которые порт рисует растром (шаги 3–5).</summary>
    public XElement? Element { get; set; }

    /// <summary>GraphicShape._XInPixel/_YInPixel.</summary>
    public int XInPixel => (int)Math.Round(X, 0);
    public int YInPixel => (int)Math.Round(Y, 0);
}

/// <summary>clsCharTruncateRule.</summary>
public sealed class CharTruncateRule
{
    public bool TrimLeft { get; set; }
    public bool TrimRight { get; set; }
    public bool RemoveCharLeft { get; set; }
    public int RemoveCharLeftNo { get; set; }
    public bool RemoveCharRight { get; set; }
    public int RemoveCharRightNo { get; set; }
    public bool KeepCharLeft { get; set; }
    public int KeepCharLeftNo { get; set; } = 6;
    public bool KeepCharRight { get; set; }
    public int KeepCharRightNo { get; set; } = 6;
    public bool RemoveDotZero { get; set; }

    /// <summary>clsCharTruncateRule.DoTruncate.</summary>
    public string DoTruncate(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return "";
        }
        string text = data;
        if (TrimLeft)
        {
            text = text.TrimStart();
        }
        if (TrimRight)
        {
            text = text.TrimEnd();
        }
        if (RemoveCharLeft && RemoveCharLeftNo > 0)
        {
            text = RemoveCharLeftNo < text.Length ? text.Substring(RemoveCharLeftNo) : "";
        }
        if (RemoveCharRight && RemoveCharRightNo > 0)
        {
            text = RemoveCharRightNo < text.Length ? text.Substring(0, text.Length - RemoveCharRightNo) : "";
        }
        if (KeepCharLeft && KeepCharLeftNo < text.Length)
        {
            text = text.Substring(0, KeepCharLeftNo);
        }
        if (KeepCharRight && KeepCharRightNo < text.Length)
        {
            text = text.Substring(text.Length - KeepCharRightNo);
        }
        if (RemoveDotZero && (text.EndsWith(".0", StringComparison.Ordinal) || text.EndsWith(",0", StringComparison.Ordinal)))
        {
            text = text.Substring(0, text.Length - 2);
        }
        return text;
    }
}

/// <summary>Graphic: объект с данными (текст, штрихкод, картинка...).</summary>
public class EzpxGraphic : EzpxShape
{
    public string Data { get; set; } = "1234";
    public string? DateFormat { get; set; }
    public string? TimeFormat { get; set; }
    public string Prompt { get; set; } = "None";
    public string FormatType { get; set; } = "None";
    public string CharFilterRule { get; set; } = "None";
    public string LinkMode { get; set; } = "OriginalData";
    public int ReplaceInfoCount { get; set; }
    public int ItemInfoCount { get; set; }
    public bool RemovePreZeroAndEmpty { get; set; }
    public bool ReplaceSpecialCharFromDB { get; set; }
    public bool GetAiFromDigitalLink { get; set; }
    public bool GraphicMode { get; set; }
    public CharTruncateRule CharTruncateRule { get; set; } = new();

    /// <summary>Graphic.rectangle (BoundRect). Конструктор Graphic ставит (x, y, 100.1, 100.3).</summary>
    public RectF BoundRect { get; set; } = new(4f, 4f, 100.1f, 100.3f);
}

/// <summary>Text: текст шрифтом принтера — команда A…/AT…/V….</summary>
public sealed class EzpxText : EzpxGraphic
{
    public EzpxText()
    {
        Name = "A";
        Data = "QLabel";
    }

    public TextAlignment TextAlign { get; set; }
    public FontSource FontType { get; set; } = FontSource.BuiltIn_Bitmap_Font;
    public FontIDs FontId { get; set; } = FontIDs.Default;
    public float FontHeight { get; set; } = 1f;
    public float FontWidth { get; set; } = 1f;
    public float TextSpace { get; set; }
    public FontEncodings Encoding { get; set; } = FontEncodings.Default;
    public bool IsInverse { get; set; }
    public bool IsUTF8 { get; set; } = true;
    public bool IsCheckDigit { get; set; }
    public bool UsePrinterClock { get; set; } = true;

    /// <summary>Семейство шрифта из FontCmd («GlavkonGOST Type A,7,B»): им GoLabel меряет строки.</summary>
    public string FontFamily { get; set; } = "Microsoft Sans Serif";

    // Стиль из FontCmd — GoLabelFONT.lfWeight/lfItalic/lfUnderline/lfStrikeOut.
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikeout { get; set; }

    /// <summary>GoLabelFONT.bTrueType (элемент BTrueType): шаг строки берётся из Font.Height.</summary>
    public bool BTrueType { get; set; } = true;

    /// <summary>Ограничение блоком (bUseBlock) — в наших шаблонах не встречается, не перенесено.</summary>
    public bool UseBlock { get; set; }
}

/// <summary>BarCode: одномерный штрихкод — команда B… или растр (подпись шрифтом Windows).</summary>
public sealed class EzpxBarCode : EzpxGraphic
{
    public EzpxBarCode()
    {
        Name = "B";
        Data = "1234";
    }

    public BarCodeType Symbology { get; set; } = BarCodeType.Code39;
    public CaptionType CaptionAlignment { get; set; } = CaptionType.BottomAndLeft;
    public float Height { get; set; } = 24f;
    public float Width { get; set; } = 4f;
    public float Narrow { get; set; } = 2f;
    public bool BuiltinFont { get; set; } = true;
    public bool SetBuiltinFontSize { get; set; }
    public Code128Subset Code128Subset { get; set; }
    public CheckDigitType CheckDigitType { get; set; } = CheckDigitType.MOD_43;
    public int BearerBarStyle { get; set; } = 3;
    public int BearerBarWidth { get; set; } = 5;
    public int QuietZoneWidth { get; set; } = 9;
    public bool CustomizeGuardBar { get; set; }
    public float GuardBarHeight { get; set; }

    /// <summary>Отступ подписи от полос, точки (атрибут Offset).</summary>
    public float Offset { get; set; } = 1f;

    /// <summary>Шрифт подписи Windows из FontCmd: семейство, размер в пунктах, стиль (B=1, I=2, U=4, S=8).</summary>
    public (string Family, float Size, System.Drawing.FontStyle Style) CaptionFont { get; set; } = ("Microsoft Sans Serif", 17f, System.Drawing.FontStyle.Regular);

    // BarCode._HeightInPixel и др.: единицы объекта — точки (GraphicShape.UnitType = Dot).
    public int HeightInPixel => (int)Math.Round(Height, 0);
    public int WidthInPixel => (int)Math.Round(Width, 0);
    public int NarrowInPixel => (int)Math.Round(Narrow, 0);

    /// <summary>BarCode.PrintMyToString: картинкой, если подпись не встроенным шрифтом.</summary>
    public bool IsRaster => !BuiltinFont || GraphicMode || Symbology == BarCodeType.Code128CheckDigit;
}

/// <summary>QRectangle: рамка — команда R….</summary>
public sealed class EzpxRectangle : EzpxShape
{
    public EzpxRectangle()
    {
        Name = "R";
        X = 0f;
        Y = 0f;
    }

    public float Width { get; set; } = 4f;
    public float Height { get; set; } = 12f;
    public float HStroke { get; set; } = 1f;
    public float VStroke { get; set; } = 1f;
}

/// <summary>Line: линия — команда L….</summary>
public sealed class EzpxLine : EzpxShape
{
    public EzpxLine()
    {
        Name = "L";
        X = 0f;
        Y = 0f;
    }

    public LineShape LineShape { get; set; } = LineShape.FillRect;
    public float Width { get; set; } = 4f;
    public float Height { get; set; } = 12f;
    public char Operation { get; set; } = 'o';
}

/// <summary>
/// Объект, который GoLabel рисует сам и шлёт растром Q (Image, Ellipse, GdiWindowTextBox,
/// WindowTextBox...). Размеры — для правила «пустой объект не печатается».
/// </summary>
public sealed class EzpxRasterShape : EzpxGraphic
{
    /// <summary>Наследник Graphic (есть BoundRect и Data) — Image, *TextBox, WindowText...</summary>
    public bool IsGraphic { get; set; }

    /// <summary>Width/Height фигуры (Ellipse, RoundRect, VarShape, ObliqueLine, TableForm).</summary>
    public float Width { get; set; }
    public float Height { get; set; }
}

internal static class Units
{
    /// <summary>
    /// GraphicShape.ConvertFactor при GlobalData.bUseRealDPI = false (по умолчанию в GoLabel и на
    /// стенде: в user.config значения нет): мм → точки = dpi / 25, а для 203 dpi — 7.99.
    /// </summary>
    public static float ConvertFactor(Unit unit, float dpi) => unit switch
    {
        Unit.Dot => 1f,
        Unit.Inch => dpi,
        Unit.Mm => dpi < 205f ? 7.99f : dpi / 25f,
        Unit.Cm => dpi * 10f / 25f,
        Unit.Point => dpi / 72f,
        _ => 1f,
    };

    /// <summary>PrintJob.GetDotPerMm / Func.GetDpiZoom(float): точек в мм (12 для 300 dpi).</summary>
    public static float DotPerMm(float dpi) => dpi == 600f ? 24f : dpi == 300f ? 12f : 8f;

    /// <summary>Func.GetDpiZoom(int).</summary>
    public static int DpiZoom(int dpi) => dpi switch { 600 => 24, 300 => 12, _ => 8 };
}
