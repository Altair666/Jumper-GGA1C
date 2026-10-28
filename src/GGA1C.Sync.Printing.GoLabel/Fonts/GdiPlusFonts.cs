using System.Runtime.InteropServices;

namespace GGA1C.Sync.Printing.GoLabel.Fonts;

/// <summary>
/// Замеры шрифтов Windows так, как их делает GoLabel через System.Drawing (GDI+), но напрямую
/// через gdiplus.dll — без пакетов. Нужен шаг строки многострочного текста принтера:
/// GoLabel берёт его из Font.Height (Text.cs, CalculateObjectSize/DrawInternalTTFont).
/// </summary>
internal static class GdiPlusFonts
{
    private const int UnitPixel = 2;
    private static readonly Lazy<IntPtr> ScreenGraphics = new(Start, isThreadSafe: true);

    /// <summary>
    /// System.Drawing.Font.Height (.NET Framework) для шрифта family/style размером
    /// emSizePixels в точках-пикселях: Math.Ceiling(GdipGetFontHeight) на графике экрана.
    /// </summary>
    public static int FontHeight(string family, int style, float emSizePixels)
    {
        var graphics = ScreenGraphics.Value;
        Check(GdipCreateFontFamilyFromName(family, IntPtr.Zero, out var fontFamily),
            $"шрифт «{family}» не найден в системе (нужен для замера строки, как в GoLabel)");
        try
        {
            Check(GdipCreateFont(fontFamily, emSizePixels, style, UnitPixel, out var font),
                $"шрифт «{family}» не создаётся со стилем {style}");
            try
            {
                Check(GdipGetFontHeight(font, graphics, out float height), "GdipGetFontHeight");
                return (int)Math.Ceiling(height);
            }
            finally
            {
                GdipDeleteFont(font);
            }
        }
        finally
        {
            GdipDeleteFontFamily(fontFamily);
        }
    }

    private static IntPtr Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Замер шрифтов GoLabel — только Windows (GDI+).");
        }
        var input = new StartupInput { Version = 1 };
        Check(GdiplusStartup(out _, ref input, IntPtr.Zero), "GdiplusStartup");
        // Graphics.FromHdcInternal(ScreenDC) в Font.Height .NET Framework.
        Check(GdipCreateFromHDC(GetDC(IntPtr.Zero), out var graphics), "GdipCreateFromHDC");
        return graphics;
    }

    private static void Check(int status, string what)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"GDI+: {what} (код {status}).");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public int Version;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out IntPtr token, ref StartupInput input, IntPtr output);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    private static extern int GdipCreateFontFamilyFromName(string name, IntPtr collection, out IntPtr family);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteFontFamily(IntPtr family);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateFont(IntPtr family, float emSize, int style, int unit, out IntPtr font);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteFont(IntPtr font);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetFontHeight(IntPtr font, IntPtr graphics, out float height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr graphics);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
}
