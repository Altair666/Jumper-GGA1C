using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using GGA1C.Sync.Printing.Profiles;

namespace GGA1C.Sync.Printing.Devices;

public sealed record DiscoveredPrinter(string IPv4, int Port, string Model);

internal static class GodexDiscovery
{
    internal const int RequestPort = 8888;
    internal const int ReplyPort = 6666;
    internal const string Request = "Where is Godex Printer?";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<IReadOnlyList<DiscoveredPrinter>> ScanAsync(PrinterConnection candidate, CancellationToken ct)
    {
        candidate.Validate(requireAddress: true);
        var target = IPAddress.Parse(candidate.IPv4);
        var targetNumber = Number(target);
        var local = NetworkInterface.GetAllNetworkInterfaces().Where(i => i.OperationalStatus == OperationalStatus.Up)
            .SelectMany(i => i.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.PrefixLength > 0 &&
                (Number(a.Address) & Mask(a.PrefixLength)) == (targetNumber & Mask(a.PrefixLength)))
            .OrderByDescending(a => a.PrefixLength).FirstOrDefault();
        if (local is null || target.GetAddressBytes()[0] is 0 or >= 224)
            throw new ArgumentException("Для поиска укажите IPv4 в непосредственно подключённой подсети хаба.");
        var mask = Mask(local.PrefixLength);
        // Only one directly connected segment (at most 254 hosts). On loopback, /31–32 or a
        // wider network, unicast the same discovery packet to the selected IPv4 only.
        var broadcast = local.PrefixLength is >= 24 and <= 30 && !IPAddress.IsLoopback(target);
        var remote = broadcast ? FromNumber(targetNumber | ~mask) : target;
        if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Поиск принтера уже выполняется. Повторите после его окончания.");
        try
        {
            using var listener = new UdpClient(AddressFamily.InterNetwork);
            listener.ExclusiveAddressUse = true;
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, ReplyPort));
            using var sender = new UdpClient(new IPEndPoint(local.Address, 0)) { EnableBroadcast = broadcast };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var found = new Dictionary<string, DiscoveredPrinter>(StringComparer.Ordinal);
            try
            {
                await sender.SendAsync(Encoding.ASCII.GetBytes(Request), new IPEndPoint(remote, RequestPort), timeout.Token).ConfigureAwait(false);
                while (found.Count < 32)
                {
                    var packet = await listener.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    var source = Number(packet.RemoteEndPoint.Address);
                    if (broadcast ? (source & mask) != (targetNumber & mask) : source != targetNumber) continue;
                    if (Parse(packet.Buffer, packet.RemoteEndPoint.Address) is { } printer &&
                        PrinterCatalog.FromTemplateModel(printer.Model) == PrinterCatalog.Zx430i)
                        found.TryAdd(printer.IPv4, printer);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            return found.Values.OrderBy(p => p.IPv4, StringComparer.Ordinal).ToArray();
        }
        catch (SocketException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Не удалось выполнить поиск GoDEX. Проверьте доступность UDP 6666 и 8888.", ex);
        }
        finally { Gate.Release(); }
    }

    private static uint Number(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
    private static uint Mask(int prefix) => uint.MaxValue << (32 - prefix);
    private static IPAddress FromNumber(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    // UDP_Server: header, alias, serial, six-byte MAC, IPv4, port, boot version, then F/W.
    internal static DiscoveredPrinter? Parse(byte[] bytes, IPAddress source)
    {
        if (bytes.Length is < 24 or > 4096 || bytes[0] != 'P') return null;
        var packet = bytes.AsSpan();
        for (var i = 0; i < 3; i++)
        {
            var next = packet.IndexOf("\r\n"u8);
            if (next < 0) return null;
            packet = packet[(next + 2)..];
        }
        if (packet.Length < 16 || packet[6] != '\r' || packet[7] != '\n' || packet[12] != '\r' || packet[13] != '\n') return null;
        var ip = new IPAddress(packet.Slice(8, 4));
        if (!ip.Equals(source)) return null;
        var lines = Encoding.ASCII.GetString(packet[14..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3 || !int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            !PrinterConnection.ValidPort(port)) return null;
        foreach (var line in lines.Skip(2))
        {
            if (!line.StartsWith("F/W", StringComparison.Ordinal) || line.Length <= 6) continue;
            var firmware = line[6..].Split('(', 2)[0].Trim();
            var separator = firmware.LastIndexOf(' ');
            if (separator > 0) return new(ip.ToString(), port, firmware[..separator].Trim());
        }
        return null;
    }
}
