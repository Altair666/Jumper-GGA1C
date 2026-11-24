// Правка .ezpx с сохранением. Читаем и пишем один XDocument: у каждой фигуры EzpxReader
// оставляет исходный элемент (EzpxShape.Element), правки вносятся прямо в него, а в модели
// дублируются для превью. Неизвестные порту поля (а их у объектов больше, чем перенесено)
// так никуда не деваются — сохранённый файл остаётся шаблоном GoLabel.
using System.Xml;
using System.Xml.Linq;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Ezpx;

/// <summary>
/// Правка настроек страницы (Setup): размер наклейки и зазор в мм, отступы в точках,
/// скорость, плотность, копии, способ печати (PrintMode 0 = ^AT термоперенос, 1 = ^AD
/// прямая). null — не менять.
/// </summary>
public sealed record EzpxSetupEdit(
    float? WidthMm = null,
    float? HeightMm = null,
    float? GapMm = null,
    int? LeftMarginDots = null,
    int? TopMarginDots = null,
    int? Speed = null,
    int? Darkness = null,
    int? Copies = null,
    bool? DirectThermal = null);

/// <summary>Правка одного объекта по его номеру в списке Shapes. null — не менять.
/// Remove = true удаляет объект; другие поля при этом игнорируются.</summary>
public sealed record EzpxShapeEdit(
    int Index,
    float? X = null,
    float? Y = null,
    float? Width = null,
    float? Height = null,
    string? Data = null,
    bool Remove = false);

public sealed class EzpxEditor
{
    private readonly XDocument _doc;
    private readonly XElement _root;

    private EzpxEditor(XDocument doc, EzpxTemplate template)
    {
        _doc = doc;
        _root = doc.Root!;
        Template = template;
    }

    public EzpxTemplate Template { get; }

    public static EzpxEditor Load(Stream stream)
    {
        // Те же настройки, что у EzpxReader.Load: в настоящих шаблонах есть ссылка на символ 0.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { CheckCharacters = false });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new FormatException("Пустой .ezpx");
        if (root.Name.LocalName != "PrintJob")
        {
            throw new FormatException($"Корневой элемент .ezpx — {root.Name.LocalName}, ожидался PrintJob");
        }
        return new EzpxEditor(doc, EzpxReader.Read(root));
    }

    /// <summary>
    /// Применить правки. Для объектов с BoundRect положение меняется и в атрибутах X/Y, и в
    /// самом BoundRect — GoLabel читает его оттуда (см. EzpxReader.ReadShape), рассинхрон
    /// означал бы, что GoLabel и хаб видят разные места. Ширина/высота у «Текста» принтера
    /// отсутствует — такая правка игнорируется.
    /// </summary>
    public void Apply(IReadOnlyList<EzpxShapeEdit> edits)
    {
        // Удаления применяются первыми, по убыванию индекса — чтобы сдвиг индексов после
        // удаления не направил остальные правки в чужие объекты. Смешивать удаление и правку
        // в одном запросе нельзя: правки с индексами после удалённого игнорируются.
        var removals = edits.Where(e => e.Remove).Select(e => e.Index).OrderDescending().ToList();

        if (removals.Count > 0)
        {
            foreach (var index in removals)
            {
                RemoveShape(index);
            }

            return;
        }

        foreach (var edit in edits)
        {
            if (edit.Index < 0 || edit.Index >= Template.Label.Shapes.Count)
            {
                continue;
            }

            var shape = Template.Label.Shapes[edit.Index];
            var e = shape.Element!;

            if (edit.Data is { } data && shape is EzpxGraphic graphic)
            {
                graphic.Data = data;
                SetChild(e, "Data", data);
            }

            bool hasRect = shape is EzpxGraphic && e.Element("BoundRect") != null;

            if (edit.X is { } x)
            {
                shape.X = x;
                SetAttr(e, "X", x);
                if (hasRect)
                {
                    SetRectField(e, "X", ((EzpxGraphic)shape).BoundRect with { X = x });
                }
            }

            if (edit.Y is { } y)
            {
                shape.Y = y;
                SetAttr(e, "Y", y);
                if (hasRect)
                {
                    SetRectField(e, "Y", ((EzpxGraphic)shape).BoundRect with { Y = y });
                }
            }

            switch (shape)
            {
                case EzpxRasterShape raster:
                    ApplySizeToRaster(raster, e, edit);
                    break;
                case EzpxBarCode barcode:
                    ApplySizeToBarCode(barcode, e, edit);
                    break;
                case EzpxRectangle rect:
                    if (edit.Width is { } rw)
                    {
                        rect.Width = rw;
                        SetChild(e, "Width", rw);
                    }
                    if (edit.Height is { } rh)
                    {
                        rect.Height = rh;
                        SetChild(e, "Height", rh);
                    }
                    break;
                case EzpxLine line:
                    if (edit.Width is { } lw)
                    {
                        line.Width = lw;
                        SetChild(e, "Width", lw);
                    }
                    if (edit.Height is { } lh)
                    {
                        line.Height = lh;
                        SetChild(e, "Height", lh);
                    }
                    break;
            }
        }
    }

    private static void ApplySizeToRaster(EzpxRasterShape raster, XElement e, EzpxShapeEdit edit)
    {
        var rect = raster.BoundRect;
        if (edit.Width is { } w)
        {
            rect.Width = w;
        }
        if (edit.Height is { } h)
        {
            rect.Height = h;
        }
        raster.BoundRect = rect;

        // Порядок у GoLabel: BoundRectWidth/Height — sibling-элементы, затем BoundRect, чьи
        // значения побеждают. Пишем во все места, чтобы GoLabel и хаб видели одно.
        if (edit.Width is { } width)
        {
            SetChild(e, "BoundRectWidth", width);
            SetBoundRectField(e, "Width", width);
        }
        if (edit.Height is { } height)
        {
            SetChild(e, "BoundRectHeight", height);
            SetBoundRectField(e, "Height", height);
        }
    }

    private static void ApplySizeToBarCode(EzpxBarCode barcode, XElement e, EzpxShapeEdit edit)
    {
        if (edit.Width is { } w)
        {
            barcode.Width = w;
            SetChild(e, "Width", w);
        }
        if (edit.Height is { } h)
        {
            barcode.Height = h;
            SetChild(e, "Height", h);
        }
    }

    private static void SetRectField(XElement e, string field, RectF rect)
    {
        if (e.Element("BoundRect") is not { } b)
        {
            return;
        }
        float value = field switch
        {
            "X" => rect.X,
            "Y" => rect.Y,
            "Width" => rect.Width,
            "Height" => rect.Height,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
        SetChild(b, field, value);
    }

    private static void SetBoundRectField(XElement e, string field, float value)
    {
        if (e.Element("BoundRect") is { } b)
        {
            SetChild(b, field, value);
        }
    }

    /// <summary>
    /// Атрибут с плавающим значением. Формат — инвариантная культура, как пишет XmlSerializer.
    /// </summary>
    private static void SetAttr(XElement e, string name, float value)
    {
        e.SetAttributeValue(name, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void SetChild(XElement parent, string name, float value)
    {
        SetChild(parent, name, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void SetChild(XElement parent, string name, string value)
    {
        if (parent.Element(name) is { } existing)
        {
            existing.Value = value;
            return;
        }

        // Элемента не было — XmlSerializer не пишет значения по умолчанию. Создаём в конце:
        // порядок элементов для XmlSerializer (а им читает и GoLabel) не важен.
        parent.SetElementValue(name, value);
    }

    /// <summary>
    /// Применить правки настроек страницы к Setup-элементу. Поля Setup — атрибуты
    /// элемента (см. EzpxReader.ReadSetup), пишем тем же способом.
    /// </summary>
    public void ApplySetup(EzpxSetupEdit edit)
    {
        var setup = _root.Element("Setup") ?? throw new InvalidOperationException("В шаблоне нет Setup.");

        void Set(string name, float value) =>
            setup.SetAttributeValue(name, value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        void SetInt(string name, int value) => setup.SetAttributeValue(name, value);

        if (edit.WidthMm is { } w)
        {
            Template.Setup.LabelWidth = w;
            Set("LabelWidth", w);
        }
        if (edit.HeightMm is { } h)
        {
            Template.Setup.LabelLength = h;
            Set("LabelLength", h);
        }
        if (edit.GapMm is { } gap)
        {
            Template.Setup.GapLength = gap;
            Set("GapLength", gap);
        }
        if (edit.LeftMarginDots is { } lm)
        {
            Template.Setup.LeftMargin = lm;
            SetInt("LeftMargin", lm);
        }
        if (edit.TopMarginDots is { } tm)
        {
            Template.Setup.TopMargin = tm;
            SetInt("TopMargin", tm);
        }
        if (edit.Speed is { } speed)
        {
            Template.Setup.Speed = speed;
            SetInt("Speed", speed);
        }
        if (edit.Darkness is { } darkness)
        {
            Template.Setup.Darkness = darkness;
            SetInt("Darkness", darkness);
        }
        if (edit.Copies is { } copies)
        {
            Template.Setup.Copy = copies;
            SetInt("Copy", copies);
        }
        if (edit.DirectThermal is { } direct)
        {
            // 0 = ^AT термоперенос, 1 = ^AD прямая печать (SetupCommands.GetCommandEZPL)
            Template.Setup.PrintMode = direct ? 1 : 0;
            SetInt("PrintMode", Template.Setup.PrintMode);
        }
    }

    /// <summary>Удалить объект: из модели и из XML-дерева. Индексы после него сдвигаются.</summary>
    public void RemoveShape(int index)
    {
        if (index < 0 || index >= Template.Label.Shapes.Count)
        {
            return;
        }

        Template.Label.Shapes[index].Element!.Remove();
        Template.Label.Shapes.RemoveAt(index);
    }

    /// <summary>
    /// Сохранить. Форматирование файла — как прочитано; GoLabel читает XmlSerializer'ом,
    /// поэтому отличия в отступах не мешают.
    ///
    /// Символ U+0000 (в настоящих шаблонах он записан как &amp;#x0;) LINQ to XML записывать
    /// не умеет ни символом, ни сущностью. На время записи он заменяется маркером, который
    /// после сериализации возвращается в точный текст GoLabel — &amp;#x0;.
    /// </summary>
    public void Save(Stream stream)
    {
        const string marker = "GGA1C-NULL";
        var restored = new List<(XText Node, string Original)>();

        foreach (var text in _doc.DescendantNodes().OfType<XText>())
        {
            if (text.Value.Contains('\0'))
            {
                restored.Add((text, text.Value));
                text.Value = text.Value.Replace("\0", marker);
            }
        }

        try
        {
            using var buffer = new MemoryStream();
            _doc.Save(buffer);
            ReplaceBytes(buffer, System.Text.Encoding.UTF8.GetBytes(marker), System.Text.Encoding.UTF8.GetBytes("&#x0;"));
            var output = buffer.ToArray();
            stream.Write(output, 0, output.Length);
        }
        finally
        {
            foreach (var (node, original) in restored)
            {
                node.Value = original;
            }
        }
    }

    private static void ReplaceBytes(MemoryStream buffer, byte[] from, byte[] to)
    {
        var data = buffer.ToArray();
        using var output = new MemoryStream(data.Length);
        int start = 0;
        while (true)
        {
            int at = IndexOf(data, from, start);
            if (at < 0)
            {
                output.Write(data, start, data.Length - start);
                break;
            }
            output.Write(data, start, at - start);
            output.Write(to, 0, to.Length);
            start = at + from.Length;
        }
        buffer.SetLength(0);
        buffer.Write(output.ToArray(), 0, (int)output.Length);
        buffer.Position = 0;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        for (int i = start; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length && match; j++)
            {
                match = data[i + j] == pattern[j];
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }
}
