using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Printing;

/// <summary>
/// Где лежат эталоны «.ezpx → байты GoLabel». Они вне репозитория (решение пользователя):
/// путь — переменная окружения <c>GGA1C_GOLDEN_DIR</c>, по умолчанию <c>C:\GGA1C\golden</c>.
/// Нет папки или эталонов — тесты эталонов пропускаются, а не падают.
/// </summary>
public static class Golden
{
    public const string EnvironmentVariable = "GGA1C_GOLDEN_DIR";
    public const string DefaultRoot = @"C:\GGA1C\golden";

    public static string Root =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } dir ? dir : DefaultRoot;

    public static IReadOnlyList<string> Names =>
        Directory.Exists(Root)
            ? Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
            : [];

    public static string? SkipReason =>
        !Directory.Exists(Root)
            ? $"Эталонов нет: папка {Root} не найдена (задать {EnvironmentVariable}, см. README.md тестов печати)."
            : Names.Count == 0
                ? $"Эталонов нет: папка {Root} пуста."
                : null;
}

/// <summary>Theory по эталонам; пропускается с понятным сообщением, если эталонов нет.</summary>
public sealed class GoldenTheoryAttribute : TheoryAttribute
{
    public GoldenTheoryAttribute()
    {
        Skip = Golden.SkipReason;
    }
}

/// <summary>
/// Проверяет раскладку эталонов (README.md): у каждого есть шаблон и байты GoLabel, а учётные
/// данные базы из шаблона вычищены.
/// </summary>
public class GoldenFolderTests
{
    public static TheoryData<string> GoldenNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Golden.Names)
        {
            data.Add(name);
        }

        // Пустой TheoryData xunit считает ошибкой; при пустой папке тест всё равно пропущен.
        if (data.Count == 0)
        {
            data.Add("(нет)");
        }
        return data;
    }

    [GoldenTheory]
    [MemberData(nameof(GoldenNames))]
    public void Golden_has_template_and_bytes(string name)
    {
        var dir = Path.Combine(Golden.Root, name);
        File.Exists(Path.Combine(dir, "template.ezpx")).ShouldBeTrue();
        File.Exists(Path.Combine(dir, "args.txt")).ShouldBeTrue();
        new FileInfo(Path.Combine(dir, "golabel.bin")).Length.ShouldBeGreaterThan(0);
    }

    [GoldenTheory]
    [MemberData(nameof(GoldenNames))]
    public void Golden_template_has_no_database_credentials(string name)
    {
        var doc = XDocument.Load(Path.Combine(Golden.Root, name, "template.ezpx"));
        foreach (var field in new[] { "UserID", "Password" })
        {
            foreach (var element in doc.Descendants(field))
            {
                element.Value.ShouldBeEmpty($"{name}: поле {field} не вычищено");
            }
        }
    }
}
