using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Ui;

/// <summary>
/// Настройки внешнего вида хаба, которые меняет администратор: пока только имя сервера в
/// заголовках. RemoteService выключен, как у остальных админских сервисов, — нужен только
/// странице «Администрирование → Настройки → Хаб».
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IUiSettingsAppService : IApplicationService
{
    /// <summary>Действующее имя сервера (из базы или значение по умолчанию).</summary>
    Task<string> GetServerNameAsync();

    /// <summary>Пусто или значение по умолчанию — строка из базы удаляется.</summary>
    Task SetServerNameAsync(string? serverName);
}
