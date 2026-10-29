using System;
using System.Text;
using Volo.Abp;

namespace GGA1C.Sync.Branding;

/// <summary>
/// Значок сайта (П16): вкладка браузера, бренд на странице входа и в шапке меню. Один файл,
/// который загружает администратор; без него — нейтральный значок из сборки.
///
/// Тип проверяется по содержимому, а не только по расширению: PNG — по сигнатуре, ICO — по
/// заголовку, SVG — текст с корнем <c>&lt;svg</c> и без скриптов (SVG — документ, и открытый
/// напрямую по адресу он исполнил бы скрипт на нашем домене; отдаётся ещё и с CSP без скриптов).
/// </summary>
public static class SiteIcon
{
    /// <summary>Значок маленький: 256 КБ хватает с запасом на PNG 512×512 и ICO с несколькими размерами.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Что можно загрузить — для подписи и атрибута accept.</summary>
    public const string AllowedExtensions = ".png,.svg,.ico";

    /// <summary>Имя файла в папке значка без расширения.</summary>
    public const string FileStem = "site-icon";

    /// <summary>Адрес, по которому хаб отдаёт действующий значок.</summary>
    public const string Url = "/site-icon";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Расширение (".png", ".svg", ".ico") по содержимому; не значок — UserFriendlyException.</summary>
    public static string Validate(string? fileName, byte[] content)
    {
        if (content.Length == 0)
        {
            throw new UserFriendlyException("Файл значка пустой.");
        }

        if (content.Length > MaxBytes)
        {
            throw new UserFriendlyException($"Значок больше {MaxBytes / 1024} КБ.");
        }

        var extension = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var detected = Detect(content);
        if (detected == null)
        {
            throw new UserFriendlyException("Значок должен быть PNG, SVG или ICO.");
        }

        if (extension != detected)
        {
            throw new UserFriendlyException($"Расширение файла ({(extension.Length == 0 ? "нет" : extension)}) не совпадает с содержимым ({detected}).");
        }

        if (detected == ".svg" && HasActiveContent(content))
        {
            throw new UserFriendlyException("В SVG-значке не должно быть скриптов, обработчиков событий и внешних ссылок.");
        }

        return detected;
    }

    public static string ContentType(string extension) => extension switch
    {
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, null),
    };

    private static string? Detect(byte[] content)
    {
        if (content.AsSpan().StartsWith(PngSignature))
        {
            return ".png";
        }

        // ICONDIR: зарезервировано 0, тип 1 (значок), число картинок > 0.
        if (content.Length >= 6 && content[0] == 0 && content[1] == 0 && content[2] == 1 && content[3] == 0
            && (content[4] | content[5]) != 0)
        {
            return ".ico";
        }

        var text = TryUtf8(content);
        if (text != null && text.Contains("<svg", StringComparison.OrdinalIgnoreCase)
            && text.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('<'))
        {
            return ".svg";
        }

        return null;
    }

    private static bool HasActiveContent(byte[] content)
    {
        var text = TryUtf8(content) ?? "";
        return text.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<foreignObject", StringComparison.OrdinalIgnoreCase)
            || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(text, @"\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(text, @"(href|src)\s*=\s*[""']\s*(https?:|//)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string? TryUtf8(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
