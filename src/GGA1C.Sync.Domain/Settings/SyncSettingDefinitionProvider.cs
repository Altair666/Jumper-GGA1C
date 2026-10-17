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
        // isVisibleToClients: false - настройка не уезжает в браузер с остальными.
        context.Add(
            new SettingDefinition(
                SyncSettings.GristApiKey,
                defaultValue: null,
                isVisibleToClients: false,
                isEncrypted: true
            )
        );
    }
}