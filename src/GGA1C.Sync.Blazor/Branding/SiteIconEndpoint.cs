using System.IO;
using GGA1C.Sync.Branding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GGA1C.Sync.Blazor.Branding;

/// <summary>
/// <c>GET /site-icon</c> — действующий значок сайта (П16): загруженный администратором файл из
/// папки <see cref="SiteIconStore.Folder"/> или значок по умолчанию из сборки
/// (<c>wwwroot/images/site-icon.svg</c>). Без входа — он нужен и странице входа.
///
/// Кэш — с проверкой (<c>no-cache</c> + ETag по времени файла): после замены значок виден без
/// перезапуска. CSP без скриптов — на случай, если SVG откроют по адресу напрямую.
/// </summary>
public static class SiteIconEndpoint
{
    public const string DefaultIconPath = "images/site-icon.svg";

    public static IEndpointConventionBuilder MapSiteIcon(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(SiteIcon.Url, (HttpContext context) =>
        {
            var store = context.RequestServices.GetRequiredService<SiteIconStore>();
            var file = store.Find();

            Stream stream;
            string contentType;
            long version;
            if (file != null)
            {
                stream = File.OpenRead(file.Path);
                contentType = file.ContentType;
                version = file.LastWriteUtc.Ticks;
            }
            else
            {
                var fallback = context.RequestServices.GetRequiredService<IWebHostEnvironment>()
                    .WebRootFileProvider.GetFileInfo(DefaultIconPath);
                if (!fallback.Exists)
                {
                    return Results.NotFound();
                }
                stream = fallback.CreateReadStream();
                contentType = "image/svg+xml";
                version = 0;
            }

            var etag = $"\"{version:x}\"";
            var headers = context.Response.Headers;
            headers.CacheControl = "no-cache";
            headers.ETag = etag;
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";

            if (context.Request.Headers.IfNoneMatch == etag)
            {
                stream.Dispose();
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Stream(stream, contentType);
        }).AllowAnonymous();
}
