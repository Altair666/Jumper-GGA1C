namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Настройки подключения к Keycloak. Секрет клиента, как и прочие секреты, не отдаётся
/// в интерфейс: при чтении приходит пустым, а вместо значения — флаг «сохранено».
/// Пустое поле при сохранении означает «оставить как было».
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

    public string ClientSecret { get; set; } = "";

    public bool HasClientSecret { get; set; }
}
