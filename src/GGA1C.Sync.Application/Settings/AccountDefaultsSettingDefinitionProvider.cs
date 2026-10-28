using Volo.Abp.Account.Settings;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Settings;

/// <summary>
/// Самостоятельной регистрации в хабе нет: люди приходят из Keycloak, первый
/// администратор — со страницы /setup. В ABP регистрация по умолчанию включена, и форма
/// /Account/Register была открыта любому в сети (проверено на стенде). Значение по
/// умолчанию меняем здесь, а не строкой в базе, чтобы чистая установка была закрыта
/// сразу; на стенде уже лежит явное <c>False</c> в AbpSettings — оно не мешает.
///
/// Провайдер живёт в слое приложения намеренно: он должен отработать после провайдера
/// модуля Account (тот определяет настройку), а модуль приложения от него зависит.
/// </summary>
public class AccountDefaultsSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        var selfRegistration = context.GetOrNull(AccountSettingNames.IsSelfRegistrationEnabled);
        if (selfRegistration != null)
        {
            selfRegistration.DefaultValue = "false";
        }
    }
}
