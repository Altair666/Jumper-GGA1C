using System.Text;

namespace GGA1C.Sync.Printing;

/// <summary>
/// Раскладка задания EZPL на команды для сравнения с эталоном: строки до CR LF, растр
/// «Q x,y,байт,высота\n» + байты + CR LF — одной командой.
/// </summary>
public static class EzplDump
{
    /// <summary>Метка растра Q в сравнении «без растров».</summary>
    public const string MaskedRaster = "Q*";

    public sealed record Command(int Offset, string Text, byte[] Bytes)
    {
        public bool IsRaster => Text.StartsWith('Q') && Bytes.Length > Text.Length + 1;
    }

    public static List<Command> Split(byte[] job)
    {
        var commands = new List<Command>();
        int i = 0;
        while (i < job.Length)
        {
            int lf = Array.IndexOf(job, (byte)'\n', i);
            int end = lf < 0 ? job.Length : lf + 1;
            if (job[i] == (byte)'Q' && lf > 0 && TryRasterHeader(job, i, lf, out int size))
            {
                int rasterEnd = Math.Min(job.Length, lf + 1 + size + 2);
                commands.Add(new Command(i, Encoding.ASCII.GetString(job, i, lf - i), job[i..rasterEnd]));
                i = rasterEnd;
                continue;
            }
            int textEnd = lf > i && job[lf - 1] == (byte)'\r' ? lf - 1 : (lf < 0 ? job.Length : lf);
            commands.Add(new Command(i, Encoding.UTF8.GetString(job, i, textEnd - i), job[i..end]));
            i = end;
        }
        return commands;
    }

    /// <summary>Команды с растрами, заменёнными на <see cref="MaskedRaster"/>.</summary>
    public static List<string> Masked(byte[] job) =>
        Split(job).Select(c => c.IsRaster ? MaskedRaster : c.Text).ToList();

    private static bool TryRasterHeader(byte[] job, int start, int lf, out int size)
    {
        size = 0;
        string header = Encoding.ASCII.GetString(job, start + 1, lf - start - 1);
        string[] parts = header.Split(',');
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out _)))
        {
            return false;
        }
        size = int.Parse(parts[2]) * int.Parse(parts[3]);
        return true;
    }

    /// <summary>Первая расходящаяся команда — для сообщения теста (зачёт только по равенству).</summary>
    public static string FirstDifference(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        int n = Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < n; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"команда #{i}: эталон «{expected[i]}», порт «{actual[i]}»";
            }
        }
        return expected.Count == actual.Count
            ? "совпадают"
            : $"длина: эталон {expected.Count} команд, порт {actual.Count}; следующая — «{(expected.Count > n ? expected[n] : actual[n])}»";
    }

    /// <summary>Первая расходящаяся команда по байтам; для растров — число расходящихся байт.</summary>
    public static string FirstByteDifference(byte[] expected, byte[] actual)
    {
        var e = Split(expected);
        var a = Split(actual);
        int n = Math.Min(e.Count, a.Count);
        for (int i = 0; i < n; i++)
        {
            if (!e[i].Bytes.AsSpan().SequenceEqual(a[i].Bytes))
            {
                string detail = e[i].IsRaster && a[i].IsRaster && e[i].Bytes.Length == a[i].Bytes.Length
                    ? $", байт растра мимо: {e[i].Bytes.Zip(a[i].Bytes).Count(p => p.First != p.Second)}"
                    : "";
                return $"команда #{i} (смещение {e[i].Offset}): эталон «{e[i].Text}», порт «{a[i].Text}»{detail}";
            }
        }
        return e.Count == a.Count ? "совпадают" : $"длина: эталон {e.Count} команд, порт {a.Count}";
    }
}
