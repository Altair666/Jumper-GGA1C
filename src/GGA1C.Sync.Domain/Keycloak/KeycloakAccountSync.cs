using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Сверяет учётные записи виджета со списком пользователей Keycloak: кого нет — заводит
/// и сразу выписывает ключ, у кого сняли доступ — отключает, кому вернули — включает.
///
/// Связь держится по <c>sub</c>, а не по email или логину: их человек меняет, sub — нет.
/// Один раз учётка, заведённая руками, привязывается по email — чтобы не получить вторую
/// на того же человека.
///
/// Ключевая тонкость — различать, кто отключил учётку. Если этого не делать, следующая же
/// сверка включит обратно того, кого администратор выключил руками, и отзыв доступа молча
/// отменится. Поэтому включаем обратно только тех, у кого стоит <c>SyncDisabledAt</c>,
/// то есть кого отключила сама сверка.
/// </summary>
public class KeycloakAccountSync : DomainService
{
    private readonly KeycloakClient _keycloak;
    private readonly ILabelClientStore _clients;
    private readonly IPrintLoginService _printLogins;
    private readonly HubUserAccess _hubUsers;
    private readonly ILogger<KeycloakAccountSync> _log;

    public KeycloakAccountSync(
        KeycloakClient keycloak,
        ILabelClientStore clients,
        IPrintLoginService printLogins,
        HubUserAccess hubUsers,
        ILogger<KeycloakAccountSync> log)
    {
        _keycloak = keycloak;
        _clients = clients;
        _printLogins = printLogins;
        _hubUsers = hubUsers;
        _log = log;
    }

    /// <summary>
    /// Сверка одного человека — при его входе в хаб. Человек уже есть в Keycloak (тот только
    /// что выдал ему токен), а сверка могла до него ещё не дойти: она идёт раз в час. Без
    /// этого новый сотрудник не мог бы войти до следующей сверки.
    ///
    /// Данные — из утверждений токена, а не из Admin API: лишний запрос служебной учёткой
    /// ничего не добавит, а <c>Enabled</c> заведомо истинно — отключённому Keycloak токен не
    /// выдаёт. Возвращает учётку или null с причиной, если её не из чего собрать.
    /// </summary>
    public async Task<(LabelClient? Client, string? Problem)> EnsureForLoginAsync(KeycloakUser user)
    {
        var report = new KeycloakSyncReport();
        var client = await SyncOneAsync(user, report, DateTime.UtcNow);

        foreach (var line in report.Details)
        {
            _log.LogInformation("Вход через Keycloak: {Detail}", line);
        }

        if (client == null)
        {
            return (null, report.Details.LastOrDefault() ?? "Учётку не удалось завести");
        }

        return (client, null);
    }

    /// <summary>
    /// Готовит человеку доступ к базе печати: логин — его почта, пароль по умолчанию
    /// (1234), права — только на представление своих строк. Делается при появлении учётки,
    /// чтобы администратору не приходилось нажимать ещё одну кнопку; сменить пароль можно
    /// в карточке.
    ///
    /// Недоступная база не должна ронять сверку: человек в хабе уже есть, доступ к печати
    /// доберём при следующей сверке или вручную в карточке.
    /// </summary>
    private async Task EnsurePrintLoginAsync(LabelClient client, KeycloakSyncReport report)
    {
        if (!client.IsEnabled || string.IsNullOrWhiteSpace(client.Email) ||
            !string.IsNullOrWhiteSpace(client.PrintLogin))
        {
            return;
        }

        try
        {
            var login = await _printLogins.EnsureAsync(
                client.Slug, client.Email, PrintLoginDefaults.Password);

            await _clients.SetPrintCredentialsAsync(
                client.Id, login, PrintLoginDefaults.Password);

            report.Details.Add($"{client.Name}: логин печати {login}, пароль по умолчанию");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Логин печати для {Name} не создан", client.Name);
            report.Details.Add($"{client.Name}: логин печати не создан ({ex.Message})");
        }
    }

    public async Task<KeycloakSyncReport> SyncAsync()
    {
        var report = new KeycloakSyncReport();

        if (!await _keycloak.IsConfiguredAsync())
        {
            report.Ok = false;
            report.Message = "Keycloak не настроен: укажите адрес, реалм и клиент";
            return report;
        }

        List<KeycloakUser> users;

        try
        {
            var token = await _keycloak.GetTokenAsync();
            users = await _keycloak.GetUsersAsync(token);
        }
        catch (Exception ex)
        {
            report.Ok = false;
            report.Message = ex.Message;
            return report;
        }

        var now = DateTime.UtcNow;
        var seenSubs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var user in users)
        {
            report.Checked++;
            seenSubs.Add(user.Id);

            await SyncOneAsync(user, report, now);
        }

        await DisableMissingAsync(seenSubs, report, now);

        _log.LogInformation(
            "Сверка с Keycloak: проверено {Checked}, создано {Created}, обновлено {Updated}, " +
            "отключено {Disabled}, включено {Restored}",
            report.Checked, report.Created, report.Updated, report.Disabled, report.Restored);

        return report;
    }

    private async Task<LabelClient?> SyncOneAsync(KeycloakUser user, KeycloakSyncReport report, DateTime now)
    {
        var client = await _clients.FindBySubAsync(user.Id);

        // Учётка могла быть заведена руками до подключения Keycloak - привязываем
        // её по email, чтобы не плодить вторую на того же человека.
        if (client == null && !string.IsNullOrWhiteSpace(user.Email))
        {
            client = await _clients.FindByEmailAsync(user.Email);

            if (client != null)
            {
                client.KeycloakSub = user.Id;
                report.Linked++;
                report.Details.Add($"Привязана существующая учётка: {client.Name}");
            }
        }

        if (client == null)
        {
            return await CreateFromUserAsync(user, report, now);
        }

        await UpdateFromUserAsync(client, user, report, now);
        return client;
    }

    private async Task<LabelClient?> CreateFromUserAsync(KeycloakUser user, KeycloakSyncReport report, DateTime now)
    {
        var baseSlug = SlugGenerator.FromName(user.DisplayName);

        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = SlugGenerator.FromName(user.Username);
        }

        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            // Фамилия не транслитерируется и логин тоже - папку не из чего собрать.
            // Пропускаем именно этого человека, а не валим всю сверку.
            report.Details.Add($"Пропущен {user.Username}: не удалось составить имя папки");
            return null;
        }

        var slug = await _clients.MakeUniqueSlugAsync(baseSlug);

        var (client, _) = await _clients.CreateAsync(user.DisplayName, slug);

        client.KeycloakSub = user.Id;
        client.KeycloakUsername = user.Username;
        client.Email = user.Email;
        client.SyncedAt = now;

        // Доступ снят ещё в Keycloak - учётка создаётся сразу отключённой
        if (!user.Enabled)
        {
            client.IsEnabled = false;
            client.SyncDisabledAt = now;
        }

        await _clients.UpdateAsync(client);

        await EnsurePrintLoginAsync(client, report);

        report.Created++;

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            // Без email Grist не сможет отдать виджету строку с ключом по правилу
            // user.Email == rec.email, и ключ придётся вписывать руками.
            report.SkippedWithoutEmail++;
            report.Details.Add(
                $"Создана учётка {client.Name} (папка {client.Slug}), но без email — " +
                "ключ придётся вписать в виджет вручную");
        }
        else
        {
            report.Details.Add($"Создана учётка {client.Name} (папка {client.Slug})");
        }

        return client;
    }

    private async Task UpdateFromUserAsync(
        LabelClient client, KeycloakUser user, KeycloakSyncReport report, DateTime now)
    {
        var changed = false;

        if (client.Name != user.DisplayName && !string.IsNullOrWhiteSpace(user.DisplayName))
        {
            client.Name = user.DisplayName;
            changed = true;
        }

        if (client.Email != user.Email)
        {
            client.Email = user.Email;
            changed = true;
        }

        if (client.KeycloakUsername != user.Username)
        {
            client.KeycloakUsername = user.Username;
            changed = true;
        }

        // Папку буфера НЕ переименовываем даже при смене фамилии: путь уже прописан
        // в источнике ODBC на машине с GoLabel2, и переименование тихо сломало бы печать.

        if (!user.Enabled && client.IsEnabled)
        {
            client.IsEnabled = false;
            client.SyncDisabledAt = now;
            report.Disabled++;
            report.Details.Add($"Отключена учётка {client.Name}: доступ снят в Keycloak");
            await _hubUsers.SetActiveAsync(client.KeycloakSub, false);
        }
        else if (user.Enabled && !client.IsEnabled && client.SyncDisabledAt != null)
        {
            client.IsEnabled = true;
            client.SyncDisabledAt = null;
            report.Restored++;
            report.Details.Add($"Включена учётка {client.Name}: доступ возвращён в Keycloak");
            await _hubUsers.SetActiveAsync(client.KeycloakSub, true);
        }

        client.SyncedAt = now;
        await _clients.UpdateAsync(client);

        // Учётка могла появиться до того, как завели базу печати - доберём здесь.
        await EnsurePrintLoginAsync(client, report);

        if (changed)
        {
            report.Updated++;
        }
    }

    /// <summary>
    /// Пользователь исчез из Keycloak — отключаем его учётку, но не удаляем: в буфере
    /// может лежать неотпечатанная партия, а удаление необратимо.
    /// </summary>
    private async Task DisableMissingAsync(
        HashSet<string> seenSubs, KeycloakSyncReport report, DateTime now)
    {
        var all = await _clients.GetAllAsync();

        var missing = all.Where(c =>
            !string.IsNullOrWhiteSpace(c.KeycloakSub) &&
            !seenSubs.Contains(c.KeycloakSub!) &&
            c.IsEnabled);

        foreach (var client in missing)
        {
            client.IsEnabled = false;
            client.SyncDisabledAt = now;
            client.SyncedAt = now;

            await _clients.UpdateAsync(client);
            await _hubUsers.SetActiveAsync(client.KeycloakSub, false);

            report.Disabled++;
            report.Details.Add($"Отключена учётка {client.Name}: пользователя нет в Keycloak");
        }
    }
}
