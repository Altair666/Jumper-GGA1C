using Volo.Abp.Domain.Entities.Auditing;

namespace GGA1C.Sync.Altium;

/// <summary>
/// Соответствие папки (тега) Altium и группы номенклатуры 1С.
/// </summary>
public class FolderMap : AuditedEntity<int>
{
    public string AltiumFolderId { get; set; } = "";

    public string AltiumFolderPath { get; set; } = "";

    public string OnecGroupGuid { get; set; } = "";

    public string OnecGroupPath { get; set; } = "";

    /// <summary>Группа в 1С создана нами автоматически, а не сопоставлена вручную.</summary>
    public bool AutoCreated { get; set; }
}
