using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Периодически сверяет учётные записи виджета с Keycloak.
///
/// Это не удобство, а требование: отключение человека в Keycloak должно закрывать ему
/// доступ и к печати этикеток. Без периодической сверки уволенный сотрудник продолжал бы
/// пользоваться своим ключом сколько угодно — ключ-то у него на руках, и сам он не
/// истекает.
///
/// Первый тик — через минуту после старта, дальше раз в час. Если Keycloak не настроен,
/// сверка просто ничего не делает и не шумит в журнале.
/// </summary>
public class KeycloakSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public KeycloakSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = 60 * 1000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var logger = workerContext.ServiceProvider
            .GetRequiredService<ILogger<KeycloakSyncWorker>>();

        // После первого тика переходим на часовой интервал
        Timer.Period = 60 * 60 * 1000;

        var keycloak = workerContext.ServiceProvider.GetRequiredService<KeycloakClient>();

        if (!await keycloak.IsConfiguredAsync())
        {
            return;
        }

        var sync = workerContext.ServiceProvider.GetRequiredService<KeycloakAccountSync>();
        var uowManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        try
        {
            // Единицу работы открываем сами: фоновый работник ABP её не создаёт, а
            // репозитории без неё обращаются к уже закрытому контексту и падают с
            // ObjectDisposedException. В запросах из интерфейса её открывает сам слой
            // приложения, поэтому там проблема не видна - и это сбивает с толку.
            using var uow = uowManager.Begin(requiresNew: true);

            var report = await sync.SyncAsync();

            if (!report.Ok)
            {
                logger.LogWarning("Сверка с Keycloak не выполнена: {Message}", report.Message);
                await uow.CompleteAsync();
                return;
            }

            // Пишем в журнал только когда что-то изменилось: иначе каждый час в логе
            // появлялась бы бессодержательная строка.
            if (report.Created + report.Updated + report.Disabled + report.Restored + report.Linked > 0)
            {
                logger.LogInformation(
                    "Сверка с Keycloak: проверено {Checked}, создано {Created}, " +
                    "обновлено {Updated}, привязано {Linked}, отключено {Disabled}, " +
                    "включено {Restored}",
                    report.Checked, report.Created, report.Updated,
                    report.Linked, report.Disabled, report.Restored);
            }

            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            // Падение одного тика не должно останавливать работника
            logger.LogError(ex, "Ошибка сверки с Keycloak");
        }
    }
}
