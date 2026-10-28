using System.Threading.Tasks;
using GGA1C.Sync.Secrets;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Карточка учётных данных (Altium, PDM, пароль печати) для учётки с известным id. Общая
/// для администратора (<see cref="LabelClientsAppService"/>, id из запроса) и для самого
/// человека (<c>MyAccountAppService</c>, id — только его собственный, по <c>sub</c>), чтобы
/// правила секретов были одни: маска длины, «одни точки — не менять, пусто — удалить».
///
/// Сам не проверяет, чья учётка: это делают сервисы приложения до вызова.
/// </summary>
public class AccountCredentialsEditor : ITransientDependency
{
    private readonly ILabelClientStore _clients;
    private readonly IPrintLoginService _printLogins;

    public AccountCredentialsEditor(ILabelClientStore clients, IPrintLoginService printLogins)
    {
        _clients = clients;
        _printLogins = printLogins;
    }

    public async Task<AccountCredentialsDto> GetAsync(int id)
    {
        var client = await _clients.FindAsync(id)
            ?? throw new BusinessException("GGA1C.Sync:ClientNotFound")
                .WithData("message", "Учётная запись не найдена");

        // Расшифровка нужна ровно для длины маски - дальше открытые значения не уходят.
        var secrets = await _clients.RevealCredentialsAsync(id);

        return new AccountCredentialsDto
        {
            Id = client.Id,
            Name = client.Name,
            AltiumLogin = client.AltiumLogin ?? "",
            PdmLogin = client.PdmLogin ?? "",

            // Вместо паролей - маски той же длины: значения наружу не уходят, но длину
            // видно, и поле можно стереть, чтобы удалить пароль.
            AltiumPassword = SecretMask.For(secrets.AltiumPassword),
            PdmPassword = SecretMask.For(secrets.PdmPassword),
            PrintLogin = client.PrintLogin ?? _printLogins.LoginFor(client.Slug, client.Email),
            PrintPassword = SecretMask.For(await _clients.RevealPrintPasswordAsync(id)),
            HasPrintPassword = !string.IsNullOrEmpty(client.PrintPasswordEncrypted),
            HasAltiumPassword = !string.IsNullOrEmpty(client.AltiumPasswordEncrypted),
            HasPdmPassword = !string.IsNullOrEmpty(client.PdmPasswordEncrypted),
            UpdatedAt = client.CredentialsUpdatedAt
        };
    }

    /// <summary>
    /// Сохранить карточку в учётку <paramref name="id"/>. <c>input.Id</c> не читается: какую
    /// учётку править, решает вызывающий.
    /// </summary>
    public async Task SaveAsync(int id, AccountCredentialsDto input)
    {
        await _clients.SetCredentialsAsync(
            id,
            new AccountCredentials(
                input.AltiumLogin,
                input.AltiumPassword,
                input.PdmLogin,
                input.PdmPassword));

        // Пароль MySQL меняем только если в поле ввели новое значение: точки означают
        // «оставить как есть», как и у прочих секретов.
        if (!SecretMask.IsMask(input.PrintPassword) &&
            !string.IsNullOrWhiteSpace(input.PrintPassword))
        {
            var client = await _clients.FindAsync(id)
                ?? throw new BusinessException("GGA1C.Sync:ClientNotFound")
                    .WithData("message", "Учётная запись не найдена");

            var login = await _printLogins.EnsureAsync(
                client.Slug, client.Email, input.PrintPassword.Trim());

            await _clients.SetPrintCredentialsAsync(id, login, input.PrintPassword.Trim());
        }
    }
}
