using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Буфер этикеток поверх таблицы <see cref="LabelRow"/> в базе хаба.
///
/// Отбор везде идёт по <see cref="LabelRow.Slug"/>, то есть по учётке: замена буфера одного
/// оператора не задевает партию другого — ровно то, ради чего раньше держали отдельный файл
/// на человека. Разница в том, что теперь это проверяет ещё и СУБД: GoLabel II читает не
/// таблицу, а представление, отбирающее строки по логину, с которым он подключился.
/// </summary>
public class LabelsStore : DomainService, ILabelsStore
{
    private readonly IRepository<LabelRow, long> _rows;
    private readonly LabelsCsvExport _csv;

    public LabelsStore(IRepository<LabelRow, long> rows, LabelsCsvExport csv)
    {
        _rows = rows;
        _csv = csv;
    }

    public async Task<int> CountAsync(string slug)
    {
        return (int)await _rows.CountAsync(x => x.Slug == slug);
    }

    public async Task<List<LabelRecord>> GetTopAsync(string slug, int count)
    {
        var query = await _rows.GetQueryableAsync();

        return query.Where(x => x.Slug == slug)
            .OrderByDescending(x => x.Id)
            .Take(Math.Max(0, count))
            .ToList()
            .Select(ToRecord)
            .ToList();
    }

    public async Task<List<LabelRecord>> GetAllAsync(string slug)
    {
        var query = await _rows.GetQueryableAsync();

        return query.Where(x => x.Slug == slug)
            .OrderByDescending(x => x.Id)
            .ToList()
            .Select(ToRecord)
            .ToList();
    }

    public async Task<int> UpsertManyAsync(string slug, IEnumerable<LabelRecord> records)
    {
        var list = records.ToList();
        var ids = list.Select(r => r.ExternalId).ToList();

        var query = await _rows.GetQueryableAsync();
        var existing = query.Where(x => x.Slug == slug && ids.Contains(x.ExternalId))
            .ToDictionary(x => x.ExternalId, x => x);

        var toInsert = new List<LabelRow>();

        foreach (var record in list)
        {
            if (existing.TryGetValue(record.ExternalId, out var row))
            {
                Fill(row, slug, record);
                await _rows.UpdateAsync(row, autoSave: true);
            }
            else
            {
                var fresh = new LabelRow();
                Fill(fresh, slug, record);
                toInsert.Add(fresh);
            }
        }

        if (toInsert.Count > 0)
        {
            await _rows.InsertManyAsync(toInsert, autoSave: true);
        }

        await ExportAsync(slug);

        return list.Count;
    }

    public async Task<int> ReplaceAllAsync(string slug, IEnumerable<LabelRecord> records)
    {
        // Удаляем запросом, а не выборкой с последующим удалением: партия бывает в тысячи
        // строк, и тянуть её в память только чтобы удалить - лишняя работа.
        await _rows.DeleteDirectAsync(x => x.Slug == slug);

        var rows = records.Select(r =>
        {
            var row = new LabelRow();
            Fill(row, slug, r);
            return row;
        }).ToList();

        // autoSave обязателен: выгрузка в CSV читает те же строки из базы, а без
        // сохранения они ещё висят в единице работы, и в файл попадал один заголовок.
        if (rows.Count > 0)
        {
            await _rows.InsertManyAsync(rows, autoSave: true);
        }

        await ExportAsync(slug);

        return rows.Count;
    }

    public async Task<bool> DeleteAsync(string slug, string externalId)
    {
        var row = await _rows.FindAsync(x => x.Slug == slug && x.ExternalId == externalId);

        if (row == null)
        {
            return false;
        }

        await _rows.DeleteAsync(row, autoSave: true);
        await ExportAsync(slug);

        return true;
    }

    public async Task<int> ClearAsync(string slug)
    {
        var count = await CountAsync(slug);
        await _rows.DeleteDirectAsync(x => x.Slug == slug);
        await ExportAsync(slug);

        return count;
    }

    /// <summary>
    /// Переписывает файл для GoLabel целиком. Делается после каждой правки буфера: человек
    /// на рабочем месте не должен ничего обновлять руками - он просто печатает.
    /// </summary>
    private async Task ExportAsync(string slug)
    {
        _csv.Write(slug, await GetAllAsync(slug));
    }

    public async Task<List<LabelsBufferInfo>> GetBuffersAsync()
    {
        var query = await _rows.GetQueryableAsync();

        return query.GroupBy(x => x.Slug)
            .Select(g => new { Slug = g.Key, Count = g.Count(), Last = g.Max(x => x.UpdatedAt) })
            .ToList()
            .Select(x => new LabelsBufferInfo(x.Slug, x.Count, x.Last))
            .ToList();
    }

    private static void Fill(LabelRow row, string slug, LabelRecord r)
    {
        row.Slug = slug;
        row.ExternalId = r.ExternalId;
        row.Date = r.Date;
        row.ProductCode = r.ProductCode;
        row.ModeCode = r.ModeCode;
        row.Article = r.Article;
        row.Name = r.Name;
        row.Manufacturer = r.Manufacturer;
        row.Barcode = r.Barcode;
        row.Qty = r.Qty;
        row.Extra1 = r.Extra1;
        row.Extra2 = r.Extra2;
        row.Extra3 = r.Extra3;
        row.UpdatedAt = DateTime.UtcNow;
    }

    private static LabelRecord ToRecord(LabelRow row)
    {
        return new LabelRecord
        {
            Id = (int)row.Id,
            ExternalId = row.ExternalId,
            Date = row.Date,
            ProductCode = row.ProductCode,
            ModeCode = row.ModeCode,
            Article = row.Article,
            Name = row.Name,
            Manufacturer = row.Manufacturer,
            Barcode = row.Barcode,
            Qty = row.Qty,
            Extra1 = row.Extra1,
            Extra2 = row.Extra2,
            Extra3 = row.Extra3,
            UpdatedAt = row.UpdatedAt
        };
    }
}
