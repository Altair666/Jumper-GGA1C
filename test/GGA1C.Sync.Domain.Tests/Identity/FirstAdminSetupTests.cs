using System.Threading.Tasks;
using GGA1C.Sync.Identity;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace GGA1C.Sync.Identity;

/// <summary>
/// Страница первого запуска: администратора тестовая база получает через
/// <see cref="FirstAdminSetup"/> (см. SyncTestDataSeedContributor), после этого второй
/// создать нельзя.
/// </summary>
public abstract class FirstAdminSetupTests<TStartupModule> : SyncDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly FirstAdminSetup _setup;
    private readonly IdentityUserManager _users;

    protected FirstAdminSetupTests()
    {
        _setup = GetRequiredService<FirstAdminSetup>();
        _users = GetRequiredService<IdentityUserManager>();
    }

    [Fact]
    public async Task Admin_Created_By_Setup_Is_In_Admin_Role()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var admins = await _users.GetUsersInRoleAsync(FirstAdminSetup.AdminRoleName);
            admins.ShouldContain(u => u.UserName == "admin");
        });
    }

    [Fact]
    public async Task Setup_Is_Closed_Once_Admin_Exists()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            (await _setup.IsOpenAsync()).ShouldBeFalse();
        });

        await Should.ThrowAsync<UserFriendlyException>(
            () => _setup.CreateAsync("second", "second@test.local", "1q2w3E*"));
    }
}
