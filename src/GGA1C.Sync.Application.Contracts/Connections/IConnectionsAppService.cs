using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Connections;

/// <summary>
/// Чтение и запись настроек подключений плюс проверка связи.
///
/// RemoteService отключён по той же причине, что и у буфера этикеток: ABP по соглашению
/// публикует сервисы приложения как REST API, и здесь это дало бы анонимный эндпоинт,
/// через который можно читать и перезаписывать адреса и пароли Altium и 1С. Сервис нужен
/// только изнутри — странице настроек.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IConnectionsAppService : IApplicationService
{
    Task<ConnectionSettingsDto> GetAsync();

    Task SaveAsync(ConnectionSettingsDto input);

    /// <summary>Проверить связь с Altium ТЕКУЩИМИ сохранёнными настройками.</summary>
    Task<ConnectionTestResultDto> TestAltiumAsync();

    /// <summary>Проверить связь с 1С текущими сохранёнными настройками.</summary>
    Task<ConnectionTestResultDto> TestOneCAsync();
}
