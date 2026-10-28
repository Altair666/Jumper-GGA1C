namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Настройки подключения к Keycloak. Секрет клиента наружу не отдаётся: вместо значения
/// приходит маска той же длины (см. <see cref="GGA1C.Sync.Secrets.SecretMask"/>), поэтому
/// видно, что секрет есть и какой он длины. При сохранении: одни точки — оставить как было,
/// пусто — удалить, иное — новое значение.
/// </summary>
public class KeycloakSettingsDto
{
    /// <summary>Базовый адрес без /realms, например http://192.168.88.238:8080</summary>
    public string BaseUrl { get; set; } = "";

    public string Realm { get; set; } = "";

    /// <summary>
    /// Клиент со включённой служебной учётной записью. Ему нужна роль view-users
    /// из realm-management, иначе список пользователей вернёт 403.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>Маска сохранённого секрета при чтении; что ввели — при сохранении.</summary>
    /// <summary>
    /// Публичный клиент, которым виджет доказывает, кто перед ним. Секрета у него нет и
    /// доступа ни к чему он не даёт — только подписанное удостоверение человека.
    /// </summary>
    public string WidgetClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public bool HasClientSecret { get; set; }

    /// <summary>
    /// Клиент входа людей в сам хаб (confidential). Применяется после перезапуска хаба:
    /// схема входа собирается при старте.
    /// </summary>
    public string LoginClientId { get; set; } = "";

    /// <summary>Маска секрета клиента входа — по тем же правилам, что и <see cref="ClientSecret"/>.</summary>
    public string LoginClientSecret { get; set; } = "";

    public bool HasLoginClientSecret { get; set; }
}
