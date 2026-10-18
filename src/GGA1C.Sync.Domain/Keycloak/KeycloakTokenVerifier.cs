using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Проверяет токен, которым виджет доказывает, кто перед ним.
///
/// Личность берётся у Keycloak, а не у Grist: Keycloak и так источник людей (учётки хаба
/// сверяются с ним по <c>sub</c>), и человек уже вошёл через него в сам Grist. В документе
/// Grist при этой схеме не нужно ни строчки: ни ключей, ни таблиц, ни прав на запись —
/// в бою их у хаба и не будет.
///
/// Виджет получает токен обычным способом для браузерного приложения — код с PKCE на
/// публичном клиенте. Публичный клиент не хранит секрета и сам по себе не даёт доступа
/// ни к чему: он лишь позволяет человеку предъявить подписанное Keycloak удостоверение.
/// Пароль человек вводит на странице Keycloak, виджет его не видит.
///
/// Хаб проверяет подпись токена открытыми ключами реалма, издателя, срок и то, что токен
/// выдан нашему клиенту. Подделать такой токен нельзя, не имея закрытого ключа Keycloak.
/// </summary>
public class KeycloakTokenVerifier : ISingletonDependency
{
    private const string DefaultWidgetClient = "gga1c-widget";

    private readonly IHttpClientFactory _factory;
    private readonly ISettingProvider _settings;
    private readonly ILogger<KeycloakTokenVerifier> _log;

    // Ключи подписи меняются редко, а проверять токен нужно на каждый запрос от виджета,
    // поэтому держим их в памяти и перечитываем, только если ключ с таким kid неизвестен.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private JsonWebKeySet? _keys;
    private string? _keysRealm;
    private DateTime _keysFetchedAt;

    public KeycloakTokenVerifier(
        IHttpClientFactory factory,
        ISettingProvider settings,
        ILogger<KeycloakTokenVerifier> log)
    {
        _factory = factory;
        _settings = settings;
        _log = log;
    }

    /// <summary>
    /// Кто предъявил токен. Возвращает <c>sub</c> (неизменный идентификатор человека в
    /// Keycloak) и почту, либо причину отказа — её увидит оператор в виджете.
    /// </summary>
    public async Task<(string? Sub, string? Email, string? Error)> VerifyAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, null, "Токен Keycloak не передан");
        }

        var baseUrl = (await Get(SyncSettings.Keycloak.BaseUrl)).TrimEnd('/');
        var realm = await Get(SyncSettings.Keycloak.Realm);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(realm))
        {
            return (null, null, "В хабе не заданы адрес Keycloak и реалм");
        }

        var clientId = await Get(SyncSettings.Keycloak.WidgetClientId);
        clientId = string.IsNullOrWhiteSpace(clientId) ? DefaultWidgetClient : clientId.Trim();

        var issuer = $"{baseUrl}/realms/{realm}";

        try
        {
            var keys = await GetKeysAsync(baseUrl, realm, force: false);

            var parameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidateIssuer = true,
                IssuerSigningKeys = keys.GetSigningKeys(),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),

                // Аудиторию проверяем сами: у id_token она равна нашему клиенту, а у
                // access_token Keycloak ставит "account", поэтому штатная проверка отвергала
                // бы вполне законный токен. Нам важно другое - что токен выдан нашему
                // клиенту, а это видно по azp.
                ValidateAudience = false
            };

            var handler = new JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(token, parameters);

            if (!result.IsValid)
            {
                // Ключи могли смениться - перечитываем и пробуем ещё раз, но только один.
                keys = await GetKeysAsync(baseUrl, realm, force: true);
                parameters.IssuerSigningKeys = keys.GetSigningKeys();
                result = await handler.ValidateTokenAsync(token, parameters);
            }

            if (!result.IsValid)
            {
                _log.LogWarning(result.Exception, "Токен Keycloak не прошёл проверку");
                return (null, null, "Токен не прошёл проверку — возможно, он истёк");
            }

            var claims = result.ClaimsIdentity;
            var azp = claims.FindFirst("azp")?.Value;
            var audience = (result.SecurityToken as JsonWebToken)?.Audiences?.ToList() ?? [];

            if (!string.Equals(azp, clientId, StringComparison.Ordinal) &&
                !audience.Contains(clientId, StringComparer.Ordinal))
            {
                // Токен подписан нашим Keycloak, но выдан другому приложению. Принимать его
                // нельзя: иначе любой клиент реалма стал бы пропуском в наш API.
                return (null, null,
                    $"Токен выдан другому клиенту ({azp ?? "?"}), а ожидается {clientId}");
            }

            var sub = claims.FindFirst("sub")?.Value;
            var email = claims.FindFirst("email")?.Value;

            if (string.IsNullOrWhiteSpace(sub) && string.IsNullOrWhiteSpace(email))
            {
                return (null, null, "В токене нет ни sub, ни email — проверьте scope клиента");
            }

            return (sub, email, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Проверка токена Keycloak не удалась");
            return (null, null, $"Не удалось проверить токен: {ex.Message}");
        }
    }

    private async Task<JsonWebKeySet> GetKeysAsync(string baseUrl, string realm, bool force)
    {
        // Перечитываем принудительно, при смене реалма и раз в сутки - на случай, если
        // Keycloak обновил ключи, а ни один запрос на этом ещё не споткнулся.
        var stale = _keys == null
                 || _keysRealm != realm
                 || DateTime.UtcNow - _keysFetchedAt > TimeSpan.FromDays(1);

        if (!force && !stale)
        {
            return _keys!;
        }

        await _lock.WaitAsync();
        try
        {
            if (!force && _keys != null && _keysRealm == realm &&
                DateTime.UtcNow - _keysFetchedAt <= TimeSpan.FromDays(1))
            {
                return _keys;
            }

            using var http = _factory.CreateClient("keycloak");
            http.Timeout = TimeSpan.FromSeconds(15);

            var json = await http.GetStringAsync(
                $"{baseUrl}/realms/{realm}/protocol/openid-connect/certs");

            _keys = new JsonWebKeySet(json);
            _keysRealm = realm;
            _keysFetchedAt = DateTime.UtcNow;

            return _keys;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> Get(string name)
    {
        return await _settings.GetOrNullAsync(name) ?? "";
    }
}
