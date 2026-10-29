using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Ui;

/// <summary>
/// Настройки внешнего вида хаба, которые меняет администратор: имя сервера в заголовках и
/// значок сайта. RemoteService выключен, как у остальных админских сервисов, — нужен только
/// странице «Администрирование → Настройки → Хаб».
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IUiSettingsAppService : IApplicationService
{
    /// <summary>Действующее имя сервера (из базы или значение по умолчанию).</summary>
    Task<string> GetServerNameAsync();

    /// <summary>Пусто или значение по умолчанию — строка из базы удаляется.</summary>
    Task SetServerNameAsync(string? serverName);

    /// <summary>Загружен ли свой значок сайта (иначе — значок по умолчанию из сборки).</summary>
    Task<SiteIconInfoDto> GetSiteIconAsync();

    /// <summary>Загрузить свой значок: PNG, SVG или ICO, не больше 256 КБ (П16).</summary>
    Task SetSiteIconAsync(string fileName, byte[] content);

    /// <summary>Вернуть значок по умолчанию.</summary>
    Task ResetSiteIconAsync();
}

public class SiteIconInfoDto
{
    /// <summary>Загружен свой значок.</summary>
    public bool IsCustom { get; set; }

    /// <summary>Адрес значка с меткой версии — чтобы браузер не показывал прежний из кэша.</summary>
    public string Url { get; set; } = "";
}
