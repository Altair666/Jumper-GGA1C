using System;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Labels;

/// <summary>
/// БАГ-1.1: решение «греть кэш паролей печати или нет» по Uptime MySQL. Ошибка здесь — либо
/// GoLabel не входит после перезапуска MySQL, либо хаб входит всеми логинами каждые 2 минуты.
/// </summary>
public class MySqlRestartDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static UptimeReading At(long uptime, double minutes) => new(uptime, T0.AddMinutes(minutes));

    [Fact]
    public void Первый_тик_после_старта_хаба_греет()
    {
        MySqlRestartDetector.ShouldWarm(null, At(86_400, 0)).ShouldBeTrue();
    }

    [Fact]
    public void Сервер_работает_дальше_не_греет()
    {
        MySqlRestartDetector.ShouldWarm(At(1000, 0), At(1120, 2)).ShouldBeFalse();
    }

    [Fact]
    public void Тик_задержался_не_греет()
    {
        // Тик пришёл через 5 мин вместо 2 — Uptime вырос на те же 5 мин.
        MySqlRestartDetector.ShouldWarm(At(1000, 0), At(1300, 5)).ShouldBeFalse();
    }

    [Fact]
    public void Uptime_меньше_прежнего_греет()
    {
        MySqlRestartDetector.ShouldWarm(At(1000, 0), At(30, 2)).ShouldBeTrue();
    }

    [Fact]
    public void Перезапуск_после_которого_Uptime_больше_прежнего_греет()
    {
        // Прошлый тик — через 10 с после старта MySQL; потом перезапуск, и к следующему тику
        // (через 2 мин) сервер работает уже 60 с: Uptime больше прежнего, но сервер новый.
        MySqlRestartDetector.ShouldWarm(At(10, 0), At(60, 2)).ShouldBeTrue();
    }

    [Fact]
    public void Секунда_округления_не_считается_перезапуском()
    {
        MySqlRestartDetector.ShouldWarm(At(1000, 0), At(1118, 2)).ShouldBeFalse();
    }
}
