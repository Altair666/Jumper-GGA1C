using System;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.Blazor.Printing;

/// <summary>
/// БАГ-1.2: кэш паролей логинов печати — по событию, без опроса MySQL. При старте хаба — один
/// прогрев; дальше хаб подписан на журнал System (<see cref="EventLogWatcher"/>, push) и на каждое
/// «служба MySQL перешла в состояние "работает"» (7036, <see cref="MySqlStartEvent"/>) снова кладёт
/// пароли в кэш: сначала ждёт, пока MySQL начнёт принимать подключения, и повторяет, если войти не
/// удалось.
///
/// Журнал System учётке хаба должен быть доступен на чтение (Administrator — да; служба под своей
/// учёткой — группа «Читатели журнала событий» / Event Log Readers). Нет доступа — предупреждение в
/// журнале хаба и только прогрев при старте; хаб не падает.
///
/// Настройки: <c>PrintLogins:CacheWarmup</c> (true) — выключить всё; <c>PrintLogins:MySqlServiceDisplayName</c>
/// ("MySQL") — отображаемое имя службы.
/// </summary>
public sealed class MySqlStartWatcher : ISingletonDependency, IDisposable
{
    /// <summary>Пауза после события: служба «работает» чуть раньше, чем MySQL слушает порт.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    public const int MaxAttempts = 6;

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MySqlStartWatcher> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EventLogWatcher? _watcher;

    public MySqlStartWatcher(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<MySqlStartWatcher> log)
    {
        _scopes = scopes;
        _configuration = configuration;
        _log = log;
    }

    public bool IsWatching => _watcher?.Enabled == true;

    /// <summary>Прогрев при старте и подписка. Не бросает: старт хаба не зависит от MySQL и журнала.</summary>
    public void Start()
    {
        if (!_configuration.GetValue("PrintLogins:CacheWarmup", true))
        {
            _log.LogWarning("Прогрев кэша паролей печати выключен (PrintLogins:CacheWarmup=false)");
            return;
        }

        _ = Task.Run(() => WarmWithRetriesAsync("старт хаба", TimeSpan.Zero));

        var displayName = _configuration["PrintLogins:MySqlServiceDisplayName"];
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = MySqlStartEvent.DefaultServiceDisplayName;
        }

        try
        {
            var query = new EventLogQuery(MySqlStartEvent.LogName, PathType.LogName, MySqlStartEvent.XPath(displayName));
            var watcher = new EventLogWatcher(query);
            watcher.EventRecordWritten += OnEvent;
            watcher.Enabled = true;
            _watcher = watcher;
            _log.LogInformation("Подписка на запуск службы «{Service}» (журнал System, событие 7036) включена", displayName);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Нет подписки на журнал System (событие 7036 службы «{Service}»): после перезапуска MySQL пароли печати " +
                "придётся класть в кэш перезапуском хаба. Учётке хаба нужно чтение журнала System (группа Event Log Readers).",
                displayName);
        }
    }

    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventException != null)
        {
            _log.LogWarning(e.EventException, "Ошибка подписки на журнал System");
            return;
        }

        using (e.EventRecord)
        {
            _log.LogWarning("Служба MySQL запущена ({Time:HH:mm:ss}) — снова кладу пароли логинов печати в кэш",
                e.EventRecord?.TimeCreated);
        }
        _ = Task.Run(() => WarmWithRetriesAsync("запуск службы MySQL", FirstDelay));
    }

    /// <summary>Прогрев; не удалось войти ни одним логином (MySQL ещё не готов) — повтор.</summary>
    private async Task WarmWithRetriesAsync(string reason, TimeSpan delay)
    {
        await _gate.WaitAsync();
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var result = await scope.ServiceProvider.GetRequiredService<PrintLoginCacheWarmer>().WarmAllAsync();
                    if (result.Total == 0 || result.Warmed > 0)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Прогрев кэша паролей печати ({Reason}), попытка {Attempt} из {Max}, не удался",
                        reason, attempt, MaxAttempts);
                }
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay);
                }
            }
            _log.LogWarning("Пароли логинов печати в кэш не легли ({Reason}) за {Max} попыток", reason, MaxAttempts);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.Enabled = false;
            _watcher.EventRecordWritten -= OnEvent;
            _watcher.Dispose();
            _watcher = null;
        }
        _gate.Dispose();
    }
}
