using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Клиент Admin API Keycloak. Ходит служебной учётной записью самого клиента
/// (grant_type=client_credentials), то есть без чьего-либо пароля.
///
/// Чтобы это работало, у клиента в Keycloak должна быть включена служебная учётная запись
/// и выдана роль <c>view-users</c> из <c>realm-management</c>. Без роли токен выдаётся
/// нормально, а список пользователей отвечает 403 — ошибка выглядит непонятной, поэтому
/// разбираем её отдельно и говорим прямо, чего не хватает.
/// </summary>
public class KeycloakClient : ITransientDependency
{
    private readonly IHttpClientFactory _factory;
    private readonly ISettingProvider _settings;
    private readonly ILogger<KeycloakClient> _log;

    public KeycloakClient(
        IHttpClientFactory factory,
        ISettingProvider settings,
        ILogger<KeycloakClient> log)
    {
        _factory = factory;
        _settings = settings;
        _log = log;
    }

    /// <summary>Настроен ли Keycloak: без адреса, реалма и клиента синхронизировать нечего.</summary>
    public async Task<bool> IsConfiguredAsync()
    {
        return !string.IsNullOrWhiteSpace(await Get(SyncSettings.Keycloak.BaseUrl))
            && !string.IsNullOrWhiteSpace(await Get(SyncSettings.Keycloak.Realm))
            && !string.IsNullOrWhiteSpace(await Get(SyncSettings.Keycloak.ClientId));
    }

    /// <summary>
    /// Проверка связи: получаем токен и запрашиваем одного пользователя. Возвращает
    /// понятное человеку сообщение — этим же пользуется кнопка в интерфейсе.
    /// </summary>
    public async Task<(bool Ok, string Message)> TestAsync()
    {
        try
        {
            var token = await GetTokenAsync();
            var users = await GetUsersAsync(token, max: 1);

            return (true, $"Подключение успешно, токен получен, пользователи читаются " +
                          $"(в выборке: {users.Count})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string> GetTokenAsync()
    {
        var baseUrl = (await Get(SyncSettings.Keycloak.BaseUrl)).TrimEnd('/');
        var realm = await Get(SyncSettings.Keycloak.Realm);
        var clientId = await Get(SyncSettings.Keycloak.ClientId);
        var clientSecret = await Get(SyncSettings.Keycloak.ClientSecret);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(realm) ||
            string.IsNullOrWhiteSpace(clientId))
        {
            throw new BusinessException("GGA1C.Sync:KeycloakNotConfigured")
                .WithData("message", "Не заданы адрес Keycloak, реалм или клиент");
        }

        using var http = _factory.CreateClient("keycloak");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });

        var url = $"{baseUrl}/realms/{realm}/protocol/openid-connect/token";
        using var response = await http.PostAsync(url, form);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("GGA1C.Sync:KeycloakTokenFailed")
                .WithData("message",
                    $"Keycloak не выдал токен ({(int)response.StatusCode}). " +
                    Hint(response.StatusCode, body));
        }

        using var json = JsonDocument.Parse(body);

        if (!json.RootElement.TryGetProperty("access_token", out var tokenNode))
        {
            throw new BusinessException("GGA1C.Sync:KeycloakTokenFailed")
                .WithData("message", "В ответе Keycloak нет access_token");
        }

        return tokenNode.GetString() ?? "";
    }

    /// <summary>Список пользователей реалма.</summary>
    public async Task<List<KeycloakUser>> GetUsersAsync(string token, int max = 500)
    {
        var baseUrl = (await Get(SyncSettings.Keycloak.BaseUrl)).TrimEnd('/');
        var realm = await Get(SyncSettings.Keycloak.Realm);

        using var http = _factory.CreateClient("keycloak");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = $"{baseUrl}/admin/realms/{realm}/users?max={max}";
        using var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("GGA1C.Sync:KeycloakUsersFailed")
                .WithData("message",
                    $"Keycloak не отдал список пользователей ({(int)response.StatusCode}). " +
                    Hint(response.StatusCode, body));
        }

        var users = JsonSerializer.Deserialize<List<KeycloakUser>>(body, JsonOptions) ?? [];

        _log.LogInformation("Из Keycloak получено пользователей: {Count}", users.Count);

        return users;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Самые частые причины отказа Keycloak, переведённые в понятные указания. Без этого
    /// 403 на списке пользователей выглядит как «что-то не так с настройками», тогда как
    /// причина почти всегда одна — не выдана роль служебной учётке.
    /// </summary>
    private static string Hint(HttpStatusCode status, string body)
    {
        var shortBody = body.Length > 200 ? body[..200] : body;

        return status switch
        {
            HttpStatusCode.Unauthorized =>
                "Неверный клиент или секрет, либо у клиента не включена служебная учётная запись " +
                "(Service accounts roles).",
            HttpStatusCode.Forbidden =>
                "Токен получен, но прав не хватает. Служебной учётной записи клиента нужна роль " +
                "view-users из realm-management.",
            HttpStatusCode.NotFound =>
                "Не найден реалм или адрес. Базовый адрес указывается без /realms.",
            _ => shortBody
        };
    }

    private async Task<string> Get(string name)
    {
        return await _settings.GetOrNullAsync(name) ?? "";
    }
}
