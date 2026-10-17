using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Работа с буфером этикеток. Пишет сюда виджет Grist (через GristLabelsController,
/// авторизация по ключу), читает GoLabel2 — но не через этот сервис, а напрямую из файла
/// буфера по ODBC или из выгрузки CSV.
///
/// <para>
/// <b>RemoteService отключён намеренно.</b> ABP по соглашению публикует сервисы приложения
/// как REST API (здесь это настроено в SyncBlazorModule через ConventionalControllers).
/// Для этого сервиса такая публикация недопустима: эндпоинты получались анонимными, то есть
/// любой в сети мог прочитать буфер через /api/app/labels и вытереть его через
/// /api/app/labels/clear-all. Проверено вживую — GET отвечал 200 без всякого ключа.
/// Единственная HTTP-дверь к буферу должна быть одна: GristLabelsController, который
/// сверяет ключ. Интерфейс остаётся доступен изнутри приложения — контроллеру и страницам.
/// </para>
/// </summary>
[RemoteService(IsEnabled = false)]
public interface ILabelsAppService : IApplicationService
{
    /// <summary>Сверяет присланный ключ с шифрованной настройкой Sync.GristApiKey.
    /// Пустая настройка означает «доступ закрыт», а не «пускать всех».</summary>
    Task<bool> IsApiKeyValidAsync(string? providedKey);

    Task<List<LabelRecord>> GetAllAsync();

    Task<int> UpsertManyAsync(List<LabelRecord> records);

    /// <summary>Заменить буфер целиком — основной режим виджета.</summary>
    Task<int> ReplaceAllAsync(List<LabelRecord> records);

    Task<bool> DeleteAsync(string externalId);

    Task ClearAllAsync();

    /// <summary>Текущий ключ для виджета. Только для вошедших пользователей.</summary>
    Task<string?> GetApiKeyAsync();

    /// <summary>Выпустить новый ключ. Старый сразу перестаёт работать — его придётся
    /// обновить в настройках виджета в Grist.</summary>
    Task<string> RegenerateApiKeyAsync();
}
