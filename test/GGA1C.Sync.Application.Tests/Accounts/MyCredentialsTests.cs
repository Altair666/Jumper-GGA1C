using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using GGA1C.Sync.Keycloak;
using GGA1C.Sync.Labels;
using GGA1C.Sync.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace GGA1C.Sync.Accounts;

/// <summary>
/// «Мои учётные данные»: человек видит и правит только свою учётку — ту, что связана с ним
/// по sub (AbpUserLogins, провайдер Keycloak). Чужой id в запросе ничего не меняет.
/// </summary>
public abstract class MyCredentialsTests<TStartupModule> : SyncApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly FakePrintLoginService _printLogins = new();

    private readonly IMyAccountAppService _me;
    private readonly ILabelClientStore _clients;
    private readonly IdentityUserManager _users;
    private readonly ICurrentPrincipalAccessor _principal;

    protected MyCredentialsTests()
    {
        _me = GetRequiredService<IMyAccountAppService>();
        _clients = GetRequiredService<ILabelClientStore>();
        _users = GetRequiredService<IdentityUserManager>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    protected override void AfterAddApplication(IServiceCollection services)
    {
        // Настоящий сервис делает ALTER USER в MySQL - в тестах только записываем вызовы.
        services.Replace(ServiceDescriptor.Singleton<IPrintLoginService>(_printLogins));
    }

    [Fact]
    public async Task Sees_Only_Own_Credentials()
    {
        var (alice, aliceClient, _, bobClient) = await SeedTwoPeopleAsync();

        using (As(alice))
        {
            var creds = await _me.GetCredentialsAsync();

            creds.Id.ShouldBe(aliceClient);
            creds.Id.ShouldNotBe(bobClient);
            creds.AltiumLogin.ShouldBe("alice.altium");
            creds.AltiumPassword.ShouldBe(new string('•', "alice-secret".Length));

            (await _me.RevealPrintPasswordAsync()).ShouldBe("alice-print");
        }
    }

    [Fact]
    public async Task Foreign_Id_In_Request_Changes_Only_Own_Account()
    {
        var (alice, aliceClient, _, bobClient) = await SeedTwoPeopleAsync();

        using (As(alice))
        {
            // Подставили id чужой учётки - сервис его не читает.
            await _me.SaveCredentialsAsync(new AccountCredentialsDto
            {
                Id = bobClient,
                AltiumLogin = "hacked",
                AltiumPassword = "hacked-pass",
                PdmLogin = "",
                PdmPassword = "",
                PrintPassword = "new-print"
            });
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var bob = await _clients.RevealCredentialsAsync(bobClient);
            bob.AltiumLogin.ShouldBe("bob.altium");
            bob.AltiumPassword.ShouldBe("bob-secret");
            (await _clients.RevealPrintPasswordAsync(bobClient)).ShouldBe("bob-print");

            var alice2 = await _clients.RevealCredentialsAsync(aliceClient);
            alice2.AltiumLogin.ShouldBe("hacked");
            alice2.AltiumPassword.ShouldBe("hacked-pass");
            alice2.PdmLogin.ShouldBeNull();
            (await _clients.RevealPrintPasswordAsync(aliceClient)).ShouldBe("new-print");
        });

        // Логин печати (ALTER USER + прогрев кэша) менялся только у своей учётки.
        _printLogins.Ensured.ShouldBe(new[] { ("alice", "new-print") });
    }

    [Fact]
    public async Task Mask_Keeps_Passwords_And_Print_Password()
    {
        var (alice, aliceClient, _, _) = await SeedTwoPeopleAsync();

        using (As(alice))
        {
            var creds = await _me.GetCredentialsAsync();
            creds.AltiumLogin = "alice.new";

            // Маски не трогали - пароли прежние, ALTER USER не нужен.
            await _me.SaveCredentialsAsync(creds);
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var saved = await _clients.RevealCredentialsAsync(aliceClient);
            saved.AltiumLogin.ShouldBe("alice.new");
            saved.AltiumPassword.ShouldBe("alice-secret");
            (await _clients.RevealPrintPasswordAsync(aliceClient)).ShouldBe("alice-print");
        });

        _printLogins.Ensured.ShouldBeEmpty();
    }

    /// <summary>П7.2: учётка 1С — те же правила секретов, что у Altium и PDM.</summary>
    [Fact]
    public async Task OneC_Credentials_Follow_Secret_Rules()
    {
        var (alice, aliceClient, _, _) = await SeedTwoPeopleAsync();

        using (As(alice))
        {
            var creds = await _me.GetCredentialsAsync();
            creds.OneCLogin = "Морозов А.";
            creds.OneCPassword = "onec-secret";
            await _me.SaveCredentialsAsync(creds);

            var shown = await _me.GetCredentialsAsync();
            shown.OneCLogin.ShouldBe("Морозов А.");
            shown.OneCPassword.ShouldBe(new string('•', "onec-secret".Length));
            shown.HasOneCPassword.ShouldBeTrue();

            // Маску не трогали — пароль прежний; Altium при этом не пострадал.
            await _me.SaveCredentialsAsync(shown);
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var saved = await _clients.RevealCredentialsAsync(aliceClient);
            saved.OneCLogin.ShouldBe("Морозов А.");
            saved.OneCPassword.ShouldBe("onec-secret");
            saved.AltiumPassword.ShouldBe("alice-secret");
        });

        using (As(alice))
        {
            var creds = await _me.GetCredentialsAsync();
            creds.OneCPassword = "";
            await _me.SaveCredentialsAsync(creds);
        }

        await WithUnitOfWorkAsync(async () =>
            (await _clients.RevealCredentialsAsync(aliceClient)).OneCPassword.ShouldBeNull());
    }

    [Fact]
    public async Task Local_User_Without_Keycloak_Gets_Clear_Error()
    {
        var local = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            (await _users.CreateAsync(new IdentityUser(local, "local.user", "local@test.local")))
                .CheckErrors();
        });

        using (As(local))
        {
            var me = await _me.GetAsync();
            me.ViaKeycloak.ShouldBeFalse();

            await Should.ThrowAsync<UserFriendlyException>(() => _me.GetCredentialsAsync());
            await Should.ThrowAsync<UserFriendlyException>(() => _me.RevealPrintPasswordAsync());
            await Should.ThrowAsync<UserFriendlyException>(() =>
                _me.SaveCredentialsAsync(new AccountCredentialsDto { Id = 1, AltiumLogin = "x" }));
        }
    }

    [Fact]
    public async Task Keycloak_User_Without_Account_Gets_Clear_Error()
    {
        var orphan = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var user = new IdentityUser(orphan, "kc.orphan", "orphan@test.local");
            (await _users.CreateAsync(user)).CheckErrors();
            (await _users.AddLoginAsync(user,
                new UserLoginInfo(HubUserAccess.LoginProvider, Guid.NewGuid().ToString(), "Keycloak")))
                .CheckErrors();
        });

        using (As(orphan))
        {
            await Should.ThrowAsync<UserFriendlyException>(() => _me.GetCredentialsAsync());
        }
    }

    [Fact]
    public void Own_Methods_Need_Login_Not_Admin_And_Take_No_Id()
    {
        // Право проверяет ABP по атрибуту на классе реализации (в тестах авторизация
        // всегда разрешена, поэтому проверяем сам атрибут).
        var mine = typeof(MyAccountAppService).GetCustomAttributes<AuthorizeAttribute>().ToList();
        mine.ShouldHaveSingleItem().Policy.ShouldBeNull();

        typeof(LabelClientsAppService).GetCustomAttributes<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == SyncPermissions.Admin);

        typeof(IMyAccountAppService).GetMethod(nameof(IMyAccountAppService.GetCredentialsAsync))!
            .GetParameters().ShouldBeEmpty();
        typeof(IMyAccountAppService).GetMethod(nameof(IMyAccountAppService.RevealPrintPasswordAsync))!
            .GetParameters().ShouldBeEmpty();
    }

    // ── Подготовка ────────────────────────────────────────────────────────────

    /// <summary>Два человека из Keycloak, у каждого своя учётка с паролями.</summary>
    private async Task<(Guid Alice, int AliceClient, Guid Bob, int BobClient)> SeedTwoPeopleAsync()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var aliceClient = await CreatePersonAsync(alice, "alice", "alice.altium", "alice-secret", "alice-print");
        var bobClient = await CreatePersonAsync(bob, "bob", "bob.altium", "bob-secret", "bob-print");
        return (alice, aliceClient, bob, bobClient);
    }

    private async Task<int> CreatePersonAsync(
        Guid userId, string name, string altiumLogin, string altiumPassword, string printPassword)
    {
        var sub = Guid.NewGuid().ToString();
        var slug = name + Guid.NewGuid().ToString("N")[..6];
        var id = 0;

        await WithUnitOfWorkAsync(async () =>
        {
            var user = new IdentityUser(userId, "kc." + slug, slug + "@test.local");
            (await _users.CreateAsync(user)).CheckErrors();
            (await _users.AddLoginAsync(user,
                new UserLoginInfo(HubUserAccess.LoginProvider, sub, "Keycloak"))).CheckErrors();

            var (client, _) = await _clients.CreateAsync(name, slug);
            client.KeycloakSub = sub;
            client.Email = slug + "@test.local";
            await _clients.UpdateAsync(client);

            await _clients.SetCredentialsAsync(client.Id,
                new AccountCredentials(altiumLogin, altiumPassword, null, null));
            await _clients.SetPrintCredentialsAsync(client.Id, slug + "@test.local", printPassword);

            id = client.Id;
        });

        _printLogins.SlugNames[slug] = name;
        return id;
    }

    private IDisposable As(Guid userId)
    {
        return _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString())
        }, "Test")));
    }

    private class FakePrintLoginService : IPrintLoginService
    {
        /// <summary>Папка → короткое имя человека, чтобы в проверках не тащить случайный хвост.</summary>
        public Dictionary<string, string> SlugNames { get; } = new();

        public List<(string Name, string Password)> Ensured { get; } = new();

        public string LoginFor(string slug, string? email) => email ?? "lbl_" + slug;

        public Task<string> EnsureAsync(string slug, string? email, string password)
        {
            Ensured.Add((SlugNames.TryGetValue(slug, out var n) ? n : slug, password));
            return Task.FromResult(LoginFor(slug, email));
        }

        public string GeneratePassword() => "generated";

        public Task DropAsync(string? login) => Task.CompletedTask;

        public Task EnsureViewAsync() => Task.CompletedTask;

        public Task<bool> ExistsAsync(string? login) => Task.FromResult(true);

        public Task WarmUpAsync(string login, string password) => Task.CompletedTask;
    }
}
