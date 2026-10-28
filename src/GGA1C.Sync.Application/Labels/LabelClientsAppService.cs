using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Connections;
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
[Authorize(GGA1C.Sync.Permissions.SyncPermissions.Admin)]
public class LabelClientsAppService : ApplicationService, ILabelClientsAppService
{
    private readonly ILabelClientStore _clients;
    private readonly ILabelsStore _labels;
    private readonly IPrintLoginService _printLogins;
    private readonly LabelsCsvExport _csv;
    private readonly ISettingManager _settingManager;
    private readonly KeycloakClient _keycloak;
    private readonly KeycloakAccountSync _keycloakSync;
    private readonly HubUserAccess _hubUsers;

    public LabelClientsAppService(
        ILabelClientStore clients,
        ILabelsStore labels,
        IPrintLoginService printLogins,
        LabelsCsvExport csv,
        ISettingManager settingManager,
        KeycloakClient keycloak,
        KeycloakAccountSync keycloakSync,
        HubUserAccess hubUsers)
    {
        _hubUsers = hubUsers;
        _clients = clients;
        _labels = labels;
        _printLogins = printLogins;
        _csv = csv;
        _settingManager = settingManager;
        _keycloak = keycloak;
        _keycloakSync = keycloakSync;
    }

    public async Task<List<LabelClientDto>> GetListAsync()
    {
        var clients = await _clients.GetAllAsync();

        // Счётчики берём одним запросом с группировкой, а не по учётке отдельно:
        // страница открывается часто, а учёток будет десятки.
        var buffers = (await _labels.GetBuffersAsync())
            .ToDictionary(b => b.Slug, b => b, StringComparer.OrdinalIgnoreCase);

        return clients.Select(c => new LabelClientDto
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            KeyPrefix = c.KeyPrefix,
            IsEnabled = c.IsEnabled,
            LastUsedAt = c.LastUsedAt,
            PrintLogin = c.PrintLogin ?? "",
            Email = c.Email ?? "",
            RowCount = buffers.TryGetValue(c.Slug, out var info) ? info.RowCount : 0,
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

        // Доступ к базе печати заводим сразу, как и при сверке с Keycloak: логин - почта,
        // а если её нет, имя папки с приставкой lbl_. Нажимать ничего не нужно.
        await EnsurePrintLoginQuietlyAsync(client);

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

        // Выключили учётку руками - вход в хаб через Keycloak закрывается вместе с печатью,
        // уже открытая сессия гаснет на ближайшей проверке куки.
        var client = await _clients.FindAsync(id);
        await _hubUsers.SetActiveAsync(client?.KeycloakSub, enabled);
    }

    public async Task DeleteAsync(int id)
    {
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        var slug = client.Slug;

        // Строки и файл выгрузки уходят вместе с учёткой, чтобы не копить мусор и не
        // оставлять чужие данные на сервере.
        await _printLogins.DropAsync(client.PrintLogin);
        await _labels.ClearAsync(slug);
        _csv.Delete(slug);

        await _clients.DeleteAsync(id);

    }

    /// <summary>
    /// Создаёт пользователя MySQL с паролем по умолчанию и запоминает его. Недоступная база
    /// не должна ронять создание учётки: человек уже заведён, доступ доберётся при сверке
    /// или при сохранении карточки.
    /// </summary>
    private async Task EnsurePrintLoginQuietlyAsync(LabelClient client)
    {
        if (!string.IsNullOrWhiteSpace(client.PrintLogin))
        {
            return;
        }

        try
        {
            var login = await _printLogins.EnsureAsync(
                client.Slug, client.Email, PrintLoginDefaults.Password);

            await _clients.SetPrintCredentialsAsync(
                client.Id, login, PrintLoginDefaults.Password);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Логин печати для {Name} не создан", client.Name);
        }
    }

    public async Task<string?> RevealPrintPasswordAsync(int id)
    {
        return await _clients.RevealPrintPasswordAsync(id);
    }

    public async Task ClearBufferAsync(int id)
    {
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        await _labels.ClearAsync(client.Slug);
    }

    // ── Буферы ────────────────────────────────────────────────────────────────

    public async Task<List<BufferContentsDto>> GetBuffersAsync(int maxRows = 50)
    {
        var clients = await _clients.GetAllAsync();
        var buffers = (await _labels.GetBuffersAsync())
            .ToDictionary(b => b.Slug, b => b, StringComparer.OrdinalIgnoreCase);

        var result = new List<BufferContentsDto>();

        foreach (var client in clients)
        {
            buffers.TryGetValue(client.Slug, out var info);

            result.Add(new BufferContentsDto
            {
                ClientId = client.Id,
                Name = client.Name,
                Slug = client.Slug,
                PrintLogin = client.PrintLogin ?? "",
                FilePath = _csv.PathFor(client.Slug),
                RowCount = info?.RowCount ?? 0,
                UpdatedAt = info?.UpdatedAt,
                Rows = (info?.RowCount ?? 0) > 0
                    ? await _labels.GetTopAsync(client.Slug, maxRows)
                    : new List<LabelRecord>()
            });

            buffers.Remove(client.Slug);
        }

        // Строки, оставшиеся от удалённых учёток, тоже показываем: иначе они копились бы
        // в таблице незамеченными.
        foreach (var orphan in buffers.Values)
        {
            result.Add(new BufferContentsDto
            {
                ClientId = null,
                Name = "Строки без учётной записи",
                Slug = orphan.Slug,
                RowCount = orphan.RowCount,
                UpdatedAt = orphan.UpdatedAt,
                Rows = await _labels.GetTopAsync(orphan.Slug, maxRows)
            });
        }

        return result;
    }

    public async Task DeleteBufferFolderAsync(string slug)
    {
        var clients = await _clients.GetAllAsync();

        if (clients.Any(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException("GGA1C.Sync:BufferBelongsToClient")
                .WithData("message",
                    $"Строки «{slug}» принадлежат учётной записи — удаляйте вместе с учёткой");
        }

        await _labels.ClearAsync(slug);
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
            PrintLogin = client.PrintLogin ?? _printLogins.LoginFor(client.Slug, client.Email),
            PrintPassword = SecretMask.For(await _clients.RevealPrintPasswordAsync(id)),
            HasPrintPassword = !string.IsNullOrEmpty(client.PrintPasswordEncrypted),
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

        // Пароль MySQL меняем только если в поле ввели новое значение: точки означают
        // «оставить как есть», как и у прочих секретов.
        if (!SecretMask.IsMask(input.PrintPassword) &&
            !string.IsNullOrWhiteSpace(input.PrintPassword))
        {
            var client = await _clients.FindAsync(input.Id)
                ?? throw new BusinessException("GGA1C.Sync:ClientNotFound")
                    .WithData("message", "Учётная запись не найдена");

            var login = await _printLogins.EnsureAsync(
                client.Slug, client.Email, input.PrintPassword.Trim());

            await _clients.SetPrintCredentialsAsync(input.Id, login, input.PrintPassword.Trim());
        }
    }

    /// <summary>Случайный пароль для кнопки «сгенерировать» в карточке.</summary>
    public string GeneratePrintPassword()
    {
        return _printLogins.GeneratePassword();
    }

    // ── Keycloak ──────────────────────────────────────────────────────────────

    public async Task<KeycloakSettingsDto> GetKeycloakSettingsAsync()
    {
        // Через ISettingProvider, а не ISettingManager: шифрованную настройку расшифровывает
        // только он. ISettingManager отдаёт её как лежит в базе, и маска получалась длиной
        // шифротекста - проверено, 128 точек вместо 86.
        var secret = await SettingProvider.GetOrNullAsync(SyncSettings.Keycloak.ClientSecret) ?? "";
        var loginSecret = await SettingProvider.GetOrNullAsync(SyncSettings.Keycloak.LoginClientSecret) ?? "";

        return new KeycloakSettingsDto
        {
            BaseUrl = await Get(SyncSettings.Keycloak.BaseUrl),
            Realm = await Get(SyncSettings.Keycloak.Realm),
            ClientId = await Get(SyncSettings.Keycloak.ClientId),
            WidgetClientId = await SettingProvider.GetOrNullAsync(SyncSettings.Keycloak.WidgetClientId) ?? "",

            // Вместо секрета - маска той же длины: значение наружу не уходит, но видно,
            // что секрет есть и какой он длины, и поле можно выделить и стереть.
            ClientSecret = SecretMask.For(secret),
            HasClientSecret = !string.IsNullOrEmpty(secret),

            LoginClientId = await Get(SyncSettings.Keycloak.LoginClientId),
            LoginClientSecret = SecretMask.For(loginSecret),
            HasLoginClientSecret = !string.IsNullOrEmpty(loginSecret)
        };
    }

    public async Task SaveKeycloakSettingsAsync(KeycloakSettingsDto input)
    {
        await Set(SyncSettings.Keycloak.BaseUrl, input.BaseUrl?.TrimEnd('/') ?? "");
        await Set(SyncSettings.Keycloak.Realm, input.Realm ?? "");
        await Set(SyncSettings.Keycloak.ClientId, input.ClientId ?? "");
        await Set(SyncSettings.Keycloak.WidgetClientId, input.WidgetClientId?.Trim() ?? "");

        // Одни точки - поле не трогали, оставляем прежний секрет. Пусто - человек стёр
        // маску, значит секрет нужно удалить. Иное - новое значение.
        if (!SecretMask.IsMask(input.ClientSecret))
        {
            await Set(SyncSettings.Keycloak.ClientSecret, input.ClientSecret?.Trim() ?? "");
        }

        await Set(SyncSettings.Keycloak.LoginClientId, input.LoginClientId?.Trim() ?? "");

        if (!SecretMask.IsMask(input.LoginClientSecret))
        {
            await Set(SyncSettings.Keycloak.LoginClientSecret, input.LoginClientSecret?.Trim() ?? "");
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
