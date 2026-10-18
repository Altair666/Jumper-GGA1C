using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using GGA1C.Sync.Grist;
using GGA1C.Sync.Keycloak;
using GGA1C.Sync.Secrets;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Labels;

// Атрибут нужен и на классе: ABP ищет его на типе реализации, пометки на интерфейсе
// недостаточно - проверено, эндпоинт оставался анонимным.
[RemoteService(IsEnabled = false)]
[Authorize]
public class LabelClientsAppService : ApplicationService, ILabelClientsAppService
{
    private readonly ILabelClientStore _clients;
    private readonly ILabelsBufferFactory _buffers;
    private readonly ISettingManager _settingManager;
    private readonly KeycloakClient _keycloak;
    private readonly KeycloakAccountSync _keycloakSync;
    private readonly GristIdentityResolver _gristIdentity;

    public LabelClientsAppService(
        ILabelClientStore clients,
        ILabelsBufferFactory buffers,
        ISettingManager settingManager,
        KeycloakClient keycloak,
        KeycloakAccountSync keycloakSync,
        GristIdentityResolver gristIdentity)
    {
        _clients = clients;
        _buffers = buffers;
        _settingManager = settingManager;
        _keycloak = keycloak;
        _keycloakSync = keycloakSync;
        _gristIdentity = gristIdentity;
    }

    public async Task<List<LabelClientDto>> GetListAsync()
    {
        var clients = await _clients.GetAllAsync();

        return clients.Select(c => new LabelClientDto
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            KeyPrefix = c.KeyPrefix,
            IsEnabled = c.IsEnabled,
            LastUsedAt = c.LastUsedAt,
            BufferPath = _buffers.PathFor(c.Slug),
            RowCount = _buffers.For(c.Slug).Count(),
            HasAltiumCredentials = !string.IsNullOrEmpty(c.AltiumPasswordEncrypted),
            HasPdmCredentials = !string.IsNullOrEmpty(c.PdmPasswordEncrypted)
        }).ToList();
    }

    public async Task<string> CreateAsync(string name, string? email)
    {
        // Папку выводим сами: сначала из фамилии, потом из email. Поле в интерфейсе
        // убрано намеренно — оно попадает в путь на диске и в адрес шары для GoLabel2,
        // и ошибиться в нём легче, чем выиграть от возможности задать его руками.
        var baseSlug = SlugGenerator.FromNameOrEmail(name, email);

        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            throw new BusinessException("GGA1C.Sync:CannotDeriveSlug")
                .WithData("message",
                    "Не удалось вывести имя папки: укажите фамилию латиницей или кириллицей, " +
                    "либо задайте email");
        }

        // Двух Ивановых достаточно, чтобы сломать наивную схему
        var slug = await _clients.MakeUniqueSlugAsync(baseSlug);

        var (client, key) = await _clients.CreateAsync(name, slug);

        if (!string.IsNullOrWhiteSpace(email))
        {
            // Email нужен не для красоты: именно по нему Grist будет отдавать виджету
            // строку с его ключом (правило user.Email == rec.email).
            client.Email = email.Trim();
            await _clients.UpdateAsync(client);
        }

        // Создаём файл буфера сразу, чтобы путь для DSN в GoLabel2 существовал ещё до
        // первой синхронизации - иначе источник ODBC не настроить.
        _buffers.For(client.Slug);

        // Без строки в таблице почт Grist не сможет опознать человека, и виджет ответит
        // "вашей строки нет".
        await SyncIdentityQuietlyAsync();

        return key;
    }

    public async Task<string> RegenerateKeyAsync(int id)
    {
        return await _clients.RegenerateKeyAsync(id);
    }

    public async Task<string?> RevealKeyAsync(int id)
    {
        return await _clients.RevealKeyAsync(id);
    }

    public async Task SetEnabledAsync(int id, bool enabled)
    {
        await _clients.SetEnabledAsync(id, enabled);

        // Отключили - строку из таблицы почт нужно убрать, включили - вернуть
        await SyncIdentityQuietlyAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        var slug = client.Slug;

        // Строку в документе убираем, пока её номер ещё известен
        try
        {
            await _gristIdentity.RemoveRowAsync(client.GristRowId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Строка почты в Grist не удалена");
        }

        await _clients.DeleteAsync(id);

        // Файл буфера удаляем вместе с учёткой, чтобы не копить мусор на сервере.
        // Порядок именно такой: сначала запись, потом файлы. Если удаление файлов
        // упрётся в занятый файл, учётка уже отозвана - ключ не работает, и повторное
        // удаление доберёт папку.
        _buffers.Delete(slug);
    }

    public async Task ClearBufferAsync(int id)
    {
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        _buffers.For(client.Slug).ClearAll();
    }

    // ── Буферы ────────────────────────────────────────────────────────────────

    public async Task<List<BufferContentsDto>> GetBuffersAsync(int maxRows = 50)
    {
        var clients = await _clients.GetAllAsync();
        var result = new List<BufferContentsDto>();

        foreach (var client in clients)
        {
            result.Add(Describe(client.Id, client.Name, client.Slug, maxRows));
        }

        var known = clients.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Папки без учётки: обычно след удалённой учётки, чей файл в тот момент держал
        // открытый источник ODBC. Без показа в интерфейсе они копились бы незамеченными.
        foreach (var folder in _buffers.ListFolders().Where(f => !known.Contains(f)))
        {
            result.Add(Describe(null, "Папка без учётной записи", folder, maxRows));
        }

        return result;
    }

    public async Task DeleteBufferFolderAsync(string slug)
    {
        var clients = await _clients.GetAllAsync();

        if (clients.Any(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            // Иначе буфер работающей учётки удалялся бы мимо подтверждения, которое
            // показывается при удалении учётной записи.
            throw new BusinessException("GGA1C.Sync:BufferBelongsToClient")
                .WithData("message",
                    $"Папка «{slug}» принадлежит учётной записи — удаляйте вместе с учёткой");
        }

        _buffers.Delete(slug);
    }

    private BufferContentsDto Describe(int? clientId, string name, string slug, int maxRows)
    {
        var dto = new BufferContentsDto
        {
            ClientId = clientId,
            Name = name,
            Slug = slug,
            Path = Path.Combine(_buffers.BuffersRoot, slug)
        };

        try
        {
            var info = _buffers.Describe(slug);

            dto.Path = info.Path;
            dto.FileExists = info.Exists;
            dto.SizeBytes = info.SizeBytes;
            dto.UpdatedAt = info.UpdatedAt;

            if (info.Exists)
            {
                var buffer = _buffers.For(slug);
                dto.RowCount = buffer.Count();
                dto.Rows = buffer.GetTop(maxRows);
            }
        }
        catch (BusinessException)
        {
            // Имя папки не проходит проверку - значит её создали не мы. Показываем как есть:
            // читать там нечего, но видеть её в списке полезно.
        }

        return dto;
    }

    // ── Учётные данные во внешних системах ────────────────────────────────────

    public async Task<AccountCredentialsDto> GetCredentialsAsync(int id)
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
            HasAltiumPassword = !string.IsNullOrEmpty(client.AltiumPasswordEncrypted),
            HasPdmPassword = !string.IsNullOrEmpty(client.PdmPasswordEncrypted),
            UpdatedAt = client.CredentialsUpdatedAt
        };
    }

    public async Task SaveCredentialsAsync(AccountCredentialsDto input)
    {
        await _clients.SetCredentialsAsync(
            input.Id,
            new AccountCredentials(
                input.AltiumLogin,
                input.AltiumPassword,
                input.PdmLogin,
                input.PdmPassword));
    }

    // ── Keycloak ──────────────────────────────────────────────────────────────

    public async Task<KeycloakSettingsDto> GetKeycloakSettingsAsync()
    {
        // Через ISettingProvider, а не ISettingManager: шифрованную настройку расшифровывает
        // только он. ISettingManager отдаёт её как лежит в базе, и маска получалась длиной
        // шифротекста - проверено, 128 точек вместо 86.
        var secret = await SettingProvider.GetOrNullAsync(SyncSettings.Keycloak.ClientSecret) ?? "";

        return new KeycloakSettingsDto
        {
            BaseUrl = await Get(SyncSettings.Keycloak.BaseUrl),
            Realm = await Get(SyncSettings.Keycloak.Realm),
            ClientId = await Get(SyncSettings.Keycloak.ClientId),

            // Вместо секрета - маска той же длины: значение наружу не уходит, но видно,
            // что секрет есть и какой он длины, и поле можно выделить и стереть.
            ClientSecret = SecretMask.For(secret),
            HasClientSecret = !string.IsNullOrEmpty(secret)
        };
    }

    public async Task SaveKeycloakSettingsAsync(KeycloakSettingsDto input)
    {
        await Set(SyncSettings.Keycloak.BaseUrl, input.BaseUrl?.TrimEnd('/') ?? "");
        await Set(SyncSettings.Keycloak.Realm, input.Realm ?? "");
        await Set(SyncSettings.Keycloak.ClientId, input.ClientId ?? "");

        // Одни точки - поле не трогали, оставляем прежний секрет. Пусто - человек стёр
        // маску, значит секрет нужно удалить. Иное - новое значение.
        if (!SecretMask.IsMask(input.ClientSecret))
        {
            await Set(SyncSettings.Keycloak.ClientSecret, input.ClientSecret?.Trim() ?? "");
        }
    }

    public async Task<ConnectionTestResultDto> TestKeycloakAsync()
    {
        var (ok, message) = await _keycloak.TestAsync();
        return new ConnectionTestResultDto { Ok = ok, Message = message };
    }

    public async Task<KeycloakSyncReport> SyncFromKeycloakAsync()
    {
        var report = await _keycloakSync.SyncAsync();

        // Появились или исчезли люди - таблица почт в документе должна это отразить,
        // иначе новый сотрудник получит от виджета "вашей строки нет".
        await SyncIdentityQuietlyAsync();

        return report;
    }

    // ── Ключи в Grist ─────────────────────────────────────────────────────────

    public async Task<GristSettingsDto> GetGristSettingsAsync()
    {
        // Ключ - через ISettingProvider: шифрованную настройку расшифровывает только он.
        var apiKey = await SettingProvider.GetOrNullAsync(SyncSettings.Grist.ApiKey) ?? "";

        return new GristSettingsDto
        {
            ServerUrl = await Get(SyncSettings.Grist.ServerUrl),
            DocId = await Get(SyncSettings.Grist.DocId),
            IdentityTable = await SettingProvider.GetOrNullAsync(SyncSettings.Grist.IdentityTable) ?? "",
            ApiKey = SecretMask.For(apiKey),
            HasApiKey = !string.IsNullOrEmpty(apiKey)
        };
    }

    public async Task SaveGristSettingsAsync(GristSettingsDto input)
    {
        await Set(SyncSettings.Grist.ServerUrl, input.ServerUrl?.TrimEnd('/') ?? "");
        await Set(SyncSettings.Grist.DocId, input.DocId?.Trim() ?? "");
        await Set(SyncSettings.Grist.IdentityTable, input.IdentityTable?.Trim() ?? "");

        if (!SecretMask.IsMask(input.ApiKey))
        {
            await Set(SyncSettings.Grist.ApiKey, input.ApiKey?.Trim() ?? "");
        }
    }

    public async Task<ConnectionTestResultDto> TestGristAsync()
    {
        var (ok, message) = await _gristIdentity.TestAsync();
        return new ConnectionTestResultDto { Ok = ok, Message = message };
    }

    public async Task<ConnectionTestResultDto> SyncGristIdentityAsync()
    {
        var (ok, message, added, removed) = await _gristIdentity.SyncIdentityTableAsync();

        return new ConnectionTestResultDto
        {
            Ok = ok,
            Message = ok ? $"{message}: добавлено {added}, убрано {removed}" : message
        };
    }

    /// <summary>
    /// Обновить таблицу почт, не роняя саму операцию: учётка уже создана или удалена, а
    /// недоступный в этот момент Grist не повод показывать человеку ошибку. Следующая
    /// сверка с Keycloak или кнопка в интерфейсе доберут.
    /// </summary>
    private async Task SyncIdentityQuietlyAsync()
    {
        if (!await _gristIdentity.IsConfiguredAsync())
        {
            return;
        }

        try
        {
            var (ok, message, _, _) = await _gristIdentity.SyncIdentityTableAsync();

            if (!ok)
            {
                Logger.LogWarning("Таблица почт в Grist не обновлена: {Message}", message);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Таблица почт в Grist не обновлена");
        }
    }

    private async Task<string> Get(string name)
    {
        return await _settingManager.GetOrNullGlobalAsync(name) ?? "";
    }

    private async Task Set(string name, string value)
    {
        await _settingManager.SetGlobalAsync(name, value ?? "");
    }
}
