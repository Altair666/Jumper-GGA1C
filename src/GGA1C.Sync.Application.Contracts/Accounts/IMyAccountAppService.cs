using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Accounts;

/// <summary>«Кто я» для вошедшего: пользователь хаба и его учётка из сверки с Keycloak.</summary>
public interface IMyAccountAppService : IApplicationService
{
    Task<MyAccountDto> GetAsync();
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
