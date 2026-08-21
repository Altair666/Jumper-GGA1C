using Microsoft.AspNetCore.Mvc;
using AltiumSync.Data;
using AltiumSync.Models;

namespace AltiumSync.Controllers;

/// <summary>
/// Приём данных из Grist для печати этикеток через GoLabel2.
/// Grist получает сюда права: добавление / редактирование / удаление / полная перезапись.
/// GoLabel2 подключается отдельно через ODBC (read-only DSN на labels.db), сюда не ходит.
/// Авторизация — заголовок X-Api-Key, сверяется с AppSettings.GristApiKey.
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
        var provided = Request.Headers["X-Api-Key"].ToString();
        var expected = _db.GetSettings().GristApiKey;
        return !string.IsNullOrEmpty(expected) &&
               !string.IsNullOrEmpty(provided) &&
               provided == expected;
    }

    // Добавить/обновить одну или несколько записей (upsert по externalId)
    [HttpPost("labels/upsert")]
    public IActionResult Upsert([FromBody] System.Text.Json.JsonElement body)
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing X-Api-Key" });

        var records = new List<LabelRecord>();
        if (body.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var item in body.EnumerateArray())
                records.Add(ParseRecord(item));
        else
            records.Add(ParseRecord(body));

        if (records.Any(r => string.IsNullOrEmpty(r.ExternalId)))
            return BadRequest(new { error = "externalId обязателен для каждой записи" });

        _labels.UpsertMany(records);
        return Ok(new { ok = true, count = records.Count });
    }

    // Полная перезапись таблицы — Grist присылает весь снапшот целиком
    [HttpPost("labels/replace-all")]
    public IActionResult ReplaceAll([FromBody] List<System.Text.Json.JsonElement> body)
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing X-Api-Key" });

        var records = body.Select(ParseRecord).ToList();
        if (records.Any(r => string.IsNullOrEmpty(r.ExternalId)))
            return BadRequest(new { error = "externalId обязателен для каждой записи" });

        _labels.ReplaceAll(records);
        return Ok(new { ok = true, count = records.Count });
    }

    // Удалить одну запись по externalId
    [HttpDelete("labels/{externalId}")]
    public IActionResult Delete(string externalId)
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing X-Api-Key" });
        var deleted = _labels.Delete(externalId);
        return Ok(new { ok = true, deleted });
    }

    // Очистить всю таблицу
    [HttpDelete("labels")]
    public IActionResult ClearAll()
    {
        if (!CheckApiKey()) return Unauthorized(new { error = "Invalid or missing X-Api-Key" });
        _labels.ClearAll();
        return Ok(new { ok = true });
    }

    private static LabelRecord ParseRecord(System.Text.Json.JsonElement e)
    {
        string Get(params string[] names)
        {
            foreach (var n in names)
                if (e.TryGetProperty(n, out var v))
                    return v.ValueKind == System.Text.Json.JsonValueKind.String
                        ? v.GetString() ?? ""
                        : v.ToString();
            return "";
        }

        return new LabelRecord
        {
            ExternalId   = Get("externalId", "ExternalId", "id", "Id"),
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
