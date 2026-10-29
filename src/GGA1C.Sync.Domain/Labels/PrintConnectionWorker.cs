using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Работник печати, раз в две минуты (первый тик — сразу при старте хаба):
///
/// 1. <b>Кэш паролей логинов печати (БАГ-1.1).</b> Читает <c>Uptime</c> MySQL; если прежнего нет
///    (первый тик после старта хаба) или сервер с прошлого тика перезапускался
///    (<see cref="MySqlRestartDetector"/>), заново кладёт пароли в кэш
///    (<see cref="PrintLoginCacheWarmer"/>) — иначе GoLabel II не войдёт.
///
/// 2. <b>Уборка подключений GoLabel II</b>, если их не закрыл сам сервер. Основную работу делает
///    MySQL: при входе человека вызывается процедура из <c>init_connect</c>, и прежние его сессии
///    закрываются сразу, по событию. Уборка — сеть безопасности на два случая: <c>init_connect</c>
///    не настроен (свежая установка) и сессии, открытые до того, как его включили. В обычной
///    работе она не находит ничего и молчит.
/// </summary>
public class PrintConnectionWorker : AsyncPeriodicBackgroundWorkerBase
{
    /// <summary>Uptime с прошлой удачной проверки; работник — одиночка, тики не пересекаются.</summary>
    private UptimeReading? _lastUptime;

    public PrintConnectionWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = 2 * 60 * 1000;
        // Старт хаба — частный случай перезапуска MySQL: прогрев на первом тике, без ожидания.
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var logger = workerContext.ServiceProvider
            .GetRequiredService<ILogger<PrintConnectionWorker>>();

        await WarmIfRestartedAsync(workerContext.ServiceProvider, logger);

        var reaper = workerContext.ServiceProvider.GetRequiredService<IPrintConnectionReaper>();

        try
        {
            var closed = await reaper.ReapAsync();

            // В журнал пишем, только когда что-то закрыли: иначе каждые две минуты
            // появлялась бы бессодержательная строка.
            if (closed > 0)
            {
                logger.LogInformation("Закрыто лишних подключений печати: {Count}", closed);
            }
        }
        catch (Exception ex)
        {
            // Падение одного тика не должно останавливать работника
            logger.LogWarning(ex, "Уборка подключений печати не удалась");
        }
    }

    private async Task WarmIfRestartedAsync(IServiceProvider services, ILogger logger)
    {
        try
        {
            var current = new UptimeReading(
                await services.GetRequiredService<IMySqlServerUptime>().GetUptimeSecondsAsync(),
                DateTimeOffset.UtcNow);

            if (!MySqlRestartDetector.ShouldWarm(_lastUptime, current))
            {
                _lastUptime = current;
                return;
            }

            if (_lastUptime is null)
            {
                logger.LogInformation(
                    "Первый тик работника печати: MySQL работает {Uptime} с — кладу пароли логинов печати в кэш",
                    current.UptimeSeconds);
            }
            else
            {
                logger.LogWarning(
                    "MySQL перезапущен (Uptime {Uptime} с, на прошлом тике {Previous} с) — снова кладу пароли логинов печати в кэш",
                    current.UptimeSeconds, _lastUptime.Value.UptimeSeconds);
            }

            await services.GetRequiredService<PrintLoginCacheWarmer>().WarmAllAsync();

            // Запоминаем только после прогрева: если он упал целиком (MySQL недоступен),
            // следующий тик попробует снова.
            _lastUptime = current;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Проверка перезапуска MySQL и прогрев кэша паролей печати не удались");
        }
    }
}
