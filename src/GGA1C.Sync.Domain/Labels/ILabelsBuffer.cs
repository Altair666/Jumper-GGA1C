using System.Collections.Generic;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Буфер этикеток одной учётной записи: его наполняет виджет Grist, а читает GoLabel2 —
/// напрямую из файла через ODBC или из выгрузки, но не через этот интерфейс.
/// </summary>
public interface ILabelsBuffer
{
    /// <summary>Полный путь к файлу — его нужно показать администратору и прописать в DSN.</summary>
    string DatabasePath { get; }

    List<LabelRecord> GetAll();

    /// <summary>
    /// Первые строки буфера (новые сверху). Нужно интерфейсу: показать таблицу буфера,
    /// не вытягивая из файла всю партию.
    /// </summary>
    List<LabelRecord> GetTop(int count);

    int Count();

    /// <summary>Добавить или обновить записи по ExternalId.</summary>
    void UpsertMany(IEnumerable<LabelRecord> records);

    /// <summary>Заменить содержимое буфера целиком — основной режим работы виджета:
    /// каждая синхронизация означает «в буфере ровно текущая партия».</summary>
    void ReplaceAll(IEnumerable<LabelRecord> records);

    bool Delete(string externalId);

    void ClearAll();
}
