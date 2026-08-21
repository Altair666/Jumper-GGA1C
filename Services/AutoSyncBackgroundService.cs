using AltiumSync.Data;

namespace AltiumSync.Services;

public class AutoSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<AutoSyncBackgroundService> _log;

    public AutoSyncBackgroundService(IServiceProvider provider,
        ILogger<AutoSyncBackgroundService> log)
    {
        _provider = provider;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope   = _provider.CreateScope();
            var db            = scope.ServiceProvider.GetRequiredService<Database>();
            var syncService   = scope.ServiceProvider.GetRequiredService<SyncService>();
            var settings      = db.GetSettings();

            if (settings.AutoSyncEnabled &&
                !string.IsNullOrEmpty(settings.AltiumServerUrl) &&
                !string.IsNullOrEmpty(settings.OnecServerUrl))
            {
                syncService.ApplySettings(settings);
                _log.LogInformation("Автосинхронизация запущена");
                try
                {
                    await syncService.SyncFoldersAsync();
                    await syncService.SyncComponentsAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Ошибка автосинхронизации");
                }
            }

            var interval = TimeSpan.FromMinutes(
                settings.SyncIntervalMin > 0 ? settings.SyncIntervalMin : 60);
            await Task.Delay(interval, ct);
        }
    }
}
