namespace GGA1C.Sync.Templates;

/// <summary>Вид шаблона (П9): PDF-документ или наклейка GoLabel (.ezpx) на изделие / на коробку.</summary>
public enum PrintTemplateKind
{
    Pdf = 0,
    ProductLabel = 1,
    BoxLabel = 2,
}

public static class PrintTemplateConsts
{
    public const int MaxNameLength = 256;
    public const int MaxFileNameLength = 256;
    public const int MaxFileRefLength = 512;

    /// <summary>Шаблон наклейки или PDF — не больше 5 МБ (самый большой .ezpx стенда ~1 МБ).</summary>
    public const int MaxFileBytes = 5 * 1024 * 1024;
}
