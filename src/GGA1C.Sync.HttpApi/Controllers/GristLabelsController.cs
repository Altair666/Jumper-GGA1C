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
/// Приём данных из Grist в буфер этикеток. Контракт перенесён из прежнего AltiumSync без
/// изменений - он проверен живым запросом от виджета widget_report_grist, менять его нельзя,
/// иначе кнопка "Синхронизация" перестанет работать.
///
/// Разделение по людям. Основной способ - токен, который виджет получает у самого Grist
/// (grist.docApi.getAccessToken): хаб этим токеном спрашивает Grist, чья это сессия, и
/// находит учётку по адресу почты. Секретов в документе при этом не лежит, а токен живёт
/// минуты и подделать его нельзя - проверяет его Grist.
///
/// Удостоверение принимается в трёх видах:
///   X-Grist-Token: TOKEN        - основной способ
///   Authorization: Bearer KEY   - прежний ключ учётки, пока виджеты не переведены
///   X-Api-Key: KEY              - он же, как задумывалось изначально
///
/// Тело принимается в трёх формах: голый массив, одиночный объект и обёртка
/// {"items":[...]} / {"records":[...]} - последнюю шлёт виджет.
/// </summary>
[ApiController]
[AllowAnonymous]
// Антифорджери-токен здесь не применим, и без этого атрибута эндпоинт ломается самым
// неочевидным образом. ABP включает автоматическую проверку токена для небезопасных
// методов, и она срабатывает, если в запросе есть cookie входа. А она там появляется
// сама: виджет живёт на том же ХОСТЕ, что и хаб, cookie портов не различают, поэтому
// у вошедшего в интерфейс пользователя браузер прикладывает cookie и к запросу виджета.
// Итог: из консоли запрос проходил (куки нет), из браузера отдавал 400 Bad Request.
//
// Защита при этом не теряется: эндпоинт cookie вообще не использует, он требует ключ
// в заголовке. Поставить произвольный заголовок кросс-сайтом нельзя без разрешения CORS,
// а CORS мы не открываем.
[IgnoreAntiforgeryToken]
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
        var (client, error) = await ResolveAsync();
        if (error != null)
        {
            return error;
        }

        var records = ParseBody(body);
        var count = await _labels.UpsertManyAsync(client!.Slug, records);
        await _labels.TouchAsync(client.Id);

        return Ok(new { ok = true, count, client = client.Name });
    }

    /// <summary>
    /// Полная перезамена буфера. Виджет работает именно так: одна синхронизация -
    /// одна партия этикеток, в буфере остаётся ровно выбранное сейчас. Затрагивается
    /// только буфер той учётки, чей ключ предъявлен.
    /// </summary>
    [HttpPost("labels/replace-all")]
    public async Task<IActionResult> ReplaceAll([FromBody] JsonElement body)
    {
        var (client, error) = await ResolveAsync();
        if (error != null)
        {
            return error;
        }

        var records = ParseBody(body);
        var count = await _labels.ReplaceAllAsync(client!.Slug, records);
        await _labels.TouchAsync(client.Id);

        return Ok(new { ok = true, count, client = client.Name });
    }

    [HttpDelete("labels/{externalId}")]
    public async Task<IActionResult> Delete(string externalId)
    {
        var (client, error) = await ResolveAsync();
        if (error != null)
        {
            return error;
        }

        var deleted = await _labels.DeleteAsync(client!.Slug, externalId);
        await _labels.TouchAsync(client.Id);

        return Ok(new { ok = true, deleted });
    }

    [HttpDelete("labels")]
    public async Task<IActionResult> ClearAll()
    {
        var (client, error) = await ResolveAsync();
        if (error != null)
        {
            return error;
        }

        await _labels.ClearAllAsync(client!.Slug);
        await _labels.TouchAsync(client.Id);

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Опознаёт человека. Сначала по токену Grist, затем - по ключу учётки, пока виджеты
    /// не переведены на токены. Отключённая учётка отличается в ответе от неизвестного
    /// удостоверения: иначе отзыв доступа выглядел бы для оператора как "ключ потерялся".
    /// </summary>
    private async Task<(LabelClientDto? Client, IActionResult? Error)> ResolveAsync()
    {
        var token = Request.Headers["X-Grist-Token"].ToString();

        if (!string.IsNullOrWhiteSpace(token))
        {
            var resolution = await _labels.ResolveClientByGristTokenAsync(token);

            if (resolution.Client == null)
            {
                return (null, Unauthorized(new { error = resolution.Error }));
            }

            if (!resolution.Client.IsEnabled)
            {
                return (null, Unauthorized(
                    new { error = $"Учётная запись \"{resolution.Client.Name}\" отключена" }));
            }

            return (resolution.Client, null);
        }

        var provided = Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrEmpty(provided))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
            {
                provided = auth["Bearer ".Length..].Trim();
            }
        }

        var client = await _labels.ResolveClientAsync(provided);

        if (client == null)
        {
            return (null, Unauthorized(new { error = "Удостоверение не принято: нет ни токена Grist, ни известного ключа" }));
        }

        if (!client.IsEnabled)
        {
            return (null, Unauthorized(new { error = $"Учётная запись \"{client.Name}\" отключена" }));
        }

        return (client, null);
    }

    /// <summary>
    /// Разворачивает тело запроса в список записей и гарантирует уникальный ExternalId:
    /// виджет ключ строки не присылает, а он у нас UNIQUE в буфере.
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