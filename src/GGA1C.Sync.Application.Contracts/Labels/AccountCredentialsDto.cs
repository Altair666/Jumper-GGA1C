using System;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Учётные данные человека во внешних системах — карточка учётки в интерфейсе.
///
/// Пароли наружу не отдаются: в ответе они всегда пусты, а есть ли сохранённое значение,
/// говорят <see cref="HasAltiumPassword"/> и <see cref="HasPdmPassword"/>. При сохранении
/// пустой пароль означает «оставить прежний» — то же правило, что у секрета Keycloak.
/// Логины не секретны и ходят в обе стороны как есть.
/// </summary>
public class AccountCredentialsDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string AltiumLogin { get; set; } = "";

    public string AltiumPassword { get; set; } = "";

    public bool HasAltiumPassword { get; set; }

    public string PdmLogin { get; set; } = "";

    public string PdmPassword { get; set; } = "";

    public bool HasPdmPassword { get; set; }

    /// <summary>
    /// Логин MySQL для GoLabel II — это почта человека. Поле только для чтения: меняется
    /// вместе с почтой.
    /// </summary>
    public string PrintLogin { get; set; } = "";

    /// <summary>
    /// Пароль MySQL. В отличие от остальных, его можно посмотреть кнопкой «глаз»: человек
    /// вписывает его в GoLabel руками. Здесь приходит маской, настоящее значение отдаёт
    /// отдельный вызов.
    /// </summary>
    public string PrintPassword { get; set; } = "";

    public bool HasPrintPassword { get; set; }

    /// <summary>Когда данные меняли последний раз — чтобы видеть, не устарел ли пароль.</summary>
    public DateTime? UpdatedAt { get; set; }
}
