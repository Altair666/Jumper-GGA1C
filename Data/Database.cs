using Dapper;
using Microsoft.Data.Sqlite;
using AltiumSync.Models;

namespace AltiumSync.Data;

public class Database
{
    private readonly string _connectionString;

    public Database(IConfiguration config)
    {
        var path = config["DatabasePath"] ?? "sync.db";
        _connectionString = $"Data Source={path}";
        InitSchema();
    }

    private SqliteConnection Connect() => new(_connectionString);

    private void InitSchema()
    {
        using var db = Connect();
        db.Open();
        db.Execute("""
            CREATE TABLE IF NOT EXISTS component_map (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                altium_id       TEXT NOT NULL UNIQUE,
                altium_item_guid TEXT NOT NULL DEFAULT '',
                altium_name     TEXT NOT NULL DEFAULT '',
                altium_path     TEXT NOT NULL DEFAULT '',
                article         TEXT NOT NULL DEFAULT '',
                description     TEXT NOT NULL DEFAULT '',
                manufacturer    TEXT NOT NULL DEFAULT '',
                name_1c         TEXT NOT NULL DEFAULT '',
                onec_guid       TEXT NOT NULL DEFAULT '',
                onec_name       TEXT NOT NULL DEFAULT '',
                sync_status     TEXT NOT NULL DEFAULT 'Pending',
                error_message   TEXT,
                synced_at       TEXT
            );

            CREATE TABLE IF NOT EXISTS folder_map (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                altium_folder_id    TEXT NOT NULL UNIQUE,
                altium_folder_path  TEXT NOT NULL DEFAULT '',
                onec_group_guid     TEXT NOT NULL DEFAULT '',
                onec_group_path     TEXT NOT NULL DEFAULT '',
                auto_created        INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS project_map (
                id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                altium_project_id       TEXT NOT NULL,
                altium_project_name     TEXT NOT NULL DEFAULT '',
                altium_revision         TEXT NOT NULL DEFAULT '',
                onec_nomenclature_guid  TEXT NOT NULL DEFAULT '',
                onec_spec_guid          TEXT NOT NULL DEFAULT '',
                sync_action             TEXT NOT NULL DEFAULT 'Skip',
                sync_status             TEXT NOT NULL DEFAULT 'Pending',
                error_message           TEXT,
                synced_at               TEXT,
                UNIQUE(altium_project_id, altium_revision)
            );

            CREATE TABLE IF NOT EXISTS sync_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   TEXT NOT NULL,
                entity_type TEXT NOT NULL DEFAULT '',
                entity_id   TEXT NOT NULL DEFAULT '',
                entity_name TEXT NOT NULL DEFAULT '',
                action      TEXT NOT NULL DEFAULT '',
                status      TEXT NOT NULL DEFAULT '',
                message     TEXT
            );

            CREATE TABLE IF NOT EXISTS request_log (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp  TEXT NOT NULL,
                source     TEXT NOT NULL DEFAULT '',
                method     TEXT NOT NULL DEFAULT '',
                path       TEXT NOT NULL DEFAULT '',
                headers    TEXT NOT NULL DEFAULT '',
                body       TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS oauth_codes (
                code        TEXT PRIMARY KEY,
                created_at  TEXT NOT NULL,
                used        INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL DEFAULT ''
            );

        """);
    }

    // ── Component Map ────────────────────────────────────────────────────────

    // Алиасы нужны потому что Dapper не конвертирует snake_case → PascalCase автоматически
    private const string ComponentSelect = """
        SELECT
            id               AS Id,
            altium_id        AS AltiumId,
            altium_item_guid AS AltiumItemGuid,
            altium_name      AS AltiumName,
            altium_path      AS AltiumPath,
            article          AS Article,
            description      AS Description,
            manufacturer     AS Manufacturer,
            name_1c          AS Name1C,
            onec_guid        AS OnecGuid,
            onec_name        AS OnecName,
            sync_status      AS SyncStatus,
            error_message    AS ErrorMessage,
            synced_at        AS SyncedAt
        FROM component_map
        """;

    private const string FolderSelect = """
        SELECT
            id                  AS Id,
            altium_folder_id    AS AltiumFolderId,
            altium_folder_path  AS AltiumFolderPath,
            onec_group_guid     AS OnecGroupGuid,
            onec_group_path     AS OnecGroupPath,
            auto_created        AS AutoCreated
        FROM folder_map
        """;

    private const string ProjectSelect = """
        SELECT
            id                      AS Id,
            altium_project_id       AS AltiumProjectId,
            altium_project_name     AS AltiumProjectName,
            altium_revision         AS AltiumRevision,
            onec_nomenclature_guid  AS OnecNomenclatureGuid,
            onec_spec_guid          AS OnecSpecGuid,
            sync_action             AS SyncAction,
            sync_status             AS SyncStatus,
            error_message           AS ErrorMessage,
            synced_at               AS SyncedAt
        FROM project_map
        """;

    private const string LogSelect = """
        SELECT
            id          AS Id,
            timestamp   AS Timestamp,
            entity_type AS EntityType,
            entity_id   AS EntityId,
            entity_name AS EntityName,
            action      AS Action,
            status      AS Status,
            message     AS Message
        FROM sync_log
        """;

    public IEnumerable<ComponentMap> GetComponents(string? status = null)
    {
        using var db = Connect();
        var sql = status == null
            ? $"{ComponentSelect} ORDER BY altium_path, altium_name"
            : $"{ComponentSelect} WHERE sync_status = @status ORDER BY altium_path, altium_name";
        return db.Query<ComponentMap>(sql, new { status }).ToList();
    }

    public ComponentMap? GetComponentByAltiumId(string altiumId)
    {
        using var db = Connect();
        return db.QueryFirstOrDefault<ComponentMap>(
            $"{ComponentSelect} WHERE altium_id = @altiumId", new { altiumId });
    }

    public ComponentMap? GetComponentByArticle(string article)
    {
        using var db = Connect();
        return db.QueryFirstOrDefault<ComponentMap>(
            $"{ComponentSelect} WHERE article = @article", new { article });
    }

    public void UpsertComponent(ComponentMap c)
    {
        using var db = Connect();
        db.Execute("""
            INSERT INTO component_map
                (altium_id, altium_item_guid, altium_name, altium_path, article,
                 description, manufacturer, name_1c,
                 onec_guid, onec_name, sync_status, error_message, synced_at)
            VALUES
                (@AltiumId, @AltiumItemGuid, @AltiumName, @AltiumPath, @Article,
                 @Description, @Manufacturer, @Name1C,
                 @OnecGuid, @OnecName, @SyncStatus, @ErrorMessage, @SyncedAt)
            ON CONFLICT(altium_id) DO UPDATE SET
                altium_item_guid = excluded.altium_item_guid,
                altium_name      = excluded.altium_name,
                altium_path      = excluded.altium_path,
                article          = excluded.article,
                description      = excluded.description,
                manufacturer     = excluded.manufacturer,
                name_1c          = excluded.name_1c,
                onec_guid        = excluded.onec_guid,
                onec_name        = excluded.onec_name,
                sync_status      = excluded.sync_status,
                error_message    = excluded.error_message,
                synced_at        = excluded.synced_at
        """, c);
    }

    public void UpdateComponentStatus(string altiumId, string status, string? error = null)
    {
        using var db = Connect();
        db.Execute("""
            UPDATE component_map
            SET sync_status = @status, error_message = @error, synced_at = @now
            WHERE altium_id = @altiumId
        """, new { altiumId, status, error, now = DateTime.UtcNow.ToString("o") });
    }

    // ── Folder Map ───────────────────────────────────────────────────────────

    public IEnumerable<FolderMap> GetFolderMaps()
    {
        using var db = Connect();
        return db.Query<FolderMap>($"{FolderSelect} ORDER BY altium_folder_path").ToList();
    }

    public FolderMap? GetFolderMapByAltiumId(string altiumFolderId)
    {
        using var db = Connect();
        return db.QueryFirstOrDefault<FolderMap>(
            $"{FolderSelect} WHERE altium_folder_id = @altiumFolderId", new { altiumFolderId });
    }

    public void UpsertFolderMap(FolderMap f)
    {
        using var db = Connect();
        db.Execute("""
            INSERT INTO folder_map
                (altium_folder_id, altium_folder_path, onec_group_guid, onec_group_path, auto_created)
            VALUES
                (@AltiumFolderId, @AltiumFolderPath, @OnecGroupGuid, @OnecGroupPath, @AutoCreated)
            ON CONFLICT(altium_folder_id) DO UPDATE SET
                altium_folder_path = excluded.altium_folder_path,
                onec_group_guid    = excluded.onec_group_guid,
                onec_group_path    = excluded.onec_group_path,
                auto_created       = excluded.auto_created
        """, f);
    }

    // ── Project Map ──────────────────────────────────────────────────────────

    public IEnumerable<ProjectMap> GetProjects(string? status = null)
    {
        using var db = Connect();
        var sql = status == null
            ? $"{ProjectSelect} ORDER BY altium_project_name, altium_revision DESC"
            : $"{ProjectSelect} WHERE sync_status = @status ORDER BY altium_project_name";
        return db.Query<ProjectMap>(sql, new { status }).ToList();
    }

    public void UpsertProject(ProjectMap p)
    {
        using var db = Connect();
        db.Execute("""
            INSERT INTO project_map
                (altium_project_id, altium_project_name, altium_revision,
                 onec_nomenclature_guid, onec_spec_guid, sync_action, sync_status, error_message, synced_at)
            VALUES
                (@AltiumProjectId, @AltiumProjectName, @AltiumRevision,
                 @OnecNomenclatureGuid, @OnecSpecGuid, @SyncAction, @SyncStatus, @ErrorMessage, @SyncedAt)
            ON CONFLICT(altium_project_id, altium_revision) DO UPDATE SET
                altium_project_name     = excluded.altium_project_name,
                onec_nomenclature_guid  = excluded.onec_nomenclature_guid,
                onec_spec_guid          = excluded.onec_spec_guid,
                sync_action             = excluded.sync_action,
                sync_status             = excluded.sync_status,
                error_message           = excluded.error_message,
                synced_at               = excluded.synced_at
        """, p);
    }

    public void UpdateProjectAction(int id, string action)
    {
        using var db = Connect();
        db.Execute("UPDATE project_map SET sync_action = @action WHERE id = @id", new { id, action });
    }

    // ── Sync Log ─────────────────────────────────────────────────────────────

    public void AddLog(SyncLogEntry entry)
    {
        entry.Timestamp = DateTime.UtcNow;
        using var db = Connect();
        db.Execute("""
            INSERT INTO sync_log (timestamp, entity_type, entity_id, entity_name, action, status, message)
            VALUES (@Timestamp, @EntityType, @EntityId, @EntityName, @Action, @Status, @Message)
        """, entry);
    }

    public IEnumerable<SyncLogEntry> GetLogs(int limit = 200, string? entityType = null, string? status = null)
    {
        using var db = Connect();
        var where = new List<string>();
        if (entityType != null) where.Add("entity_type = @entityType");
        if (status != null)     where.Add("status = @status");
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        return db.Query<SyncLogEntry>(
            $"{LogSelect} {clause} ORDER BY timestamp DESC LIMIT @limit",
            new { entityType, status, limit }).ToList();
    }

    // ── Request Log ───────────────────────────────────────────────────────────

    public void SaveRequestLog(AltiumSync.Services.RequestLogItem item)
    {
        using var db = Connect();
        db.Execute("""
            INSERT INTO request_log (timestamp, source, method, path, headers, body)
            VALUES (@Timestamp, @Source, @Method, @Path, @Headers, @Body)
        """, new {
            Timestamp = item.Timestamp.ToString("o"),
            item.Source, item.Method, item.Path, item.Headers, item.Body
        });

        // Оставляем только последние 500 записей
        db.Execute("""
            DELETE FROM request_log WHERE id NOT IN (
                SELECT id FROM request_log ORDER BY id DESC LIMIT 500)
        """);
    }

    public IEnumerable<AltiumSync.Services.RequestLogItem> GetRequestLogs(
        int limit = 100, string? source = null, string? pathFilter = null)
    {
        using var db = Connect();
        var where = new List<string>();
        if (source != null)     where.Add("source = @source");
        if (pathFilter != null) where.Add("path LIKE @pathFilter");
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        return db.Query<AltiumSync.Services.RequestLogItem>($"""
            SELECT id AS Id, timestamp AS Timestamp, source AS Source,
                   method AS Method, path AS Path, headers AS Headers, body AS Body
            FROM request_log {clause}
            ORDER BY id DESC LIMIT @limit
        """, new { source, pathFilter = pathFilter != null ? $"%{pathFilter}%" : null, limit })
        .ToList();
    }

    public void ClearRequestLogs()
    {
        using var db = Connect();
        db.Execute("DELETE FROM request_log");
    }

    // ── OAuth Codes ───────────────────────────────────────────────────────────

    public void SaveOAuthCode(string code)
    {
        using var db = Connect();
        db.Execute(
            "INSERT OR REPLACE INTO oauth_codes (code, created_at, used) VALUES (@code, @now, 0)",
            new { code, now = DateTime.UtcNow.ToString("o") });

        // Чистим старые коды (старше 10 минут)
        db.Execute("DELETE FROM oauth_codes WHERE created_at < @cutoff",
            new { cutoff = DateTime.UtcNow.AddMinutes(-10).ToString("o") });
    }

    public bool ValidateAndConsumeOAuthCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        using var db = Connect();
        var row = db.QueryFirstOrDefault<(string code, string created_at, int used)>(
            "SELECT code, created_at, used FROM oauth_codes WHERE code = @code",
            new { code });

        if (row.code == null) return false;
        if (row.used == 1)    return false;

        // Отмечаем как использованный
        db.Execute("UPDATE oauth_codes SET used = 1 WHERE code = @code", new { code });
        return true;
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    public AppSettings GetSettings()
    {
        using var db = Connect();
        var rows = db.Query<(string key, string value)>(
            "SELECT key, value FROM app_settings").ToDictionary(r => r.key, r => r.value);

        var result = new AppSettings
        {
            AltiumServerUrl         = rows.GetValueOrDefault("AltiumServerUrl",         ""),
            AltiumBrowserCookies    = rows.GetValueOrDefault("AltiumBrowserCookies",    ""),
            AltiumUser              = rows.GetValueOrDefault("AltiumUser",              ""),
            AltiumPassword          = rows.GetValueOrDefault("AltiumPassword",          ""),
            AltiumApiComponents     = rows.GetValueOrDefault("AltiumApiComponents",     "/components/api/components"),
            AltiumApiFolders        = rows.GetValueOrDefault("AltiumApiFolders",        "/components/api/tags"),
            AltiumApiProjects       = rows.GetValueOrDefault("AltiumApiProjects",       "/api/v1/projects"),
            AltiumLifecycleFilter   = rows.GetValueOrDefault("AltiumLifecycleFilter",   ""),
            OnecServerUrl           = rows.GetValueOrDefault("OnecServerUrl",           ""),
            OnecBaseName            = rows.GetValueOrDefault("OnecBaseName",            ""),
            OnecUser                = rows.GetValueOrDefault("OnecUser",               ""),
            OnecPassword            = rows.GetValueOrDefault("OnecPassword",           ""),
            SyncIntervalMin         = int.TryParse(rows.GetValueOrDefault("SyncIntervalMin", "60"), out var i) ? i : 60,
            AutoSyncEnabled         = rows.GetValueOrDefault("AutoSyncEnabled",         "false") == "true",
            DefaultSyncAction       = rows.GetValueOrDefault("DefaultSyncAction",       "Skip"),
            GristApiKey             = rows.GetValueOrDefault("GristApiKey",             ""),
        };

        // Автогенерация ключа при первом обращении, чтобы Grist-интеграция
        // была готова к использованию сразу без ручного шага
        if (string.IsNullOrEmpty(result.GristApiKey))
        {
            result.GristApiKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
            db.Execute("INSERT OR REPLACE INTO app_settings (key, value) VALUES ('GristApiKey', @v)",
                new { v = result.GristApiKey });
        }

        return result;
    }

    public void SaveSettings(AppSettings s)
    {
        using var db = Connect();
        var pairs = new Dictionary<string, string>
        {
            ["AltiumServerUrl"]         = s.AltiumServerUrl,
            ["AltiumBrowserCookies"]    = s.AltiumBrowserCookies,
            ["AltiumUser"]              = s.AltiumUser,
            ["AltiumPassword"]          = s.AltiumPassword,
            ["AltiumApiComponents"]     = s.AltiumApiComponents,
            ["AltiumApiFolders"]        = s.AltiumApiFolders,
            ["AltiumApiProjects"]       = s.AltiumApiProjects,
            ["AltiumLifecycleFilter"]   = s.AltiumLifecycleFilter,
            ["OnecServerUrl"]           = s.OnecServerUrl,
            ["OnecBaseName"]            = s.OnecBaseName,
            ["OnecUser"]                = s.OnecUser,
            ["OnecPassword"]            = s.OnecPassword,
            ["SyncIntervalMin"]         = s.SyncIntervalMin.ToString(),
            ["AutoSyncEnabled"]         = s.AutoSyncEnabled ? "true" : "false",
            ["DefaultSyncAction"]       = s.DefaultSyncAction,
            ["GristApiKey"]             = s.GristApiKey,
        };
        foreach (var kv in pairs)
            db.Execute("INSERT OR REPLACE INTO app_settings (key, value) VALUES (@k, @v)",
                new { k = kv.Key, v = kv.Value });
    }

    // ── Stats ────────────────────────────────────────────────────────────────

    public object GetStats()
    {
        using var db = Connect();
        return new
        {
            TotalComponents = db.ExecuteScalar<int>("SELECT COUNT(*) FROM component_map"),
            SyncedComponents = db.ExecuteScalar<int>("SELECT COUNT(*) FROM component_map WHERE sync_status = 'Synced'"),
            ErrorComponents  = db.ExecuteScalar<int>("SELECT COUNT(*) FROM component_map WHERE sync_status = 'Error'"),
            PendingComponents = db.ExecuteScalar<int>("SELECT COUNT(*) FROM component_map WHERE sync_status = 'Pending'"),
            TotalProjects    = db.ExecuteScalar<int>("SELECT COUNT(*) FROM project_map"),
            SyncedProjects   = db.ExecuteScalar<int>("SELECT COUNT(*) FROM project_map WHERE sync_status = 'Synced'"),
            PendingProjects  = db.ExecuteScalar<int>("SELECT COUNT(*) FROM project_map WHERE sync_status = 'Pending'"),
            TotalFolders     = db.ExecuteScalar<int>("SELECT COUNT(*) FROM folder_map"),
            LastSync         = db.ExecuteScalar<string?>("SELECT MAX(timestamp) FROM sync_log WHERE status = 'OK'"),
        };
    }
}
