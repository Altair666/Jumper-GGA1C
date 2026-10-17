using Volo.Abp.Settings;

namespace GGA1C.Sync.Settings;

public class SyncSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(SyncSettings.MySetting1));
    }
}
