using Dapper;
using Microsoft.Data.Sqlite;
using AltiumSync.Models;

namespace AltiumSync.Data;

/// <summary>
/// Отдельный физический файл БД (labels.db), не смешанный с sync.db.
/// Это позволяет GoLabel2 подключаться через ODBC только к этому файлу
/// и настроить DSN в режиме "только чтение", в то время как Grist
/// пишет сюда через наш HTTP API (полный CRUD, отдельный API-ключ).
/// </summary>
public class LabelsDatabase
{
    private readonly string _connectionString;

    public LabelsDatabase(IConfiguration config)
    {
        // labels.db кладём рядом с основной БД, в той же папке
        var mainPath = config["DatabasePath"] ?? "sync.db";
        var dir      = Path.GetDirectoryName(Path.GetFullPath(mainPath)) ?? ".";
        var path     = Path.Combine(dir, "labels.db");

        _connectionString = $"Data Source={path}";
        InitSchema();
    }

    private SqliteConnection Connect() => new(_connectionString);

    private void InitSchema()
    {
        using var db = Connect();
        db.Open();
        db.Execute("""
            CREATE TABLE IF NOT EXISTS labels (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                external_id  TEXT NOT NULL UNIQUE,
                date         TEXT NOT NULL DEFAULT '',
                product_code TEXT NOT NULL DEFAULT '',
                mode_code    TEXT NOT NULL DEFAULT '',
                article      TEXT NOT NULL DEFAULT '',
                name         TEXT NOT NULL DEFAULT '',
                manufacturer TEXT NOT NULL DEFAULT '',
                barcode      TEXT NOT NULL DEFAULT '',
                qty          TEXT NOT NULL DEFAULT '',
                extra1       TEXT NOT NULL DEFAULT '',
                extra2       TEXT NOT NULL DEFAULT '',
                extra3       TEXT NOT NULL DEFAULT '',
                updated_at   TEXT NOT NULL
            );
        """);

        // Миграция уже существующих labels.db (созданных до появления полей Grist-виджета).
        // SQLite не умеет ADD COLUMN IF NOT EXISTS — смотрим фактический список колонок.
        var existing = db.Query<string>("SELECT name FROM pragma_table_info('labels')")
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var col in new[] { "date", "product_code", "mode_code" })
            if (!existing.Contains(col))
                db.Execute($"ALTER TABLE labels ADD COLUMN {col} TEXT NOT NULL DEFAULT ''");
    }

    private const string Select = """
        SELECT
            id           AS Id,
            external_id  AS ExternalId,
            date         AS Date,
            product_code AS ProductCode,
            mode_code    AS ModeCode,
            article      AS Article,
            name         AS Name,
            manufacturer AS Manufacturer,
            barcode      AS Barcode,
            qty          AS Qty,
            extra1       AS Extra1,
            extra2       AS Extra2,
            extra3       AS Extra3,
            updated_at   AS UpdatedAt
        FROM labels
        """;

    // Один список колонок/значений на все операции вставки — чтобы поля не разъезжались
    private const string InsertSql = """
        INSERT INTO labels
            (external_id, date, product_code, mode_code, article, name, manufacturer,
             barcode, qty, extra1, extra2, extra3, updated_at)
        VALUES
            (@ExternalId, @Date, @ProductCode, @ModeCode, @Article, @Name, @Manufacturer,
             @Barcode, @Qty, @Extra1, @Extra2, @Extra3, @Now)
        """;

    private const string UpsertSql = InsertSql + """

        ON CONFLICT(external_id) DO UPDATE SET
            date         = excluded.date,
            product_code = excluded.product_code,
            mode_code    = excluded.mode_code,
            article      = excluded.article,
            name         = excluded.name,
            manufacturer = excluded.manufacturer,
            barcode      = excluded.barcode,
            qty          = excluded.qty,
            extra1       = excluded.extra1,
            extra2       = excluded.extra2,
            extra3       = excluded.extra3,
            updated_at   = excluded.updated_at
        """;

    private static object Params(LabelRecord r) => new
    {
        r.ExternalId, r.Date, r.ProductCode, r.ModeCode,
        r.Article, r.Name, r.Manufacturer, r.Barcode, r.Qty,
        r.Extra1, r.Extra2, r.Extra3,
        Now = DateTime.UtcNow.ToString("o"),
    };

    public List<LabelRecord> GetAll()
    {
        using var db = Connect();
        return db.Query<LabelRecord>(Select + " ORDER BY id DESC").ToList();
    }

    public int Count()
    {
        using var db = Connect();
        return db.ExecuteScalar<int>("SELECT COUNT(*) FROM labels");
    }

    // Вставка/обновление одной записи по ExternalId (ключ строки в Grist)
    public void Upsert(LabelRecord r)
    {
        using var db = Connect();
        db.Execute(UpsertSql, Params(r));
    }

    public void UpsertMany(IEnumerable<LabelRecord> records)
    {
        using var db = Connect();
        db.Open();
        using var tx = db.BeginTransaction();
        foreach (var r in records)
            db.Execute(UpsertSql, Params(r), tx);
        tx.Commit();
    }

    public bool Delete(string externalId)
    {
        using var db = Connect();
        var rows = db.Execute("DELETE FROM labels WHERE external_id = @id", new { id = externalId });
        return rows > 0;
    }

    // Полная перезапись таблицы — Grist присылает весь снапшот, мы заменяем всё
    public void ReplaceAll(IEnumerable<LabelRecord> records)
    {
        using var db = Connect();
        db.Open();
        using var tx = db.BeginTransaction();
        db.Execute("DELETE FROM labels", transaction: tx);
        foreach (var r in records)
            db.Execute(InsertSql, Params(r), tx);
        tx.Commit();
    }

    public void ClearAll()
    {
        using var db = Connect();
        db.Execute("DELETE FROM labels");
    }
}
