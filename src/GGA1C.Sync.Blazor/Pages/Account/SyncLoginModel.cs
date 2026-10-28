using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GGA1C.Sync.Identity;
using GGA1C.Sync.Keycloak;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.OpenIddict;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace GGA1C.Sync.Blazor.Pages.Account;

/// <summary>
/// Страница входа ABP с нашей обработкой возврата из Keycloak. Разметку не трогаем —
/// подменяем только модель (ABP разрешает модель страницы через DI).
///
/// Почему не штатный колбэк ABP (проверено декомпиляцией 10.7): он ищет пользователя
/// хаба по email и молча привязывает к нему вход, а незнакомого отправляет на страницу
/// регистрации. У нас связь — только по <c>sub</c> (почту в Keycloak правят, а
/// <c>lmp.admin</c> вообще носит почту владельца), и регистрации нет.
///
/// Порядок: учётка из сверки (<see cref="KeycloakAccountSync.EnsureForLoginAsync"/>) →
/// она включена → пользователь хаба по <c>sub</c> (нет — создаём) → его можно впускать
/// (активен) → вход.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(LoginModel), typeof(OpenIddictSupportedLoginModel))]
public class SyncLoginModel : OpenIddictSupportedLoginModel
{
    private readonly FirstAdminSetup _setup;
    private readonly KeycloakAccountSync _accounts;
    private readonly HubUserAccess _hubUsers;

    public SyncLoginModel(
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IOptions<IdentityOptions> identityOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        AbpOpenIddictRequestHelper openIddictRequestHelper,
        IWebHostEnvironment webHostEnvironment,
        FirstAdminSetup setup,
        KeycloakAccountSync accounts,
        HubUserAccess hubUsers)
        : base(schemeProvider, accountOptions, identityOptions,
            identityDynamicClaimsPrincipalContributorCache, openIddictRequestHelper, webHostEnvironment)
    {
        _setup = setup;
        _accounts = accounts;
        _hubUsers = hubUsers;
    }

    /// <summary>Пока администратора нет, входить некому — ведём на страницу первого запуска.</summary>
    public override async Task<IActionResult> OnGetAsync()
    {
        if (await _setup.IsOpenAsync())
        {
            return Redirect("~/setup");
        }

        return await base.OnGetAsync();
    }

    public override async Task<IActionResult> OnGetExternalLoginCallbackAsync(
        string returnUrl = "", string returnUrlHash = "", string remoteError = null!)
    {
        if (remoteError != null)
        {
            Logger.LogWarning("Keycloak вернул ошибку входа: {Error}", remoteError);
            return await RefuseAsync($"Keycloak вернул ошибку: {remoteError}");
        }

        await IdentityOptions.SetAsync();

        var info = await SignInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            Logger.LogWarning("Вход через Keycloak: нет данных внешнего входа (кука истекла?)");
            return await RefuseAsync("Вход через Keycloak не завершён — попробуйте ещё раз.");
        }

        if (info.LoginProvider != HubUserAccess.LoginProvider)
        {
            return await base.OnGetExternalLoginCallbackAsync(returnUrl, returnUrlHash, remoteError);
        }

        var principal = info.Principal;
        var kcUser = new KeycloakUser
        {
            Id = info.ProviderKey,
            Username = Claim(principal, "preferred_username") ?? info.ProviderKey,
            Email = Claim(principal, ClaimTypes.Email, "email"),
            FirstName = Claim(principal, ClaimTypes.GivenName, "given_name"),
            LastName = Claim(principal, ClaimTypes.Surname, "family_name"),
            Enabled = true
        };

        var (client, problem) = await _accounts.EnsureForLoginAsync(kcUser);
        if (client == null)
        {
            return await RefuseAsync($"Учётку в хабе не удалось завести: {problem}");
        }

        if (!client.IsEnabled)
        {
            Logger.LogWarning("Вход через Keycloak отклонён: учётка {Name} отключена", client.Name);
            return await RefuseAsync("Ваша учётка в хабе отключена. Обратитесь к администратору.");
        }

        var user = await _hubUsers.FindBySubAsync(kcUser.Id);
        if (user == null)
        {
            var (created, error) = await CreateUserAsync(kcUser);
            if (created == null)
            {
                return await RefuseAsync(error!);
            }

            user = created;
        }
        else
        {
            await RefreshUserAsync(user, kcUser);
        }

        // Тот же отбор, что у штатного входа: неактивного (отключён вслед за учёткой или
        // руками в «Пользователях») не впускаем.
        var check = await ((AbpSignInManager)SignInManager).CallPreSignInCheckAsync(user);
        if (check != null && !check.Succeeded)
        {
            Logger.LogWarning("Вход через Keycloak отклонён: пользователь {UserName} неактивен", user.UserName);
            return await RefuseAsync("Вход в хаб для вас закрыт. Обратитесь к администратору.");
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        await SignInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);

        await IdentitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext
        {
            Identity = IdentitySecurityLogIdentityConsts.IdentityExternal,
            Action = IdentitySecurityLogActionConsts.LoginSucceeded,
            UserName = user.UserName
        });

        await IdentityDynamicClaimsPrincipalContributorCache.ClearAsync(user.Id, user.TenantId);

        return await RedirectSafelyAsync(returnUrl, returnUrlHash);
    }

    private async Task<(IdentityUser? User, string? Error)> CreateUserAsync(KeycloakUser kc)
    {
        if (string.IsNullOrWhiteSpace(kc.Email))
        {
            return (null, "В Keycloak у вас не указана почта — без неё учётку в хабе не завести.");
        }

        // Совпадение логина или почты с уже существующим пользователем хаба не считаем
        // поводом связать их: так чужая локальная учётка досталась бы вошедшему.
        if (await UserManager.FindByNameAsync(kc.Username) != null)
        {
            return (null, $"Логин {kc.Username} в хабе уже занят другой учёткой. Обратитесь к администратору.");
        }

        if (await UserManager.FindByEmailAsync(kc.Email) != null)
        {
            return (null, $"Почта {kc.Email} в хабе уже занята другой учёткой. Обратитесь к администратору.");
        }

        var user = new IdentityUser(GuidGenerator.Create(), kc.Username, kc.Email, CurrentTenant.Id)
        {
            Name = kc.FirstName,
            Surname = kc.LastName
        };

        CheckIdentityErrors(await UserManager.CreateAsync(user));
        CheckIdentityErrors(await UserManager.AddLoginAsync(user,
            new UserLoginInfo(HubUserAccess.LoginProvider, kc.Id, "Keycloak")));

        Logger.LogInformation("Вход через Keycloak: создан пользователь хаба {UserName}", user.UserName);
        return (user, null);
    }

    /// <summary>Имя и почту берём из Keycloak при каждом входе — там их и правят.</summary>
    private async Task RefreshUserAsync(IdentityUser user, KeycloakUser kc)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(kc.FirstName) && user.Name != kc.FirstName)
        {
            user.Name = kc.FirstName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(kc.LastName) && user.Surname != kc.LastName)
        {
            user.Surname = kc.LastName;
            changed = true;
        }

        if (changed)
        {
            CheckIdentityErrors(await UserManager.UpdateAsync(user));
        }

        if (!string.IsNullOrWhiteSpace(kc.Email) &&
            !string.Equals(user.Email, kc.Email, System.StringComparison.OrdinalIgnoreCase))
        {
            var result = await UserManager.SetEmailAsync(user, kc.Email);
            if (!result.Succeeded)
            {
                // Новая почта занята другой учёткой - входу это не мешает, только пишем.
                Logger.LogWarning("Почта {Email} для {UserName} не обновлена: {Errors}",
                    kc.Email, user.UserName, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private async Task<IActionResult> RefuseAsync(string message)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        Alerts.Danger(message);
        return await base.OnGetAsync();
    }

    private static string? Claim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
