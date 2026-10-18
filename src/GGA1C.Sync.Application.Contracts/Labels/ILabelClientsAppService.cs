using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Connections;
using GGA1C.Sync.Grist;
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

    /// <summary>
    /// Создать учётку. Ключ возвращается в открытом виде один раз — при создании.
    /// Папка буфера не передаётся: она выводится из фамилии, а если из неё выжать нечего —
    /// из email. Спрашивать её у человека незачем, а ошибиться в пути на диске легко.
    /// </summary>
    Task<string> CreateAsync(string name, string? email);

    Task<string> RegenerateKeyAsync(int id);

    /// <summary>Показать ключ повторно. Расшифровывает сохранённую копию.</summary>
    Task<string?> RevealKeyAsync(int id);

    Task SetEnabledAsync(int id, bool enabled);

    /// <summary>
    /// Удалить учётку вместе с папкой буфера, чтобы не копить мусор на сервере. Если файл
    /// занят (открыт источник ODBC), ключ всё равно уже отозван, а папка остаётся видна
    /// в интерфейсе как буфер без учётной записи.
    /// </summary>
    Task DeleteAsync(int id);

    /// <summary>Очистить буфер учётки, не трогая саму учётку.</summary>
    Task ClearBufferAsync(int id);

    // ── Буферы ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Все буферы в корне: содержимое по каждой учётке и папки без хозяина.
    /// <paramref name="maxRows"/> ограничивает показываемые строки — в буфере их может
    /// быть сколько угодно, а в интерфейсе нужна таблица, а не выгрузка.
    /// </summary>
    Task<List<BufferContentsDto>> GetBuffersAsync(int maxRows = 50);

    /// <summary>
    /// Удалить папку буфера, которой не соответствует ни одна учётка. Учётный буфер
    /// так не удалить — для него есть удаление вместе с учёткой.
    /// </summary>
    Task DeleteBufferFolderAsync(string slug);

    // ── Учётные данные во внешних системах ────────────────────────────────────

    /// <summary>Логины Altium и PDM плюс признаки «пароль задан». Пароли не возвращаются.</summary>
    Task<AccountCredentialsDto> GetCredentialsAsync(int id);

    /// <summary>Сохранить учётные данные. Пустой пароль оставляет прежний.</summary>
    Task SaveCredentialsAsync(AccountCredentialsDto input);

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

    // ── Опознание людей через Grist ───────────────────────────────────────────

    Task<GristSettingsDto> GetGristSettingsAsync();

    Task SaveGristSettingsAsync(GristSettingsDto input);

    /// <summary>Проверить связь: прочитать таблицу с адресами почты в документе.</summary>
    Task<ConnectionTestResultDto> TestGristAsync();
}