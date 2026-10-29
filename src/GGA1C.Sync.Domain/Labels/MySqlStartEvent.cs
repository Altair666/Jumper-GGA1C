using System;

namespace GGA1C.Sync.Labels;

/// <summary>
/// БАГ-1.2: как узнать о запуске службы MySQL без опроса. Service Control Manager пишет в журнал
/// System событие 7036; в EventData <c>param1</c> — отображаемое имя службы, <c>param2</c> —
/// состояние латиницей (<c>running</c>/<c>stopped</c>, не переводится — переведено только
/// сообщение). Проверено на стенде: запуски MySQL 18.10 и 25.10 находятся этим фильтром.
/// </summary>
public static class MySqlStartEvent
{
    public const string LogName = "System";

    /// <summary>Отображаемое имя службы MySQL на стенде (служба <c>MySQL</c>).</summary>
    public const string DefaultServiceDisplayName = "MySQL";

    /// <summary>XPath-фильтр для подписки на журнал System: «служба перешла в состояние "работает"».</summary>
    public static string XPath(string serviceDisplayName)
    {
        if (string.IsNullOrWhiteSpace(serviceDisplayName) || serviceDisplayName.IndexOfAny(['\'', '"', '<', '>', '&']) >= 0)
        {
            // В XPath журнала событий нет экранирования кавычек — такое имя не подставить.
            throw new ArgumentException("Имя службы MySQL пустое или с кавычками и угловыми скобками.", nameof(serviceDisplayName));
        }

        return "*[System[Provider[@Name='Service Control Manager'] and (EventID=7036)]]"
            + " and *[EventData[Data[@Name='param1']='" + serviceDisplayName.Trim() + "']"
            + " and EventData[Data[@Name='param2']='running']]";
    }
}
