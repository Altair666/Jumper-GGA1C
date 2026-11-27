using GGA1C.Sync.Printing.Profiles;
using Xunit;

namespace GGA1C.Sync.Printing;

public sealed class PrinterProfileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gga1c-printer-profile-test-" + Guid.NewGuid().ToString("N"));
    private const string Printer = PrinterCatalog.Zx430i;
    private const string Length = PrinterCatalog.NonPrintableLength;

    private PrinterProfiles Open() => new(new FilePrinterProfileStore(_folder));
    private static Dictionary<string, double> LengthValue(double value) => new() { [Length] = value };

    [Fact]
    public async Task CoreOwnsCatalogueDefaultsAndValidation()
    {
        Assert.Equal(new[] { "cab EOS", "ZX430i" }, PrinterCatalog.Printers.Select(item => item.Caption));
        var core = Open();
        Assert.Empty((await core.ReadAsync(null, "cab-eos")).Values);
        var zx = await core.ReadAsync(null, Printer);
        Assert.Equal(0, zx.Value(Length));
        Assert.Equal(300, zx.Value("resolution"));
        Assert.Equal(12, zx.Definition.DotsPerMm);
        foreach (var value in new[] { -1, double.NaN, double.PositiveInfinity })
            await Assert.ThrowsAsync<ArgumentException>(() => core.SaveAsync(null, Printer, zx.Revision, LengthValue(value)));
        await Assert.ThrowsAsync<ArgumentException>(() => core.SaveAsync(null, Printer, zx.Revision,
            new Dictionary<string, double> { ["resolution"] = 600 }));
        await Assert.ThrowsAsync<ArgumentException>(() => core.SaveAsync(null, Printer, zx.Revision,
            new Dictionary<string, double> { ["speed"] = 4 }));
        Assert.Equal(zx.Revision, (await core.ReadAsync(null, Printer)).Revision);
    }

    [Fact]
    public async Task SavesWithoutRoundingAndPublishesOnlyToTheMatchingTenant()
    {
        var core = Open();
        var tenant = Guid.NewGuid();
        var delivered = new List<double>();
        var wrongTenant = false;
        using var right = core.Observe(tenant, change => delivered.Add(change.Value(Length)));
        using var wrong = core.Observe(null, _ => wrongTenant = true);
        var before = await core.ReadAsync(tenant, Printer);
        var mm = LengthUnits.ToMillimetres(1.23, LengthUnit.Dot, before.Definition.DotsPerMm);
        var after = await core.SaveAsync(tenant, Printer, before.Revision, LengthValue(mm));
        Assert.Equal(0.1025, after.Value(Length), 13);
        Assert.Equal(after.Value(Length), (await Open().ReadAsync(tenant, Printer)).Value(Length));
        Assert.Equal(new[] { mm }, delivered);
        Assert.False(wrongTenant);
        Assert.Equal(0, (await core.ReadAsync(null, Printer)).Value(Length));
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.SaveAsync(tenant, Printer, before.Revision, LengthValue(10)));
        right.Dispose();
        await core.SaveAsync(tenant, Printer, after.Revision, LengthValue(10));
        Assert.Single(delivered);
    }

    [Fact]
    public async Task ImportDoesNotOverwriteConflictingValuesAndPersistsOneChoice()
    {
        var core = Open();
        var pending = await core.OfferLegacyLengthsAsync(null, [10, 5.725, 10, -2, double.NaN]);
        Assert.Equal(0, pending.Value(Length));
        Assert.Equal(new[] { 5.725, 10 }, pending.LegacyChoices);
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.SaveAsync(null, Printer, pending.Revision, LengthValue(3)));
        Assert.Equal(pending.LegacyChoices, (await Open().ReadAsync(null, Printer)).LegacyChoices);
        var resolved = await core.ResolveLegacyLengthsAsync(null, 10);
        Assert.Equal(10, resolved.Value(Length));
        Assert.Empty(resolved.LegacyChoices);
        Assert.Equal(10, (await core.ResolveLegacyLengthsAsync(null, 5.725)).Value(Length));
        var later = await Open().OfferLegacyLengthsAsync(null, [7]);
        Assert.Empty(later.LegacyChoices);
        Assert.Equal(10, later.Value(Length));
    }

    [Fact]
    public async Task LateBrowserCanSurfaceConflictWithoutUnderminingExplicitZeroOrReplayingOldRecords()
    {
        var core = Open();
        var imported = await core.OfferLegacyLengthsAsync(null, [10]);
        Assert.Equal(10, imported.Value(Length));
        var edited = await core.SaveAsync(null, Printer, imported.Revision, LengthValue(0));
        Assert.Empty(edited.LegacyChoices);
        Assert.Empty((await core.OfferLegacyLengthsAsync(null, [10])).LegacyChoices);
        var conflict = await core.OfferLegacyLengthsAsync(null, [5]);
        Assert.Equal(0, conflict.Value(Length));
        Assert.Equal(new[] { 0d, 5, 10 }, conflict.LegacyChoices);
        Assert.Equal(0, (await core.ResolveLegacyLengthsAsync(null, 0)).Value(Length));
    }

    [Theory]
    [InlineData(LengthUnit.Millimetre, 10)]
    [InlineData(LengthUnit.Centimetre, 1)]
    [InlineData(LengthUnit.Dot, 120)]
    [InlineData(LengthUnit.Inch, 10 / 25.4)]
    public void UnitConversionsAreReversible(LengthUnit unit, double expected)
    {
        Assert.Equal(expected, LengthUnits.FromMillimetres(10, unit, 12), 12);
        Assert.Equal(10, LengthUnits.ToMillimetres(expected, unit, 12), 12);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
