using System.Globalization;
using System.Xml.Linq;
using GGA1C.Sync.Printing.Profiles;

namespace GGA1C.Sync.Printing.GoLabel.Ezpx;

public static class EzpxPrinterSettings
{
    public static PrinterConnection? ReadEmbeddedLan(XElement root)
    {
        if (root.Element("CommunicationType")?.Value != PrinterConnection.Network ||
            !uint.TryParse(root.Element("NetworkIPAddress")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ip) || ip == 0 ||
            !int.TryParse(root.Element("NetworkPort")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            !PrinterConnection.ValidPort(port)) return null;
        return new(PrinterConnection.DecodeIPv4(ip), port);
    }

    public static Dictionary<string, double> TemplateValues(EzpxSetup setup) => new()
    {
        ["Darkness"] = setup.Darkness,
        ["Speed"] = setup.Speed,
        ["Stripper"] = setup.Stripper,
        ["PrintMode"] = setup.PrintMode,
        ["Stop"] = setup.Stop,
        ["PageDirection"] = (int)setup.PageDirection,
        ["DrawMode"] = setup.DrawMode,
        ["Rotate180"] = setup.Rotate180,
    };
}

public sealed partial class EzpxEditor
{
    public PrinterConnection? EmbeddedLan => EzpxPrinterSettings.ReadEmbeddedLan(_root);

    // A missing model can be bound only by an explicit choice; never relabel an existing model.
    public void BindMissingPrinterModel(string printerId)
    {
        if (!string.IsNullOrWhiteSpace(Template.PrinterModel))
            throw new InvalidOperationException("Модель уже задана в файле шаблона.");
        var caption = PrinterCatalog.Get(printerId).Caption;
        _root.SetElementValue("PrinterModel", caption);
        Template.PrinterModel = caption;
    }

    public void ApplyPrinterConnection(PrinterConnection connection)
    {
        connection.Validate();
        if (connection.IPv4.Length == 0) return;
        _root.SetElementValue("CommunicationType", connection.Type);
        _root.SetElementValue("NetworkIPAddress", PrinterConnection.EncodeIPv4(connection.IPv4));
        _root.SetElementValue("NetworkPort", connection.Port);
    }

    public void ApplyPrinterSettings(IReadOnlyDictionary<string, double> changes)
    {
        if (changes.Count == 0) return;
        var setup = _root.Element("Setup") ?? throw new InvalidOperationException("В шаблоне нет Setup.");
        var current = EzpxPrinterSettings.TemplateValues(Template.Setup);
        foreach (var (id, value) in changes)
        {
            // Unedited out-of-list values and loader fallbacks must not be written to XML.
            if (current.TryGetValue(id, out var original) && original == value) continue;
            var field = PrinterSettingsCatalog.Field(PrinterSettingsCatalog.Template, id);
            field.Validate(value);
            switch (id)
            {
                case "Darkness": setup.SetAttributeValue(id, Template.Setup.Darkness = (int)value); break;
                case "Speed": setup.SetAttributeValue(id, Template.Setup.Speed = (int)value); break;
                case "Stripper": setup.SetAttributeValue(id, Template.Setup.Stripper = (int)value); break;
                case "PrintMode": setup.SetAttributeValue(id, Template.Setup.PrintMode = (int)value); break;
                case "Stop": setup.SetAttributeValue(id, Template.Setup.Stop = (float)value); break;
                case "PageDirection":
                    Template.Setup.PageDirection = (PageDirection)value;
                    setup.SetAttributeValue(id, Template.Setup.PageDirection.ToString());
                    break;
                case "DrawMode": setup.SetAttributeValue(id, Template.Setup.DrawMode = (int)value); break;
                case "Rotate180": setup.SetAttributeValue(id, Template.Setup.Rotate180 = (int)value); break;
                default: throw new ArgumentException("Неизвестная настройка EZPX.");
            }
        }
    }
}
