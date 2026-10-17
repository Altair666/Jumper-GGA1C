using GGA1C.Sync.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace GGA1C.Sync.Permissions;

public class SyncPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(SyncPermissions.GroupName);
        //Define your own permissions here. Example:
        //myGroup.AddPermission(SyncPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SyncResource>(name);
    }
}
