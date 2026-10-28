using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Keycloak;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace GGA1C.Sync.Accounts;

// Без отдельного права: себя видит любой вошедший. Наружу (HTTP) не публикуется — нужен
// только странице Blazor.
[RemoteService(IsEnabled = false)]
[Authorize]
public class MyAccountAppService : ApplicationService, IMyAccountAppService
{
    private readonly IdentityUserManager _users;
    private readonly ILabelClientStore _clients;
    private readonly AccountCredentialsEditor _credentials;
    private readonly IPrintLoginService _printLogins;

    public MyAccountAppService(
        IdentityUserManager users,
        ILabelClientStore clients,
        AccountCredentialsEditor credentials,
        IPrintLoginService printLogins)
    {
        _users = users;
        _clients = clients;
        _credentials = credentials;
        _printLogins = printLogins;
    }

    public async Task<MyAccountDto> GetAsync()
    {
        var user = await _users.GetByIdAsync(CurrentUser.GetId());

        var dto = new MyAccountDto
        {
            UserName = user.UserName ?? "",
            FullName = string.Join(" ", new[] { user.Name, user.Surname }
                .Where(s => !string.IsNullOrWhiteSpace(s))),
            Email = user.Email ?? ""
        };

        var sub = await FindMySubAsync(user);

        if (string.IsNullOrWhiteSpace(sub))
        {
            return dto;
        }

        dto.ViaKeycloak = true;

        var client = await _clients.FindBySubAsync(sub);
        if (client == null)
        {
            return dto;
        }

        dto.HasAccount = true;
        dto.AccountName = client.Name;
        dto.KeycloakUsername = client.KeycloakUsername ?? "";
        dto.Slug = client.Slug;
        dto.AccountEnabled = client.IsEnabled;
        dto.PrintLogin = client.PrintLogin ?? "";

        return dto;
    }

    // ── Мои учётные данные ────────────────────────────────────────────────────

    public async Task<AccountCredentialsDto> GetCredentialsAsync()
    {
        var client = await GetMyClientAsync();
        return await _credentials.GetAsync(client.Id);
    }

    public async Task SaveCredentialsAsync(AccountCredentialsDto input)
    {
        // Id учётки - только свой, из sub текущего пользователя; input.Id не смотрим.
        var client = await GetMyClientAsync();
        await _credentials.SaveAsync(client.Id, input);
    }

    public async Task<string?> RevealPrintPasswordAsync()
    {
        var client = await GetMyClientAsync();
        return await _clients.RevealPrintPasswordAsync(client.Id);
    }

    public string GeneratePrintPassword()
    {
        return _printLogins.GeneratePassword();
    }

    private async Task<string?> FindMySubAsync(IdentityUser user)
    {
        return (await _users.GetLoginsAsync(user))
            .FirstOrDefault(l => l.LoginProvider == HubUserAccess.LoginProvider)?.ProviderKey;
    }

    /// <summary>
    /// Учётка текущего пользователя: по записи AbpUserLogins (Keycloak, sub) - как при входе
    /// (П7). Ни логин, ни почта для поиска не годятся: их меняют.
    /// </summary>
    private async Task<LabelClient> GetMyClientAsync()
    {
        var user = await _users.GetByIdAsync(CurrentUser.GetId());
        var sub = await FindMySubAsync(user);

        if (string.IsNullOrWhiteSpace(sub))
        {
            throw new UserFriendlyException(
                "Вы вошли не через Keycloak, поэтому своей учётки с учётными данными у вас нет. " +
                "Учётные данные людей правятся на странице «Grist → GoLabel2».");
        }

        var client = await _clients.FindBySubAsync(sub);
        if (client == null)
        {
            throw new UserFriendlyException(
                "Ваша учётка в хабе не найдена: её заводит сверка с Keycloak. Обратитесь к " +
                "администратору хаба.");
        }

        return client;
    }
}
