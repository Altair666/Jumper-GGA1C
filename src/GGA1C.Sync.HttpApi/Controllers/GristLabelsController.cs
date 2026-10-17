using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace GGA1C.Sync.Controllers;

/// <summary>
/// Приём данных из Grist в буфер этикеток. Перенесено из прежнего AltiumSync без изменения
/// контракта: он проверен живым запросом от виджета widget_report_grist, менять его нельзя,
/// иначе кнопка «Синхронизация» в виджете перестанет работать.
///
/// Авторизация — не по сессии, а по ключу, потому что запрос приходит из iframe виджета:
///   Authorization: Bearer KEY   — так шлёт виджет
///   X-Api-Key: KEY              — как задумывалось изначально, оставлено для совместимости
///
/// Тело принимается в трёх формах: голый массив, одиночный объект и обёртка
/// {"items":[...]} / {"records":[...]} — последнюю шлёт виджет.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/grist")]
public class GristLabelsController : AbpControllerBase
{
    private readonly ILabelsAppService _labels;

    public GristLabelsController(ILabelsAppService labels)
    {
        _labels = labels;
    }

    [HttpPost("labels/upsert")]
    public async Task<IActionResult> Upsert([FromBody] JsonElement body)
    {
        if (!await CheckKeyAsync())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        var records = ParseBody(body);
        var count = await _labels.UpsertManyAsync(records);
        return Ok(new { ok = true, count });
    }

    /// <summary>
    /// Полная перезамена буфера. Виджет работает именно так: одна синхронизация —
    /// одна партия этикеток, в буфере остаётся ровно выбранное сейчас.
    /// </summary>
    [HttpPost("labels/replace-all")]
    public async Task<IActionResult> ReplaceAll([FromBody] JsonElement body)
    {
        if (!await CheckKeyAsync())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        var records = ParseBody(body);
        var count = await _labels.ReplaceAllAsync(records);
        return Ok(new { ok = true, count });
    }

    [HttpDelete("labels/{externalId}")]
    public async Task<IActionResult> Delete(string externalId)
    {
        if (!await CheckKeyAsync())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        var deleted = await _labels.DeleteAsync(externalId);
        return Ok(new { ok = true, deleted });
    }

    [HttpDelete("labels")]
    public async Task<IActionResult> ClearAll()
    {
        if (!await CheckKeyAsync())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        await _labels.ClearAllAsync();
        return Ok(new { ok = true });
    }

    private async Task<bool> CheckKeyAsync()
    {
        var provided = Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrEmpty(provided))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
            {
                provided = auth["Bearer ".Length..].Trim();
            }
        }

        return await _labels.IsApiKeyValidAsync(provided);
    }

    /// <summary>
    /// Разворачивает тело запроса в список записей и гарантирует уникальный ExternalId:
    /// виджет ключ не присылает, а он у нас UNIQUE в буфере.
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
            JsonElement wrapped = default;
            var hasWrapper = body.TryGetProperty("items", out wrapped)
                          || body.TryGetProperty("records", out wrapped);

            if (hasWrapper && wrapped.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(wrapped.EnumerateArray());
            }
            else
            {
                items.Add(body);
            }
        }

        var records = new List<LabelRecord>();
        var used = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var item in items)
        {
            var r = ParseRecord(item);

            if (string.IsNullOrEmpty(r.ExternalId))
            {
                r.ExternalId = string.Join("|", new[] { r.ProductCode, r.ModeCode }
                    .Where(s => !string.IsNullOrEmpty(s)));
            }

            if (string.IsNullOrEmpty(r.ExternalId))
            {
                r.ExternalId = $"row-{records.Count + 1}";
            }

            // Дубли внутри одной пачки развалили бы вставку на UNIQUE-ограничении
            var baseId = r.ExternalId;
            for (var n = 2; !used.Add(r.ExternalId); n++)
            {
                r.ExternalId = $"{baseId}#{n}";
            }

            records.Add(r);
        }

        return records;
    }

    private static LabelRecord ParseRecord(JsonElement e)
    {
        string Get(params string[] names)
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            foreach (var n in names)
            {
                if (e.TryGetProperty(n, out var v))
                {
                    return v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString() ?? "",
                        JsonValueKind.Null => "",
                        _ => v.ToString()
                    };
                }
            }

            return "";
        }

        return new LabelRecord
        {
            ExternalId = Get("externalId", "ExternalId", "external_id", "id", "Id"),
            Date = Get("date", "Date"),
            ProductCode = Get("product_code", "productCode", "ProductCode"),
            ModeCode = Get("mode_code", "modeCode", "ModeCode"),
            Article = Get("article", "Article"),
            Name = Get("name", "Name"),
            Manufacturer = Get("manufacturer", "Manufacturer"),
            Barcode = Get("barcode", "Barcode"),
            Qty = Get("qty", "Qty", "quantity", "Quantity"),
            Extra1 = Get("extra1", "Extra1"),
            Extra2 = Get("extra2", "Extra2"),
            Extra3 = Get("extra3", "Extra3")
        };
    }
}
