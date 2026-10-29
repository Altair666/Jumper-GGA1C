using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Файлы шаблонов в папке данных вне репозитория — до FastAPI-сервиса на NAS. Папка —
/// настройка <c>Templates:Folder</c> (по умолчанию <c>C:\GGA1C\templates</c>). Ссылка — имя
/// файла <c>&lt;guid&gt;&lt;расширение&gt;</c>; путь наружу не выходит.
/// </summary>
public class FolderTemplateFileStore : ITemplateFileStore, ITransientDependency
{
    public const string DefaultFolder = @"C:\GGA1C\templates";

    private readonly IConfiguration _configuration;

    public FolderTemplateFileStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Folder
    {
        get
        {
            var folder = _configuration["Templates:Folder"];
            return string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
        }
    }

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (extension.Length > 10 || !extension.Skip(1).All(char.IsAsciiLetterOrDigit))
        {
            extension = "";
        }

        Directory.CreateDirectory(Folder);
        var fileRef = Guid.NewGuid().ToString("N") + extension;
        var path = PathOf(fileRef);
        var temp = path + ".tmp";
        await using (var file = File.Create(temp))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        File.Move(temp, path);
        return fileRef;
    }

    public Task<Stream> OpenAsync(string fileRef, CancellationToken cancellationToken = default)
    {
        var path = PathOf(fileRef);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Файла шаблона {fileRef} нет в хранилище.", fileRef);
        }
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string fileRef, CancellationToken cancellationToken = default)
    {
        var path = PathOf(fileRef);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    /// <summary>Ссылка — только имя файла из [0-9a-z.]: иначе «..\..» увела бы за папку.</summary>
    private string PathOf(string fileRef)
    {
        if (string.IsNullOrEmpty(fileRef) || fileRef.Length > 64
            || !fileRef.All(c => char.IsAsciiLetterOrDigit(c) || c == '.') || fileRef.StartsWith('.') || fileRef.Contains(".."))
        {
            throw new ArgumentException("Неверная ссылка на файл шаблона.", nameof(fileRef));
        }
        return Path.Combine(Folder, fileRef);
    }
}
