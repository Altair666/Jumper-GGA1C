using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Делает имя папки буфера из имени учётной записи или из email.
///
/// Папка попадает в путь на диске и в адрес сетевой шары для GoLabel2, поэтому кириллица
/// там неуместна: имена шар с не-ASCII символами доставляют проблемы и в строках ODBC,
/// и при настройке источника на клиентских машинах. Отсюда транслитерация.
///
/// В интерфейсе папка не спрашивается — её выводят отсюда, поэтому обе функции обязаны
/// возвращать пустую строку, а не мусор, если выжать нечего: вызывающий тогда скажет
/// человеку, что не хватает данных, вместо того чтобы создать папку со случайным именем.
/// </summary>
public static class SlugGenerator
{
    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
    };

    /// <summary>
    /// Имя папки из имени учётки: первое слово (фамилия), транслитерация, нижний регистр.
    /// «Иванов Пётр Сергеевич» даёт ivanov, «Иванов, участок сборки» — тоже ivanov.
    /// </summary>
    public static string FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var surname = name.Trim()
            .Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return Sanitize(surname);
    }

    /// <summary>
    /// Имя папки из email: берётся часть до собачки. Запасной путь, когда фамилию
    /// транслитерировать не удалось — например имя записано только латиницей со знаками
    /// или вообще отсутствует.
    /// </summary>
    public static string FromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "";
        }

        var localPart = email.Trim().Split('@', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return Sanitize(localPart);
    }

    /// <summary>
    /// Имя папки: сначала пробуем фамилию, потом email. Именно в этом порядке —
    /// папка с фамилией понятна человеку, который будет настраивать источник ODBC
    /// на машине с GoLabel2, а a-morozov из адреса читается хуже.
    /// </summary>
    public static string FromNameOrEmail(string? name, string? email)
    {
        var fromName = FromName(name);
        return string.IsNullOrEmpty(fromName) ? FromEmail(email) : fromName;
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var sb = new StringBuilder();

        foreach (var ch in value.ToLowerInvariant())
        {
            if (Translit.TryGetValue(ch, out var replacement))
            {
                sb.Append(replacement);
            }
            else if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (ch is '-' or '_')
            {
                sb.Append(ch);
            }
            // остальное отбрасываем: точки, кавычки, пробелы в путях не нужны
        }

        return sb.ToString().Trim('-', '_');
    }
}
