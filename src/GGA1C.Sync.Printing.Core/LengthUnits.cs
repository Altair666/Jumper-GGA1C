namespace GGA1C.Sync.Printing;

public enum LengthUnit { Millimetre, Centimetre, Dot, Inch }

public static class LengthUnits
{
    public static string Symbol(LengthUnit unit) => unit switch
    {
        LengthUnit.Centimetre => "cm",
        LengthUnit.Dot => "dot",
        LengthUnit.Inch => "inch",
        _ => "mm",
    };

    public static LengthUnit Next(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimetre => LengthUnit.Centimetre,
        LengthUnit.Centimetre => LengthUnit.Dot,
        LengthUnit.Dot => LengthUnit.Inch,
        _ => LengthUnit.Millimetre,
    };

    public static double PerMillimetre(LengthUnit unit, double dotsPerMm) => unit switch
    {
        LengthUnit.Centimetre => 0.1,
        LengthUnit.Dot => dotsPerMm,
        LengthUnit.Inch => 1.0 / 25.4,
        _ => 1,
    };

    public static double FromMillimetres(double value, LengthUnit unit, double dotsPerMm) =>
        value * PerMillimetre(unit, dotsPerMm);

    public static double ToMillimetres(double value, LengthUnit unit, double dotsPerMm) =>
        value / PerMillimetre(unit, dotsPerMm);
}
