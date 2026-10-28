// Перечисления модели .ezpx. Источник: декомпилят QLabelSDK 1.5 (C:\GGA1C\golabel-src\QLabelSDK),
// одноимённые файлы. Имена и порядок значений не менять: в .ezpx они записаны именами, а в
// команды EZPL уходят числовые значения ((int)Direction, (int)CaptionAlignment ...).
namespace GGA1C.Sync.Printing.GoLabel.Ezpx;

public enum Orientation
{
    Angle0,
    Angle90,
    Angle180,
    Angle270,
    Angle0_Vertical,
    Angle90_Vertical,
    Angle180_Vertical,
    Angle270_Vertical
}

public enum CaptionType
{
    None,
    BottomAndLeft,
    TopAndLeft,
    BottomAndCenter,
    TopAndCenter,
    BottomAndRight,
    TopAndRight,
    BottomAndJustify,
    TopAndJustify
}

public enum FontSource
{
    BuiltIn_Bitmap_Font,
    TrueType_Font,
    DownLoad_Bitmap_Font,
    DownLoad_Asia_Font_1,
    DownLoad_Asia_Font_2,
    DownLoad_Asia_Font_3,
    DownLoad_Asia_Font_4,
    DownLoad_Asia_Font_5,
    DownLoad_Asia_Font_6,
    DownLoad_Asia_Font_7,
    DownLoad_Asia_Font_8
}

public enum FontIDs
{
    Default, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    BuiltIn_1, BuiltIn_2, BuiltIn_3, BuiltIn_4, BuiltIn_5
}

public enum FontEncodings
{
    Default, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z
}

public enum TextAlignment
{
    Left,
    Center,
    Right,
    Distributed
}

public enum AcrossType
{
    Copied,
    LeftToRight,
    TopToButtom
}

public enum PageDirection
{
    Portrait,
    LandScape,
    Portrait180,
    LandScape180
}

public enum Unit
{
    Cm,
    Inch,
    Mm,
    Dot,
    Point
}

public enum PageAlignment
{
    None,
    Top,
    Center,
    Bottom
}

public enum BarCodeType
{
    Code39Extended, Code39ExtendedCheckDigit, Code39, Code39CheckDigit, EAN8, EAN8Add2, EAN8Add5,
    EAN13, EAN13Add2, EAN13Add5, UPCA, UPCAAdd2, UPCAAdd5, UPCE, UPCEAdd2, UPCEAdd5, I2of5,
    I2of5CheckDigit, Codabar, Code93, Code128Auto, Code128Subset, UCC128, PostNet, DUN14, EAN128,
    Plessy, RPS128, ChinaPost, HIBC, I2of5ShippingBearerBar, UCC128KMart, UCC128RANDAN, Telepen,
    FIM, MSI1Mod10, MSI2Mod10, MSI1Mod1110, MSI1NoCheck, GermanPost, Planet, Code11, Standard2of5,
    Industrial2of5, Logmars, ISBT, Code32, Code39CheckDigitStart, Code39Start, Matrix2of5,
    Pharmacode, Code128CheckDigit, EAN13P7
}

public enum CheckDigitType
{
    None,
    MOD_43,
    UCC_MOD_10,
    MSI_MOD_10,
    UNISON_MOD_10
}

// Code128 — перечисление Neodynamic (в декомпиляте его нет): читается по имени, числовое
// значение в команды не уходит.
public enum Code128Subset
{
    Auto,
    A,
    B,
    C
}

public enum LineShape
{
    HLine,
    VLine,
    FillRect
}
