using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Кладёт в кэш MySQL пароли всех логинов печати — при старте хаба и по событию запуска службы
/// MySQL (БАГ-1.2, MySqlStartWatcher в Blazor: событие 7036 журнала System, без опроса).
///
/// Без этого после перезапуска MySQL GoLabel II не входит в базу вообще: он подключается
/// с <c>SslMode=none</c>, а <c>caching_sha2_password</c> без TLS пускает только по кэшу
/// (см. <see cref="IPrintLoginService.WarmUpAsync"/>). Пароли у хаба есть, поэтому он сам
/// входит каждым логином по TLS. Отключённые учётки пропускаем — им печать закрыта.
/// </summary>
public class PrintLoginCacheWarmer : ITransientDependency
{
    private readonly ILabelClientStore _clients;
    private readonly IPrintLoginService _logins;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly ILogger<PrintLoginCacheWarmer> _log;

    public PrintLoginCacheWarmer(
        ILabelClientStore clients,
        IPrintLoginService logins,
        IUnitOfWorkManager uowManager,
        ILogger<PrintLoginCacheWarmer> log)
    {
        _clients = clients;
        _logins = logins;
        _uowManager = uowManager;
        _log = log;
    }

    /// <summary>Скольким логинам удалось войти и сколько их было.</summary>
    public async Task<PrintLoginWarmResult> WarmAllAsync()
    {
        var targets = new List<(string Login, string Password)>();

        // Вызывается при старте, вне запроса - единицу работы открываем сами,
        // иначе репозитории падают с ObjectDisposedException.
        using (var uow = _uowManager.Begin(requiresNew: true))
        {
            foreach (var client in await _clients.GetAllAsync())
            {
                if (!client.IsEnabled ||
                    string.IsNullOrEmpty(client.PrintLogin) ||
                    string.IsNullOrEmpty(client.PrintPasswordEncrypted))
                {
                    continue;
                }

                var password = await _clients.RevealPrintPasswordAsync(client.Id);
                if (!string.IsNullOrEmpty(password))
                {
                    targets.Add((client.PrintLogin, password));
                }
            }

            await uow.CompleteAsync();
        }

        var warmed = 0;
        foreach (var (login, password) in targets)
        {
            try
            {
                await _logins.WarmUpAsync(login, password);
                warmed++;
            }
            catch (Exception ex)
            {
                // Один неудачный логин (например, пароль сменили в MySQL руками)
                // не должен оставить без кэша остальных.
                _log.LogWarning(ex, "Не удалось положить в кэш пароль логина печати {Login}", login);
            }
        }

        _log.LogInformation("Пароли логинов печати в кэше MySQL: {Warmed} из {Total}",
            warmed, targets.Count);

        return new PrintLoginWarmResult(warmed, targets.Count);
    }
}

/// <summary>Итог прогрева: вошли <see cref="Warmed"/> логинов из <see cref="Total"/>.</summary>
public record PrintLoginWarmResult(int Warmed, int Total);
