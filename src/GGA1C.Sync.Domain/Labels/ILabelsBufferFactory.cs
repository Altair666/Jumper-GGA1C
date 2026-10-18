using System;
using System.Collections.Generic;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Сведения о файле буфера, снятые с диска. Нужны в интерфейсе: по размеру и времени
/// изменения видно, живой буфер или в него с прошлой недели никто не писал.
/// </summary>
public record BufferFileInfo(string Path, bool Exists, long SizeBytes, DateTime? UpdatedAt);

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

    /// <summary>Размер и время изменения файла буфера, не открывая его.</summary>
    BufferFileInfo Describe(string slug);

    /// <summary>
    /// Имена папок, фактически лежащих в корне буферов. Нужно, чтобы показать в интерфейсе
    /// буферы без учётки: удалённая учётка могла оставить папку, если файл в тот момент
    /// держал открытый источник ODBC.
    /// </summary>
    List<string> ListFolders();

    /// <summary>
    /// Удалить буфер учётки вместе с папкой, чтобы не копить мусор на сервере.
    /// Вызывается при удалении учётной записи.
    /// </summary>
    void Delete(string slug);
}
