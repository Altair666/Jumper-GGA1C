namespace GGA1C.Sync.Printing.Profiles;

public enum PrinterFieldKind { Length, Number }

public sealed record PrinterFieldDefinition(string Id, string Caption, PrinterFieldKind Kind,
    string Unit, double DefaultValue, bool ReadOnly = false, double? Minimum = null, double? Maximum = null,
    string? Hint = null)
{
    internal bool Accepts(double value) => double.IsFinite(value) &&
        (Minimum is null || value >= Minimum) && (Maximum is null || value <= Maximum);
}

public sealed record PrinterDefinition(string Id, string Caption, double DotsPerMm,
    IReadOnlyList<PrinterFieldDefinition> Fields)
{
    public PrinterFieldDefinition Field(string id) => Fields.FirstOrDefault(field => field.Id == id)
        ?? throw new ArgumentException("Неизвестное поле профиля принтера.", nameof(id));
}

public static class PrinterCatalog
{
    public const string CabEos = "cab-eos";
    public const string Zx430i = "zx430i";
    public const string NonPrintableLength = "non-printable-length";

    public static IReadOnlyList<PrinterDefinition> Printers { get; } = Array.AsReadOnly<PrinterDefinition>([
        new(CabEos, "cab EOS", 0, []),
        new(Zx430i, "ZX430i", 12, Array.AsReadOnly<PrinterFieldDefinition>([
            new(NonPrintableLength, "Непечатаемая длина", PrinterFieldKind.Length, "mm", 0, Minimum: 0,
                Hint: "принтеру передаётся 0-3 мм"),
            new("resolution", "Разрешение", PrinterFieldKind.Number, "dpi", 300, ReadOnly: true),
        ])),
    ]);

    public static string? FromTemplateModel(string? model) => model?.Trim() switch
    {
        string name when name.Equals("ZX430i", StringComparison.OrdinalIgnoreCase) => Zx430i,
        string name when name.Equals("cab EOS", StringComparison.OrdinalIgnoreCase) => CabEos,
        _ => null,
    };

    public static PrinterDefinition Get(string id) => Printers.FirstOrDefault(printer => printer.Id == id)
        ?? throw new ArgumentException("Неизвестный принтер.", nameof(id));
}
