using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace GGA1C.Sync.Altium;

/// <summary>
/// Соответствие проекта Altium и пары «номенклатура + спецификация» в 1С.
/// </summary>
public class ProjectMap : AuditedEntity<int>
{
    public string AltiumProjectId { get; set; } = "";

    public string AltiumProjectName { get; set; } = "";

    public string AltiumRevision { get; set; } = "";

    public string OnecNomenclatureGuid { get; set; } = "";

    public string OnecSpecGuid { get; set; } = "";

    /// <summary>Что делать при синхронизации: Update | New | Skip.</summary>
    public string SyncAction { get; set; } = "Skip";

    /// <summary>Pending | OK | Error | Skipped</summary>
    public string SyncStatus { get; set; } = "Pending";

    public string? ErrorMessage { get; set; }

    public DateTime? SyncedAt { get; set; }
}
