// Чтение .ezpx. В GoLabel это XmlSerializer над PrintJob (QLabelSDK ObjectXML.Load,
// QLabelTools.XMLLoad) и правки после загрузки (GlobalInfo GlobalPrint.InitLabel). Здесь — разбор
// тех же элементов вручную: XmlSerializer потребовал бы перенести все ~80 классов объектов с
// [XmlInclude], а нужно меньше десятка. Неизвестный тип объекта читается как растровый.
using System.Xml;
using System.Xml.Linq;

namespace GGA1C.Sync.Printing.GoLabel.Ezpx;

public static class EzpxReader
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    // Типы QLabelSDK, наследники Graphic (у них BoundRect и Data, а не Width/Height).
    private static readonly HashSet<string> GraphicTypes = new(StringComparer.Ordinal)
    {
        "Text", "BarCode", "Image", "WindowText", "WindowTextBox", "GdiWindowTextBox", "RichTextImage",
        "RichTextImageID", "WinWordArt", "CurvedText", "QRCode", "DataMatrix", "Pdf417", "MicroPdf417",
        "Aztec", "MaxiCode", "GS1DataBar", "GS1Composite", "DotCode", "HanXin", "CodaBlockF", "TLC39",
        "HIBC1D", "HIBCQRCode", "HIBCDataMatrix", "HIBCAztec", "RFIDCode",
    };

    public static EzpxTemplate Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static EzpxTemplate Load(Stream stream)
    {
        // В настоящих шаблонах GoLabel есть ссылка на символ 0 (&#x0;), недопустимая в XML 1.0:
        // GoLabel читает XmlTextReader с Normalization = false, мы — с CheckCharacters = false.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { CheckCharacters = false });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new FormatException("Пустой .ezpx");
        if (root.Name.LocalName != "PrintJob")
        {
            throw new FormatException($"Корневой элемент .ezpx — {root.Name.LocalName}, ожидался PrintJob");
        }

        var template = new EzpxTemplate
        {
            GraphicMode = Bool(root.Element("GraphicMode"), false),
            FormatVersion = Int(root.Element("FormatVersion"), 0),
            PrinterModel = root.Element("PrinterModel")?.Value ?? "",
            PrinterLanguage = root.Element("PrinterLanguage")?.Value ?? "EZPL",
        };
        if (root.Element("Setup") is { } setup)
        {
            template.Setup = ReadSetup(setup);
        }
        if (root.Element("Label") is { } label)
        {
            template.Label = ReadLabel(label);
        }
        InitLabel(template);
        return template;
    }

    private static EzpxSetup ReadSetup(XElement e)
    {
        var s = new EzpxSetup
        {
            InfinityPrint = Bool(e.Attribute("bInfinityPrint"), false),
            LabelLength = Float(e.Attribute("LabelLength"), 50f),
            LabelWidth = Float(e.Attribute("LabelWidth"), 50f),
            LeftMargin = Int(e.Attribute("LeftMargin"), 0),
            TopMargin = Int(e.Attribute("TopMargin"), 0),
            LabelType = Int(e.Attribute("LabelType"), 1),
            GapLength = Float(e.Attribute("GapLength"), 3f),
            FeedLength = Float(e.Attribute("FeedLength"), 10f),
            ZSign = Char(e.Attribute("ZSign"), '+'),
            BlackMark = Float(e.Attribute("BlackMark"), 0f),
            Position = Float(e.Attribute("Position"), 0f),
            Speed = Int(e.Attribute("Speed"), 20),
            Copy = Int(e.Attribute("Copy"), 1),
            Stripper = Int(e.Attribute("Stripper"), 0),
            LabelsPerCutRaw = Int(e.Attribute("LabelsPerCut"), 0),
            DoubleCut_Enable = Bool(e.Attribute("DoubleCut_Enable"), false),
            DoubleCut_OffsetLen = Int(e.Attribute("DoubleCut_OffsetLen"), 0),
            DoubleCut_FirstCutMode = Int(e.Attribute("DoubleCut_FirstCutMode"), 1),
            Rotate180 = Int(e.Attribute("Rotate180"), 300),
            Stop = Float(e.Attribute("Stop"), 12f),
            Darkness = Int(e.Attribute("Darkness"), 5),
            Number = Int(e.Attribute("Number"), 1),
            CutDataBase = Bool(e.Attribute("bCutDataBase"), false),
            BatchCut = Bool(e.Attribute("bBatchCut"), false),
            PartialCut = Bool(e.Attribute("bPartialCut"), false),
            FullCutLast = Bool(e.Attribute("bFullCutLast"), false),
            PageDirection = Enum(e.Attribute("PageDirection"), PageDirection.Portrait),
            PrintMode = Int(e.Attribute("PrintMode"), 1),
            UsePrinterRFIDCfg = Bool(e.Attribute("bUsePrinterRFIDCfg"), true),
            DrawMode = Int(e.Attribute("DrawMode"), 0),
            UnitType = Enum(e.Element("UnitType"), Unit.Mm),
            Dpi = Float(e.Element("Dpi"), 200f),
        };
        if (e.Element("Layout") is { } l)
        {
            s.Layout = new EzpxLayout
            {
                Shape = Int(l.Attribute("Shape"), 0),
                AcrossType = Enum(l.Attribute("AcrossType"), AcrossType.Copied),
                PageDirection = Enum(l.Attribute("PageDirection"), PageDirection.Portrait),
                HorAcross = Int(l.Attribute("HorAcross"), 1),
                VerAcross = Int(l.Attribute("VerAcross"), 1),
                HorGap = Float(l.Attribute("HorGap"), 0f),
                VerGap = Float(l.Attribute("VerGap"), 0f),
                LabelMode = Int(l.Attribute("LabelMode"), 0),
                BottomMargin = Int(l.Attribute("BottomMargin"), 0),
                RightMargin = Int(l.Attribute("RightMargin"), 0),
            };
        }
        return s;
    }

    private static EzpxLabel ReadLabel(XElement e)
    {
        var label = new EzpxLabel
        {
            DateFormat = e.Element("DateFormat")?.Value,
            TimeFormat = e.Element("TimeFormat")?.Value,
            DataBaseFormat = e.Element("DataBaseFormat")?.Value ?? "None",
        };
        if (e.Element("SerialFormat") is { } sf)
        {
            label.SerialFormat = Strings(sf);
        }
        if (e.Element("SerialLeadingCode") is { } lc)
        {
            label.SerialLeadingCode = lc.Elements().Select(x => Int(x, 0)).ToArray();
        }
        if (e.Element("SerialCustomSequence") is { } cs)
        {
            label.SerialCustomSequence = Strings(cs);
        }
        if (e.Element("bSerialSpecialCarry") is { } sc)
        {
            label.SerialSpecialCarry = sc.Elements().Select(x => Bool(x, false)).ToArray();
        }
        if (e.Element("VariableFormat") is { } vf)
        {
            label.VariableFormat = Strings(vf);
        }
        if (e.Element("VariableOpFormat") is { } vo)
        {
            label.VariableOpFormat = vo.Elements().Select(x => x.Value).ToList();
        }
        if (e.Element("qlabel") is { } shapes)
        {
            foreach (var shape in shapes.Elements("GraphicShape"))
            {
                label.Shapes.Add(ReadShape(shape));
            }
        }
        return label;
    }

    private static EzpxShape ReadShape(XElement e)
    {
        string type = (string?)e.Attribute(Xsi + "type") ?? "";
        EzpxShape shape = type switch
        {
            "Text" => ReadText(e),
            "BarCode" => ReadBarCode(e),
            "QRectangle" => new EzpxRectangle
            {
                Width = Float(e.Element("Width"), 4f),
                Height = Float(e.Element("Height"), 12f),
                HStroke = Float(e.Element("HStroke"), 1f),
                VStroke = Float(e.Element("VStroke"), 1f),
            },
            "Line" => new EzpxLine
            {
                LineShape = Enum(e.Element("lineShape"), LineShape.FillRect),
                Width = Float(e.Element("Width"), 4f),
                Height = Float(e.Element("Height"), 12f),
                Operation = Char(e.Element("Operation"), 'o'),
            },
            _ => ReadRaster(e, type),
        };
        shape.TypeName = type;
        shape.Element = e;
        if (e.Element("Name") is { } name)
        {
            shape.Name = name.Value;
        }
        shape.X = Float(e.Attribute("X"), shape.X);
        shape.Y = Float(e.Attribute("Y"), shape.Y);
        shape.Direction = Enum(e.Attribute("Direction"), Orientation.Angle0);
        // GraphicShape.Direction: у всех, кроме Text и WindowText, — только 4 поворота.
        if (type != "Text" && type != "WindowText")
        {
            shape.Direction = (Orientation)((int)shape.Direction % 4);
        }
        shape.Alignment = Enum(e.Attribute("Alignment"), shape.Alignment);
        shape.IsPrint = Bool(e.Attribute("IsPrint"), true);
        shape.PageAlignment = Enum(e.Attribute("PageAlignment"), PageAlignment.None);
        return shape;
    }

    private static void ReadGraphic(XElement e, EzpxGraphic g)
    {
        if (e.Element("Data") is { } data)
        {
            g.Data = data.Value;
        }
        g.DateFormat = e.Element("DateFormat")?.Value;
        g.TimeFormat = e.Element("TimeFormat")?.Value;
        g.Prompt = e.Element("Prompt")?.Value ?? "None";
        g.FormatType = e.Element("FormatType")?.Value ?? "None";
        g.CharFilterRule = e.Element("CharFilterRule")?.Value ?? "None";
        g.LinkMode = e.Element("LinkMode")?.Value ?? "OriginalData";
        g.ReplaceInfoCount = e.Element("ReplaceInfoItems")?.Elements().Count() ?? 0;
        g.ItemInfoCount = e.Element("ItemInfoList")?.Elements().Count() ?? 0;
        g.RemovePreZeroAndEmpty = Bool(e.Element("bRemovePreZeroAndEmpty"), false);
        g.ReplaceSpecialCharFromDB = Bool(e.Element("bReplaceSpecialCharFromDB"), false);
        g.GetAiFromDigitalLink = Bool(e.Element("GetAiFromDigitalLink"), false);
        g.GraphicMode = Bool(e.Element("GraphicMode"), false);
        g.BoundRect = ReadBoundRect(e, g.BoundRect);
        if (e.Element("CharTruncateRule") is { } t)
        {
            g.CharTruncateRule = new CharTruncateRule
            {
                TrimLeft = Bool(t.Element("TrimLeft"), false),
                TrimRight = Bool(t.Element("TrimRight"), false),
                RemoveCharLeft = Bool(t.Element("RemoveCharLeft"), false),
                RemoveCharLeftNo = Int(t.Element("RemoveCharLeftNo"), 0),
                RemoveCharRight = Bool(t.Element("RemoveCharRight"), false),
                RemoveCharRightNo = Int(t.Element("RemoveCharRightNo"), 0),
                KeepCharLeft = Bool(t.Element("KeepCharLeft"), false),
                KeepCharLeftNo = Int(t.Element("KeepCharLeftNo"), 6),
                KeepCharRight = Bool(t.Element("KeepCharRight"), false),
                KeepCharRightNo = Int(t.Element("KeepCharRightNo"), 6),
                RemoveDotZero = Bool(t.Element("RemoveDotZero"), false),
            };
        }
    }

    /// <summary>
    /// Graphic.BoundRect. XmlSerializer ставит по порядку BoundRectWidth, BoundRectHeight, затем
    /// BoundRect (его X/Y/Width/Height идут последними и побеждают Location/Size).
    /// </summary>
    private static RectF ReadBoundRect(XElement e, RectF rect)
    {
        rect.Width = Float(e.Element("BoundRectWidth"), rect.Width);
        rect.Height = Float(e.Element("BoundRectHeight"), rect.Height);
        if (e.Element("BoundRect") is { } b)
        {
            if (b.Element("Location") is { } loc)
            {
                rect.X = Float(loc.Element("X"), rect.X);
                rect.Y = Float(loc.Element("Y"), rect.Y);
            }
            if (b.Element("Size") is { } size)
            {
                rect.Width = Float(size.Element("Width"), rect.Width);
                rect.Height = Float(size.Element("Height"), rect.Height);
            }
            rect.X = Float(b.Element("X"), rect.X);
            rect.Y = Float(b.Element("Y"), rect.Y);
            rect.Width = Float(b.Element("Width"), rect.Width);
            rect.Height = Float(b.Element("Height"), rect.Height);
        }
        return rect;
    }

    private static EzpxText ReadText(XElement e)
    {
        var t = new EzpxText();
        ReadGraphic(e, t);
        t.TextAlign = Enum(e.Attribute("TextAlign"), TextAlignment.Left);
        t.FontType = Enum(e.Attribute("FontType"), FontSource.BuiltIn_Bitmap_Font);
        // Сеттер Text.FontType для TrueType ставит размер 32 pt, если дальше в файле его нет.
        float defaultSize = t.FontType == FontSource.TrueType_Font ? 32f : 1f;
        t.FontHeight = Float(e.Attribute("FontHeight"), defaultSize);
        t.FontWidth = Float(e.Attribute("FontWidth"), defaultSize);
        t.FontId = Enum(e.Attribute("FontId"), FontIDs.Default);
        t.TextSpace = Float(e.Attribute("TextSpace"), 0f);
        t.Encoding = Enum(e.Attribute("Encoding"), FontEncodings.Default);
        t.IsInverse = Bool(e.Attribute("IsInverse"), false);
        t.IsUTF8 = Bool(e.Attribute("IsUTF8"), true);
        t.IsCheckDigit = Bool(e.Attribute("IsCheckDigit"), false);
        t.UsePrinterClock = Bool(e.Attribute("UsePrinterClock"), true);
        t.UseBlock = Bool(e.Element("bUseBlock"), false);
        t.BTrueType = Bool(e.Element("BTrueType"), true);
        // Text.FontCmd → StringToFont/StringToGFont: «семейство,размер[,BIUS]».
        if ((string?)e.Attribute("FontCmd") is { } fontCmd)
        {
            string[] parts = fontCmd.Split(',');
            t.FontFamily = parts[0];
            string style = parts.Length > 2 ? parts[2] : "";
            t.Bold = style.Contains('B');
            t.Italic = style.Contains('I');
            t.Underline = style.Contains('U');
            t.Strikeout = style.Contains('S');
        }
        return t;
    }

    private static EzpxBarCode ReadBarCode(XElement e)
    {
        var b = new EzpxBarCode();
        ReadGraphic(e, b);
        b.Symbology = Enum(e.Attribute("Symbology"), BarCodeType.Code39);
        b.CaptionAlignment = Enum(e.Attribute("CaptionAlignment"), CaptionType.BottomAndLeft);
        b.Height = Float(e.Attribute("Height"), 24f);
        b.Width = Float(e.Attribute("Width"), 4f);
        b.Narrow = Float(e.Attribute("Narrow"), 2f);
        b.BuiltinFont = Bool(e.Attribute("bBuiltinFont"), true);
        b.SetBuiltinFontSize = Bool(e.Attribute("bSetBuiltinFontSize"), false);
        b.Code128Subset = Enum(e.Attribute("Code128Subset"), Code128Subset.Auto);
        b.BearerBarStyle = Int(e.Attribute("BearerBarStyle"), 3);
        b.BearerBarWidth = Int(e.Attribute("BearerBarWidth"), 5);
        b.QuietZoneWidth = Int(e.Attribute("QuietZoneWidth"), 9);
        b.CheckDigitType = Enum(e.Element("CheckDigitType"), CheckDigitType.MOD_43);
        b.CustomizeGuardBar = Bool(e.Element("CustomizeGuardBar"), false);
        b.GuardBarHeight = Float(e.Element("GuardBarHeight"), 0f);
        return b;
    }

    private static EzpxRasterShape ReadRaster(XElement e, string type)
    {
        var r = new EzpxRasterShape { IsGraphic = GraphicTypes.Contains(type) };
        if (r.IsGraphic)
        {
            r.Data = e.Element("Data")?.Value ?? "1234";
            r.BoundRect = ReadBoundRect(e, new RectF(4f, 4f, 100.1f, 100.3f));
        }
        else
        {
            r.Width = Float(e.Element("Width"), 0f);
            r.Height = Float(e.Element("Height"), 0f);
        }
        return r;
    }

    /// <summary>
    /// Правки после загрузки: GlobalPrint.InitLabel (GlobalInfo) и QLabelTools.XMLLoad.
    /// </summary>
    private static void InitLabel(EzpxTemplate t)
    {
        if (t.FormatVersion < 1)
        {
            throw new NotSupportedException(
                "Шаблон старого формата (FormatVersion < 1): перенос переменных 30–38 в 90–98 не перенесён.");
        }
        if (t.Label.VariableFormat.Any(v => v != null && v.Contains(",DataSourceFromObject:", StringComparison.Ordinal)))
        {
            throw new NotSupportedException("Переменная со ссылкой на объект (DataSourceFromObject) не перенесена.");
        }

        // GlobalInfo.UnitTypeDot = false (по умолчанию и на стенде): единицы Setup — миллиметры.
        var s = t.Setup;
        if (s.UnitType == Unit.Dot)
        {
            int zoom = Units.DpiZoom((int)s.Dpi);
            s.UnitType = Unit.Mm;
            s.LabelLength /= zoom;
            s.GapLength /= zoom;
            s.FeedLength /= zoom;
            s.BlackMark /= zoom;
            s.Position /= zoom;
        }

        var label = t.Label;
        label.SerialFormat = Resize(label.SerialFormat, EzpxLabel.MaxSerial, "");
        label.SerialLeadingCode = Resize(label.SerialLeadingCode, EzpxLabel.MaxSerial, 0);
        label.VariableFormat = Resize(label.VariableFormat, EzpxLabel.MaxVariable, "");
        // Эти два InitLabel не выравнивает; дополняем, чтобы не упасть на коротком массиве.
        if (label.SerialCustomSequence.Length < EzpxLabel.MaxSerial)
        {
            label.SerialCustomSequence = Resize(label.SerialCustomSequence, EzpxLabel.MaxSerial, null);
        }
        if (label.SerialSpecialCarry.Length < EzpxLabel.MaxSerial)
        {
            label.SerialSpecialCarry = Resize(label.SerialSpecialCarry, EzpxLabel.MaxSerial, false);
        }

        // Переносы строк в тексте принтера приводятся к \r\n.
        foreach (var text in label.Shapes.OfType<EzpxText>())
        {
            string[] lines = text.Data.Split("\r\n");
            text.Data = string.Join("\r\n", lines.Select(line => line.Replace("\n", "\r\n")));
        }
    }

    private static T[] Resize<T>(T[] source, int length, T fill)
    {
        if (source.Length == length)
        {
            return source;
        }
        var result = new T[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = i < source.Length ? source[i] : fill;
        }
        return result;
    }

    private static string?[] Strings(XElement array) =>
        array.Elements()
            .Select(x => (string?)x.Attribute(Xsi + "nil") == "true" ? null : x.Value)
            .ToArray();

    private static float Float(XObject? node, float fallback) =>
        Text(node) is { } s ? XmlConvert.ToSingle(s) : fallback;

    private static int Int(XObject? node, int fallback) =>
        Text(node) is { } s ? XmlConvert.ToInt32(s) : fallback;

    private static bool Bool(XObject? node, bool fallback) =>
        Text(node) is { } s ? XmlConvert.ToBoolean(s) : fallback;

    /// <summary>char XmlSerializer пишет числом (ZSign="45", Operation=111).</summary>
    private static char Char(XObject? node, char fallback) =>
        Text(node) is { } s ? (char)XmlConvert.ToUInt16(s) : fallback;

    private static T Enum<T>(XObject? node, T fallback) where T : struct, System.Enum =>
        Text(node) is { } s ? System.Enum.Parse<T>(s.Trim()) : fallback;

    private static string? Text(XObject? node) => node switch
    {
        XAttribute a => a.Value,
        XElement e => e.Value,
        _ => null,
    };

}
