using System.Net;
using System.Net.Sockets;
using GGA1C.Sync.FakePrinter;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Printing;

public sealed class FakePrinterServerTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "gga1c-fakeprinter-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Writes_received_bytes_one_to_one()
    {
        // Все 256 значений байта, CR/LF и нули — ничего не должно перекодироваться или потеряться.
        var payload = new byte[300_000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 7 % 256);
        }

        using var server = new FakePrinterServer(IPAddress.Loopback, 0, _outDir);
        server.Start();
        var run = server.RunAsync(maxJobs: 1);

        await SendAsync(server.Port, payload);
        var files = await run.WaitAsync(TimeSpan.FromSeconds(10));

        files.Count.ShouldBe(1);
        (await File.ReadAllBytesAsync(files[0])).ShouldBe(payload);
    }

    [Fact]
    public async Task Each_connection_goes_to_its_own_file_in_order()
    {
        var first = "^Q100,3\r\n^W102\r\nE\r\n"u8.ToArray();
        var second = "~S,CHECK\r\n"u8.ToArray();

        using var server = new FakePrinterServer(IPAddress.Loopback, 0, _outDir);
        server.Start();
        var run = server.RunAsync(maxJobs: 2);

        await SendAsync(server.Port, first);
        await SendAsync(server.Port, second);
        var files = await run.WaitAsync(TimeSpan.FromSeconds(10));

        files.Count.ShouldBe(2);
        (await File.ReadAllBytesAsync(files[0])).ShouldBe(first);
        (await File.ReadAllBytesAsync(files[1])).ShouldBe(second);
    }

    [Fact]
    public async Task Idle_exit_stops_after_last_job()
    {
        using var server = new FakePrinterServer(IPAddress.Loopback, 0, _outDir);
        server.Start();
        var run = server.RunAsync(idleExit: TimeSpan.FromMilliseconds(300));

        await SendAsync(server.Port, [1, 2, 3]);
        var files = await run.WaitAsync(TimeSpan.FromSeconds(10));

        files.Count.ShouldBe(1);
    }

    private static async Task SendAsync(int port, byte[] payload)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await client.GetStream().WriteAsync(payload);
        client.Client.Shutdown(SocketShutdown.Send);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outDir))
        {
            Directory.Delete(_outDir, recursive: true);
        }
    }
}
