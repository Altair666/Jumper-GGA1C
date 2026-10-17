using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace GGA1C.Sync.Altium;

/// <summary>
/// Соответствие компонента Altium Enterprise Server и номенклатуры 1С.
///
/// Altium не хранит у себя ссылку на 1С, поэтому связь держим у себя. Ключ сопоставления -
/// AltiumId (вид R_...): по нему работал upsert в прежней версии и он есть во всех ответах
/// API. ItemGUID приходит не всегда, поэтому как ключ не годится.
/// </summary>
public class ComponentMap : AuditedEntity<int>
{
    /// <summary>Идентификатор вида R_... — то, что показывает веб-интерфейс Altium.</summary>
    public string AltiumId { get; set; } = "";

    /// <summary>Устойчивый ключ компонента в Altium.</summary>
    public string AltiumItemGuid { get; set; } = "";

    /// <summary>Поле Comment в Altium — фактически артикул.</summary>
    public string AltiumName { get; set; } = "";

    /// <summary>Путь тега (папки) в Altium.</summary>
    public string AltiumPath { get; set; } = "";

    public string Article { get; set; } = "";
    public string Description { get; set; } = "";
    public string Manufacturer { get; set; } = "";

    /// <summary>Кастомное поле «Наименование 1С» из карточки компонента Altium.</summary>
    public string Name1C { get; set; } = "";

    public string OnecGuid { get; set; } = "";
    public string OnecName { get; set; } = "";

    /// <summary>Pending | OK | Error | Skipped</summary>
    public string SyncStatus { get; set; } = "Pending";

    public string? ErrorMessage { get; set; }

    public DateTime? SyncedAt { get; set; }
}
