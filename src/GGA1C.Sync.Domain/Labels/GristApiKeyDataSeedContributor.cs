using System;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Выдаёт ключ для виджета Grist при первом засеве.
///
/// Значение берётся из конфигурации Labels:ApiKey, если она задана (удобно для стенда:
/// переменная окружения Labels__ApiKey), иначе генерируется случайно. В обоих случаях
/// ложится в настройку Sync.GristApiKey, которая хранится шифрованной — поэтому в
/// appsettings.json ключ прописывать не нужно и не следует.
///
/// Уже существующее значение не перезатирается: перевыпуск ключа — отдельное осознанное
/// действие, а не побочный эффект накатывания миграций.
/// </summary>
public class GristApiKeyDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly ISettingManager _settingManager;
    private readonly IConfiguration _configuration;

    public GristApiKeyDataSeedContributor(
        ISettingManager settingManager,
        IConfiguration configuration)
    {
        _settingManager = settingManager;
        _configuration = configuration;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var existing = await _settingManager.GetOrNullGlobalAsync(SyncSettings.GristApiKey);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return;
        }

        var fromConfig = _configuration["Labels:ApiKey"];

        var key = string.IsNullOrWhiteSpace(fromConfig)
            ? Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8]
            : fromConfig;

        await _settingManager.SetGlobalAsync(SyncSettings.GristApiKey, key);
    }
}
