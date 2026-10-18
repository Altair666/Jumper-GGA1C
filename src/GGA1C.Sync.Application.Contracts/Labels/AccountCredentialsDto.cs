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

    /// <summary>Когда данные меняли последний раз — чтобы видеть, не устарел ли пароль.</summary>
    public DateTime? UpdatedAt { get; set; }
}
