using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Буфер этикеток в отдельном файле SQLite. Создаётся фабрикой на каждую учётную запись:
/// у каждой свой файл, поэтому синхронизация одного оператора не затирает партию другого,
/// и блокировка записи у каждого своя — в SQLite она на уровне файла.
///
/// Режим журнала намеренно оставлен по умолчанию (delete, не WAL): в WAL читателю нужны
/// права на запись в папку, и read-only доступ GoLabel2 через ODBC перестал бы работать.
/// </summary>
public class SqliteLabelsBuffer : ILabelsBuffer
{
    private readonly string _connectionString;

    public SqliteLabelsBuffer(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Не задан путь к файлу буфера", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);

        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={DatabasePath}";
        InitSchema();
    }

    public string DatabasePath { get; }

    private SqliteConnection Connect() => new(_connectionString);

    private void InitSchema()
    {
        using var db = Connect();
        db.Open();
        db.Execute(
            """
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

        // Миграция буфера, созданного прежней версией сервиса: там не было полей виджета.
        // SQLite не умеет ADD COLUMN IF NOT EXISTS, поэтому смотрим фактический список колонок.
        var existing = db.Query<string>("SELECT name FROM pragma_table_info('labels')")
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var col in new[] { "date", "product_code", "mode_code" })
        {
            if (!existing.Contains(col))
            {
                db.Execute($"ALTER TABLE labels ADD COLUMN {col} TEXT NOT NULL DEFAULT ''");
            }
        }
    }

    private const string SelectSql =
        """
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

    // Один список колонок на все вставки — чтобы поля не разъезжались при правках
    private const string InsertSql =
        """
        INSERT INTO labels
            (external_id, date, product_code, mode_code, article, name, manufacturer,
             barcode, qty, extra1, extra2, extra3, updated_at)
        VALUES
            (@ExternalId, @Date, @ProductCode, @ModeCode, @Article, @Name, @Manufacturer,
             @Barcode, @Qty, @Extra1, @Extra2, @Extra3, @Now)
        """;

    private const string UpsertSql = InsertSql +
        """

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
        Now = DateTime.UtcNow.ToString("o")
    };

    public List<LabelRecord> GetAll()
    {
        using var db = Connect();
        return db.Query<LabelRecord>(SelectSql + " ORDER BY id DESC").ToList();
    }

    public int Count()
    {
        using var db = Connect();
        return db.ExecuteScalar<int>("SELECT COUNT(*) FROM labels");
    }

    public void UpsertMany(IEnumerable<LabelRecord> records)
    {
        using var db = Connect();
        db.Open();
        using var tx = db.BeginTransaction();

        foreach (var r in records)
        {
            db.Execute(UpsertSql, Params(r), tx);
        }

        tx.Commit();
    }

    public void ReplaceAll(IEnumerable<LabelRecord> records)
    {
        using var db = Connect();
        db.Open();
        using var tx = db.BeginTransaction();

        db.Execute("DELETE FROM labels", transaction: tx);

        foreach (var r in records)
        {
            db.Execute(InsertSql, Params(r), tx);
        }

        tx.Commit();
    }

    public bool Delete(string externalId)
    {
        using var db = Connect();
        return db.Execute("DELETE FROM labels WHERE external_id = @id", new { id = externalId }) > 0;
    }

    public void ClearAll()
    {
        using var db = Connect();
        db.Execute("DELETE FROM labels");
    }
}
