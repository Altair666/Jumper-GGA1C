using System.Collections.Generic;
using System.Threading.Tasks;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Буфер этикеток в базе хаба. Пишет сюда виджет Grist (через контроллер), читает
/// GoLabel II — но не через хаб, а напрямую из MySQL по своим учётным данным, через
/// представление <c>my_labels</c>, которое отдаёт человеку только его строки.
/// </summary>
public interface ILabelsStore
{
    Task<int> CountAsync(string slug);

    /// <summary>Первые строки учётки (новые сверху) — для таблицы в интерфейсе.</summary>
    Task<List<LabelRecord>> GetTopAsync(string slug, int count);

    Task<List<LabelRecord>> GetAllAsync(string slug);

    /// <summary>Добавить или обновить строки по ExternalId внутри учётки.</summary>
    Task<int> UpsertManyAsync(string slug, IEnumerable<LabelRecord> records);

    /// <summary>
    /// Заменить буфер учётки целиком — основной режим виджета: одна синхронизация
    /// означает «в буфере ровно текущая партия». Чужие строки не затрагиваются.
    /// </summary>
    Task<int> ReplaceAllAsync(string slug, IEnumerable<LabelRecord> records);

    Task<bool> DeleteAsync(string slug, string externalId);

    /// <summary>Очистить буфер учётки.</summary>
    Task<int> ClearAsync(string slug);

    /// <summary>Сведения о буферах всех учёток: сколько строк и когда писали последний раз.</summary>
    Task<List<LabelsBufferInfo>> GetBuffersAsync();
}

/// <summary>Сколько строк в буфере учётки и когда в него писали.</summary>
public record LabelsBufferInfo(string Slug, int RowCount, System.DateTime? UpdatedAt);
