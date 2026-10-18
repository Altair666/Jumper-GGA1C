using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using GGA1C.Sync.Keycloak;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;

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

    public LabelClientsAppService(
        ILabelClientStore clients,
        ILabelsBufferFactory buffers,
        ISettingManager settingManager,
        KeycloakClient keycloak,
        KeycloakAccountSync keycloakSync)
    {
        _clients = clients;
        _buffers = buffers;
        _settingManager = settingManager;
        _keycloak = keycloak;
        _keycloakSync = keycloakSync;
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
            RowCount = _buffers.For(c.Slug).Count()
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
    }

    public async Task DeleteAsync(int id)
    {
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        var slug = client.Slug;

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

    // ── Keycloak ──────────────────────────────────────────────────────────────

    public async Task<KeycloakSettingsDto> GetKeycloakSettingsAsync()
    {
        var secret = await Get(SyncSettings.Keycloak.ClientSecret);

        return new KeycloakSettingsDto
        {
            BaseUrl = await Get(SyncSettings.Keycloak.BaseUrl),
            Realm = await Get(SyncSettings.Keycloak.Realm),
            ClientId = await Get(SyncSettings.Keycloak.ClientId),

            // Секрет наружу не отдаём - только факт, что он задан
            ClientSecret = "",
            HasClientSecret = !string.IsNullOrEmpty(secret)
        };
    }

    public async Task SaveKeycloakSettingsAsync(KeycloakSettingsDto input)
    {
        await Set(SyncSettings.Keycloak.BaseUrl, input.BaseUrl?.TrimEnd('/') ?? "");
        await Set(SyncSettings.Keycloak.Realm, input.Realm ?? "");
        await Set(SyncSettings.Keycloak.ClientId, input.ClientId ?? "");

        // Пустое поле означает "оставить прежний секрет", иначе каждое сохранение формы
        // стирало бы секрет, которого в ней и не было.
        if (!string.IsNullOrWhiteSpace(input.ClientSecret))
        {
            await Set(SyncSettings.Keycloak.ClientSecret, input.ClientSecret);
        }
    }

    public async Task<ConnectionTestResultDto> TestKeycloakAsync()
    {
        var (ok, message) = await _keycloak.TestAsync();
        return new ConnectionTestResultDto { Ok = ok, Message = message };
    }

    public async Task<KeycloakSyncReport> SyncFromKeycloakAsync()
    {
        return await _keycloakSync.SyncAsync();
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
