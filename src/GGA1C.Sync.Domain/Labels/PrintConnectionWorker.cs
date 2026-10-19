using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Раз в две минуты прибирает подключения GoLabel II: у каждого печатающего остаётся одно,
/// самое свежее.
///
/// Две минуты — компромисс: достаточно часто, чтобы за смену не набралось сотни сессий, и
/// достаточно редко, чтобы не дёргать базу попусту. Сама уборка трогает только спящие
/// сессии логинов печати, так что лишнего не закроет.
/// </summary>
public class PrintConnectionWorker : AsyncPeriodicBackgroundWorkerBase
{
    public PrintConnectionWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = 2 * 60 * 1000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var logger = workerContext.ServiceProvider
            .GetRequiredService<ILogger<PrintConnectionWorker>>();

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
}
