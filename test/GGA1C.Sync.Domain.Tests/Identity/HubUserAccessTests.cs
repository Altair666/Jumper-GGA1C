using System;
using System.Threading.Tasks;
using GGA1C.Sync.Keycloak;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace GGA1C.Sync.Identity;

/// <summary>
/// Отключение учётки гасит пользователя хаба, связанного с ней по sub: он становится
/// неактивным, и у него меняется штамп безопасности (по нему умирает открытая кука).
/// </summary>
public abstract class HubUserAccessTests<TStartupModule> : SyncDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly HubUserAccess _access;
    private readonly IdentityUserManager _users;

    protected HubUserAccessTests()
    {
        _access = GetRequiredService<HubUserAccess>();
        _users = GetRequiredService<IdentityUserManager>();
    }

    [Fact]
    public async Task Deactivates_And_Reactivates_Linked_User()
    {
        var sub = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            var user = new IdentityUser(id, "kc.user", "kc.user@test.local");
            (await _users.CreateAsync(user)).CheckErrors();
            (await _users.AddLoginAsync(user,
                new UserLoginInfo(HubUserAccess.LoginProvider, sub, "Keycloak"))).CheckErrors();
        });

        string? stampBefore = null;
        await WithUnitOfWorkAsync(async () =>
        {
            stampBefore = (await _users.GetByIdAsync(id)).SecurityStamp;
            await _access.SetActiveAsync(sub, false);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _users.GetByIdAsync(id);
            user.IsActive.ShouldBeFalse();
            user.SecurityStamp.ShouldNotBe(stampBefore);

            await _access.SetActiveAsync(sub, true);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            (await _users.GetByIdAsync(id)).IsActive.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Unknown_Sub_Is_Ignored()
    {
        await WithUnitOfWorkAsync(() => _access.SetActiveAsync(Guid.NewGuid().ToString(), false));
        await WithUnitOfWorkAsync(() => _access.SetActiveAsync(null, false));
    }
}
