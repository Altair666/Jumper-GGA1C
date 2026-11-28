namespace GGA1C.Sync.Printing.Profiles;

/// <summary>Application settings, separate from individual printer profiles and EZPX.</summary>
public sealed record PrinterApplicationOptions(bool SaveSerial, bool ShowOutRange)
{
    internal static PrinterApplicationOptions Defaults() => new(
        PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Application, PrinterSettingsCatalog.SaveSerial).Default == 1,
        PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Application, PrinterSettingsCatalog.ShowOutRange).Default == 1);

    public bool Enabled(string id) => id switch
    {
        PrinterSettingsCatalog.SaveSerial => SaveSerial,
        PrinterSettingsCatalog.ShowOutRange => ShowOutRange,
        _ => throw new ArgumentException("Неизвестная опция приложения."),
    };

    internal PrinterApplicationOptions With(string id, bool enabled) => id switch
    {
        PrinterSettingsCatalog.SaveSerial => this with { SaveSerial = enabled },
        PrinterSettingsCatalog.ShowOutRange => this with { ShowOutRange = enabled },
        _ => throw new ArgumentException("Неизвестная опция приложения."),
    };
}
