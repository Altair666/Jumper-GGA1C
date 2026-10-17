using System.Collections.Generic;

namespace GGA1C.Sync.Altium;

/// <summary>
/// Элемент очереди на синхронизацию. Приходит извне (например, от Add-In в Altium
/// Designer) и в базе не хранится — это транспортный объект.
/// </summary>
public class SyncQueueItem
{
    /// <summary>component | project</summary>
    public string Type { get; set; } = "";

    public string AltiumId { get; set; } = "";

    public string AltiumName { get; set; } = "";

    public string AltiumPath { get; set; } = "";

    /// <summary>Update | New | Skip</summary>
    public string SyncAction { get; set; } = "Skip";

    public Dictionary<string, string> Parameters { get; set; } = new();

    public List<BomLine> BomLines { get; set; } = new();
}

/// <summary>
/// Строка состава изделия (BOM).
/// </summary>
public record BomLine
{
    public string Designator { get; init; } = "";

    public string AltiumId { get; init; } = "";

    public string Name { get; init; } = "";

    public int Quantity { get; init; }

    public string Unit { get; init; } = "шт";
}
