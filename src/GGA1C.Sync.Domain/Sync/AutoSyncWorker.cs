using System;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Settings;
using Volo.Abp.Threading;

namespace GGA1C.Sync.Sync;

/// <summary>
/// Автоматическая синхронизация Altium → 1С по расписанию.
///
/// Перенесено с BackgroundService на штатный механизм ABP: тот сам заводит область
/// внедрения зависимостей и единицу работы на каждый тик, тогда как прежняя версия
/// создавала scope руками. Интервал перечитывается из настроек на каждом тике — поэтому
/// смена расписания в интерфейсе подхватывается без перезапуска сервиса.
///
/// Когда подключим Elsa, этот работник станет триггером процесса, а сами шаги переедут
/// в активности — чтобы повторы и состояние держал движок.
/// </summary>
public class AutoSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int DefaultIntervalMinutes = 60;

    public AutoSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        // Первый тик - через минуту после старта, дальше период берётся из настроек.
        Timer.Period = 60 * 1000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var settings = workerContext.ServiceProvider.GetRequiredService<ISettingProvider>();
        var logger = workerContext.ServiceProvider
            .GetRequiredService<ILogger<AutoSyncWorker>>();

        var intervalMinutes = await settings.GetAsync(SyncSettings.IntervalMinutes, DefaultIntervalMinutes);
        Timer.Period = Math.Max(1, intervalMinutes) * 60 * 1000;

        if (!await settings.GetAsync<bool>(SyncSettings.AutoSyncEnabled))
        {
            return;
        }

        var altiumUrl = await settings.GetOrNullAsync(SyncSettings.Altium.ServerUrl);
        var onecUrl = await settings.GetOrNullAsync(SyncSettings.OneC.ServerUrl);

        if (string.IsNullOrWhiteSpace(altiumUrl) || string.IsNullOrWhiteSpace(onecUrl))
        {
            logger.LogWarning(
                "Автосинхронизация включена, но адреса не заданы — пропускаю тик");
            return;
        }

        var configurator = workerContext.ServiceProvider.GetRequiredService<ConnectionConfigurator>();
        await configurator.ApplyAllAsync();

        var sync = workerContext.ServiceProvider.GetRequiredService<SyncService>();

        logger.LogInformation("Автосинхронизация запущена");

        try
        {
            await sync.SyncFoldersAsync();
            await sync.SyncComponentsAsync();
            logger.LogInformation("Автосинхронизация завершена");
        }
        catch (Exception ex)
        {
            // Падение одного тика не должно останавливать работника: следующий тик
            // попробует снова, а причина останется в журнале.
            logger.LogError(ex, "Ошибка автосинхронизации");
        }
    }
}
