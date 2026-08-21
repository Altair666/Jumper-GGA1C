using Microsoft.AspNetCore.Mvc;
using AltiumSync.Data;
using AltiumSync.Models;
using System.Text;

namespace AltiumSync.Controllers;

[ApiController]
public class EdaGatewayController : ControllerBase
{
    private readonly Database _db;
    private readonly ILogger<EdaGatewayController> _log;

    private const string ValidUser = "admin";
    private const string ValidPass = "admin";

    private static readonly Dictionary<string, DateTime> Sessions = new();

    public EdaGatewayController(Database db, ILogger<EdaGatewayController> log)
    {
        _db  = db;
        _log = log;
    }

    private string Base => $"{Request.Scheme}://{Request.Host}";

    // ── Корень /tc ────────────────────────────────────────────────────────────

    [HttpGet("/tc")]
    public IActionResult TcRoot()
    {
        Log("GET /tc");
        return Ok(new
        {
            server            = "Teamcenter EDA Gateway",
            version           = "6.1.3",
            status            = "running",
            edaClientLocation = $"{Base}/tc/eda",
        });
    }

    // ── Аутентификация ────────────────────────────────────────────────────────

    [HttpPost("/tc/login")]
    [HttpPost("/tc/auth")]
    [HttpPost("/tc/session")]
    [HttpPost("/tc/EDAGateway/login")]
    [HttpPost("/tc/EDAGatewayService/login")]
    public async Task<IActionResult> Login()
    {
        // Читаем тело запроса
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        // Пробуем получить credentials из разных мест
        string user = "", pass = "";

        // Basic Auth
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..]));
                var parts   = decoded.Split(':', 2);
                if (parts.Length == 2) { user = parts[0]; pass = parts[1]; }
            }
            catch { }
        }

        // JSON body
        if (string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(body))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                foreach (var name in new[] { "username","UserName","user","login" })
                    if (root.TryGetProperty(name, out var v)) { user = v.GetString() ?? ""; break; }
                foreach (var name in new[] { "password","Password","pass" })
                    if (root.TryGetProperty(name, out var v)) { pass = v.GetString() ?? ""; break; }
            }
            catch { }
        }

        // Form body
        if (string.IsNullOrEmpty(user) && Request.HasFormContentType)
        {
            user = Request.Form["username"].ToString()
                ?? Request.Form["UserName"].ToString() ?? "";
            pass = Request.Form["password"].ToString()
                ?? Request.Form["Password"].ToString() ?? "";
        }

        Log($"POST /tc/login user={user} body={body[..Math.Min(body.Length,100)]}");

        if (!user.Equals(ValidUser, StringComparison.OrdinalIgnoreCase) || pass != ValidPass)
        {
            _log.LogWarning("EDA auth failed: user={U}", user);
            return Unauthorized(new { error = "Authentication failed", code = 401 });
        }

        var token = Guid.NewGuid().ToString("N");
        Sessions[token] = DateTime.UtcNow;

        return Ok(new
        {
            sessionId         = token,
            token             = token,
            username          = user,
            edaClientLocation = $"{Base}/tc/eda",
            status            = "ok",
        });
    }

    // ── EDA файлы ─────────────────────────────────────────────────────────────

    [HttpGet("/tc/eda")]
    public IActionResult EdaRoot()
    {
        Log("GET /tc/eda");
        return Ok(new
        {
            edaClientLocation = $"{Base}/tc/eda",
            files = new[]
            {
                $"{Base}/tc/eda/altiumLibrary_edadef.xml",
                $"{Base}/tc/eda/altiumPcb_edadef.xml",
                $"{Base}/tc/eda/CustomEDACallbacks.jar",
                $"{Base}/tc/eda/CustomEDACallbacks-TCEDA-6.1.3.jar",
            }
        });
    }

    [HttpGet("/tc/eda/{fileName}")]
    public IActionResult GetEdaFile(string fileName)
    {
        Log($"GET /tc/eda/{fileName}");
        var path = Path.Combine(
            Directory.GetCurrentDirectory(), "wwwroot", "tc", "eda", fileName);

        if (!System.IO.File.Exists(path))
            return NotFound(new { error = $"Not found: {fileName}" });

        var mime = fileName.EndsWith(".xml") ? "application/xml"
                 : fileName.EndsWith(".jar") ? "application/java-archive"
                 : "application/octet-stream";

        return PhysicalFile(path, mime);
    }

    [HttpGet("/tc/health")]
    [HttpGet("/tc/status")]
    public IActionResult Health()
    {
        Log("GET /tc/health");
        return Ok(new { status = "ok", version = "6.1.3" });
    }

    // ── Catch-all /tc/* ───────────────────────────────────────────────────────

    [HttpGet("/tc/{**path}")]
    [HttpPost("/tc/{**path}")]
    [HttpPut("/tc/{**path}")]
    [HttpDelete("/tc/{**path}")]
    [HttpPatch("/tc/{**path}")]
    public async Task<IActionResult> Catch(string path)
    {
        string body = "";
        if ((Request.ContentLength ?? 0) > 0)
        {
            Request.EnableBuffering();
            using var r = new StreamReader(Request.Body, leaveOpen: true);
            body = await r.ReadToEndAsync();
            Request.Body.Position = 0;
        }

        Log($"CATCH {Request.Method} /tc/{path} body={body[..Math.Min(body.Length,200)]}");

        return Ok(new
        {
            status            = "ok",
            path              = $"/tc/{path}",
            edaClientLocation = $"{Base}/tc/eda",
        });
    }

    private void Log(string action)
    {
        _db.AddLog(new SyncLogEntry
        {
            EntityType = "EDA",
            EntityId   = Request.Path,
            EntityName = action,
            Action     = Request.Method,
            Status     = "OK",
        });
    }
}

public class LoginRequest
{
    public string? Username { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
}

public class SessionInfo
{
    public string   Username  { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
