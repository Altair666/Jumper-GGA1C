using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GGA1C.Sync.FakePrinter;

/// <summary>
/// Фейковый сетевой принтер: принимает TCP-соединения и пишет всё пришедшее по каждому
/// соединению в отдельный файл, байт в байт. Ничего не отвечает, если не включён режим ответов.
/// </summary>
public sealed class FakePrinterServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _outDir;
    private readonly bool _reply;
    private int _jobCounter;

    public FakePrinterServer(IPAddress bind, int port, string outDir, bool reply = false)
    {
        _listener = new TcpListener(bind, port);
        _outDir = outDir;
        _reply = reply;
    }

    /// <summary>Порт, на котором реально слушаем (нужен, когда задан порт 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Вызывается после записи файла задания.</summary>
    public event Action<string, int>? JobSaved;

    public void Start()
    {
        Directory.CreateDirectory(_outDir);
        _listener.Start();
    }

    /// <summary>
    /// Принимает соединения по одному, пока не отменят или пока не наберётся <paramref name="maxJobs"/>
    /// заданий. <paramref name="idleExit"/> — выйти, если после первого задания нет нового столько времени.
    /// Возвращает пути записанных файлов по порядку.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        int? maxJobs = null, TimeSpan? idleExit = null, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        while (!cancellationToken.IsCancellationRequested && (maxJobs is null || files.Count < maxJobs))
        {
            using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (idleExit is not null && files.Count > 0)
            {
                acceptCts.CancelAfter(idleExit.Value);
            }

            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(acceptCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using (client)
            {
                files.Add(await ReceiveJobAsync(client, cancellationToken));
            }
        }

        return files;
    }

    private async Task<string> ReceiveJobAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var number = Interlocked.Increment(ref _jobCounter);
        var path = Path.Combine(_outDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{number}.bin");
        var stream = client.GetStream();
        var buffer = new byte[64 * 1024];

        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (_reply)
                    {
                        await ReplyAsync(stream, buffer.AsSpan(0, read).ToArray(), cancellationToken);
                    }
                }
            }
            catch (IOException)
            {
                // GoLabel рвёт соединение, когда не дождался ответа на опрос принтера
                // (^XGET, ~S,OFFSETX и т.п.). Принятое сохраняем, слушать продолжаем —
                // иначе один обрыв останавливал весь фейк-принтер.
            }
        }

        JobSaved?.Invoke(path, (int)new FileInfo(path).Length);
        return path;
    }

    /// <summary>
    /// Минимальные ответы на запросы состояния. GoLabel при обычной печати по сети их не шлёт
    /// (см. docs/printing/p2-fake-printer.md, раздел 5) — режим на будущее, по умолчанию выключен.
    /// </summary>
    private static async Task ReplyAsync(NetworkStream stream, byte[] chunk, CancellationToken cancellationToken)
    {
        var text = Encoding.ASCII.GetString(chunk);
        if (text.Contains("~S,CHECK", StringComparison.Ordinal))
        {
            await stream.WriteAsync("00\r\n"u8.ToArray(), cancellationToken);
        }
    }

    public void Dispose() => _listener.Stop();
}
