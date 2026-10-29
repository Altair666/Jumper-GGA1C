using System.Drawing;
using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Fonts;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Printing;

/// <summary>
/// БАГ-3, вариант А: принтер без P2/P3 (ZX430i v2.110) — порт сам ставит начало строки. Сверка
/// с эталоном GoLabel: те же команды, кроме текста по центру/вправо — у него нет суффикса, а x
/// сдвинут на полширины/ширину строки назад по направлению письма. Таблица «было → стало» —
/// в выводе теста.
/// </summary>
public class TextAlignmentTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public TextAlignmentTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> AlignedGoldens()
    {
        var data = new TheoryData<string>();
        foreach (var name in Golden.Names.Except(GoLabelPortTests.FromDatabase))
        {
            data.Add(name);
        }
        if (data.Count == 0)
        {
            data.Add("(нет)");
        }
        return data;
    }

    [GoldenTheory]
    [MemberData(nameof(AlignedGoldens))]
    public void Self_alignment_shifts_start_and_drops_suffix(string name)
    {
        var golden = EzplDump.Masked(GoLabelPortTests.GoldenBytes(name));
        var g = GoLabelPortTests.LoadValues(name);
        var values = new PrintValues
        {
            Serials = g.Serials,
            Variables = g.Variables,
            SerialDefaultsFromTemplate = g.SerialDefaultsFromTemplate,
            Records = g.Records,
            Clock = g.Clock,
            PrinterAlignsText = false,
        };
        var template = GoLabelPortTests.LoadTemplate(name);
        var texts = template.Label.Shapes.OfType<GoLabel.Ezpx.EzpxText>().ToList();
        var actual = EzplDump.Masked(new EzplJob(template, values, new MaskOnly()).Build());
        actual.Count.ShouldBe(golden.Count);

        for (int i = 0; i < golden.Count; i++)
        {
            string before = golden[i];
            string after = actual[i];
            if (!IsAlignedText(before, out var fields, out int styleIndex, out string suffix))
            {
                after.ShouldBe(before, $"{name} #{i}");
                continue;
            }
            // Тот же текст, без P2/P3, сдвиг только по направлению письма.
            var a = after.Split(',', fields.Length);
            a[styleIndex].ShouldBe(fields[styleIndex][..^2], $"{name} #{i}: суффикс выравнивания не убран");
            a[^1].ShouldBe(fields[^1], $"{name} #{i}: текст строки");
            a[3..6].ShouldBe(fields[3..6], $"{name} #{i}: размер и промежуток");
            a[7..^1].ShouldBe(fields[7..^1], $"{name} #{i}: прочие поля");
            char rotation = fields[styleIndex][0];
            int x = int.Parse(fields[1]), y = int.Parse(fields[2]);
            int nx = int.Parse(a[1]), ny = int.Parse(a[2]);
            string line = fields[^1];
            float width = Width(texts, fields, line);
            int back = (int)Math.Round(suffix == "P2" ? width / 2f : width, 0, MidpointRounding.AwayFromZero);
            (int ex, int ey) = rotation switch
            {
                '0' => (x - back, y),
                '1' => (x, y - back),
                '2' => (x + back, y),
                _ => (x, y + back),
            };
            (nx, ny).ShouldBe((ex, ey), $"{name} #{i} «{line}»");
            back.ShouldBeGreaterThan(0);
            _output.WriteLine($"{name} #{i} {suffix} поворот {rotation} «{line}»: ({x},{y}) → ({nx},{ny}), ширина {width:0.#}");
        }
    }

    /// <summary>Текст принтера с суффиксом P2/P3 в поле стиля (после поворота).</summary>
    private static bool IsAlignedText(string command, out string[] fields, out int styleIndex, out string suffix)
    {
        // Данные — всё после последнего служебного поля (в них бывают запятые).
        int count = command.StartsWith("AT", StringComparison.Ordinal) ? 10 : 8;
        fields = command.Split(',', count);
        styleIndex = 6;
        suffix = "";
        if (!command.StartsWith('A') || fields.Length < count)
        {
            return false;
        }
        string style = fields[styleIndex];
        if (style.Length > 2 && char.IsDigit(style[0]) && (style.EndsWith("P2") || style.EndsWith("P3")))
        {
            suffix = style[^2..];
            return true;
        }
        return false;
    }

    /// <summary>Ширина строки — тем же замером, что у порта, по параметрам самой команды.</summary>
    private static float Width(List<GoLabel.Ezpx.EzpxText> texts, string[] fields, string line)
    {
        if (fields[0].StartsWith("AT", StringComparison.Ordinal))
        {
            // ATx,x,y,w,h,gap,…: семейство — из шаблона (текст с тем же шрифтом принтера).
            var t = texts.First(s => s.FontType == GoLabel.Ezpx.FontSource.TrueType_Font && fields[0] == "AT" + s.FontId);
            int w = int.Parse(fields[3]), h = int.Parse(fields[4]);
            var style = (fields[6].Contains('B') ? FontStyle.Bold : 0) | (fields[6].Contains('T') ? FontStyle.Italic : 0);
            return PrinterTextWidth.Measure(t.FontFamily, style, h, (float)w / h, float.Parse(fields[5], System.Globalization.CultureInfo.InvariantCulture), line);
        }
        // AD,x,y,mx,my,gap,…: встроенный D — Arial 33 px (таблица IBfonts300 GoLabel).
        fields[0].ShouldBe("AD");
        return PrinterTextWidth.Measure("Arial", 0, 33f, int.Parse(fields[3]), float.Parse(fields[5], System.Globalization.CultureInfo.InvariantCulture), line);
    }

    private sealed class MaskOnly : IRasterRenderer
    {
        public void Render(RasterRequest request, EzplWriter output) => output.WriteLine(EzplDump.MaskedRaster);
    }
}
