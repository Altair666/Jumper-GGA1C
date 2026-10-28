using System.Net;
using GGA1C.Sync.FakePrinter;

// Фейк-принтер для эталонов GoLabel (П2). Слушает TCP и пишет каждое соединение в файл.
//   GGA1C.Sync.FakePrinter [--port 9100] [--bind 127.0.0.1] [--out .\jobs]
//                          [--once] [--jobs N] [--idle-exit сек] [--reply]

var port = 9100;
var bind = IPAddress.Loopback;
var outDir = Path.Combine(Environment.CurrentDirectory, "jobs");
int? maxJobs = null;
TimeSpan? idleExit = null;
var reply = false;

for (var i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Нет значения для {args[i]}");

    switch (args[i].ToLowerInvariant())
    {
        case "--port": port = int.Parse(Next()); break;
        case "--bind": bind = IPAddress.Parse(Next()); break;
        case "--out": outDir = Path.GetFullPath(Next()); break;
        case "--once": maxJobs = 1; break;
        case "--jobs": maxJobs = int.Parse(Next()); break;
        case "--idle-exit": idleExit = TimeSpan.FromSeconds(double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture)); break;
        case "--reply": reply = true; break;
        case "-h":
        case "--help":
            Console.WriteLine("GGA1C.Sync.FakePrinter [--port 9100] [--bind 127.0.0.1] [--out .\\jobs] [--once] [--jobs N] [--idle-exit сек] [--reply]");
            return 0;
        default:
            Console.Error.WriteLine($"Неизвестный ключ: {args[i]}");
            return 2;
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var server = new FakePrinterServer(bind, port, outDir, reply);
server.JobSaved += (path, length) => Console.WriteLine($"{length} байт -> {path}");
server.Start();
Console.WriteLine($"Слушаю {bind}:{server.Port}, пишу в {outDir}");

var files = await server.RunAsync(maxJobs, idleExit, cts.Token);
Console.WriteLine($"Заданий: {files.Count}");
return 0;
