using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using GGA1C.Sync.Keycloak;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Управление учётными записями виджета: создание, выдача и перевыпуск ключей, отключение.
/// Только для вошедших в интерфейс; публикация как REST API отключена по той же причине,
/// что и у ILabelsAppService.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface ILabelClientsAppService : IApplicationService
{
    Task<List<LabelClientDto>> GetListAsync();

    /// <summary>Создать учётку. Ключ возвращается в открытом виде один раз - при создании.</summary>
    Task<string> CreateAsync(string name, string slug);

    Task<string> RegenerateKeyAsync(int id);

    /// <summary>Показать ключ повторно. Расшифровывает сохранённую копию.</summary>
    Task<string?> RevealKeyAsync(int id);

    Task SetEnabledAsync(int id, bool enabled);

    /// <summary>Удалить учётку. Файл буфера остаётся на диске - в нём может быть
    /// неотпечатанная партия.</summary>
    Task DeleteAsync(int id);

    /// <summary>Очистить буфер учётки, не трогая саму учётку.</summary>
    Task ClearBufferAsync(int id);

    // ── Keycloak ──────────────────────────────────────────────────────────────

    Task<KeycloakSettingsDto> GetKeycloakSettingsAsync();

    Task SaveKeycloakSettingsAsync(KeycloakSettingsDto input);

    /// <summary>Проверить связь: получить токен служебной учётки и прочитать пользователей.</summary>
    Task<ConnectionTestResultDto> TestKeycloakAsync();

    /// <summary>
    /// Сверить учётки со списком пользователей Keycloak: завести новых с ключами,
    /// отключить тех, у кого сняли доступ, включить обратно тех, кому вернули.
    /// </summary>
    Task<KeycloakSyncReport> SyncFromKeycloakAsync();
}