using System.Collections.Generic;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Буфер этикеток, который наполняет Grist и читает GoLabel2.
///
/// Вынесено за интерфейс не ради красоты: по требованию у каждой учётной записи должен быть
/// свой буфер (своя папка со своим файлом). Сейчас реализация одна и общая, но когда вопрос
/// опознания пользователя решится, достаточно будет подменить реализацию на такую, что
/// выбирает файл по текущему пользователю — вызывающий код останется прежним.
/// </summary>
public interface ILabelsBuffer
{
    List<LabelRecord> GetAll();

    int Count();

    /// <summary>Добавить или обновить записи по ExternalId.</summary>
    void UpsertMany(IEnumerable<LabelRecord> records);

    /// <summary>Заменить содержимое буфера целиком — основной режим работы виджета:
    /// каждая синхронизация означает «в буфере ровно текущая партия».</summary>
    void ReplaceAll(IEnumerable<LabelRecord> records);

    bool Delete(string externalId);

    void ClearAll();
}
