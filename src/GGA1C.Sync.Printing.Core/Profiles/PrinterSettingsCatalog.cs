using System.Globalization;

namespace GGA1C.Sync.Printing.Profiles;

public enum PrinterSettingKind { Choice, Length, Integer, Checkbox, ReadOnly }

public sealed record PrinterChoice(int Value, string Caption);

public sealed record PrinterSetting(string Id, string Caption, PrinterSettingKind Kind, string? Command = null,
    IReadOnlyList<PrinterChoice>? Choices = null, double? Minimum = null, double? Maximum = null,
    string? Unit = null, string? Hint = null, double? Default = null,
    double CheckedValue = 1, double UncheckedValue = 0)
{
    public string? Error(double value)
    {
        if (double.IsFinite(value) && (Minimum is null || value >= Minimum) && (Maximum is null || value <= Maximum) &&
            (Choices is null || Choices.Any(choice => choice.Value == value)) &&
            (Kind != PrinterSettingKind.Integer || Math.Truncate(value) == value)) return null;
        if (Minimum is { } min && Maximum is { } max)
            return Kind == PrinterSettingKind.Integer ? $"Введите целое число от {min} до {max}." : $"Допустимый диапазон: {min}–{max} {Unit}.";
        if (Minimum is { } lower) return $"Введите число не меньше {lower}.";
        return "Выберите значение из списка принтера.";
    }

    public void Validate(double value)
    {
        if (Kind == PrinterSettingKind.ReadOnly || Error(value) is { })
            throw new ArgumentException($"{Caption}: {Error(value) ?? "поле только для чтения"}");
    }

    public string FormatCommand(double value) => string.Format(CultureInfo.InvariantCulture, Command!, value);
}

/// <summary>ZX430i: PrinterModel.xml, PrinterSetup.cs and the ru-RU PrinterSetup satellite resources.</summary>
public static class PrinterSettingsCatalog
{
    public const string SaveSerial = "save-serial";
    public const string ShowOutRange = "show-out-range";
    public const double MaxDeviceLengthMm = 3;
    public const string DeviceLengthWarning = "принтер принимает 0-3 мм, в профиле значение сохранено";

    private static IReadOnlyList<PrinterChoice> Indexed(params string[] labels) =>
        Array.AsReadOnly(labels.Select((label, i) => new PrinterChoice(i, label)).ToArray());
    private static IReadOnlyList<PrinterChoice> Numbers(params int[] numbers) =>
        Array.AsReadOnly(numbers.Select(n => new PrinterChoice(n, n.ToString(CultureInfo.InvariantCulture))).ToArray());
    private static readonly IReadOnlyList<PrinterChoice> OnOff = Array.AsReadOnly<PrinterChoice>([new(1, "Вкл."), new(0, "Выкл.")]);
    private static PrinterSetting List(string id, string caption, string? command, IReadOnlyList<PrinterChoice> choices, string? hint = null) =>
        new(id, caption, PrinterSettingKind.Choice, command, choices, Hint: hint);

    public static IReadOnlyList<PrinterSetting> Template { get; } = Array.AsReadOnly<PrinterSetting>([
        new("resolution", "Разрешение", PrinterSettingKind.ReadOnly, Unit: "dpi"),
        List("Darkness", "Яркость", "^H{0}", Numbers(Enumerable.Range(0, 20).ToArray())),
        List("Speed", "Скорость", "^S{0}", Numbers(2, 3, 4)),
        List("Stripper", "Отделитель", "^O{0}", Indexed("Нет", "Отделитель Вкл.")),
        List("PrintMode", "Режим печати", "^AT / ^AD", Indexed("Термотрансферная", "Прямая термопечать")),
        new("Stop", "Позиция отрыва", PrinterSettingKind.Length, "^E{0}", Minimum: 0, Maximum: 60, Unit: "mm"),
        List("PageDirection", "Направление страницы", null, Indexed("0°", "90°", "180°", "270°"),
            "Сборка задания печати поддерживает только направление 0°."),
        List("DrawMode", "Draw Mode", "^XSET,DRAWMODE,{0}", Indexed("Or", "Xor", "Overwrite")),
        new("Rotate180", "Повернуть", PrinterSettingKind.Checkbox, "~R{0}", Choices: Numbers(0, 255), CheckedValue: 0, UncheckedValue: 255),
    ]);

    public static IReadOnlyList<PrinterSetting> Application { get; } = Array.AsReadOnly<PrinterSetting>([
        new(SaveSerial, "Сохранить серийный номер до выхода", PrinterSettingKind.Checkbox, Default: 1),
        new(ShowOutRange, "Показать оповещение OutRange", PrinterSettingKind.Checkbox, Default: 0),
    ]);

    public static IReadOnlyList<PrinterSetting> Miscellaneous { get; } = Array.AsReadOnly<PrinterSetting>([
        List("CODEPAGE", "Кодовая страница", "^XSET,CODEPAGE,{0}", Indexed("850", "852", "437", "860", "863", "865", "857", "861", "862", "855", "866", "737", "851", "869", "Win 1252", "Win 1250", "Win 1251", "Win 1253", "Win 1254", "Win 1255", "Win 1257")),
        List("LANGUAGE", "Язык дисплея", "^XSET,LANGUAGE,{0}", Indexed("Английский", "Немецкий", "Французский", "Испанский", "Итальянский", "Упрощенный Китайский", "Традиционный Китайский", "Турецкий", "Японский / Польский", "Русский")),
        List("KEYBOARD", "Код страны клавиатуры", "^XSET,KEYBOARD,{0}", Indexed("США", "Великобритания", "Французский", "Немецкий", "Испанский", "Итальянский", "Финский", "Голландский", "Бельгийский", "Русский", "Норвежский язык", "Исландский", "Шведский", "Греческий", "Czech")),
        List("SENSOR", "Режим сенсоров", "^G{0}", Indexed("На отражение", "На просвет", "Автоматически")),
        List("BACKFEED", "Предварительная печать (BACKFEED)", "^XSET,BACKFEED,{0}", OnOff),
        List("TOPOFFORM", "Верх формы", "^XSET,TOPOFFORM,{0}", Indexed("0 - Выкл.", "1 - Вкл.", "2 - Выкл. при включении", "3 - Выкл. (но с резервным копированием)")),
        List("BUZZER", "Зуммер", "^XSET,BUZZER,{0}", OnOff),
        ProfileLength(),
        new("OFFSETX", "Смещение по оси X", PrinterSettingKind.Integer, "~S,OFFSETX,{0}", Minimum: -400, Maximum: 400, Unit: "dot"),
        new("OFFSETY", "Смещение по оси Y", PrinterSettingKind.Integer, "~S,OFFSETY,{0}", Minimum: -400, Maximum: 400, Unit: "dot"),
    ]);

    private static PrinterSetting ProfileLength()
    {
        var field = PrinterCatalog.Get(PrinterCatalog.Zx430i).Field(PrinterCatalog.NonPrintableLength);
        return new(field.Id, field.Caption, PrinterSettingKind.Length, "^XSET,DISMOFFSET,0,{0}",
            Minimum: field.Minimum, Maximum: field.Maximum, Unit: field.Unit, Hint: field.Hint);
    }

    public static PrinterSetting Field(IReadOnlyList<PrinterSetting> fields, string id) =>
        fields.FirstOrDefault(field => field.Id == id) ?? throw new ArgumentException("Неизвестное поле настроек принтера.");
}
