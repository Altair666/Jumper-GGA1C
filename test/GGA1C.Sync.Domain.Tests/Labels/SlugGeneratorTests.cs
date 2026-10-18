using GGA1C.Sync.Labels;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Имя папки-учётки попадает в логин MySQL (с приставкой <c>lbl_</c>), поэтому
/// транслитерация проверяется отдельно: ошибка здесь означает либо логин, который база не
/// примет, либо двух людей с одним логином.
/// </summary>
public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Иванов Пётр", "ivanov")]
    [InlineData("Иванов, участок сборки", "ivanov")]
    [InlineData("Щербаков", "shcherbakov")]
    [InlineData("Кузнецова Елена Сергеевна", "kuznetsova")]
    [InlineData("Volkov", "volkov")]
    [InlineData("  Морозов  ", "morozov")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("...", "")]
    public void Имя_папки_выводится_из_фамилии(string? name, string expected)
    {
        SlugGenerator.FromName(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("a.morozov@test.local", "amorozov")]
    [InlineData("tester@test.local", "tester")]
    [InlineData("p_ivanov@mp-lab.ru", "p_ivanov")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Имя_папки_выводится_из_email(string? email, string expected)
    {
        SlugGenerator.FromEmail(email).ShouldBe(expected);
    }

    [Theory]
    // фамилия в приоритете: логин lbl_ivanov понятнее тому, кто настраивает GoLabel
    [InlineData("Иванов Пётр", "p.ivanov@mp-lab.ru", "ivanov")]
    // из имени выжать нечего - берём email
    [InlineData("...", "p.ivanov@mp-lab.ru", "pivanov")]
    [InlineData("", "tester@test.local", "tester")]
    // нет ни того, ни другого - пусто, вызывающий обязан сказать об этом человеку
    [InlineData("", "", "")]
    public void Имя_папки_из_фамилии_или_почты(string? name, string? email, string expected)
    {
        SlugGenerator.FromNameOrEmail(name, email).ShouldBe(expected);
    }
}
