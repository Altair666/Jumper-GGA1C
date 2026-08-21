namespace AltiumSync.Models;

public class ComponentMap
{
    public int    Id             { get; set; }
    public string AltiumId       { get; set; } = "";    // R_...
    public string AltiumItemGuid { get; set; } = "";    // ItemGUID
    public string AltiumName     { get; set; } = "";    // Comment (артикул)
    public string AltiumPath     { get; set; } = "";    // путь тега
    public string Article        { get; set; } = "";    // Comment
    public string Description    { get; set; } = "";
    public string Manufacturer   { get; set; } = "";
    public string Name1C         { get; set; } = "";    // "Наименование 1С" из Altium
    public string OnecGuid       { get; set; } = "";
    public string OnecName       { get; set; } = "";
    public string SyncStatus     { get; set; } = "Pending";
    public string? ErrorMessage  { get; set; }
    public DateTime? SyncedAt   { get; set; }
}

public class FolderMap
{
    public int    Id               { get; set; }
    public string AltiumFolderId   { get; set; } = "";
    public string AltiumFolderPath { get; set; } = "";
    public string OnecGroupGuid    { get; set; } = "";
    public string OnecGroupPath    { get; set; } = "";
    public bool   AutoCreated      { get; set; }
}

public class ProjectMap
{
    public int    Id                  { get; set; }
    public string AltiumProjectId     { get; set; } = "";
    public string AltiumProjectName   { get; set; } = "";
    public string AltiumRevision      { get; set; } = "";
    public string OnecNomenclatureGuid { get; set; } = "";
    public string OnecSpecGuid        { get; set; } = "";
    public string SyncAction          { get; set; } = "Skip"; // Update | New | Skip
    public string SyncStatus          { get; set; } = "Pending";
    public string? ErrorMessage       { get; set; }
    public DateTime? SyncedAt        { get; set; }
}

public class SyncLogEntry
{
    public int      Id         { get; set; }
    public DateTime Timestamp  { get; set; }
    public string   EntityType { get; set; } = ""; // Component | Folder | Project
    public string   EntityId   { get; set; } = "";
    public string   EntityName { get; set; } = "";
    public string   Action     { get; set; } = ""; // Create | Update | Skip | Error
    public string   Status     { get; set; } = ""; // OK | Error
    public string?  Message    { get; set; }
}

// DTO для очереди (приходит от Add-In)
public class SyncQueueItem
{
    public string Type        { get; set; } = ""; // "component" | "project"
    public string AltiumId    { get; set; } = "";
    public string AltiumName  { get; set; } = "";
    public string AltiumPath  { get; set; } = "";
    public string SyncAction  { get; set; } = "Skip";
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<BomLine> BomLines { get; set; } = new();
}

public record BomLine
{
    public string Designator { get; init; } = "";
    public string AltiumId   { get; init; } = "";
    public string Name       { get; init; } = "";
    public int    Quantity   { get; init; }
    public string Unit       { get; init; } = "шт";
}

// Запись для печати этикеток (Grist → GoLabel2)
public class LabelRecord
{
    public int      Id          { get; set; }
    public string   ExternalId  { get; set; } = "";   // ID строки в Grist — ключ для upsert/delete
    public string   Date        { get; set; } = "";   // "август 2026" — из виджета Grist
    public string   ProductCode { get; set; } = "";   // Products.product_code
    public string   ModeCode    { get; set; } = "";   // Modifications.mode_code
    public string   Article     { get; set; } = "";
    public string   Name        { get; set; } = "";
    public string   Manufacturer{ get; set; } = "";
    public string   Barcode     { get; set; } = "";
    public string   Qty         { get; set; } = "";
    public string   Extra1      { get; set; } = "";
    public string   Extra2      { get; set; } = "";
    public string   Extra3      { get; set; } = "";
    public DateTime UpdatedAt   { get; set; }
}

// Настройки подключений
public class AppSettings
{
    public string AltiumServerUrl        { get; set; } = "";
    public string AltiumUser             { get; set; } = "";
    public string AltiumPassword         { get; set; } = "";
    // Куки браузера — если заданы, используются вместо программной авторизации
    public string AltiumBrowserCookies   { get; set; } = "";
    public string AltiumApiComponents    { get; set; } = "/components/api/components";
    public string AltiumApiFolders       { get; set; } = "/components/api/tags";
    public string AltiumApiProjects      { get; set; } = "/api/v1/projects";
    public string AltiumLifecycleFilter  { get; set; } = "";
    public string OnecServerUrl          { get; set; } = "";
    public string OnecBaseName           { get; set; } = "";
    public string OnecUser               { get; set; } = "";
    public string OnecPassword           { get; set; } = "";
    public int    SyncIntervalMin        { get; set; } = 60;
    public bool   AutoSyncEnabled        { get; set; } = false;
    public string DefaultSyncAction      { get; set; } = "Skip";
    // Grist → GoLabel2: ключ для авторизации входящих запросов от Grist
    public string GristApiKey            { get; set; } = "";
}
