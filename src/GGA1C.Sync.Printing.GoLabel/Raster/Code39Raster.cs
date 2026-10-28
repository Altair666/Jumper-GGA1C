// Штрихкод Code39 с подписью шрифтом Windows — растром, как его рисует GoLabel через Neodynamic
// (BarCode.PrintBmpBarcode → Render2Bmp → BarcodeProfessional.GetBarcodeImage, QLabelSDK/BarCode.cs:3261,
// :1841). Код Neodynamic не переносится: кодер свой, раскладка подписи выведена по эталонам П2
// (docs/printing/p5-plan.md, «Шаг 4»).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
internal static class Code39Raster
{
    // Code39: на знак 5 полос и 4 промежутка, w — широкий элемент, n — узкий.
    private static readonly Dictionary<char, string> Patterns = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn", ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw",
        ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw", ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn",
        ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn", ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww",
        ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn",
        ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn", ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw",
        ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw", ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['*'] = "nwnnwnwnn",
        ['$'] = "nwnwnwnnn", ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn",
    };

    /// <summary>Картинка штрихкода (до порога), 24 бит. Положение Q — у вызывающего.</summary>
    public static Bitmap Render(EzpxBarCode b, string data)
    {
        if (b.Symbology != BarCodeType.Code39 || b.Direction != Orientation.Angle0)
        {
            throw new NotSupportedException($"Штрихкод растром {b.Symbology} ({b.Direction}) не перенесён — есть только Code39 без поворота.");
        }
        if (b.CaptionAlignment is not (CaptionType.TopAndLeft or CaptionType.BottomAndCenter))
        {
            throw new NotSupportedException($"Подпись штрихкода {b.CaptionAlignment} не перенесена (в эталонах только TopAndLeft и BottomAndCenter).");
        }
        int narrow = (int)Math.Round(b.Narrow, 0);
        int wide = (int)Math.Round(b.Width, 0);
        string code = "*" + data + "*";
        var bars = new List<(int X, int Width)>();
        int x = 0;
        foreach (char c in code)
        {
            if (!Patterns.TryGetValue(c, out var pattern))
            {
                throw new NotSupportedException($"Знак «{c}» не кодируется Code39.");
            }
            for (int k = 0; k < 9; k++)
            {
                int w = pattern[k] == 'w' ? wide : narrow;
                if (k % 2 == 0)
                {
                    bars.Add((x, w));
                }
                x += w;
            }
            x += narrow;
        }
        int width = x - narrow;

        var (family, emPoints, style) = b.CaptionFont;
        using var fontFamily = new FontFamily(family);
        float emPixels = emPoints * 96f / 72f;
        float lineHeight = emPixels * fontFamily.GetLineSpacing(style) / fontFamily.GetEmHeight(style);
        int height = (int)Math.Round(b.Height + b.Offset + lineHeight, 0);
        int barsHeight = (int)Math.Ceiling(b.Height);
        bool top = b.CaptionAlignment == CaptionType.TopAndLeft;
        int barsTop = top ? height - barsHeight : 0;

        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(96f, 96f);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        foreach (var bar in bars)
        {
            g.FillRectangle(Brushes.Black, bar.X, barsTop, bar.Width, barsHeight);
        }
        using var font = new Font(fontFamily, emPixels, style, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        float textX = 0f;
        if (!top)
        {
            textX = (width - g.MeasureString(data, font, 32767, format).Width) / 2f;
        }
        float textY = top ? 0f : b.Height + b.Offset;
        g.DrawString(data, font, Brushes.Black, textX, textY, format);
        return bitmap;
    }
}
