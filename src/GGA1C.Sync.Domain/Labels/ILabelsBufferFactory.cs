namespace GGA1C.Sync.Labels;

/// <summary>
/// Выдаёт буфер по учётной записи. У каждой учётки свой файл в своей папке, поэтому
/// синхронизация одного оператора не затирает партию другого.
/// </summary>
public interface ILabelsBufferFactory
{
    /// <summary>Корень, внутри которого лежат папки учёток. Нужен для подсказок в интерфейсе.</summary>
    string BuffersRoot { get; }

    ILabelsBuffer For(string slug);

    /// <summary>Путь к файлу буфера, не открывая его.</summary>
    string PathFor(string slug);
}
