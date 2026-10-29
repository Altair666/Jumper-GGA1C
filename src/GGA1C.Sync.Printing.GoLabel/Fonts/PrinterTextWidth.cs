using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace GGA1C.Sync.Printing.GoLabel.Fonts;

/// <summary>
/// БАГ-3, вариант А: ширина строки текста шрифтом принтера в точках — чтобы порт сам выравнивал
/// по центру и вправо, когда принтер не понимает суффиксы <c>P2</c>/<c>P3</c> (ZX430i, прошивка
/// v2.110). Метрики — тем же шрифтом Windows, что GoLabel рисует на экране: загруженный в
/// принтер TrueType — тот же TTF (семейство из FontCmd), встроенный — его экранный образ из
/// таблиц GoLabel (IBfonts). Замер без подгонки под сетку (AntiAlias): принтер растеризует сам.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PrinterTextWidth
{
    private static readonly object Lock = new();

    /// <summary>
    /// Ширина строки: GDI+ MeasureString (GenericTypographic) шрифтом <paramref name="family"/>
    /// размером <paramref name="emPixels"/> точек, × <paramref name="scaleX"/> (ширина знака к
    /// высоте или множитель по горизонтали) + промежуток <paramref name="gap"/> между знаками.
    /// </summary>
    public static float Measure(string family, FontStyle style, float emPixels, float scaleX, float gap, string line)
    {
        if (line.Length == 0)
        {
            return 0f;
        }
        lock (Lock)
        {
            using var bitmap = new Bitmap(1, 1, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bitmap);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var font = new Font(family, emPixels, style, GraphicsUnit.Pixel);
            if (!string.Equals(font.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Шрифт «{family}» не установлен в Windows — ширину строки для выравнивания не посчитать.");
            }
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap,
            };
            float width = g.MeasureString(line, font, int.MaxValue, format).Width;
            return width * scaleX + gap * (line.Length - 1);
        }
    }
}
