using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Переносит настройки Keycloak из конфигурации в хранилище настроек при первом запуске.
///
/// Нужно для развёртывания: адрес, реалм, клиент и секрет задаются переменными окружения
/// (<c>Keycloak__BaseUrl</c> и далее), а дальше живут в базе в шифрованном виде. Иначе
/// секрет клиента пришлось бы вписывать руками через интерфейс на каждой установке.
///
/// Уже заданные значения не перезатираются: правка через интерфейс не должна отменяться
/// при следующем накатывании миграций.
/// </summary>
public class KeycloakSettingsDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly ISettingManager _settingManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakSettingsDataSeedContributor> _logger;

    public KeycloakSettingsDataSeedContributor(
        ISettingManager settingManager,
        IConfiguration configuration,
        ILogger<KeycloakSettingsDataSeedContributor> logger)
    {
        _settingManager = settingManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var seeded = 0;

        seeded += await SeedIfEmpty(SyncSettings.Keycloak.BaseUrl, _configuration["Keycloak:BaseUrl"]);
        seeded += await SeedIfEmpty(SyncSettings.Keycloak.Realm, _configuration["Keycloak:Realm"]);
        seeded += await SeedIfEmpty(SyncSettings.Keycloak.ClientId, _configuration["Keycloak:ClientId"]);
        seeded += await SeedIfEmpty(SyncSettings.Keycloak.ClientSecret, _configuration["Keycloak:ClientSecret"]);
        seeded += await SeedIfEmpty(SyncSettings.Keycloak.LoginClientId, _configuration["Keycloak:LoginClientId"]);
        seeded += await SeedIfEmpty(SyncSettings.Keycloak.LoginClientSecret, _configuration["Keycloak:LoginClientSecret"]);

        if (seeded > 0)
        {
            _logger.LogInformation("Настройки Keycloak перенесены из конфигурации: {Count}", seeded);
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
