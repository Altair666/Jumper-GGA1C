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

    /// <summary>
    /// Логин в MySQL, которым GoLabel II забирает этикетки этого человека. Пусто, пока
    /// логин не выдан. Пароль здесь не передаётся: он показывается один раз при выдаче.
    /// </summary>
    public string PrintLogin { get; set; } = "";

    /// <summary>Сколько этикеток сейчас в буфере этой учётки.</summary>
    public int RowCount { get; set; }

    /// <summary>Заданы ли учётные данные Altium Designer. Сами данные здесь не передаются.</summary>
    public bool HasAltiumCredentials { get; set; }

    /// <summary>Заданы ли учётные данные SOLIDWORKS PDM.</summary>
    public bool HasPdmCredentials { get; set; }
}
