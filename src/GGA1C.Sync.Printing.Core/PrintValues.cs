namespace GGA1C.Sync.Printing;

/// <summary>
/// Значения, которые печать получает от хаба вместо окон запроса GoLabel: стартовые значения
/// счётчиков (как ключ <c>-S&lt;n&gt; &lt;значение&gt;</c> GoLabel), значения переменных
/// (<c>-V&lt;nn&gt; &lt;значение&gt;</c>) и часы. Дата и время берутся только из <see cref="Clock"/>,
/// не из <see cref="DateTime.Now"/>, — так задание воспроизводимо в тестах.
/// </summary>
public sealed class PrintValues
{
    /// <summary>Счётчики: номер счётчика (C00 = 0) → стартовое значение.</summary>
    public IReadOnlyDictionary<int, string> Serials { get; init; } = new Dictionary<int, string>();

    /// <summary>Переменные: номер переменной (V00 = 0) → значение.</summary>
    public IReadOnlyDictionary<int, string> Variables { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// Счётчик без значения в <see cref="Serials"/> начинается со значения из шаблона (как если бы
    /// в окне запроса GoLabel нажали «ОК»). Иначе — ошибка: значение должен дать хаб.
    /// </summary>
    public bool SerialDefaultsFromTemplate { get; init; }

    /// <summary>
    /// Строки данных для шаблона, привязанного к базе (поля ^F00, ^F01 … — по порядку столбцов).
    /// Даёт хаб вместо запроса GoLabel к MySQL/CSV.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object?>>? Records { get; init; }

    /// <summary>Часы для даты и времени, которые печать подставляет сама.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
