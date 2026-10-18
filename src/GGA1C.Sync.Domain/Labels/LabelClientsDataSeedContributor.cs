using System.IO;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Заводит первую учётную запись виджета при пустой базе и переносит на неё наследство
/// прежней версии сервиса.
///
/// Перенос нужен, чтобы обновление не ломало уже работающее. До разделения по учёткам
/// ключ был один (глобальная настройка Sync.GristApiKey), а буфер лежал одним файлом в
/// корне буферов. Если просто завести новую учётку с новым ключом, все настроенные
/// виджеты немедленно начнут получать 401, а накопленная партия этикеток окажется
/// в файле, который больше никто не читает.
///
/// Поэтому: прежний ключ достаётся из настройки и присваивается учётке "Общий", а старый
/// файл буфера переезжает в её папку. Виджеты продолжают работать с теми же ключами,
/// а дальше администратор заводит персональные учётки и раздаёт новые ключи по людям.
/// </summary>
public class LabelClientsDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private const string LegacyClientName = "Общий";
    private const string LegacyClientSlug = "shared";

    private readonly ILabelClientStore _clients;
    private readonly ISettingManager _settingManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LabelClientsDataSeedContributor> _logger;

    public LabelClientsDataSeedContributor(
        ILabelClientStore clients,
        ISettingManager settingManager,
        IConfiguration configuration,
        ILogger<LabelClientsDataSeedContributor> logger)
    {
        _clients = clients;
        _settingManager = settingManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var existing = await _clients.GetAllAsync();
        if (existing.Count > 0)
        {
            return;
        }

        // Прежний общий ключ: сначала из настройки, потом из конфигурации (её задают
        // переменной окружения Labels__ApiKey на стенде). Если нет нигде - сгенерируется.
        var legacyKey = await _settingManager.GetOrNullGlobalAsync(SyncSettings.GristApiKey);
        if (string.IsNullOrWhiteSpace(legacyKey))
        {
            legacyKey = _configuration["Labels:ApiKey"];
        }

        var (client, key) = await _clients.CreateAsync(LegacyClientName, LegacyClientSlug, legacyKey);

        _logger.LogInformation(
            "Создана учётная запись виджета \"{Name}\" (папка {Slug}), ключ {Prefix}...",
            client.Name, client.Slug, key[..8]);

    }
}