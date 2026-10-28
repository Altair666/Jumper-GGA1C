using GGA1C.Sync.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace GGA1C.Sync.Permissions;

public class SyncPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(SyncPermissions.GroupName, L("Permission:Sync"));
        group.AddPermission(SyncPermissions.Admin, L("Permission:Sync.Admin"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SyncResource>(name);
    }
}
