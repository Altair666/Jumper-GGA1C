using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Делает имя папки буфера из имени учётной записи.
///
/// Папка попадает в путь на диске и в адрес сетевой шары для GoLabel2, поэтому кириллица
/// там неуместна: имена шар с не-ASCII символами доставляют проблемы и в ODBC-строках,
/// и при настройке источника на клиентских машинах. Отсюда транслитерация.
///
/// Берётся первое слово — фамилия: «Иванов Пётр Сергеевич» даёт ivanov. Если слов нет
/// или после чистки ничего не осталось, вернётся пустая строка, и вызывающий решает,
/// что делать — молча подставлять мусор нельзя, это путь на диске.
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
    /// Имя папки из имени учётки: первое слово, транслитерация, нижний регистр.
    /// Возвращает пустую строку, если выжать нечего.
    /// </summary>
    public static string FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        // Фамилия — первое слово. Разделителями считаем и пробел, и запятую:
        // «Иванов, участок сборки» тоже должно дать ivanov.
        var surname = name.Trim().Split([' ', ',', ';', '\t'], System.StringSplitOptions.RemoveEmptyEntries)
                          .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(surname))
        {
            return "";
        }

        var sb = new StringBuilder();

        foreach (var ch in surname.ToLowerInvariant())
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
            // остальное отбрасываем молча: точки, кавычки и прочее в путях не нужны
        }

        return sb.ToString().Trim('-', '_');
    }
}
