using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GGA1C.Sync.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GGA1C.Sync.Middleware;

/// <summary>
/// Пишет каждый входящий запрос в журнал диагностики: метод, путь, заголовки и тело.
///
/// Перенесено из прежней версии сервиса. Именно так был раскопан недокументированный API
/// Altium Enterprise Server, и для продолжения работы над пагинацией компонентов журнал
/// нужен в прежнем виде.
///
/// Отличие от прежней версии одно: расширен список исключений. Раньше сервис отдавал один
/// статический файл, а теперь это приложение Blazor, которое на каждую страницу тянет
/// десятки файлов из _framework и _content — без фильтра журнал забивался бы ими полностью
/// и стал бы бесполезен.
/// </summary>
public class RequestLoggerMiddleware
{
    private const int MaxBodyBytes = 32768;

    private static readonly string[] SkipPrefixes =
    [
        "/_blazor",
        "/_framework",
        "/_content",
        "/libs",
        "/css",
        "/js",
        "/images",
        "/fonts",
        "/favicon",
        "/Abp/",
        "/swagger"
    ];

    private static readonly HashSet<string> SkipHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "connection", "accept-encoding", "user-agent",
        "cache-control", "pragma", "accept-language"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggerMiddleware> _log;

    public RequestLoggerMiddleware(RequestDelegate next, ILogger<RequestLoggerMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, IRequestLogStore store)
    {
        var path = ctx.Request.Path.Value ?? "";

        if (ShouldSkip(path, ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        // Тело нужно прочитать до обработчика, поэтому разрешаем перечитывание
        ctx.Request.EnableBuffering();

        var body = "";
        var length = (int)Math.Min(ctx.Request.ContentLength ?? 0, MaxBodyBytes);

        if (length > 0)
        {
            var buffer = new byte[length];
            var read = await ctx.Request.Body.ReadAsync(buffer);
            ctx.Request.Body.Position = 0;

            if (read > 0)
            {
                body = Encoding.UTF8.GetString(buffer, 0, read);
            }
        }

        var headers = ctx.Request.Headers
            .Where(h => !SkipHeaders.Contains(h.Key))
            .Select(h => $"{h.Key}: {h.Value}")
            .ToList();

        var source = DetectSource(path, ctx.Request.Headers.UserAgent.ToString());

        var entry = new RequestLogItem
        {
            Timestamp = DateTime.UtcNow,
            Source = source,
            Method = ctx.Request.Method,
            Path = ctx.Request.Path + ctx.Request.QueryString,
            Headers = string.Join("\n", headers),
            Body = body
        };

        await store.SaveAsync(entry);

        // В консоль выводим только чужие клиенты - свой браузер и так виден в логе запросов
        if (source != "[Browser]")
        {
            _log.LogInformation(
                "{Source} {Method} {Path}\n  Headers: {Headers}\n  Body: {Body}",
                source, entry.Method, entry.Path,
                string.Join(", ", headers.Take(5)),
                body.Length > 200 ? body[..200] + "..." : body);
        }

        await _next(ctx);
    }

    private static bool ShouldSkip(string path, string method)
    {
        if (SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return (path == "/" || path == "/index.html") &&
               string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectSource(string path, string userAgent)
    {
        if (path.StartsWith("/tc", StringComparison.OrdinalIgnoreCase))
        {
            return "[TC/EDA]";
        }

        if (path.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase))
        {
            return "[OAuth]";
        }

        // Браузер узнаём по User-Agent; всё остальное считаем машинным клиентом
        var isBrowser = userAgent.Contains("Mozilla") || userAgent.Contains("Chrome")
                     || userAgent.Contains("Safari") || userAgent.Contains("Edg");

        return isBrowser ? "[Browser]" : "[AES]";
    }
}
