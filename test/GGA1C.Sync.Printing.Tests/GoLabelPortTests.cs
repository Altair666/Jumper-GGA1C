using System.Text;
using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Printing;

/// <summary>
/// Порт ядра GoLabel против эталонов (П5): шаблон из папки эталона + значения из args.txt →
/// байты, которые должны совпасть с golabel.bin. Зачёт — только равенство; при расхождении
/// сообщение называет первую расходящуюся команду.
/// </summary>
public class GoLabelPortTests
{
    /// <summary>Эталоны, которые порт уже собирает целиком (растров Q в них нет или они перенесены).</summary>
    public static readonly string[] FullyPorted =
    [
        "sample2",
        // шаг 3: Image, Ellipse. gkv3-product, gkv1-oem-product(-nologo) — нет: уменьшение логотипа
        // бикубикой в 64-битном gdiplus.dll даёт 1–4 точки мимо, в 32-битном (как у GoLabel) — 0
        // (docs/printing/p5-plan.md, «Шаг 3»).
        "gkv3-vm-product",
        // шаг 4: Code39 растром
        "gkv1-vm-box", "gkv3-vm-box",
    ];

    /// <summary>Печать из базы — шаг 6: без снимка данных команды наклеек не сверить.</summary>
    public static readonly string[] FromDatabase = ["gkv3-box-var-mysql"];

    /// <summary>Дата съёма эталонов (П2) — для даты ПК, которую GoLabel рисует сам.</summary>
    public static readonly DateTimeOffset CaptureTime = new(2026, 10, 28, 21, 0, 0, TimeSpan.FromHours(3));

    public static TheoryData<string> AllGoldens() => Data(Golden.Names);

    public static TheoryData<string> CommandGoldens() => Data(Golden.Names.Except(FromDatabase));

    public static TheoryData<string> FullGoldens() => Data(Golden.Names.Intersect(FullyPorted));

    [GoldenTheory]
    [MemberData(nameof(AllGoldens))]
    public void Header_matches_golden(string name)
    {
        var job = new EzplJob(LoadTemplate(name), LoadValues(name));
        byte[] header = job.BuildHeader();
        byte[] golden = GoldenBytes(name);
        var actual = EzplDump.Split(header).Select(c => c.Text).ToList();
        var goldenHeader = HeaderOf(golden);
        header.SequenceEqual(goldenHeader).ShouldBeTrue(
            $"{name}: шапка не равна эталону — {EzplDump.FirstDifference(EzplDump.Split(goldenHeader).Select(c => c.Text).ToList(), actual)}");
    }

    [GoldenTheory]
    [MemberData(nameof(CommandGoldens))]
    public void Commands_match_golden_with_masked_rasters(string name)
    {
        var job = new EzplJob(LoadTemplate(name), LoadValues(name), new MaskingRasterRenderer());
        var expected = EzplDump.Masked(GoldenBytes(name));
        var actual = EzplDump.Masked(job.Build());
        actual.SequenceEqual(expected).ShouldBeTrue(
            $"{name}: команды (растры Q замаскированы) не равны эталону — {EzplDump.FirstDifference(expected, actual)}");
    }

    [GoldenTheory]
    [MemberData(nameof(FullGoldens))]
    public void Bytes_match_golden(string name)
    {
        byte[] golden = GoldenBytes(name);
        byte[] actual = new GoLabel.GoLabelDriver().Build(File.OpenRead(Path.Combine(Golden.Root, name, "template.ezpx")), LoadValues(name));
        actual.SequenceEqual(golden).ShouldBeTrue($"{name}: байты не равны эталону — {EzplDump.FirstByteDifference(golden, actual)}");
    }

    [Fact]
    public void Serial_counter_steps_like_golabel()
    {
        EzplJob.UpdateSerialFormat("086,+1,SerialNumber", 1, 1, null, false).ShouldBe("087,+1,SerialNumber");
        EzplJob.UpdateSerialFormat("33100816,+1,MMM", 1, 0, null, false).ShouldBe("33100817,+1,MMM");
        EzplJob.UpdateSerialFormat("0099,+1,SN", 1, 0, null, false).ShouldBe(" 100,+1,SN");
        EzplJob.UpdateSerialFormat("A0F,+1,hex", 1, 1, null, false).ShouldBe("A10,+1,hex");
    }

    /// <summary>Всё до первой наклейки: до «^L», а при печати из базы — до «^C» перед ней.</summary>
    private static byte[] HeaderOf(byte[] golden)
    {
        var commands = EzplDump.Split(golden);
        int index = commands.FindIndex(c => c.Text == "^L");
        index.ShouldBeGreaterThan(0, "в эталоне нет ^L");
        if (commands[index - 1].Text.StartsWith("^C", StringComparison.Ordinal)
            && commands.Take(index - 1).Any(c => c.Text.StartsWith("^C", StringComparison.Ordinal)))
        {
            index--;
        }
        return golden[..commands[index].Offset];
    }

    internal static EzpxTemplate LoadTemplate(string name) =>
        EzpxReader.Load(Path.Combine(Golden.Root, name, "template.ezpx"));

    internal static byte[] GoldenBytes(string name) => File.ReadAllBytes(Path.Combine(Golden.Root, name, "golabel.bin"));

    /// <summary>Значения -S/-V из первой строки args.txt; часы — дата съёма.</summary>
    internal static PrintValues LoadValues(string name)
    {
        string first = File.ReadLines(Path.Combine(Golden.Root, name, "args.txt"), Encoding.UTF8).First().TrimStart('﻿');
        string[] tokens = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var serials = new Dictionary<int, string>();
        var variables = new Dictionary<int, string>();
        for (int i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i].StartsWith("-S", StringComparison.OrdinalIgnoreCase) && int.TryParse(tokens[i][2..], out int s))
            {
                serials[s] = tokens[++i];
            }
            else if (tokens[i].StartsWith("-V", StringComparison.OrdinalIgnoreCase) && int.TryParse(tokens[i][2..], out int v))
            {
                variables[v] = tokens[++i];
            }
        }
        return new PrintValues { Serials = serials, Variables = variables, Clock = new FixedClock(CaptureTime) };
    }

    private static TheoryData<string> Data(IEnumerable<string> names)
    {
        var data = new TheoryData<string>();
        foreach (var name in names)
        {
            data.Add(name);
        }
        if (data.Count == 0)
        {
            data.Add("(нет)");
        }
        return data;
    }

    /// <summary>Вместо растра — метка Q*: сверяются только команды.</summary>
    private sealed class MaskingRasterRenderer : IRasterRenderer
    {
        public void Render(RasterRequest request, EzplWriter output) => output.WriteLine(EzplDump.MaskedRaster);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("стенд", now.Offset, "стенд", "стенд");
    }
}
