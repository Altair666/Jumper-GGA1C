using System;
using System.Collections.Generic;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Содержимое одного буфера: то, что сейчас лежит в файле и что прочитает GoLabel2.
///
/// Один и тот же вид используется и для буферов учёток, и для осиротевших папок —
/// у вторых <see cref="ClientId"/> пуст. Так в интерфейсе виден весь корень буферов
/// целиком, включая папки, оставшиеся после удалённых учёток: иначе они копились бы
/// на сервере незамеченными.
/// </summary>
public class BufferContentsDto
{
    /// <summary>Пусто, если папке не соответствует ни одна учётная запись.</summary>
    public int? ClientId { get; set; }

    /// <summary>Имя учётки или пометка о том, что хозяина нет.</summary>
    public string Name { get; set; } = "";

    public string Slug { get; set; } = "";

    /// <summary>Полный путь к файлу — его прописывают в источнике ODBC для GoLabel2.</summary>
    public string Path { get; set; } = "";

    public bool FileExists { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Когда в файл последний раз писали.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Сколько строк в буфере всего.</summary>
    public int RowCount { get; set; }

    /// <summary>Первые строки буфера — показать таблицу, не вытягивая её целиком.</summary>
    public List<LabelRecord> Rows { get; set; } = new();
}
