using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using AltiumSync.Data;
using AltiumSync.Models;

namespace AltiumSync.Controllers;

/// <summary>
/// Приём данных из Grist для печати этикеток через GoLabel2.
/// Grist получает сюда права: добавление / редактирование / удаление / полная перезапись.
/// GoLabel2 подключается отдельно через ODBC (read-only DSN на labels.db), сюда не ходит.
///
/// Авторизация — один и тот же ключ (AppSettings.GristApiKey) в любом из двух видов:
///   X-Api-Key: KEY              — как задумывалось изначально
///   Authorization: Bearer KEY   — так шлёт кастомный виджет widget_report_grist
///
/// Тело запроса принимается в трёх формах: голый массив, одиночный объект
/// и обёртка {"items":[...]} / {"records":[...]} — последнюю шлёт виджет.
/// </summary>
[ApiController]
[Route("api/grist")]
public class GristLabelsController : ControllerBase
{
    private readonly Database       _db;
    private readonly LabelsDatabase _labels;

    public GristLabelsController(Database db, LabelsDatabase labels)
    {
        _db     = db;
        _labels = labels;
    }

    private bool CheckApiKey()
    {
        var expected = _db.GetSettings().GristApiKey;
        if (string.IsNullOrEmpty(expected)) return false;

        var provided = Request.Headers["X-Api-Key"].ToString();

        // Виджет шлёт ключ как Bearer-токен
        if (string.IsNullOrEmpty(provided))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = auth["Bearer ".Length..].Trim();
        }

        return !string.IsNullOrEmpty(provided) && provided == expected;
    }

    // Добавить/обновить одну или несколько записей (upsert по externalId)
    [HttpPost("labels/upsert")]
    public IActionResult Upsert([FromBody] JsonElement body)
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing API key" });

        var records = ParseBody(body);
        _labels.UpsertMany(records);
        return Ok(new { ok = true, count = records.Count });
    }

    // Полная перезапись таблицы — Grist присылает весь снапшот целиком.
    // Виджет использует именно этот режим: каждая синхронизация = текущая партия этикеток.
    [HttpPost("labels/replace-all")]
    public IActionResult ReplaceAll([FromBody] JsonElement body)
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing API key" });

        var records = ParseBody(body);
        _labels.ReplaceAll(records);
        return Ok(new { ok = true, count = records.Count });
    }

    // Удалить одну запись по externalId
    [HttpDelete("labels/{externalId}")]
    public IActionResult Delete(string externalId)
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing API key" });
        var deleted = _labels.Delete(externalId);
        return Ok(new { ok = true, deleted });
    }

    // Очистить всю таблицу
    [HttpDelete("labels")]
    public IActionResult ClearAll()
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing API key" });
        _labels.ClearAll();
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Разворачивает тело запроса в список записей и гарантирует уникальный ExternalId
    /// у каждой: виджет ключ не присылает, а он у нас PRIMARY-ключ строки.
    /// </summary>
    private static List<LabelRecord> ParseBody(JsonElement body)
    {
        var items = new List<JsonElement>();

        if (body.ValueKind == JsonValueKind.Array)
        {
            items.AddRange(body.EnumerateArray());
        }
        else if (body.ValueKind == JsonValueKind.Object)
        {
            // {"items":[...]} — формат виджета; {"records":[...]} — формат Grist API
            JsonElement wrapped = default;
            var hasWrapper = body.TryGetProperty("items", out wrapped)
                          || body.TryGetProperty("records", out wrapped);

            if (hasWrapper && wrapped.ValueKind == JsonValueKind.Array)
                items.AddRange(wrapped.EnumerateArray());
            else
                items.Add(body);   // одиночная запись
        }

        var records = new List<LabelRecord>();
        var used    = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var r = ParseRecord(item);

            // Виджет externalId не шлёт — собираем стабильный ключ из содержимого строки
            if (string.IsNullOrEmpty(r.ExternalId))
                r.ExternalId = string.Join("|", new[] { r.ProductCode, r.ModeCode }
                                   .Where(s => !string.IsNullOrEmpty(s)));

            if (string.IsNullOrEmpty(r.ExternalId))
                r.ExternalId = $"row-{records.Count + 1}";

            // Дубли в пределах одной пачки развели бы UNIQUE-ограничение
            var baseId = r.ExternalId;
            for (var n = 2; !used.Add(r.ExternalId); n++)
                r.ExternalId = $"{baseId}#{n}";

            records.Add(r);
        }

        return records;
    }

    private static LabelRecord ParseRecord(JsonElement e)
    {
        string Get(params string[] names)
        {
            if (e.ValueKind != JsonValueKind.Object) return "";
            foreach (var n in names)
                if (e.TryGetProperty(n, out var v))
                    return v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString() ?? "",
                        JsonValueKind.Null   => "",
                        _                    => v.ToString(),
                    };
            return "";
        }

        return new LabelRecord
        {
            ExternalId   = Get("externalId", "ExternalId", "external_id", "id", "Id"),
            Date         = Get("date", "Date"),
            ProductCode  = Get("product_code", "productCode", "ProductCode"),
            ModeCode     = Get("mode_code", "modeCode", "ModeCode"),
            Article      = Get("article", "Article"),
            Name         = Get("name", "Name"),
            Manufacturer = Get("manufacturer", "Manufacturer"),
            Barcode      = Get("barcode", "Barcode"),
            Qty          = Get("qty", "Qty", "quantity", "Quantity"),
            Extra1       = Get("extra1", "Extra1"),
            Extra2       = Get("extra2", "Extra2"),
            Extra3       = Get("extra3", "Extra3"),
        };
    }
}
