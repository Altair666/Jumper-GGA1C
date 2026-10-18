using System;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Учётная запись виджета. Ключ целиком здесь не передаётся — только первые символы,
/// чтобы различать учётки в интерфейсе. Полный ключ отдаётся отдельным вызовом и только
/// по явному действию администратора.
/// </summary>
public class LabelClientDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Папка буфера внутри корня буферов.</summary>
    public string Slug { get; set; } = "";

    public string KeyPrefix { get; set; } = "";

    public bool IsEnabled { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>Полный путь к файлу буфера — его прописывают в DSN для GoLabel2.</summary>
    public string BufferPath { get; set; } = "";

    /// <summary>Сколько этикеток сейчас в буфере этой учётки.</summary>
    public int RowCount { get; set; }
}
