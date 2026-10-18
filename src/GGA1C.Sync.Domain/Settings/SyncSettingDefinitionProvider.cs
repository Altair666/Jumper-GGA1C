using Volo.Abp.Settings;

namespace GGA1C.Sync.Settings;

public class SyncSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // isEncrypted: значение шифруется в базе ключом StringEncryption:DefaultPassPhrase.
        // Сам ключ в appsettings.json намеренно пустой и берётся из переменной окружения -
        // иначе шифрованные пароли и ключ от них лежали бы в одном репозитории.
        //
        // isVisibleToClients: false везде - ни пароли, ни ключи не должны уезжать в браузер
        // вместе с остальными настройками.
        context.Add(
            new SettingDefinition(SyncSettings.GristApiKey, isVisibleToClients: false, isEncrypted: true),

            new SettingDefinition(SyncSettings.Altium.ServerUrl, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Altium.User, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Altium.Password, isVisibleToClients: false, isEncrypted: true),
            new SettingDefinition(SyncSettings.Altium.BrowserCookies, isVisibleToClients: false, isEncrypted: true),
            new SettingDefinition(SyncSettings.Altium.ApiComponents, "/components/api/components", isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Altium.ApiFolders, "/components/api/tags", isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Altium.ApiProjects, "/api/v1/projects", isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Altium.LifecycleFilter, isVisibleToClients: false),

            new SettingDefinition(SyncSettings.OneC.ServerUrl, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.OneC.BaseName, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.OneC.User, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.OneC.Password, isVisibleToClients: false, isEncrypted: true),

            new SettingDefinition(SyncSettings.Keycloak.BaseUrl, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Keycloak.Realm, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Keycloak.ClientId, isVisibleToClients: false),
            new SettingDefinition(SyncSettings.Keycloak.ClientSecret, isVisibleToClients: false, isEncrypted: true),

            new SettingDefinition(SyncSettings.AutoSyncEnabled, "false", isVisibleToClients: false),
            new SettingDefinition(SyncSettings.IntervalMinutes, "60", isVisibleToClients: false),
            new SettingDefinition(SyncSettings.DefaultSyncAction, "Skip", isVisibleToClients: false)
        );
    }
}