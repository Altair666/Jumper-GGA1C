using System.Globalization;
using GGA1C.Sync.Printing.Profiles;

namespace GGA1C.Sync.Printing.Devices;

public sealed record PrinterSettingsRead(IReadOnlyDictionary<string, double> Values, double? MeasuredLengthMm, bool Complete);
public sealed record PrinterSettingsResult(IReadOnlyList<string> Messages);

internal static class GodexSettingsProtocol
{
    internal const string BuzzerOn = "^XSET,BUZZER,1";
    internal const string BuzzerOff = "^XSET,BUZZER,0";
    internal static readonly TimeSpan BuzzerPause = TimeSpan.FromMilliseconds(200);

    // GoLabel II: PrinterSetup.GetMiscellaneous, but no fallback to index 0 on missing/invalid data.
    internal static PrinterSettingsRead ParseMiscellaneous(string reply)
    {
        var values = new Dictionary<string, double>();
        double? length = null;
        void Add(string key, string text)
        {
            var field = PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Miscellaneous, key);
            if (int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) &&
                field.Error(number) is null) values[key] = number;
        }

        foreach (var segment in reply.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = segment.Trim();
            if (part.StartsWith("^G", StringComparison.Ordinal)) Add("SENSOR", part[2..]);
            if (part.StartsWith("^X", StringComparison.Ordinal))
            {
                var tokens = part.Split(',');
                if (tokens.Length >= 3 && tokens[1] is "CODEPAGE" or "BACKFEED" or "TOPOFFORM" or "BUZZER")
                    Add(tokens[1], tokens[2]);
            }
            if (part.StartsWith("^Y", StringComparison.Ordinal))
            {
                // Four ordered results following the NETCONFIG ^Y record. GoLabel also allows
                // extra ERROR09 lines before the offset results; never shift an ambiguous reply.
                var tokens = part.Split(',', 3);
                if (tokens.Length != 3) continue;
                var lines = tokens[2].Split("\r\n", StringSplitOptions.TrimEntries);
                var results = lines.Skip(1).TakeWhile(s => !s.Contains("rintable:", StringComparison.Ordinal)).ToArray();
                if (results.Length == 4 || (results.Length == 6 && results[2] == "ERROR09" && results[4] == "ERROR09"))
                {
                    Add("LANGUAGE", results[0]);
                    Add("KEYBOARD", results[1]);
                    Add("OFFSETX", results[results.Length == 4 ? 2 : 3]);
                    Add("OFFSETY", results[results.Length == 4 ? 3 : 5]);
                }
            }
        }

        var marker = reply.IndexOf("Unprintable: ", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var line = reply[(marker + "Unprintable: ".Length)..].Split(["\r\n", ";"], 2, StringSplitOptions.None)[0];
            var tokens = line.Split(',');
            if (tokens.Length == 3 && tokens.All(t => int.TryParse(t.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                length = int.Parse(tokens[0], CultureInfo.InvariantCulture) / 10.0;
        }
        return new(values, length, values.Count == PrinterSettingsCatalog.Miscellaneous.Count - 1 && length is not null);
    }

    internal static bool? BuzzerState(string reply)
    {
        var fields = reply.Split([';', ',']);
        if (fields.Length <= 16 || (fields.Length == 17 && !reply.EndsWith("\r\n", StringComparison.Ordinal))) return null;
        return fields[16].Trim() switch { "0" => false, "1" => true, _ => null };
    }

    internal static List<string> BuildCommands(IReadOnlyDictionary<string, double> changes, double lengthMm)
    {
        foreach (var (id, value) in changes)
        {
            if (id == PrinterCatalog.NonPrintableLength) throw new ArgumentException("Длина берётся из профиля принтера.");
            PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Miscellaneous, id).Validate(value);
        }
        var result = new List<string>();
        // The desktop sends these in a different order than it displays them.
        foreach (var id in new[] { "LANGUAGE", "KEYBOARD", "CODEPAGE", "BUZZER", "SENSOR", "BACKFEED", "TOPOFFORM", "OFFSETX", "OFFSETY" })
            if (changes.TryGetValue(id, out var value))
                result.Add(PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Miscellaneous, id).FormatCommand(value));
        var length = PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Miscellaneous, PrinterCatalog.NonPrintableLength);
        length.Validate(lengthMm);
        if (lengthMm <= PrinterSettingsCatalog.MaxDeviceLengthMm)
            result.Add(length.FormatCommand(10 * Math.Round(lengthMm, MidpointRounding.AwayFromZero)));
        return result;
    }
}
