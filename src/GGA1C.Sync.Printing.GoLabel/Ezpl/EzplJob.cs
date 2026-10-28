// Сборка задания EZPL так, как это делает GoLabel при печати из командной строки
// (GoLabel.exe -f <шаблон> -S<n> <значение> -V<nn> <значение>).
// Источник (декомпилят QLabelSDK 1.5 / GlobalInfo, C:\GGA1C\golabel-src, вне git):
//   GlobalPrint.PrintJob_Execute (GlobalInfo/GlobalPrint.cs:392) — выкидывание пустых объектов,
//   CheckSerialPrompt/CheckVariablePrompt (:709, :782), GlobalInfo.UseNormalPrint (GlobalInfo.cs:3661),
//   PrintJob.Print/BeforePrintSetup (QLabelSDK/PrintJob.cs:607, :301), Setup.GetCommandEZPL (Setup.cs:322),
//   QLabel.PrintNormalCommandEZPL (QLabel.cs:3027), Layout (Layout.cs:71), OutOfPageArea (QLabel.cs:6651).
// Всё статическое состояние GoLabel (PrintJob.gQLabel, PrintJob.Dpi, QLabel.Label_W_Dot ...) —
// поля этого объекта: одно задание — один экземпляр.
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

public sealed partial class EzplJob
{
    private readonly EzpxTemplate _template;
    private readonly PrintValues _values;
    private readonly IRasterRenderer _raster;
    private readonly List<string> _beginJobCommands = [];
    private readonly List<string> _endJobCommands = [];
    private bool _partialCutNow;
    private bool _prepared;

    // QLabel.Label_W_Dot/Label_H_Dot — размер одной наклейки, точки (PrintJob.GetPageArea).
    private int _labelWidthDot;
    private int _labelHeightDot;

    public EzplJob(EzpxTemplate template, PrintValues values, IRasterRenderer? raster = null)
    {
        _template = template;
        _values = values;
        _raster = raster ?? UnsupportedRasterRenderer.Instance;
    }

    public EzpxTemplate Template => _template;
    public EzpxLabel Label => _template.Label;
    public EzpxSetup Setup => _template.Setup;
    public PrintValues Values => _values;

    /// <summary>PrintJob.Dpi на время печати = Setup.Dpi.</summary>
    public float Dpi => _template.Setup.Dpi;

    /// <summary>Печать из базы (Print(records)): у шаблона есть источник данных.</summary>
    public bool FromRecords => !string.Equals(Label.DataBaseFormat, "None", StringComparison.Ordinal);

    /// <summary>Всё задание целиком — байты, которые GoLabel отправил бы в принтер.</summary>
    public byte[] Build()
    {
        var output = new EzplWriter();
        Prepare();
        if (FromRecords)
        {
            throw new NotSupportedException("Печать из базы данных (Print(records)) — шаг 6, ещё не перенесена.");
        }
        if (UseNormalPrint())
        {
            throw new NotSupportedException("Ветка PrintNornal GoLabel (счётчики принтера C00) не перенесена.");
        }
        WriteHeader(output);
        WriteLabels(output);
        return output.ToArray();
    }

    /// <summary>Шапка — всё до первой наклейки (^L или, при печати из базы, ^C).</summary>
    public byte[] BuildHeader()
    {
        var output = new EzplWriter();
        // Значения счётчиков и переменных на шапку не влияют — без них.
        Prepare(applyValues: false);
        WriteHeader(output);
        return output.ToArray();
    }

    private void Prepare(bool applyValues = true)
    {
        if (_prepared)
        {
            throw new InvalidOperationException("Задание уже собрано: EzplJob — на одну сборку.");
        }
        _prepared = true;
        if (!string.Equals(_template.PrinterLanguage, "EZPL", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Язык принтера {_template.PrinterLanguage}: перенесён только EZPL.");
        }
        RemoveEmptyShapes();
        if (applyValues)
        {
            CheckSerialPrompt();
            CheckVariablePrompt();
        }
        if (Setup.PageDirection != PageDirection.Portrait)
        {
            throw new NotSupportedException("Поворот страницы (SetDivideLayout) не перенесён.");
        }
        BeforePrintSetup();
    }

    /// <summary>
    /// GlobalPrint.PrintJob_Execute: объект нулевой ширины или высоты не печатается, если в его
    /// данных нет «^» (переменной, счётчика...).
    /// </summary>
    private void RemoveEmptyShapes()
    {
        var shapes = Label.Shapes;
        for (int i = shapes.Count - 1; i >= 0; i--)
        {
            var shape = shapes[i];
            int width;
            int height;
            string? data = null;
            bool graphic;
            switch (shape)
            {
                case EzpxRectangle r:
                    width = (int)r.Width;
                    height = (int)r.Height;
                    graphic = false;
                    break;
                case EzpxLine l:
                    width = (int)l.Width;
                    height = (int)l.Height;
                    graphic = false;
                    break;
                case EzpxGraphic g:
                    width = (int)g.BoundRect.Width;
                    height = (int)g.BoundRect.Height;
                    data = g.Data;
                    graphic = true;
                    break;
                case EzpxRasterShape { IsGraphic: true } rg:
                    width = (int)rg.BoundRect.Width;
                    height = (int)rg.BoundRect.Height;
                    data = rg.Data;
                    graphic = true;
                    break;
                case EzpxRasterShape rs:
                    width = (int)rs.Width;
                    height = (int)rs.Height;
                    graphic = false;
                    break;
                default:
                    continue;
            }
            if (width != 0 && height != 0)
            {
                continue;
            }
            if (shape.TypeName == "RichTextImage")
            {
                throw new NotSupportedException("RichTextImage нулевого размера не перенесён.");
            }
            if (!graphic || !(data ?? "").Contains('^'))
            {
                shapes.RemoveAt(i);
            }
        }
    }

    /// <summary>GlobalInfo.UseNormalPrint: можно ли печатать счётчиками принтера (PrintNornal).</summary>
    private bool UseNormalPrint()
    {
        if (_template.GraphicMode)
        {
            return false;
        }
        if (Setup.PartialCut && Setup.FullCutLast)
        {
            return false;
        }
        if (Setup.LabelsPerCut > 1)
        {
            return false;
        }
        if (Label.VariableOpFormat.Any(v => v.Contains("V#OP") || v.Contains("V#STRSUB") || v.Contains("V#STRCUT")))
        {
            return false;
        }
        if (Label.VariableFormat.Any(v => v != null && v.Trim() != ""))
        {
            return false;
        }
        if (Setup.Layout.LabelMode == 0 && (Setup.Layout.HorAcross > 1 || Setup.Layout.VerAcross > 1))
        {
            return false;
        }
        foreach (var shape in Label.Shapes)
        {
            bool rendered = false;
            string text = "";
            string formatType = "None";
            CaptionType caption = CaptionType.None;
            switch (shape)
            {
                case EzpxBarCode b:
                    rendered = !b.BuiltinFont;
                    text = b.Data;
                    formatType = b.FormatType;
                    caption = b.CaptionAlignment;
                    break;
                case EzpxGraphic g:
                    text = g.Data;
                    formatType = g.FormatType;
                    break;
                case EzpxRasterShape r:
                    text = r.Data ?? "";
                    break;
            }
            if (formatType != "None" || shape.PageAlignment == PageAlignment.Center)
            {
                return false;
            }
            if (shape is EzpxGraphic { RemovePreZeroAndEmpty: true })
            {
                return false;
            }
            switch (shape.TypeName)
            {
                case "RichTextImage":
                    throw new NotSupportedException("RichTextImage не перенесён.");
                case "CurvedText" or "GdiWindowTextBox" or "WindowText" or "WindowTextBox" or "WinWordArt" or "HanXin":
                    rendered = true;
                    break;
                default:
                    if (!rendered && shape.TypeName != "BarCode" && caption != CaptionType.None)
                    {
                        return false;
                    }
                    break;
            }
            if (rendered && (HasNumberedTag(text, "^C") || HasNumberedTag(text, "^V")))
            {
                return false;
            }
        }
        return true;
    }

    // Splitor_Serial/_Old/_Variable в GlobalInfo: «^C00»…«^C99», «^C0»…«^C9», «^V00»…«^V99».
    private static bool HasNumberedTag(string text, string tag)
    {
        for (int i = text.IndexOf(tag, StringComparison.Ordinal); i >= 0; i = text.IndexOf(tag, i + 1, StringComparison.Ordinal))
        {
            if (i + 2 < text.Length && char.IsAsciiDigit(text[i + 2]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>PrintJob.BeforePrintSetup (EZPL).</summary>
    private void BeforePrintSetup()
    {
        if (Setup.DrawMode != 0)
        {
            // Картинки и фигуры — в начало списка (рисуются первыми).
            var shapes = Label.Shapes;
            var moved = new List<EzpxShape>();
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                if (shapes[i].TypeName is "Image" or "ObliqueLine" or "Ellipse" or "RoundRect" or "VarShape")
                {
                    moved.Add(shapes[i]);
                    shapes.RemoveAt(i);
                }
            }
            for (int i = 0; i < moved.Count; i++)
            {
                shapes.Insert(0, moved[i]);
            }
        }
        string doubleCut = "^XSETCUT,DOUBLECUT,";
        _beginJobCommands.Add(Setup.DoubleCut_Enable
            ? doubleCut + Setup.DoubleCut_OffsetLen + "," + Setup.DoubleCut_FirstCutMode
            : doubleCut + "0");
        if (Setup.DoubleCut_Enable)
        {
            _endJobCommands.Add(doubleCut + "0");
        }

        // PrintJob.GetPageArea.
        float dotPerMm = Units.DotPerMm(Setup.Dpi);
        var lo = Setup.Layout;
        float height = Setup.UnitType != Unit.Dot
            ? (Setup.LabelLength - lo.VerGap * (float)(lo.VerAcross - 1)) / (float)lo.VerAcross
            : (Setup.LabelLength / (float)(int)dotPerMm - lo.VerGap * (float)(lo.VerAcross - 1)) / (float)lo.VerAcross;
        float width = (Setup.LabelWidth - lo.HorGap * (float)(lo.HorAcross - 1)) / (float)lo.HorAcross;
        _labelHeightDot = (int)(height * dotPerMm);
        _labelWidthDot = (int)(width * dotPerMm);
    }

    /// <summary>
    /// Начало PrintJob.Print / Print(records): отрез, команды «в начало задания», Setup.GetCommandEZPL.
    /// </summary>
    private void WriteHeader(EzplWriter output)
    {
        if (Setup.PartialCut && !_partialCutNow)
        {
            output.WriteLine("^XSETCUT,MODE,1");
            _partialCutNow = true;
        }
        foreach (var command in _beginJobCommands)
        {
            output.WriteLine(command);
        }
        // Print() печатает по одной: Setup.Number = 1 на время шапки; Print(records) — ещё и Copy = 1.
        int copy = FromRecords ? 1 : Setup.Copy;
        foreach (var command in SetupCommands.GetCommandEZPL(Setup, number: 1, copy: copy))
        {
            output.WriteLine(command);
        }
        if (Label.Shapes.Any(s => s.TypeName == "RFIDCode") && !Setup.UsePrinterRFIDCfg)
        {
            throw new NotSupportedException("RFID не перенесён.");
        }
    }

    /// <summary>Тело PrintJob.Print после шапки.</summary>
    private void WriteLabels(EzplWriter output)
    {
        int number = Setup.Number;
        int labelsPerCut = Setup.LabelsPerCut;
        if (labelsPerCut > 1)
        {
            throw new NotSupportedException("Отрез через несколько наклеек (LabelsPerCut > 1) не перенесён.");
        }
        for (int i = 0; i < number; i++)
        {
            if (labelsPerCut > 0 && Setup.PartialCut && Setup.FullCutLast && i == number - 1)
            {
                throw new NotSupportedException("Полный отрез последней наклейки не перенесён.");
            }
            PrintNormalCommandEZPL(output);
        }
        foreach (var command in _endJobCommands)
        {
            output.WriteLine(command);
        }
        // PrintJob.Set_Full_Cut.
        if (Setup.PartialCut && _partialCutNow)
        {
            output.WriteLine("^XSETCUT,MODE,0");
            _partialCutNow = false;
        }
    }

    /// <summary>QLabel.PrintNormalCommandEZPL(Layout, Setup): одна страница (наклейка или ряд).</summary>
    private void PrintNormalCommandEZPL(EzplWriter output)
    {
        var lo = Setup.Layout;
        int number = lo.Number;
        int count = lo.HorAcross * lo.VerAcross;
        MoveXorToFront();
        output.WriteLine((Setup.DrawMode != 0 ? "^XSET,DRAWMODE," + Setup.DrawMode + "\r\n" : "") + "^L");
        if (!string.IsNullOrEmpty(Label.DateFormat))
        {
            output.WriteLine("D" + Label.DateFormat);
        }
        if (!string.IsNullOrEmpty(Label.TimeFormat))
        {
            output.WriteLine("T" + Label.TimeFormat);
        }
        var shapes = Label.Shapes;
        int shapeCount = shapes.Count;
        if (number == 1)
        {
            UpdateVariableFormatAll();
        }
        for (int i = 0; i < count; i++)
        {
            if (number > 1)
            {
                UpdateVariableFormatAll();
            }
            var (x, y) = lo.Offset(i, Setup);
            for (int k = shapeCount - 1; k >= 0; k--)
            {
                var shape = shapes[k];
                if (shape.PageAlignment == PageAlignment.Center)
                {
                    throw new NotSupportedException("Выравнивание объекта по центру страницы не перенесено.");
                }
                if (shape.IsPrint && !OutOfPageArea(shape))
                {
                    PrintShape(shape, x, y, output);
                }
            }
            if (number > 1)
            {
                UpdateSerialFormatAll(1);
            }
        }
        if (number == 1)
        {
            UpdateSerialFormatAll(1);
        }
        output.WriteLine((Setup.DrawMode != 0 ? "^XSET,DRAWMODE,0\r\n" : "") + "E");
    }

    /// <summary>QLabel.OutOfPageArea (bPrintPageAreaOnly = true): начало объекта за наклейкой.</summary>
    private bool OutOfPageArea(EzpxShape shape) =>
        shape.X > (float)_labelWidthDot || shape.Y > (float)_labelHeightDot;

    /// <summary>QLabel.MoveXorToFront: линии XOR — в начало списка.</summary>
    private void MoveXorToFront()
    {
        var shapes = Label.Shapes;
        var moved = new List<EzpxShape>();
        for (int i = shapes.Count - 1; i >= 0; i--)
        {
            if (shapes[i] is EzpxLine { Operation: 'e' })
            {
                moved.Add(shapes[i]);
                shapes.RemoveAt(i);
            }
            else if (shapes[i].TypeName == "RFIDCode")
            {
                throw new NotSupportedException("RFID не перенесён.");
            }
        }
        foreach (var line in moved)
        {
            shapes.Insert(0, line);
        }
    }

    private void PrintShape(EzpxShape shape, float x, float y, EzplWriter output)
    {
        switch (shape)
        {
            case EzpxText text:
                PrintText(text, x, y, output);
                break;
            case EzpxBarCode barCode when !barCode.IsRaster:
                PrintBarCode(barCode, x, y, output);
                break;
            case EzpxRectangle rect:
                output.WriteLine(rect.Name + Fmt.F(x + rect.XInPixel) + "," + Fmt.F(y + rect.YInPixel) + ","
                    + Fmt.F(x + rect.XInPixel + (int)Math.Round(rect.Width, 0) - 1f) + ","
                    + Fmt.F(y + rect.YInPixel + (int)Math.Round(rect.Height, 0) - 1f) + ","
                    + (int)Math.Round(rect.VStroke, 0) + "," + (int)Math.Round(rect.HStroke, 0));
                break;
            case EzpxLine line:
                output.WriteLine(line.Name + line.Operation + "," + Fmt.F(x + line.XInPixel) + "," + Fmt.F(y + line.YInPixel) + ","
                    + Fmt.F(x + line.XInPixel + (int)Math.Round(line.Width, 0) - 1f) + ","
                    + Fmt.F(y + line.YInPixel + (int)Math.Round(line.Height, 0) - 1f));
                break;
            default:
                _raster.Render(new RasterRequest(shape, x, y, this), output);
                break;
        }
    }
}
