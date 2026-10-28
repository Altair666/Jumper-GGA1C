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

    public MyAccountAppService(IdentityUserManager users, ILabelClientStore clients)
    {
        _users = users;
        _clients = clients;
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

        var sub = (await _users.GetLoginsAsync(user))
            .FirstOrDefault(l => l.LoginProvider == HubUserAccess.LoginProvider)?.ProviderKey;

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
}
