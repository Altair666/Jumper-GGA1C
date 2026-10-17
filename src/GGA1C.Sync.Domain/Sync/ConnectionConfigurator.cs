using System.Threading.Tasks;
using GGA1C.Sync.Altium;
using GGA1C.Sync.OneC;
using GGA1C.Sync.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Sync;

/// <summary>
/// Переносит настройки подключений из хранилища ABP в клиентов Altium и 1С.
///
/// Клиенты намеренно ничего не знают про настройки и конфигурируются вызовом Configure —
/// так они остаются пригодными и для тестов, и для разовых диагностических запросов
/// с другими параметрами. Обязанность «взять актуальные настройки и применить» живёт здесь.
///
/// Вызывать перед работой с клиентами, а не только при старте: настройки правятся в
/// интерфейсе на ходу, и клиент должен подхватить новый адрес без перезапуска сервиса.
/// </summary>
public class ConnectionConfigurator : ITransientDependency
{
    private readonly ISettingProvider _settings;
    private readonly AltiumService _altium;
    private readonly OneCService _onec;

    public ConnectionConfigurator(
        ISettingProvider settings,
        AltiumService altium,
        OneCService onec)
    {
        _settings = settings;
        _altium = altium;
        _onec = onec;
    }

    public async Task ApplyAltiumAsync()
    {
        _altium.Configure(
            await Get(SyncSettings.Altium.ServerUrl),
            await Get(SyncSettings.Altium.User),
            await Get(SyncSettings.Altium.Password),
            await Get(SyncSettings.Altium.ApiComponents),
            await Get(SyncSettings.Altium.ApiFolders),
            await Get(SyncSettings.Altium.ApiProjects),
            await Get(SyncSettings.Altium.LifecycleFilter),
            await Get(SyncSettings.Altium.BrowserCookies)
        );
    }

    public async Task ApplyOneCAsync()
    {
        _onec.Configure(
            await Get(SyncSettings.OneC.ServerUrl),
            await Get(SyncSettings.OneC.BaseName),
            await Get(SyncSettings.OneC.User),
            await Get(SyncSettings.OneC.Password)
        );
    }

    public async Task ApplyAllAsync()
    {
        await ApplyAltiumAsync();
        await ApplyOneCAsync();
    }

    private async Task<string> Get(string name)
    {
        return await _settings.GetOrNullAsync(name) ?? "";
    }
}
