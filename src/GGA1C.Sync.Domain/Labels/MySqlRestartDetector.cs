using System;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Решает по <c>Uptime</c> MySQL, перезапускался ли сервер с прошлого тика работника печати
/// (БАГ-1.1): после перезапуска кэш паролей <c>caching_sha2_password</c> пуст, и GoLabel II
/// не войдёт, пока хаб снова не положит пароли (<see cref="PrintLoginCacheWarmer"/>).
///
/// Сравнивается не «Uptime меньше прежнего», а момент запуска сервера: прежний Uptime плюс
/// прошедшее время. Иначе пропустился бы перезапуск, после которого сервер успел проработать
/// дольше, чем было на прошлом тике (прошлый тик — через 10 с после старта, следующий — через
/// 2 мин после нового перезапуска). Первый тик (прежнего нет — хаб только что запущен) тоже
/// «греть»: старт хаба — частный случай того же механизма.
/// </summary>
public static class MySqlRestartDetector
{
    /// <summary>Запас на неточность часов и округление Uptime до секунды.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    public static bool ShouldWarm(UptimeReading? previous, UptimeReading current)
    {
        if (previous is null)
        {
            return true;
        }

        var elapsed = current.ReadAt - previous.Value.ReadAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var expected = TimeSpan.FromSeconds(previous.Value.UptimeSeconds) + elapsed;
        return TimeSpan.FromSeconds(current.UptimeSeconds) + Slack < expected;
    }
}

/// <summary>Uptime MySQL (секунды) и когда он прочитан.</summary>
public readonly record struct UptimeReading(long UptimeSeconds, DateTimeOffset ReadAt);
