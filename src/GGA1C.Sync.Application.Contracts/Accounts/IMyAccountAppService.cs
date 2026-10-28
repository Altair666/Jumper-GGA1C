using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Accounts;

/// <summary>«Кто я» для вошедшего: пользователь хаба и его учётка из сверки с Keycloak.</summary>
public interface IMyAccountAppService : IApplicationService
{
    Task<MyAccountDto> GetAsync();

    // ── Мои учётные данные во внешних системах ────────────────────────────────
    // Та же карточка, что у администратора на /labels, но без id: учётка всегда своя —
    // найденная по sub текущего пользователя (AbpUserLogins, провайдер Keycloak). Id из
    // запроса не читается, иначе человек мог бы подставить чужой.

    /// <summary>Своя карточка: логины и маски паролей. Нет своей учётки — понятная ошибка.</summary>
    Task<AccountCredentialsDto> GetCredentialsAsync();

    /// <summary>Сохранить свою карточку. <c>input.Id</c> игнорируется.</summary>
    Task SaveCredentialsAsync(AccountCredentialsDto input);

    /// <summary>Свой пароль печати (MySQL) в открытом виде — кнопка «глаз».</summary>
    Task<string?> RevealPrintPasswordAsync();

    /// <summary>Случайный пароль печати; применяется только сохранением карточки.</summary>
    string GeneratePrintPassword();
}

public class MyAccountDto
{
    public string UserName { get; set; } = "";

    /// <summary>Имя и фамилия из пользователя хаба.</summary>
    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    /// <summary>Вошёл через Keycloak (а не локальным паролем).</summary>
    public bool ViaKeycloak { get; set; }

    /// <summary>Есть ли учётка из сверки (LabelClient) с тем же sub.</summary>
    public bool HasAccount { get; set; }

    public string AccountName { get; set; } = "";

    public string KeycloakUsername { get; set; } = "";

    /// <summary>Папка буфера этикеток.</summary>
    public string Slug { get; set; } = "";

    public bool AccountEnabled { get; set; }

    public string PrintLogin { get; set; } = "";
}
