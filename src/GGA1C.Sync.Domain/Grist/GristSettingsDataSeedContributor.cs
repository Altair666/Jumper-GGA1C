using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;

namespace GGA1C.Sync.Grist;

/// <summary>
/// Переносит доступ к Grist из конфигурации в хранилище настроек при первом запуске —
/// тем же способом, что и настройки Keycloak.
///
/// Нужно для развёртывания: адрес, документ и ключ API задаются переменными окружения
/// (<c>Grist__ServerUrl</c> и далее), дальше живут в базе, ключ — шифрованным. Иначе ключ
/// пришлось бы вписывать руками через интерфейс на каждой установке.
///
/// Уже заданные значения не перезатираются: правка через интерфейс не должна отменяться
/// при следующем накатывании миграций.
/// </summary>
public class GristSettingsDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly ISettingManager _settingManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GristSettingsDataSeedContributor> _logger;

    public GristSettingsDataSeedContributor(
        ISettingManager settingManager,
        IConfiguration configuration,
        ILogger<GristSettingsDataSeedContributor> logger)
    {
        _settingManager = settingManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var seeded = 0;

        seeded += await SeedIfEmpty(SyncSettings.Grist.ServerUrl, _configuration["Grist:ServerUrl"]);
        seeded += await SeedIfEmpty(SyncSettings.Grist.DocId, _configuration["Grist:DocId"]);
        seeded += await SeedIfEmpty(SyncSettings.Grist.ApiKey, _configuration["Grist:ApiKey"]);
        seeded += await SeedIfEmpty(SyncSettings.Grist.IdentityTable, _configuration["Grist:IdentityTable"]);

        if (seeded > 0)
        {
            _logger.LogInformation("Настройки Grist перенесены из конфигурации: {Count}", seeded);
        }
    }

    private async Task<int> SeedIfEmpty(string settingName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var existing = await _settingManager.GetOrNullGlobalAsync(settingName);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return 0;
        }

        await _settingManager.SetGlobalAsync(settingName, value.Trim());
        return 1;
    }
}
