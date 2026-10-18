using GGA1C.Sync.Secrets;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Secrets;

/// <summary>
/// Маска решает, что произойдёт с сохранённым секретом, поэтому разбор «точки или нет»
/// проверяется отдельно: ошибка здесь либо стирает чужой пароль при сохранении формы,
/// либо, наоборот, не даёт его удалить.
/// </summary>
public class SecretMaskTests
{
    [Theory]
    [InlineData("secret", 6)]
    [InlineData("a", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Маска_повторяет_длину_секрета(string? secret, int expectedLength)
    {
        var mask = SecretMask.For(secret);

        mask.Length.ShouldBe(expectedLength);
        mask.ShouldBe(new string('•', expectedLength));
    }

    [Theory]
    // поле не трогали - одни точки
    [InlineData("••••", true)]
    [InlineData("•", true)]
    // часть точек стёрли: нового значения всё равно нет, пароль не меняем
    [InlineData("••", true)]
    // пусто - человек стёр маску целиком, значит пароль нужно удалить
    [InlineData("", false)]
    [InlineData(null, false)]
    // настоящее значение
    [InlineData("1q2w3E*", false)]
    [InlineData("••••x", false)]
    public void Одни_точки_означают_не_менять(string? value, bool isMask)
    {
        SecretMask.IsMask(value).ShouldBe(isMask);
    }

    [Fact]
    public void Маска_сохранённого_секрета_распознаётся_как_маска()
    {
        // Ровно тот путь, которым значение ходит в интерфейс и обратно
        SecretMask.IsMask(SecretMask.For("ZvN8sQ-example-secret")).ShouldBeTrue();

        // А у пустого секрета маска пустая, и это уже «удалить», а не «не менять»
        SecretMask.IsMask(SecretMask.For("")).ShouldBeFalse();
    }
}
