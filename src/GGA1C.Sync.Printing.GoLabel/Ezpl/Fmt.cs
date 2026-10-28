using System.Globalization;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

/// <summary>
/// Числа в командах — как их печатает .NET Framework, на котором собран GoLabel:
/// float.ToString() = «G» с 7 знаками, double.ToString() = «G» с 15 знаками. На .NET 10 без
/// точности «G» даёт кратчайшее представление, поэтому точность задаётся явно. Культура —
/// инвариантная (GoLabel сам меняет запятую на точку в шапке, а в координатах дробей нет).
/// </summary>
internal static class Fmt
{
    public static string F(float value) => value.ToString("G7", CultureInfo.InvariantCulture);

    public static string D(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
