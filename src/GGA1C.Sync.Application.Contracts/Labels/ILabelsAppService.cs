using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Работа с буфером этикеток. Пишет сюда виджет Grist (через GristLabelsController,
/// авторизация по ключу), читает GoLabel2 — но не через этот сервис, а напрямую из файла
/// буфера по ODBC или из выгрузки CSV.
/// </summary>
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
}
