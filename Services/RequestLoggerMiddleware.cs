using System.Text;
using AltiumSync.Data;

namespace AltiumSync.Services;

// Логирует ВСЕ входящие запросы — нужно для реверс-инжиниринга протокола EDA Gateway
public class RequestLoggerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggerMiddleware> _log;

    public RequestLoggerMiddleware(RequestDelegate next,
        ILogger<RequestLoggerMiddleware> log)
    {
        _next = next;
        _log  = log;
    }

    public async Task InvokeAsync(HttpContext ctx, Database db)
    {
        // Пропускаем статику и UI
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/css") || path.StartsWith("/js") ||
            (path == "/" || path == "/index.html") && ctx.Request.Method == "GET")
        {
            await _next(ctx);
            return;
        }

        // Читаем тело запроса
        ctx.Request.EnableBuffering();
        var bodyBytes = new byte[Math.Min(ctx.Request.ContentLength ?? 0, 32768)];
        int bytesRead = 0;
        if (bodyBytes.Length > 0)
        {
            bytesRead = await ctx.Request.Body.ReadAsync(bodyBytes);
            ctx.Request.Body.Position = 0;
        }
        var body = bytesRead > 0
            ? Encoding.UTF8.GetString(bodyBytes, 0, bytesRead)
            : "";

        // Собираем заголовки (кроме стандартных браузерных)
        var skipHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "host", "connection", "accept-encoding", "user-agent",
              "cache-control", "pragma", "accept-language" };

        var headers = ctx.Request.Headers
            .Where(h => !skipHeaders.Contains(h.Key))
            .Select(h => $"{h.Key}: {h.Value}")
            .ToList();

        // Определяем источник
        var ua      = ctx.Request.Headers.UserAgent.ToString();
        var isTc    = path.StartsWith("/tc");
        var isOauth = path.StartsWith("/oauth");
        var isAes   = isTc || isOauth ||
                      (!ua.Contains("Mozilla") && !ua.Contains("Chrome") &&
                       !ua.Contains("Safari") && !ua.Contains("Edg"));
        var source = isTc    ? "[TC/EDA]"
                   : isOauth ? "[OAuth]"
                   : isAes   ? "[AES]"
                   : "[Browser]";

        var entry = new RequestLogItem
        {
            Timestamp  = DateTime.UtcNow,
            Source     = source,
            Method     = ctx.Request.Method,
            Path       = ctx.Request.Path + ctx.Request.QueryString,
            Headers    = string.Join("\n", headers),
            Body       = body,
        };

        // Пишем в БД
        db.SaveRequestLog(entry);

        // И в консоль
        if (isAes || path.StartsWith("/tc") || path.StartsWith("/oauth"))
        {
            _log.LogInformation(
                "{Source} {Method} {Path}\n  Headers: {Headers}\n  Body: {Body}",
                source, entry.Method, entry.Path,
                string.Join(", ", headers.Take(5)),
                body.Length > 200 ? body[..200] + "..." : body);
        }

        await _next(ctx);
    }
}

public class RequestLogItem
{
    public int       Id        { get; set; }
    public DateTime  Timestamp { get; set; }
    public string    Source    { get; set; } = "";
    public string    Method    { get; set; } = "";
    public string    Path      { get; set; } = "";
    public string    Headers   { get; set; } = "";
    public string    Body      { get; set; } = "";
}
