using System;
using System.Collections.Generic;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Содержимое одного буфера: то, что сейчас лежит в файле и что прочитает GoLabel2.
///
/// Один и тот же вид используется и для буферов учёток, и для строк, оставшихся без
/// учётной записи — у вторых <see cref="ClientId"/> пуст. Так в интерфейсе видно всё
/// содержимое таблицы, включая хвосты от удалённых учёток.
/// </summary>
public class BufferContentsDto
{
    /// <summary>Пусто, если папке не соответствует ни одна учётная запись.</summary>
    public int? ClientId { get; set; }

    /// <summary>Имя учётки или пометка о том, что хозяина нет.</summary>
    public string Name { get; set; } = "";

    public string Slug { get; set; } = "";

    /// <summary>Логин в MySQL, если буфер читают подключением к базе.</summary>
    public string PrintLogin { get; set; } = "";

    /// <summary>
    /// Файл, который читает GoLabel II. Путь прописывается в шаблоне один раз, дальше
    /// человек просто печатает — файл обновляет хаб.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>Когда в файл последний раз писали.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Сколько строк в буфере всего.</summary>
    public int RowCount { get; set; }

    /// <summary>Первые строки буфера — показать таблицу, не вытягивая её целиком.</summary>
    public List<LabelRecord> Rows { get; set; } = new();
}
