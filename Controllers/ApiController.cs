using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using AltiumSync.Data;
using AltiumSync.Models;
using AltiumSync.Services;

namespace AltiumSync.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly Database       _db;
    private readonly SyncService    _sync;
    private readonly LabelsDatabase _labels;

    public ApiController(Database db, SyncService sync, LabelsDatabase labels)
    {
        _db     = db;
        _sync   = sync;
        _labels = labels;
    }

    // ── Grist → GoLabel2 (admin, для UI — без API-ключа) ────────────────────────

    [HttpGet("labels")]
    public IActionResult GetLabels() => Ok(_labels.GetAll());

    [HttpDelete("labels/{externalId}")]
    public IActionResult DeleteLabel(string externalId)
    {
        var ok = _labels.Delete(externalId);
        return Ok(new { ok });
    }

    [HttpDelete("labels")]
    public IActionResult ClearLabels()
    {
        _labels.ClearAll();
        return Ok(new { ok = true });
    }

    [HttpPost("labels/regenerate-key")]
    public IActionResult RegenerateGristApiKey()
    {
        var s = _db.GetSettings();
        s.GristApiKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
        _db.SaveSettings(s);
        return Ok(new { apiKey = s.GristApiKey });
    }

    // ── Stats (Dashboard) ─────────────────────────────────────────────────────

    [HttpGet("stats")]
    public IActionResult GetStats() => Ok(_db.GetStats());

    // ── Settings ──────────────────────────────────────────────────────────────

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(_db.GetSettings());

    [HttpPost("settings")]
    public IActionResult SaveSettings([FromBody] AppSettings settings)
    {
        _db.SaveSettings(settings);
        _sync.ApplySettings(settings);
        return Ok(new { ok = true });
    }

    [HttpPost("settings/test-altium")]
    public async Task<IActionResult> TestAltium([FromBody] AppSettings s)
    {
        var altium = HttpContext.RequestServices.GetRequiredService<AltiumService>();
        altium.Configure(s.AltiumServerUrl, s.AltiumUser, s.AltiumPassword);
        var (ok, message) = await altium.TestConnectionAsync();
        return Ok(new { ok, message });
    }

    [HttpPost("settings/test-onec")]
    public async Task<IActionResult> TestOneC([FromBody] AppSettings s)
    {
        var onec = HttpContext.RequestServices.GetRequiredService<OneCService>();
        onec.Configure(s.OnecServerUrl, s.OnecBaseName, s.OnecUser, s.OnecPassword);
        var ok = await onec.TestConnectionAsync();
        return Ok(new { ok, message = ok ? "Подключение успешно" : "Не удалось подключиться" });
    }

    // ── Components ────────────────────────────────────────────────────────────

    [HttpGet("components")]
    public IActionResult GetComponents([FromQuery] string? status) =>
        Ok(_db.GetComponents(status));

    [HttpPost("components/sync")]
    public async Task<IActionResult> SyncComponents()
    {
        var s = _db.GetSettings();
        _sync.ApplySettings(s);
        await _sync.SyncFoldersAsync();
        await _sync.SyncComponentsAsync();
        return Ok(new { ok = true });
    }

    // ── Projects / Queue ──────────────────────────────────────────────────────

    [HttpGet("projects")]
    public IActionResult GetProjects([FromQuery] string? status) =>
        Ok(_db.GetProjects(status));

    [HttpPatch("projects/{id}/action")]
    public IActionResult SetProjectAction(int id, [FromBody] ActionDto dto)
    {
        _db.UpdateProjectAction(id, dto.Action);
        return Ok(new { ok = true });
    }

    [HttpPost("projects/{id}/sync")]
    public async Task<IActionResult> SyncProject(int id)
    {
        var projects = _db.GetProjects();
        var project  = projects.FirstOrDefault(p => p.Id == id);
        if (project == null) return NotFound();

        var s = _db.GetSettings();
        _sync.ApplySettings(s);

        var altium  = HttpContext.RequestServices.GetRequiredService<AltiumService>();
        var bomLines = await altium.GetProjectBomAsync(
            project.AltiumProjectId, project.AltiumRevision);

        await _sync.SyncProjectAsync(project, bomLines);
        return Ok(new { ok = true });
    }

    // ── Folder Mapping ────────────────────────────────────────────────────────

    [HttpGet("folders")]
    public IActionResult GetFolderMaps() => Ok(_db.GetFolderMaps());

    [HttpPost("folders")]
    public IActionResult SaveFolderMap([FromBody] FolderMap map)
    {
        _db.UpsertFolderMap(map);
        return Ok(new { ok = true });
    }

    [HttpPost("folders/sync")]
    public async Task<IActionResult> SyncFolders()
    {
        var s = _db.GetSettings();
        _sync.ApplySettings(s);
        await _sync.SyncFoldersAsync();
        return Ok(new { ok = true });
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    [HttpGet("logs")]
    public IActionResult GetLogs(
        [FromQuery] int    limit      = 200,
        [FromQuery] string? entityType = null,
        [FromQuery] string? status    = null)
        => Ok(_db.GetLogs(limit, entityType, status));

    // ── Diagnostics ───────────────────────────────────────────────────────────

    // Показывает HTML страницы логина — для диагностики CSRF токена
    [HttpGet("diag/login-page")]
    public async Task<IActionResult> LoginPageDebug()
    {
        var s = _db.GetSettings();
        if (string.IsNullOrEmpty(s.AltiumServerUrl))
            return BadRequest(new { error = "URL сервера не настроен" });

        try
        {
            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                AllowAutoRedirect = true,
            };
            using var http = new HttpClient(handler);
            var url  = s.AltiumServerUrl.TrimEnd('/') + "/Account/Login";
            var resp = await http.GetAsync(url);
            var html = await resp.Content.ReadAsStringAsync();

            var inputs = System.Text.RegularExpressions.Regex.Matches(html,
                @"<input[^>]+>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .ToList();

            var cookies = cookieContainer
                .GetCookies(new Uri(s.AltiumServerUrl))
                .Cast<Cookie>()
                .Select(c => $"{c.Name} = {c.Value[..Math.Min(c.Value.Length, 40)]}...")
                .ToList();

            return Ok(new
            {
                url,
                statusCode  = (int)resp.StatusCode,
                cookies,
                inputTags   = inputs,
                htmlPreview = html.Length > 4000 ? html[..4000] + "\n...(cut)" : html,
                htmlLength  = html.Length,
            });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("diag/probe")]
    public async Task<IActionResult> Probe([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Ok(new { ok = false, status = 0, path = "", body = "path is required" });
        try
        {
            var s = _db.GetSettings();
            _sync.ApplySettings(s);
            var altium = HttpContext.RequestServices.GetRequiredService<AltiumService>();
            var (ok, status, body) = await altium.ProbeAsync(path);

            // Пробуем красиво отформатировать если JSON
            string pretty = body;
            try
            {
                var doc = JsonDocument.Parse(body);
                pretty = JsonSerializer.Serialize(
                    doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { /* не JSON — возвращаем как есть */ }

            return Ok(new { ok, status, path, body = pretty });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, status = 0, path, body = ex.Message });
        }
    }

    // Показывает куки текущей сессии Altium
    [HttpGet("diag/cookies")]
    public async Task<IActionResult> ShowCookies()
    {
        var s = _db.GetSettings();
        _sync.ApplySettings(s);
        var altium = HttpContext.RequestServices.GetRequiredService<AltiumService>();
        var cookies = altium.GetSessionCookies();
        return Ok(new { count = cookies.Count, cookies });
    }

    // Тест с реальными куками браузера — проверяем работает ли фильтрация
    [HttpPost("diag/raw-request")]
    public async Task<IActionResult> RawRequest([FromBody] RawRequestDto dto)
    {
        var s = _db.GetSettings();
        if (string.IsNullOrEmpty(s.AltiumServerUrl))
            return BadRequest(new { error = "URL не задан" });
        try
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                UseCookies = false,
            };
            using var http = new HttpClient(handler);
            http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");

            var url = s.AltiumServerUrl.TrimEnd('/') + dto.Path;
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", dto.Cookies);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Accept", "application/json, text/plain, */*");
            request.Headers.Add("Referer",
                s.AltiumServerUrl.TrimEnd('/') + "/components");

            var resp  = await http.SendAsync(request);
            var body  = await resp.Content.ReadAsStringAsync();
            int count = 0; string firstIds = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("components", out var arr))
                {
                    count    = arr.GetArrayLength();
                    firstIds = string.Join(", ", arr.EnumerateArray().Take(3)
                        .Select(e => e.TryGetProperty("Id", out var id)
                            ? id.GetString()?[..Math.Min(12, id.GetString()?.Length ?? 0)] ?? ""
                            : "?"));
                }
            }
            catch { }
            return Ok(new { url, status = (int)resp.StatusCode, count, firstIds,
                bodyPreview = body.Length > 300 ? body[..300] : body });
        }
        catch (Exception ex) { return Ok(new { error = ex.Message }); }
    }

    [HttpGet("diag/pagination-test")]
    public async Task<IActionResult> PaginationTest()
    {
        var s = _db.GetSettings();
        if (string.IsNullOrEmpty(s.AltiumServerUrl))
            return Ok(new[] { new { query = "error", httpStatus = 0, count = 0, hasTotal = false, total = 0, error = "URL сервера Altium не задан в настройках" } });

        _sync.ApplySettings(s);
        var altium = HttpContext.RequestServices.GetRequiredService<AltiumService>();

        // Сначала проверяем что авторизация работает
        var (authOk, authMsg) = await altium.TestConnectionAsync();
        if (!authOk)
            return Ok(new[] { new { query = "auth", httpStatus = 0, count = 0, hasTotal = false, total = 0, error = $"Авторизация не прошла: {authMsg}" } });

        var results = new List<object>();

        foreach (var q in new[]
        {
            // Базовый запрос
            "/components/api/components",
            // Правильный формат (Integrated Circuits — 283 шт)
            "/components/api/components?fields[]=Id&fields[]=Comment&text=&tag=6EBE8F4E-156C-4F0F-9001-258398410752&start=0&limit=50",
            "/components/api/components?fields[]=Id&fields[]=Comment&text=&tag=6EBE8F4E-156C-4F0F-9001-258398410752&start=50&limit=50",
            "/components/api/components?fields[]=Id&fields[]=Comment&text=&tag=6EBE8F4E-156C-4F0F-9001-258398410752&start=100&limit=50",
            // Audio (3 шт) — должно вернуть 3
            "/components/api/components?fields[]=Id&fields[]=Comment&text=&tag=5670FFA6-6F08-4305-9CE5-8B0DE7C0FA5F&start=0&limit=50",
            // Все компоненты без тега start=50
            "/components/api/components?fields[]=Id&fields[]=Comment&text=&start=50&limit=50",
            "/components/api/components?fields[]=Id&fields[]=Comment&text=&start=100&limit=50",
        })
        {
            var (ok, status, body) = await altium.ProbeAsync(q);
            int count = 0;
            bool hasTotal = false;
            int total = 0;
            string error = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("components", out var arr))
                    count = arr.GetArrayLength();
                else if (root.TryGetProperty("items", out var arr2))
                    count = arr2.GetArrayLength();
                if (root.TryGetProperty("totalCount", out var tc))
                { hasTotal = true; total = tc.GetInt32(); }
            }
            catch { error = body.Length > 120 ? body[..120] : body; }

            string firstIds = "";
            try
            {
                using var doc2 = JsonDocument.Parse(body);
                var root2 = doc2.RootElement;
                JsonElement arr2;
                if      (root2.TryGetProperty("components", out arr2)) { }
                else if (root2.TryGetProperty("items",      out arr2)) arr2 = arr2;
                else arr2 = default;
                if (arr2.ValueKind == JsonValueKind.Array)
                    firstIds = string.Join(", ", arr2.EnumerateArray()
                        .Take(2)
                        .Select(e => e.TryGetProperty("Id", out var id)
                            ? id.GetString()?[..Math.Min(8, id.GetString()?.Length ?? 0)] ?? ""
                            : "?"));
            }
            catch { }

            results.Add(new { query = q, httpStatus = status, count, hasTotal, total, error, firstIds });
        }
        return Ok(results);
    }
    public IActionResult DiagPaths()
    {
        var s = _db.GetSettings();
        var base_ = s.AltiumServerUrl.TrimEnd('/');
        return Ok(new
        {
            configured = new
            {
                components = s.AltiumApiComponents,
                folders    = s.AltiumApiFolders,
                projects   = s.AltiumApiProjects,
            },
            candidates = new[]
            {
                "/api/v1/component-service/components",
                "/api/v1/component-service/components?limit=5",
                "/api/v1/ComponentService/Components",
                "/api/v1/components",
                "/api/v1/component-service/folders",
                "/api/v1/component-service/folders?limit=5",
                "/api/v1/projects",
                "/api/v1/health",
                "/api/health",
                "/api/v1/userinfo",
            }
        });
    }

    // ── Request Inspector ─────────────────────────────────────────────────────

    [HttpGet("diag/requests")]
    public IActionResult GetRequestLogs(
        [FromQuery] int     limit      = 100,
        [FromQuery] string? source     = null,
        [FromQuery] string? pathFilter = null)
        => Ok(_db.GetRequestLogs(limit, source, pathFilter));

    [HttpDelete("diag/requests")]
    public IActionResult ClearRequestLogs()
    {
        _db.ClearRequestLogs();
        return Ok(new { ok = true });
    }

    [HttpPost("addin/push")]
    public async Task<IActionResult> AddInPush([FromBody] SyncQueueItem item)
    {
        var s = _db.GetSettings();
        _sync.ApplySettings(s);
        await _sync.ProcessQueueItemAsync(item);
        return Ok(new { ok = true });
    }

    // ── OAuth2 endpoints (для fake Windchill) ────────────────────────────────

    [HttpGet("~/oauth/authorize")]
    public IActionResult OAuthAuthorize(
        [FromQuery] string? redirect_uri,
        [FromQuery] string? state,
        [FromQuery] string? client_id,
        [FromQuery] string? response_type)
    {
        var code = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        _db.SaveOAuthCode(code);

        var sep = redirect_uri?.Contains('?') == true ? "&" : "?";
        var url = string.IsNullOrEmpty(redirect_uri)
            ? "/"
            : $"{redirect_uri}{sep}code={code}{(state != null ? $"&state={Uri.EscapeDataString(state)}" : "")}";

        return Redirect(url);
    }

    [HttpPost("~/oauth/token")]
    public IActionResult OAuthToken(
        [FromForm] string? grant_type,
        [FromForm] string? code,
        [FromForm] string? client_id,
        [FromForm] string? client_secret)
    {
        var validCode = grant_type == "authorization_code"
            ? _db.ValidateAndConsumeOAuthCode(code ?? "")
            : true;

        if (!validCode)
            return BadRequest(new { error = "invalid_grant" });

        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        return Ok(new
        {
            access_token = token,
            token_type   = "Bearer",
            expires_in   = 86400,
            scope        = "openid",
        });
    }

    [HttpGet("~/.well-known/openid-configuration")]
    public IActionResult OidcDiscovery()
    {
        var base_ = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            issuer                   = base_,
            authorization_endpoint   = $"{base_}/oauth/authorize",
            token_endpoint           = $"{base_}/oauth/token",
            scopes_supported         = new[] { "openid" },
            response_types_supported = new[] { "code" },
            grant_types_supported    = new[] { "authorization_code", "client_credentials" },
        });
    }
}

public record ActionDto(string Action);
public record RawRequestDto(string Path, string Cookies);
