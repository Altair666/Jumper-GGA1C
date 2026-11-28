using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GGA1C.Sync.Printing.Devices;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using GGA1C.Sync.Printing.Profiles;
using Xunit;

namespace GGA1C.Sync.Printing;

public sealed class Zx430iSettingsTests : IDisposable
{
    private const string Model = PrinterCatalog.Zx430i;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gga1c-zx430i-" + Guid.NewGuid().ToString("N"));
    private PrinterProfiles Open() => new(new FilePrinterProfileStore(_folder));

    [Theory]
    [InlineData("192.168.1.50", 838969536U)]
    [InlineData("10.0.0.1", 16777226U)]
    [InlineData("255.255.255.255", 4294967295U)]
    [InlineData("", 0U)]
    public void IPv4UsesGoLabelByteOrder(string input, uint number)
    {
        Assert.Equal(number, PrinterConnection.EncodeIPv4(input));
        Assert.Equal(input, PrinterConnection.DecodeIPv4(number));
    }

    [Theory]
    [InlineData("127.1")]
    [InlineData("010.0.0.1")]
    [InlineData("256.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("1.2.3.4 ")]
    [InlineData("1.2.3.٤")]
    public void RejectsNonCanonicalOrInvalidIPv4(string input) =>
        Assert.Throws<ArgumentException>(() => PrinterConnection.EncodeIPv4(input));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task RejectsInvalidProfilePorts(int port)
    {
        var core = Open();
        await Assert.ThrowsAsync<ArgumentException>(() => core.SaveConnectionAsync(null, Model, 0, new("10.0.0.1", port)));
        Assert.Equal(new PrinterConnection(), (await core.ReadAsync(null, Model)).Connection);
    }

    [Fact]
    public async Task LegacyProfileReadsAndUpgradesKeepingLengthAndSeparateConnection()
    {
        var file = Path.Combine(_folder, "host", Model + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, """
            {"Revision":4,"Values":{"non-printable-length":10},"LegacyValues":[],"LegacyConflictPending":false,"LegacyChoiceMade":false}
            """);
        var core = Open();
        var before = await core.ReadAsync(null, Model);
        Assert.Equal(10, before.Value(PrinterCatalog.NonPrintableLength));
        Assert.Equal("", before.Connection.IPv4);
        Assert.Equal(9100, before.Connection.Port);
        var decision = await core.SaveConnectionAsync(null, Model, before.Revision, before.Connection with { ImportDecided = true });
        Assert.Equal(10, decision.Value(PrinterCatalog.NonPrintableLength));
        Assert.True((await Open().ReadAsync(null, Model)).Connection.ImportDecided);
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.SaveConnectionAsync(null, Model, before.Revision, new("10.0.0.1")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("Network", json.RootElement.GetProperty("Connection").GetProperty("Type").GetString());
        Assert.Equal(new PrinterApplicationOptions(true, false), await core.ReadOptionsAsync(null));
        await core.SaveOptionAsync(null, PrinterSettingsCatalog.ShowOutRange, true);
        Assert.Equal(new PrinterApplicationOptions(true, true), await Open().ReadOptionsAsync(null));
        Assert.Equal(new PrinterApplicationOptions(true, false), await core.ReadOptionsAsync(Guid.NewGuid()));
    }

    [Fact]
    public void EZPXChangesOnlyExplicitAttributesAndConfiguredLanElements()
    {
        var editor = Load("""
            <PrintJob Unknown="original"><FormatVersion>1</FormatVersion><PrinterModel>ZX430i</PrinterModel><PrinterLanguage>EZPL</PrinterLanguage>
              <CommunicationType>Network</CommunicationType><NetworkIPAddress>838969536</NetworkIPAddress><NetworkPort>9101</NetworkPort>
              <VendorFeature Extra="yes">untouched</VendorFeature><Setup Darkness="5" Rotate180="300" Mystery="keep"><Dpi>203</Dpi></Setup>
            </PrintJob>
            """);
        Assert.Equal(new PrinterConnection("192.168.1.50", 9101), editor.EmbeddedLan);
        editor.ApplyPrinterSettings(new Dictionary<string, double> { ["Speed"] = 20, ["Darkness"] = 12, ["Rotate180"] = 300,
            ["PrintMode"] = 1, ["PageDirection"] = 3, ["Stop"] = 5.25, ["DrawMode"] = 2 });
        editor.ApplyPrinterConnection(new());
        var withoutLanChange = Save(editor);
        Assert.Equal("838969536", withoutLanChange.Element("NetworkIPAddress")!.Value);
        Assert.Equal("9101", withoutLanChange.Element("NetworkPort")!.Value);
        editor.ApplyPrinterConnection(new("10.0.0.1", 12345));
        var root = Save(editor);
        Assert.Equal("16777226", root.Element("NetworkIPAddress")!.Value);
        Assert.Equal("12345", root.Element("NetworkPort")!.Value);
        Assert.Equal("ZX430i", root.Element("PrinterModel")!.Value);
        Assert.Equal("EZPL", root.Element("PrinterLanguage")!.Value);
        Assert.Equal("yes", root.Element("VendorFeature")!.Attribute("Extra")!.Value);
        var setup = root.Element("Setup")!;
        Assert.Null(setup.Attribute("Speed"));
        Assert.Null(setup.Element("Layout"));
        Assert.Equal("203", setup.Element("Dpi")!.Value);
        Assert.Equal("keep", setup.Attribute("Mystery")!.Value);
        Assert.Equal("300", setup.Attribute("Rotate180")!.Value);
        Assert.Equal("12", setup.Attribute("Darkness")!.Value);
        Assert.Equal("LandScape180", setup.Attribute("PageDirection")!.Value);
        Assert.Equal("5.25", setup.Attribute("Stop")!.Value);
        Assert.Equal("2", setup.Attribute("DrawMode")!.Value);
        Assert.Throws<ArgumentException>(() => editor.ApplyPrinterSettings(new Dictionary<string, double> { ["Speed"] = 5 }));
        Assert.Throws<InvalidOperationException>(() => editor.BindMissingPrinterModel(PrinterCatalog.CabEos));
    }

    [Fact]
    public void EmptyLanDoesNotCreateElementsAndMissingModelRequiresExplicitBinding()
    {
        var editor = Load("<PrintJob><FormatVersion>1</FormatVersion><Setup /></PrintJob>");
        editor.ApplyPrinterConnection(new());
        Assert.Null(Save(editor).Element("NetworkIPAddress"));
        editor.BindMissingPrinterModel(Model);
        Assert.Equal("ZX430i", Save(editor).Element("PrinterModel")!.Value);
        Assert.Null(Save(editor).Element("PrinterLanguage"));
        editor.ApplyPrinterConnection(new("10.0.0.1", 65535));
        Assert.Equal("65535", Save(editor).Element("NetworkPort")!.Value);
    }

    [Fact]
    public async Task SettingsReadExactWireFormsSeparateMeasuredLengthAndHandleDisconnect()
    {
        var core = Open();
        var logs = new List<string>();
        var device = new GodexPrinterSettings(core, dryRun: true, logs.Add);
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var saved = await core.SaveConnectionAsync(null, Model, 0, new("127.0.0.1", port));
        saved = await core.SaveAsync(null, Model, saved.Revision, new Dictionary<string,double> { [PrinterCatalog.NonPrintableLength] = 10 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var reading = device.RefreshAsync(null, timeout.Token);
        using (var client = await server.AcceptTcpClientAsync(timeout.Token))
        {
            using var stream = client.GetStream();
            var request = "^XSET,NETCONFIG\r\n^XGET,LANGUAGE\r\n^XGET,KEYBOARD\r\n~S,OFFSETX\r\n~S,OFFSETY\r\n^XSET,DISMOFFSET\r\n";
            var bytes = new byte[request.Length];
            await stream.ReadExactlyAsync(bytes, timeout.Token);
            Assert.Equal(request, Encoding.ASCII.GetString(bytes));
            await stream.WriteAsync("^G2;^XSET,CODEPAGE,16;^XSET,BACKFEED,1;^XSET,TOPOFFORM,3;^XSET,BUZZER,0;^Y1,a,b\r\n8\r\n14\r\n-400\r\n400\r\nUnprintable: "u8.ToArray(), timeout.Token);
            await stream.WriteAsync("25,0,30\r\n"u8.ToArray(), timeout.Token);
            var result = await reading;
            Assert.True(result.Complete);
            Assert.Equal(2.5, result.MeasuredLengthMm);
            Assert.Equal(-400, result.Values["OFFSETX"]);
            Assert.Equal(14, result.Values["KEYBOARD"]);
            Assert.Equal(10, (await core.ReadAsync(null, Model)).Value(PrinterCatalog.NonPrintableLength));
        }
        var setting = await device.SetAsync(null, new Dictionary<string, double> { ["CODEPAGE"] = 16, ["SENSOR"] = 2, ["OFFSETY"] = -400 }, timeout.Token);
        Assert.Contains("Была бы отправлена команда: ^XSET,CODEPAGE,16", setting.Messages);
        Assert.Contains("Была бы отправлена команда: ^G2", setting.Messages);
        Assert.Contains("Была бы отправлена команда: ~S,OFFSETY,-400", setting.Messages);
        Assert.DoesNotContain(setting.Messages, m => m.Contains("DISMOFFSET"));
        Assert.Contains(PrinterSettingsCatalog.DeviceLengthWarning, logs);
        var buzzer = await device.BuzzerAsync(null, timeout.Token);
        Assert.Contains(buzzer.Messages, m => m.Contains("^XGET,APPINFO2"));
        Assert.Contains(buzzer.Messages, m => m.Split("^XSET,BUZZER,1").Length == 4 && m.Split("200 мс").Length == 4);
        Assert.Contains(buzzer.Messages, m => m.Contains("^XSET,BUZZER,0") && m.Contains("только если"));
        Assert.False(server.Pending());
        await Assert.ThrowsAsync<ArgumentException>(() => device.SetAsync(null, new Dictionary<string, double> { ["OFFSETX"] = 400.1 }, timeout.Token));
        await core.SaveAsync(null, Model, saved.Revision, new Dictionary<string,double> { [PrinterCatalog.NonPrintableLength] = 2.5 });
        setting = await device.SetAsync(null, new Dictionary<string, double>(), timeout.Token);
        Assert.Contains("Была бы отправлена команда: ^XSET,DISMOFFSET,0,30", setting.Messages);
        Assert.False(server.Pending());
        server.Stop();
        await Assert.ThrowsAsync<IOException>(() => device.RefreshAsync(null, timeout.Token));
        Assert.NotEmpty((await device.BuzzerAsync(null, timeout.Token)).Messages);
    }

    [Fact]
    public async Task DiscoveryNeedsUdpModelReplyAndCanBeCancelled()
    {
        var core = Open();
        var device = new GodexPrinterSettings(core, dryRun: true, _ => { });
        using var printer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 8888));
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var search = device.ScanAsync(new("127.0.0.1"), limit.Token);
        var request = await printer.ReceiveAsync(limit.Token);
        Assert.Equal("Where is Godex Printer?", Encoding.ASCII.GetString(request.Buffer));
        var target = new IPEndPoint(IPAddress.Loopback, 6666);
        await printer.SendAsync("ZX430i"u8.ToArray(), target, limit.Token);
        foreach (var model in new[] { "ZX430i+", "ZX430i" })
        {
            byte[] reply = [.. "P\r\nAlias\r\nSerial\r\n"u8, 1,2,3,4,5,6,13,10,127,0,0,1,13,10,
                .. Encoding.ASCII.GetBytes("9100\r\nBoot\r\nF/W = " + model + " V2.110\r\n")];
            await printer.SendAsync(reply, target, limit.Token);
        }
        var found = Assert.Single(await search);
        Assert.Equal(new DiscoveredPrinter("127.0.0.1", 9100, "ZX430i"), found);
        using var cancelled = new CancellationTokenSource();
        var second = device.ScanAsync(new("127.0.0.1"), cancelled.Token);
        await printer.ReceiveAsync(limit.Token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
    }

    private static EzpxEditor Load(string xml) => EzpxEditor.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    private static XElement Save(EzpxEditor editor)
    {
        using var stream = new MemoryStream();
        editor.Save(stream);
        stream.Position = 0;
        return XDocument.Load(stream).Root!;
    }
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
