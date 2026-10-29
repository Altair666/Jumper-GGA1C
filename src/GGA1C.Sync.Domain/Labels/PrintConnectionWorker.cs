using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Подчищает подключения GoLabel II, если их не закрыл сам сервер.
///
/// Основную работу делает не этот работник, а MySQL: при входе человека вызывается
/// процедура из <c>init_connect</c>, и прежние его сессии закрываются сразу, по событию.
/// Работник остаётся сетью безопасности на два случая: <c>init_connect</c> не настроен
/// (свежая установка, про настройку забыли) и сессии, открытые до того, как его включили.
/// В обычной работе он не находит ничего и молчит.
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
