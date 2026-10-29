using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Branding;

/// <summary>
/// Загруженный значок сайта — файлом в папке данных вне репозитория (в базе хаба файлы не
/// храним). Папка — настройка <c>Branding:Folder</c>, по умолчанию <c>C:\GGA1C\branding</c>.
/// В папке не больше одного файла <c>site-icon.(png|svg|ico)</c>; нет файла — значок по
/// умолчанию из сборки.
/// </summary>
public class SiteIconStore : ISingletonDependency
{
    public const string DefaultFolder = @"C:\GGA1C\branding";

    private static readonly string[] Extensions = [".png", ".svg", ".ico"];

    private readonly IConfiguration _configuration;
    private readonly object _lock = new();

    public SiteIconStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Folder
    {
        get
        {
            var folder = _configuration["Branding:Folder"];
            return string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
        }
    }

    /// <summary>Загруженный значок или null, если его нет (тогда — значок из сборки).</summary>
    public SiteIconFile? Find()
    {
        foreach (var extension in Extensions)
        {
            var path = Path.Combine(Folder, SiteIcon.FileStem + extension);
            if (File.Exists(path))
            {
                return new SiteIconFile(path, SiteIcon.ContentType(extension), File.GetLastWriteTimeUtc(path));
            }
        }

        return null;
    }

    /// <summary>Проверяет и кладёт значок, убирая прежний другого типа.</summary>
    public async Task SaveAsync(string? fileName, byte[] content)
    {
        var extension = SiteIcon.Validate(fileName, content);
        Directory.CreateDirectory(Folder);

        var target = Path.Combine(Folder, SiteIcon.FileStem + extension);
        var temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, content);

        lock (_lock)
        {
            File.Move(temp, target, overwrite: true);
            foreach (var other in Extensions.Where(e => e != extension))
            {
                File.Delete(Path.Combine(Folder, SiteIcon.FileStem + other));
            }
        }
    }

    /// <summary>Вернуть значок по умолчанию: загруженный файл удаляется.</summary>
    public void Reset()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        lock (_lock)
        {
            foreach (var extension in Extensions)
            {
                File.Delete(Path.Combine(Folder, SiteIcon.FileStem + extension));
            }
        }
    }
}

public record SiteIconFile(string Path, string ContentType, DateTime LastWriteUtc);
