using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
