using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GGA1C.Sync.Permissions;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Xunit;

namespace GGA1C.Sync.Ui;

/// <summary>Имя сервера в заголовках: настройка Sync.Ui.ServerName, меняет только Sync.Admin.</summary>
public abstract class UiSettingsTests<TStartupModule> : SyncApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IUiSettingsAppService _ui;
    private readonly ISettingManager _settingManager;

    protected UiSettingsTests()
    {
        _ui = GetRequiredService<IUiSettingsAppService>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    [Fact]
    public async Task Default_Is_Sync()
    {
        (await _ui.GetServerNameAsync()).ShouldBe(SyncSettings.Ui.DefaultServerName);
    }

    [Fact]
    public async Task Saves_Trimmed_Name_And_Empty_Removes_Row()
    {
        await _ui.SetServerNameAsync("  Хаб цеха  ");
        (await _ui.GetServerNameAsync()).ShouldBe("Хаб цеха");
        (await _settingManager.GetOrNullGlobalAsync(SyncSettings.Ui.ServerName)).ShouldBe("Хаб цеха");

        await _ui.SetServerNameAsync("   ");
        (await _ui.GetServerNameAsync()).ShouldBe(SyncSettings.Ui.DefaultServerName);
    }

    [Fact]
    public async Task Too_Long_Name_Is_Rejected()
    {
        await Should.ThrowAsync<UserFriendlyException>(
            () => _ui.SetServerNameAsync(new string('x', SyncSettings.Ui.MaxServerNameLength + 1)));
    }

    [Fact]
    public void Requires_Sync_Admin()
    {
        typeof(UiSettingsAppService).GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy).ShouldContain(SyncPermissions.Admin);
    }
}
