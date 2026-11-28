using System.Net;
using System.Net.Sockets;
using System.Text;
using GGA1C.Sync.Printing.Profiles;

namespace GGA1C.Sync.Printing.Devices;

internal enum GodexQuery { Miscellaneous, BuzzerStatus }

/// <summary>Separate settings connection; nothing here is used for print jobs.</summary>
internal static class GodexSettingsTransport
{
    internal const string MiscellaneousQuery = "^XSET,NETCONFIG\r\n^XGET,LANGUAGE\r\n^XGET,KEYBOARD\r\n~S,OFFSETX\r\n~S,OFFSETY\r\n^XSET,DISMOFFSET";
    internal const string BuzzerQuery = "^XGET,APPINFO2";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private const int MaxReply = 8192;

    internal static async Task<string> QueryAsync(PrinterConnection connection, GodexQuery query, CancellationToken ct)
    {
        var command = query == GodexQuery.Miscellaneous ? MiscellaneousQuery : BuzzerQuery;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(Timeout);
        using var client = await ConnectAsync(connection, timer.Token, ct).ConfigureAwait(false);
        using var stream = client.GetStream();
        if (!await SendAsync(stream, command, timer.Token, ct).ConfigureAwait(false)) return "";
        var reply = new StringBuilder();
        var buffer = new byte[1024];
        try
        {
            while (reply.Length < MaxReply)
            {
                var size = await stream.ReadAsync(buffer, timer.Token).ConfigureAwait(false);
                if (size == 0) break;
                reply.Append(Encoding.ASCII.GetString(buffer, 0, size));
                // Keep the complete marker line when TCP splits the reply inside that line.
                var text = reply.ToString();
                var marker = query == GodexQuery.Miscellaneous ? "rintable" : "V";
                var at = text.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0 && text.IndexOf("\r\n", at, StringComparison.Ordinal) >= 0 &&
                    (query == GodexQuery.Miscellaneous || GodexSettingsProtocol.BuzzerState(text) is not null)) break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* Parse whatever was received. */ }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Соединение с принтером прервано при чтении.", ex);
        }
        return reply.ToString();
    }

    internal static async Task WriteAsync(PrinterConnection connection, string commands, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(Timeout);
        using var client = await ConnectAsync(connection, timer.Token, ct).ConfigureAwait(false);
        using var stream = client.GetStream();
        if (!await SendAsync(stream, commands, timer.Token, ct).ConfigureAwait(false))
            throw new TimeoutException("Принтер не принял команды за отведённое время.");
    }

    private static async Task<bool> SendAsync(NetworkStream stream, string commands, CancellationToken timeout, CancellationToken ct)
    {
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(commands + "\r\n"), timeout).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Соединение с принтером прервано при отправке.", ex);
        }
    }

    private static async Task<TcpClient> ConnectAsync(PrinterConnection connection, CancellationToken timeout, CancellationToken ct)
    {
        connection.Validate(requireAddress: true);
        var address = IPAddress.Parse(connection.IPv4);
        var octet = address.GetAddressBytes()[0];
        if (octet is 0 or >= 224) throw new ArgumentException("Укажите адрес конкретного принтера, а не широковещательный адрес.");
        var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(address, connection.Port, timeout).ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException("Принтер не ответил за отведённое время. Проверьте IPv4 и порт в профиле.");
        }
        catch (SocketException ex) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new IOException("Принтер недоступен. Проверьте IPv4 и порт в профиле.", ex);
        }
        catch { client.Dispose(); throw; }
    }
}
