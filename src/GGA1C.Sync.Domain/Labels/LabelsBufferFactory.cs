using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Labels;

public class LabelsBufferFactory : ILabelsBufferFactory, ISingletonDependency
{
    private const string DefaultRoot = @"C:\GGA1C\buffers";

    // Буферы кешируем: в конструкторе каждого идёт создание папки и проверка схемы,
    // а запросы от виджета идут пачками.
    private readonly ConcurrentDictionary<string, SqliteLabelsBuffer> _buffers = new();

    public LabelsBufferFactory(IConfiguration configuration)
    {
        var root = configuration["Labels:BuffersRoot"];
        BuffersRoot = string.IsNullOrWhiteSpace(root) ? DefaultRoot : Path.GetFullPath(root);
        Directory.CreateDirectory(BuffersRoot);
    }

    public string BuffersRoot { get; }

    public ILabelsBuffer For(string slug)
    {
        var safe = Normalize(slug);
        return _buffers.GetOrAdd(safe, s => new SqliteLabelsBuffer(PathFor(s)));
    }

    public string PathFor(string slug)
    {
        return Path.Combine(BuffersRoot, Normalize(slug), "labels.db");
    }

    public BufferFileInfo Describe(string slug)
    {
        var path = PathFor(slug);
        var file = new FileInfo(path);

        return file.Exists
            ? new BufferFileInfo(path, true, file.Length, file.LastWriteTimeUtc)
            : new BufferFileInfo(path, false, 0, null);
    }

    public List<string> ListFolders()
    {
        if (!Directory.Exists(BuffersRoot))
        {
            return [];
        }

        return Directory.GetDirectories(BuffersRoot)
            .Select(d => Path.GetFileName(d))
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Delete(string slug)
    {
        var safe = Normalize(slug);

        _buffers.TryRemove(safe, out _);

        // Без этого папка не удалится: Microsoft.Data.Sqlite держит соединения в пуле,
        // и файл остаётся открытым даже после закрытия всех наших SqliteConnection.
        // Пулы остальных буферов тоже сбросятся - они просто откроются заново.
        SqliteConnection.ClearAllPools();

        var dir = Path.Combine(BuffersRoot, safe);

        if (!Directory.Exists(dir))
        {
            return;
        }

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException ex)
        {
            // Файл могла держать внешняя программа - например открытый источник ODBC
            // в GoLabel2 на клиентской машине. Сообщаем прямо, а не глотаем.
            throw new BusinessException("GGA1C.Sync:BufferFileLocked")
                .WithData("message",
                    $"Не удалось удалить папку буфера {dir}: файл занят. " +
                    "Возможно, он открыт в GoLabel2 или в другой программе.")
                .WithData("details", ex.Message);
        }
    }

    /// <summary>
    /// Slug попадает в путь на диске, поэтому проверяется строго. Без этого значение вида
    /// "..\..\GGA1C" уводило бы запись за пределы корня буферов — туда, где лежат Sync.db
    /// и ключ шифрования.
    /// </summary>
    private static string Normalize(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new BusinessException("GGA1C.Sync:EmptySlug")
                .WithData("message", "У учётной записи не задана папка буфера");
        }

        var trimmed = slug.Trim().ToLowerInvariant();

        if (!trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
        {
            throw new BusinessException("GGA1C.Sync:InvalidSlug")
                .WithData("message",
                    "Папка буфера может содержать только латиницу, цифры, дефис и подчёркивание");
        }

        return trimmed;
    }
}
