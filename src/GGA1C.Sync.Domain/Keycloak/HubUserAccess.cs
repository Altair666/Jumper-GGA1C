using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Держит пользователя хаба (ABP Identity), вошедшего через Keycloak, в одном состоянии с
/// его учёткой <see cref="Labels.LabelClient"/>.
///
/// Связь — запись внешнего входа в <c>AbpUserLogins</c>: провайдер
/// <see cref="LoginProvider"/>, ключ — <c>sub</c> из Keycloak, тот же, что в
/// <c>LabelClient.KeycloakSub</c>. Отдельного столбца не нужно.
///
/// Отключили учётку (сверкой или руками) — пользователь становится неактивным, и ему
/// меняется штамп безопасности. Неактивного ABP не пускает при входе
/// (<c>AbpSignInManager.PreSignInCheck</c>), а смена штампа гасит уже открытую куку при
/// ближайшей проверке (<c>SecurityStampValidatorOptions.ValidationInterval</c>).
/// </summary>
public class HubUserAccess : ITransientDependency
{
    /// <summary>Имя провайдера внешнего входа; совпадает с именем схемы OIDC в хабе.</summary>
    public const string LoginProvider = "Keycloak";

    private readonly IdentityUserManager _users;
    private readonly ILogger<HubUserAccess> _log;

    public HubUserAccess(IdentityUserManager users, ILogger<HubUserAccess> log)
    {
        _users = users;
        _log = log;
    }

    public Task<IdentityUser?> FindBySubAsync(string sub)
    {
        return _users.FindByLoginAsync(LoginProvider, sub);
    }

    /// <summary>
    /// Включает или выключает пользователя хаба, связанного с этим <c>sub</c>. Нет такого
    /// (человек ещё ни разу не входил в хаб) — делать нечего.
    /// </summary>
    public async Task SetActiveAsync(string? sub, bool active)
    {
        if (string.IsNullOrWhiteSpace(sub))
        {
            return;
        }

        var user = await FindBySubAsync(sub);
        if (user == null || user.IsActive == active)
        {
            return;
        }

        user.SetIsActive(active);

        // Смена штампа сохраняет пользователя целиком, вместе с IsActive.
        (await _users.UpdateSecurityStampAsync(user)).CheckErrors();

        _log.LogInformation(
            "Пользователь хаба {UserName} {State} вслед за учёткой",
            user.UserName, active ? "включён" : "отключён");
    }
}
