using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AltiumSync.Models;

namespace AltiumSync.Services;

public class OneCService
{
    private readonly HttpClient _http;
    private readonly ILogger<OneCService> _log;
    private string _baseUrl = "";

    public OneCService(IHttpClientFactory factory, ILogger<OneCService> log)
    {
        _http = factory.CreateClient("onec");
        _log  = log;
    }

    public void Configure(string serverUrl, string baseName, string user, string password)
    {
        _baseUrl = $"{serverUrl.TrimEnd('/')}/{baseName}/odata/standard.odata";
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    // ── Группы номенклатуры (папки) ──────────────────────────────────────────

    public async Task<List<OneCGroup>> GetGroupsAsync()
    {
        try
        {
            var json = await GetAsync(
                "Catalog_Номенклатура?$format=json&$filter=IsFolder eq true" +
                "&$select=Ref_Key,Description,Parent_Key");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("value").EnumerateArray()
                .Select(e => new OneCGroup
                {
                    Guid        = e.GetProperty("Ref_Key").GetString() ?? "",
                    Name        = e.GetProperty("Description").GetString() ?? "",
                    ParentGuid  = e.TryGetProperty("Parent_Key", out var p) ? p.GetString() : null,
                }).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения групп номенклатуры из 1С");
            return new();
        }
    }

    public async Task<string> CreateGroupAsync(string name, string? parentGuid = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["Description"] = name,
            ["IsFolder"]    = true,
        };
        if (parentGuid != null) payload["Parent_Key"] = parentGuid;

        var json = await PostAsync("Catalog_Номенклатура", payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Ref_Key").GetString() ?? "";
    }

    // ── Номенклатура ─────────────────────────────────────────────────────────

    public async Task<List<OneCNomenclature>> GetNomenclatureAsync(string? filter = null)
    {
        try
        {
            var query = "Catalog_Номенклатура?$format=json&$filter=IsFolder eq false" +
                        "&$select=Ref_Key,Code,Description,Артикул,Parent_Key";
            if (filter != null) query += $" and {filter}";
            var json = await GetAsync(query);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("value").EnumerateArray()
                .Select(e => new OneCNomenclature
                {
                    Guid     = e.GetProperty("Ref_Key").GetString()    ?? "",
                    Code     = e.TryGetProperty("Code", out var c)     ? c.GetString() ?? "" : "",
                    Name     = e.GetProperty("Description").GetString() ?? "",
                    Article  = e.TryGetProperty("Артикул", out var a)  ? a.GetString() ?? "" : "",
                    GroupGuid = e.TryGetProperty("Parent_Key", out var p) ? p.GetString() : null,
                }).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка получения номенклатуры из 1С");
            return new();
        }
    }

    public async Task<OneCNomenclature?> FindByArticleAsync(string article)
    {
        var list = await GetNomenclatureAsync($"Артикул eq '{EscapeOdata(article)}'");
        return list.FirstOrDefault();
    }

    public async Task<string> CreateNomenclatureAsync(string name, string article,
        string? groupGuid = null, string? comment = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["Description"] = name,
            ["Артикул"]     = article,
            ["Комментарий"] = comment ?? "",
        };
        if (groupGuid != null) payload["Parent_Key"] = groupGuid;

        var json = await PostAsync("Catalog_Номенклатура", payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Ref_Key").GetString() ?? "";
    }

    // ── Спецификации ─────────────────────────────────────────────────────────

    public async Task<string> CreateSpecificationAsync(string nomenclatureGuid,
        string name, List<BomLine> lines, Dictionary<string, string>? extraParams = null)
    {
        // Строки спецификации
        var rows = lines.Select(l => new Dictionary<string, object>
        {
            ["Номенклатура_Key"] = l.AltiumId,  // TODO: заменить на onec_guid из маппинга
            ["Количество"]      = l.Quantity,
            ["ЕдиницаИзмерения_Key"] = "шт",    // TODO: маппинг единиц
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["Description"]          = name,
            ["Владелец_Key"]         = nomenclatureGuid,
            ["СпецификацияСтроки"]  = rows,
        };

        if (extraParams != null)
            foreach (var kv in extraParams)
                payload[kv.Key] = kv.Value;

        var json = await PostAsync("Catalog_Спецификации", payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Ref_Key").GetString() ?? "";
    }

    public async Task UpdateSpecificationAsync(string specGuid, List<BomLine> lines)
    {
        var rows = lines.Select(l => new Dictionary<string, object>
        {
            ["Номенклатура_Key"] = l.AltiumId,
            ["Количество"]      = l.Quantity,
        }).ToList();

        var payload = new Dictionary<string, object>
        {
            ["СпецификацияСтроки"] = rows,
        };

        await PatchAsync($"Catalog_Спецификации(guid'{specGuid}')", payload);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await GetAsync("Catalog_Номенклатура?$format=json&$top=1&$select=Ref_Key");
            return true;
        }
        catch { return false; }
    }

    // ── Внутренние методы ────────────────────────────────────────────────────

    private async Task<string> GetAsync(string path)
    {
        var resp = await _http.GetAsync(_baseUrl + "/" + path);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> PostAsync(string path, object payload)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(_baseUrl + "/" + path, content);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task PatchAsync(string path, object payload)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json");
        var req  = new HttpRequestMessage(HttpMethod.Patch, _baseUrl + "/" + path) { Content = content };
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    private static string EscapeOdata(string value) => value.Replace("'", "''");
}

// ── DTO 1С ────────────────────────────────────────────────────────────────────

public class OneCGroup
{
    public string  Guid       { get; set; } = "";
    public string  Name       { get; set; } = "";
    public string? ParentGuid { get; set; }
}

public class OneCNomenclature
{
    public string  Guid      { get; set; } = "";
    public string  Code      { get; set; } = "";
    public string  Name      { get; set; } = "";
    public string  Article   { get; set; } = "";
    public string? GroupGuid { get; set; }
}
