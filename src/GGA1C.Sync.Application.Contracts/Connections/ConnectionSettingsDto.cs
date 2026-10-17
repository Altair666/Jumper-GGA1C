namespace GGA1C.Sync.Connections;

/// <summary>
/// Настройки подключений к Altium Enterprise Server и 1С.
///
/// Пароли и куки в этом объекте НЕ передаются из сервиса в интерфейс: при чтении они
/// приходят пустыми, а вместо значения выставляется флаг HasXxx — «что-то сохранено».
/// Так секрет не уезжает в браузер только ради того, чтобы нарисовать звёздочки в поле.
/// При сохранении пустое поле означает «оставить как было», а не «затереть».
/// </summary>
public class ConnectionSettingsDto
{
    // ── Altium Enterprise Server ──────────────────────────────────────────────

    public string AltiumServerUrl { get; set; } = "";
    public string AltiumUser { get; set; } = "";
    public string AltiumPassword { get; set; } = "";
    public bool HasAltiumPassword { get; set; }

    /// <summary>
    /// Заголовок Cookie из DevTools браузера. Временный обход нерешённой проблемы
    /// пагинации компонентов: с программной сессией Altium отдаёт первые 50 записей,
    /// игнорируя tag и start, а с настоящими куками браузера отвечает правильно.
    /// Живёт около суток, пока жива сессия ASP.NET на стороне Altium.
    /// </summary>
    public string AltiumBrowserCookies { get; set; } = "";
    public bool HasAltiumBrowserCookies { get; set; }

    public string AltiumApiComponents { get; set; } = "";
    public string AltiumApiFolders { get; set; } = "";
    public string AltiumApiProjects { get; set; } = "";
    public string AltiumLifecycleFilter { get; set; } = "";

    // ── 1С ────────────────────────────────────────────────────────────────────

    public string OnecServerUrl { get; set; } = "";
    public string OnecBaseName { get; set; } = "";
    public string OnecUser { get; set; } = "";
    public string OnecPassword { get; set; } = "";
    public bool HasOnecPassword { get; set; }

    // ── Синхронизация ─────────────────────────────────────────────────────────

    public bool AutoSyncEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Update | New | Skip</summary>
    public string DefaultSyncAction { get; set; } = "Skip";
}
