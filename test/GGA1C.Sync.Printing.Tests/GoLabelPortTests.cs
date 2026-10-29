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
        // шаг 6: печать из базы (data.csv)
        "gkv3-box-var-mysql",
    ];

    /// <summary>
    /// Печать из базы без снимка данных (data.csv в папке эталона) не сверить; у
    /// gkv3-box-var-mysql снимок есть (4 строки my_labels, решение пользователя 5).
    /// </summary>
    public static readonly string[] FromDatabase = Golden.Names
        .Where(n => !File.Exists(Path.Combine(Golden.Root, n, "data.csv"))
            && File.ReadAllText(Path.Combine(Golden.Root, n, "template.ezpx")).Contains("<DataBaseFormat>MySQL</DataBaseFormat>"))
        .ToArray();

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

    /// <summary>Допуск для растров Image (решение пользователя, вариант «В»): точек на картинку.</summary>
    public const int ImageToleranceDots = 16;

    /// <summary>Допуск для растров Image: доля точек картинки.</summary>
    public const double ImageToleranceShare = 0.005;

    /// <summary>
    /// Зачёт с допуском (решение пользователя): строго байт в байт всё, кроме растров Q из
    /// объектов Image — у них совпадают заголовок Q, а точек мимо не больше 16 и не больше 0,5 %
    /// (64-битный gdiplus.dll уменьшает логотипы бикубикой иначе, чем 32-битный у GoLabel).
    /// Число точек мимо по каждой картинке пишется в вывод теста.
    /// </summary>
    /// <summary>
    /// Не проходят зачёт: строку шрифтом Impact (GdiWindowTextBox) 64-битный gdiplus.dll рисует
    /// иначе, чем 32-битный у GoLabel (PowerShell x86 — как эталон, x64 — как порт; 5888 точек).
    /// Допуск на текст пользователь не давал — открытый вопрос (p5-plan.md).
    /// </summary>
    public static readonly Dictionary<string, string> OpenGdiPlus64Text = new()
    {
        ["gkv-avto-product"] = "Q345,24,",
        ["gkv-avto-product-ru-korpus"] = "Q378,9,",
    };

    public static TheoryData<string> ToleranceGoldens() => Data(Golden.Names.Except(FromDatabase).Except(OpenGdiPlus64Text.Keys));

    public static TheoryData<string> OpenGoldens() => Data(Golden.Names.Intersect(OpenGdiPlus64Text.Keys));

    [GoldenTheory]
    [MemberData(nameof(ToleranceGoldens))]
    public void Bytes_match_golden_with_image_tolerance(string name) => CompareWithImageTolerance(name, ignoredRaster: null);

    /// <summary>
    /// Не зачёт, а страховка от регресса для <see cref="OpenGdiPlus64Text"/>: то же сравнение,
    /// но растр строки Impact пропускается (его число точек мимо — в выводе теста).
    /// </summary>
    [GoldenTheory]
    [MemberData(nameof(OpenGoldens))]
    public void Open_gdiplus64_text_goldens_match_except_that_raster(string name) =>
        CompareWithImageTolerance(name, OpenGdiPlus64Text[name]);

    private void CompareWithImageTolerance(string name, string? ignoredRaster)
    {
        var golden = EzplDump.Split(GoldenBytes(name));
        var renderer = new TaggingRenderer(new GoLabel.Raster.GdiRasterRenderer());
        var job = new EzplJob(LoadTemplate(name), LoadValues(name), renderer);
        var actual = EzplDump.Split(job.Build());
        actual.Count.ShouldBe(golden.Count, $"{name}: число команд — {EzplDump.FirstDifference(golden.Select(c => c.Text).ToList(), actual.Select(c => c.Text).ToList())}");
        var failures = new List<string>();
        for (int i = 0; i < golden.Count; i++)
        {
            bool image = renderer.Types.TryGetValue(actual[i].Offset, out var type) && type == "Image";
            if (golden[i].Bytes.AsSpan().SequenceEqual(actual[i].Bytes))
            {
                if (image)
                {
                    _output.WriteLine($"{name} #{i} {golden[i].Text} (Image): 0 точек мимо");
                }
                continue;
            }
            if (ignoredRaster != null && golden[i].Text.StartsWith(ignoredRaster, StringComparison.Ordinal) && actual[i].Text == golden[i].Text
                && golden[i].Bytes.Length == actual[i].Bytes.Length)
            {
                _output.WriteLine($"{name} #{i} {golden[i].Text} ({type}): {golden[i].Bytes.Zip(actual[i].Bytes).Sum(p => System.Numerics.BitOperations.PopCount((uint)(p.First ^ p.Second)))} точек мимо — открытый вопрос 32/64 бит");
                continue;
            }
            if (image && golden[i].IsRaster && actual[i].Text == golden[i].Text && golden[i].Bytes.Length == actual[i].Bytes.Length)
            {
                int dots = golden[i].Bytes.Zip(actual[i].Bytes).Sum(p => System.Numerics.BitOperations.PopCount((uint)(p.First ^ p.Second)));
                string[] header = golden[i].Text[1..].Split(',');
                int total = int.Parse(header[2]) * 8 * int.Parse(header[3]);
                _output.WriteLine($"{name} #{i} {golden[i].Text} (Image): {dots} точек мимо из {total}");
                if (dots <= ImageToleranceDots && dots <= total * ImageToleranceShare)
                {
                    continue;
                }
                failures.Add($"#{i} {golden[i].Text} (Image): {dots} точек мимо — больше допуска");
                continue;
            }
            failures.Add($"#{i} ({type ?? "команда"}): эталон «{golden[i].Text}», порт «{actual[i].Text}»"
                + (golden[i].IsRaster && golden[i].Bytes.Length == actual[i].Bytes.Length
                    ? $", точек мимо {golden[i].Bytes.Zip(actual[i].Bytes).Sum(p => System.Numerics.BitOperations.PopCount((uint)(p.First ^ p.Second)))}"
                    : ""));
        }
        failures.ShouldBeEmpty($"{name}: расходится сверх допуска");
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public GoLabelPortTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Запоминает, какой объект нарисовал растр по какому смещению задания.</summary>
    private sealed class TaggingRenderer(IRasterRenderer inner) : IRasterRenderer
    {
        public Dictionary<long, string> Types { get; } = [];

        public void Render(RasterRequest request, EzplWriter output)
        {
            Types[output.Length] = request.Shape.TypeName;
            inner.Render(request, output);
        }
    }

    /// <summary>
    /// Шаг 7: задание не зависит от культуры потока — ru-RU (стенд), en-US и sv-SE (минус U+2212,
    /// запятая в дробях) дают те же байты.
    /// </summary>
    [GoldenTheory]
    [MemberData(nameof(CommandGoldens))]
    public void Bytes_do_not_depend_on_thread_culture(string name)
    {
        var reference = BuildWithRasters(name);
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "ru-RU", "en-US", "sv-SE" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                BuildWithRasters(name).SequenceEqual(reference).ShouldBeTrue($"{name}: другие байты при культуре {culture}");
                System.Globalization.CultureInfo.CurrentCulture.Name.ShouldBe(culture, "сборка должна вернуть культуру потока");
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>
    /// Шаг 7: общего состояния между заданиями нет — все эталоны, собранные одновременно по
    /// четыре раза в разных потоках, дают те же байты, что и по одному.
    /// </summary>
    [GoldenFact]
    public void Parallel_jobs_give_same_bytes()
    {
        var names = Golden.Names.Except(FromDatabase).ToList();
        var expected = names.ToDictionary(n => n, BuildWithRasters);
        var work = names.SelectMany(n => Enumerable.Repeat(n, 4)).ToList();
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Name, byte[] Bytes)>();
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n => results.Add((n, BuildWithRasters(n))));
        results.Count.ShouldBe(work.Count);
        foreach (var (n, bytes) in results)
        {
            bytes.SequenceEqual(expected[n]).ShouldBeTrue($"{n}: параллельная сборка дала другие байты");
        }
    }

    /// <summary>
    /// Отрицательное смещение сверху (~Q-5) при культуре sv-SE: её знак минуса — U+2212, а
    /// принтеру нужен ASCII «-». Без инвариантной культуры в EzplJob тест падает.
    /// </summary>
    [GoldenFact]
    public void Negative_numbers_use_ascii_minus_under_any_culture()
    {
        var template = LoadTemplate("sample2");
        template.Setup.TopMargin = -5;
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            string header = Encoding.UTF8.GetString(new EzplJob(template, LoadValues("sample2")).BuildHeader());
            header.ShouldContain("~Q-5\r\n");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    private static byte[] BuildWithRasters(string name) =>
        new EzplJob(LoadTemplate(name), LoadValues(name), new GoLabel.Raster.GdiRasterRenderer()).Build();

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
        // Счётчик без -S GoLabel спрашивает окном; при съёме в нём нажимали «ОК» — значение шаблона.
        return new PrintValues
        {
            Serials = serials,
            Variables = variables,
            SerialDefaultsFromTemplate = true,
            Records = LoadRecords(name),
            Clock = new FixedClock(CaptureTime),
        };
    }

    /// <summary>data.csv папки эталона: 1-я строка — имена столбцов, значения без запятых и кавычек.</summary>
    private static IReadOnlyList<IReadOnlyList<object?>>? LoadRecords(string name)
    {
        string path = Path.Combine(Golden.Root, name, "data.csv");
        if (!File.Exists(path))
        {
            return null;
        }
        return File.ReadLines(path, Encoding.UTF8).Skip(1).Where(line => line.Length > 0)
            .Select(line => (IReadOnlyList<object?>)line.Split(',').Select(v => (object?)v).ToList())
            .ToList();
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
