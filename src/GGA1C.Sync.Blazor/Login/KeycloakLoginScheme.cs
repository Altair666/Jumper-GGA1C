using System;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using GGA1C.Sync.Keycloak;

namespace GGA1C.Sync.Blazor.Login;

/// <summary>
/// Вход людей в хаб через Keycloak — схема OpenID Connect с именем
/// <see cref="HubUserAccess.LoginProvider"/>. Страница входа ABP показывает кнопку сама:
/// она перечисляет схемы с отображаемым именем.
///
/// Схема регистрируется при старте, а не в ConfigureServices, потому что адрес, реалм,
/// клиент и секрет живут в настройках ABP (в базе, секрет шифрованный), а базы на этапе
/// ConfigureServices ещё нет. Не задан клиент входа — схемы нет вовсе, и хаб работает как
/// раньше, с одним локальным входом. Будь схема зарегистрирована без настроек, проверка
/// параметров OIDC падала бы на каждом запросе. Правка настроек — после перезапуска.
/// </summary>
public static class KeycloakLoginScheme
{
    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<KeycloakLoginParameters>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, KeycloakOidcOptionsSetup>();

        // То, что делает AddOpenIdConnect, кроме регистрации самой схемы.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.TryAddTransient<OpenIdConnectHandler>();
    }

    public static async Task RegisterAsync(IServiceProvider rootServices)
    {
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KeycloakLoginScheme));
        var parameters = services.GetRequiredService<KeycloakLoginParameters>();

        // Настройки читаются из базы - нужна единица работы (на старте её никто не открыл).
        using (var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var settings = services.GetRequiredService<ISettingProvider>();
            var baseUrl = (await settings.GetOrNullAsync(SyncSettings.Keycloak.BaseUrl) ?? "").Trim().TrimEnd('/');
            var realm = (await settings.GetOrNullAsync(SyncSettings.Keycloak.Realm) ?? "").Trim();

            parameters.ClientId = (await settings.GetOrNullAsync(SyncSettings.Keycloak.LoginClientId) ?? "").Trim();
            parameters.ClientSecret = (await settings.GetOrNullAsync(SyncSettings.Keycloak.LoginClientSecret) ?? "").Trim();
            parameters.Authority = baseUrl.Length > 0 && realm.Length > 0 ? $"{baseUrl}/realms/{realm}" : "";

            await uow.CompleteAsync();
        }

        if (!parameters.IsConfigured)
        {
            log.LogInformation(
                "Вход через Keycloak выключен: не заданы адрес, реалм, клиент входа или его секрет " +
                "(страница /keycloak, раздел «Вход людей в хаб»)");
            return;
        }

        var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();
        if (await schemes.GetSchemeAsync(HubUserAccess.LoginProvider) == null)
        {
            schemes.AddScheme(new AuthenticationScheme(
                HubUserAccess.LoginProvider, "Keycloak", typeof(OpenIdConnectHandler)));
        }

        log.LogInformation("Вход через Keycloak включён: {Authority}, клиент {ClientId}",
            parameters.Authority, parameters.ClientId);
    }
}

/// <summary>Параметры схемы, прочитанные из настроек при старте.</summary>
public class KeycloakLoginParameters
{
    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public bool IsConfigured =>
        Authority.Length > 0 && ClientId.Length > 0 && ClientSecret.Length > 0;
}

public class KeycloakOidcOptionsSetup : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private readonly KeycloakLoginParameters _parameters;

    public KeycloakOidcOptionsSetup(KeycloakLoginParameters parameters)
    {
        _parameters = parameters;
    }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != HubUserAccess.LoginProvider)
        {
            return;
        }

        options.Authority = _parameters.Authority;
        options.ClientId = _parameters.ClientId;
        options.ClientSecret = _parameters.ClientSecret;

        // Код + PKCE, клиент confidential: токены идут сервер-сервер, в браузер не попадают.
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.GetClaimsFromUserInfoEndpoint = true;
        options.SaveTokens = false;

        // preferred_username штатно отбрасывается при разборе userinfo - забираем явно,
        // из него логин пользователя хаба.
        options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");

        // Итог входа - во внешнюю куку Identity: из неё страница входа ABP достаёт
        // ExternalLoginInfo (провайдер и sub), а сама кука хаба выдаётся уже нами.
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.CallbackPath = "/signin-oidc";
    }

    public void Configure(OpenIdConnectOptions options)
    {
        Configure(Options.DefaultName, options);
    }
}
