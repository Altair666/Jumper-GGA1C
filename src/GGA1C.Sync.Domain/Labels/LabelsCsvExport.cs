using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Выгружает буфер человека в CSV, который читает GoLabel II.
///
/// Почему файл, а не подключение к базе: GoLabel остаётся программой печати — в нём размеры
/// наклеек, повороты, настройки принтера и шаблоны, и переносить это в веб означало бы писать
/// второй GoLabel. А из источников данных файл самый дешёвый: на рабочем месте не нужно ни
/// драйверов, ни учётных записей в СУБД. Разделение по людям держит их собственная учётная
/// запись Windows — права на папку, а не второй логин с паролем.
///
/// Файл пишется целиком и подменяется одним движением (во временный, потом переименование):
/// иначе GoLabel может прочитать половину партии, если печать совпала с синхронизацией.
/// </summary>
public class LabelsCsvExport : ITransientDependency
{
    private const string DefaultRoot = @"C:\GGA1C\labels";
    private const string FileName = "labels.csv";

    /// <summary>
    /// Порядок столбцов — он же порядок полей в шаблоне GoLabel, поэтому менять его нельзя
    /// без правки шаблонов: привязка там идёт по имени, но людям привычнее смотреть глазами.
    /// </summary>
    private static readonly string[] Columns =
    [
        "date", "product_code", "mode_code", "article", "name", "manufacturer",
        "barcode", "qty", "extra1", "extra2", "extra3", "external_id", "updated_at"
    ];

    private readonly IConfiguration _configuration;
    private readonly ILogger<LabelsCsvExport> _log;

    public LabelsCsvExport(IConfiguration configuration, ILogger<LabelsCsvExport> log)
    {
        _configuration = configuration;
        _log = log;
    }

    /// <summary>Корень, внутри которого лежат папки людей. Нужен подсказкам в интерфейсе.</summary>
    public string Root
    {
        get
        {
            var root = _configuration["Labels:ExportRoot"];
            return string.IsNullOrWhiteSpace(root) ? DefaultRoot : Path.GetFullPath(root);
        }
    }

    /// <summary>Путь к файлу человека — его прописывают в шаблоне GoLabel.</summary>
    public string PathFor(string slug)
    {
        return Path.Combine(Root, Normalize(slug), FileName);
    }

    public void Write(string slug, IEnumerable<LabelRecord> records)
    {
        var path = PathFor(slug);
        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.AppendLine(string.Join(',', Columns));

        foreach (var r in records)
        {
            text.AppendLine(string.Join(',', new[]
            {
                r.Date, r.ProductCode, r.ModeCode, r.Article, r.Name, r.Manufacturer,
                r.Barcode, r.Qty, r.Extra1, r.Extra2, r.Extra3, r.ExternalId,
                r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            }.Select(Quote)));
        }

        // UTF-8 с BOM: без метки .NET-овский читатель CSV в GoLabel принимает файл за ANSI,
        // и кириллица в наименованиях превращается в кашу. Если всё же окажется каша -
        // менять кодировку здесь, в одном месте.
        var temp = path + ".tmp";
        File.WriteAllText(temp, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.Move(temp, path, overwrite: true);

        _log.LogDebug("Выгружено в {Path}", path);
    }

    /// <summary>Удалить папку человека вместе с файлом — при удалении учётки.</summary>
    public void Delete(string slug)
    {
        var directory = Path.Combine(Root, Normalize(slug));

        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            // Файл может держать открытый шаблон в GoLabel на рабочем месте. Говорим прямо,
            // а не глотаем: иначе папка останется, и человек не поймёт, почему.
            throw new BusinessException("GGA1C.Sync:LabelsFileLocked")
                .WithData("message",
                    $"Не удалось удалить папку {directory}: файл занят. " +
                    "Возможно, он открыт в GoLabel на машине с принтером.")
                .WithData("details", ex.Message);
        }
    }

    private static string Quote(string? value)
    {
        var text = value ?? "";

        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? '"' + text.Replace("\"", "\"\"") + '"'
            : text;
    }

    /// <summary>
    /// Имя папки попадает в путь на диске, поэтому проверяется строго: иначе значение вида
    /// "..\..\GGA1C" увело бы запись туда, где лежат ключи и база.
    /// </summary>
    private static string Normalize(string slug)
    {
        var trimmed = (slug ?? "").Trim().ToLowerInvariant();

        if (trimmed.Length == 0 ||
            !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
        {
            throw new BusinessException("GGA1C.Sync:InvalidSlug")
                .WithData("message",
                    "Папка человека может содержать только латиницу, цифры, дефис и подчёркивание");
        }

        return trimmed;
    }
}
