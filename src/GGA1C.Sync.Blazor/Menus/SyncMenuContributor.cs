using System.Threading.Tasks;
using GGA1C.Sync.Localization;
using GGA1C.Sync.MultiTenancy;
using GGA1C.Sync.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity.Blazor.MudBlazor;
using Volo.Abp.SettingManagement.Blazor.MudBlazor.Menus;
using Volo.Abp.TenantManagement.Blazor.MudBlazor.Navigation;
using Volo.Abp.UI.Navigation;

namespace GGA1C.Sync.Blazor.Menus;

public class SyncMenuContributor : IMenuContributor
{
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
    }

    private Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var administration = context.Menu.GetAdministration();
        var l = context.GetLocalizer<SyncResource>();

        context.Menu.Items.Insert(
            0,
            new ApplicationMenuItem(
                SyncMenus.Home,
                l["Menu:Home"],
                "/",
                icon: "fas fa-home",
                order: 0
            )
        );

        context.Menu.Items.Insert(
            1,
            new ApplicationMenuItem(
                SyncMenus.Labels,
                "Grist → GoLabel2",
                "/labels",
                icon: "fas fa-tags",
                order: 1,
                requiredPermissionName: SyncPermissions.Admin
            )
        );

        context.Menu.Items.Insert(
            2,
            new ApplicationMenuItem(
                SyncMenus.Altium1C,
                "Altium → 1С",
                "/altium-1c",
                icon: "fas fa-microchip",
                order: 2,
                requiredPermissionName: SyncPermissions.Admin
            )
        );

        context.Menu.Items.Insert(
            3,
            new ApplicationMenuItem(
                SyncMenus.Keycloak,
                "Keycloak",
                "/keycloak",
                icon: "fas fa-id-badge",
                order: 3,
                requiredPermissionName: SyncPermissions.Admin
            )
        );

        // Своя карточка учётных данных - всем вошедшим, без права Sync.Admin.
        context.Menu.Items.Insert(
            4,
            new ApplicationMenuItem(
                SyncMenus.MyCredentials,
                "Мои учётные данные",
                "/my-credentials",
                icon: "fas fa-user-lock",
                order: 4
            ).RequireAuthenticated()
        );

        if (MultiTenancyConsts.IsEnabled)
        {
            administration.SetSubItemOrder(TenantManagementMenuNames.GroupName, 1);
        }
        else
        {
            administration.TryRemoveMenuItem(TenantManagementMenuNames.GroupName);
        }

        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 2);
        administration.SetSubItemOrder(SettingManagementMenus.GroupName, 3);

        return Task.CompletedTask;
    }
}
