using System.Threading.Tasks;
using GGA1C.Sync.Blazor.Components.Settings;
using GGA1C.Sync.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.SettingManagement.Blazor.MudBlazor;

namespace GGA1C.Sync.Blazor.Settings;

/// <summary>
/// Вкладка «Хаб» в штатном «Администрирование → Настройки» ABP: имя сервера в заголовках.
/// Только для права Sync.Admin.
/// </summary>
public class HubSettingComponentContributor : ISettingComponentContributor
{
    public async Task ConfigureAsync(SettingComponentCreationContext context)
    {
        if (await CheckPermissionsAsync(context))
        {
            context.Groups.Add(new SettingComponentGroup("GGA1C.Sync.Hub", "Хаб", typeof(HubSettingGroup), order: 0));
        }
    }

    public async Task<bool> CheckPermissionsAsync(SettingComponentCreationContext context)
    {
        var authorization = context.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return await authorization.IsGrantedAsync(SyncPermissions.Admin);
    }
}
