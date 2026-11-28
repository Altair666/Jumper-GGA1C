using GGA1C.Sync.Printing.Profiles;

namespace GGA1C.Sync.Printing.Devices;

public sealed class GodexPrinterSettings(PrinterProfiles profiles, bool dryRun, Action<string> log)
{
    public bool DryRun { get; } = dryRun;

    public Task<IReadOnlyList<DiscoveredPrinter>> ScanAsync(PrinterConnection draft, CancellationToken ct) =>
        GodexDiscovery.ScanAsync(draft, ct);

    public async Task<PrinterSettingsRead> RefreshAsync(Guid? tenantId, CancellationToken ct)
    {
        var profile = await ProfileAsync(tenantId).ConfigureAwait(false);
        // Explicit allowlist of read forms; the two argumentless XSET forms are reads in GoLabel II.
        var reply = await GodexSettingsTransport.QueryAsync(profile.Connection, GodexQuery.Miscellaneous, ct).ConfigureAwait(false);
        var result = GodexSettingsProtocol.ParseMiscellaneous(reply);
        if (result.Values.Count == 0 && result.MeasuredLengthMm is null)
            throw new IOException("Принтер не вернул читаемых настроек. Проверьте IPv4 и порт в профиле.");
        return result;
    }

    public async Task<PrinterSettingsResult> SetAsync(Guid? tenantId, IReadOnlyDictionary<string, double> values, CancellationToken ct)
    {
        var profile = await ProfileAsync(tenantId).ConfigureAwait(false);
        var length = profile.Value(PrinterCatalog.NonPrintableLength);
        var commands = GodexSettingsProtocol.BuildCommands(values, length);
        if (length > PrinterSettingsCatalog.MaxDeviceLengthMm)
            log(PrinterSettingsCatalog.DeviceLengthWarning);
        if (DryRun) return Simulate(commands);
        if (commands.Count != 0)
            await GodexSettingsTransport.WriteAsync(profile.Connection, string.Join("\r\n", commands), ct).ConfigureAwait(false);
        return new(commands.Count != 0 ? ["Команды отправлены."] : ["Нет значений для отправки."]);
    }

    public async Task<PrinterSettingsResult> BuzzerAsync(Guid? tenantId, CancellationToken ct)
    {
        var profile = await ProfileAsync(tenantId).ConfigureAwait(false);
        if (DryRun)
        {
            var simulated = Simulate([GodexSettingsTransport.BuzzerQuery,
                GodexSettingsProtocol.BuzzerOn + " → 200 мс → " + GodexSettingsProtocol.BuzzerOn + " → 200 мс → " +
                GodexSettingsProtocol.BuzzerOn + " → 200 мс",
                GodexSettingsProtocol.BuzzerOff + " (только если исходное состояние подтверждено как «Выкл.»)"]);
            return simulated;
        }

        var reply = await GodexSettingsTransport.QueryAsync(profile.Connection, GodexQuery.BuzzerStatus, ct).ConfigureAwait(false);
        var wasOn = GodexSettingsProtocol.BuzzerState(reply);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                await GodexSettingsTransport.WriteAsync(profile.Connection, GodexSettingsProtocol.BuzzerOn, ct).ConfigureAwait(false);
                await Task.Delay(GodexSettingsProtocol.BuzzerPause, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (wasOn == false)
                await GodexSettingsTransport.WriteAsync(profile.Connection, GodexSettingsProtocol.BuzzerOff, CancellationToken.None).ConfigureAwait(false);
        }
        return new(wasOn is null ? ["Исходное состояние неизвестно; зуммер не выключался."] : ["Команды зуммера отправлены."]);
    }

    private async Task<PrinterProfile> ProfileAsync(Guid? tenantId)
    {
        var profile = await profiles.ReadAsync(tenantId, PrinterCatalog.Zx430i).ConfigureAwait(false);
        profile.Connection.Validate(requireAddress: true);
        return profile;
    }

    private PrinterSettingsResult Simulate(IEnumerable<string> commands)
    {
        var messages = commands.Select(cmd => "Была бы отправлена команда: " + cmd).ToArray();
        foreach (var message in messages) log(message);
        return new(messages.Length != 0 ? messages : ["Нет значений для отправки."]);
    }
}
