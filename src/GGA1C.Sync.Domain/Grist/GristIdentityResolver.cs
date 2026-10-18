using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace GGA1C.Sync.Grist;

/// <summary>
/// Узнаёт у самого Grist, кто прислал запрос из виджета.
///
/// Виджету личность не выдаётся (в plugin API нет getUser, кука Grist помечена HttpOnly),
/// поэтому раньше он предъявлял хабу ключ. Ключи при этом приходилось держать в таблице
/// документа открытым текстом — их видел всякий, у кого полный доступ к документу, и они
/// попадали в выгрузки и снимки. Теперь вместо ключа виджет шлёт короткоживущий токен,
/// который ему выдаёт сам Grist (<c>grist.docApi.getAccessToken({readOnly: true})</c>),
/// а хаб этим токеном читает в документе таблицу из одной колонки с адресами почты.
/// Правило доступа <c>user.Email == rec.email</c> оставляет в ответе ровно одну строку —
/// того, кому Grist выдал токен. Секретов в документе при этом нет вовсе, а токен живёт
/// минуты и подделать его нельзя: проверяет его Grist, а не мы.
/// </summary>
public class GristIdentityResolver : DomainService
{
    private const string DefaultTable = "LabelIdentity";

    private readonly IHttpClientFactory _factory;
    private readonly ISettingProvider _settings;
    private readonly ILogger<GristIdentityResolver> _log;

    public GristIdentityResolver(
        IHttpClientFactory factory,
        ISettingProvider settings,
        ILogger<GristIdentityResolver> log)
    {
        _factory = factory;
        _settings = settings;
        _log = log;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        return !string.IsNullOrWhiteSpace(await Get(SyncSettings.Grist.ServerUrl))
            && !string.IsNullOrWhiteSpace(await Get(SyncSettings.Grist.DocId));
    }

    /// <summary>
    /// Кто предъявил токен. Возвращает адрес почты либо причину отказа — её видит оператор
    /// в виджете, поэтому она должна говорить, что делать.
    /// </summary>
    public async Task<(string? Email, string? Error)> ResolveEmailAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, "Токен Grist не передан");
        }

        if (!await IsConfiguredAsync())
        {
            return (null, "В хабе не заданы адрес Grist и документ");
        }

        List<string> emails;

        try
        {
            emails = await ReadEmailsAsync(token);
        }
        catch (Exception ex)
        {
            // Отказ Grist - это не поломка хаба, а ответ оператору: токен истёк, таблицы
            // нет, правило не пускает. Исключение наружу означало бы 500 вместо подсказки.
            return (null, ex.Message);
        }

        return emails.Count switch
        {
            1 => (emails[0], null),

            0 => (null, "Grist не нашёл вашу строку в таблице пользователей — " +
                        "попросите администратора добавить вашу почту"),

            // Несколько строк означает, что правило доступа не ограничивает выборку
            // одним человеком. Записывать в чей-то буфер наугад нельзя.
            _ => (null, "Правило доступа к таблице пользователей отдаёт больше одной строки — " +
                        "проверьте условие user.Email == rec.email")
        };
    }

    /// <summary>
    /// Проверка настройки: читаем таблицу ключом API администратора (он нужен только для
    /// этой проверки). Видно, существует ли таблица и сколько в ней людей.
    /// </summary>
    public async Task<(bool Ok, string Message)> TestAsync()
    {
        if (!await IsConfiguredAsync())
        {
            return (false, "Не заданы адрес Grist или документ");
        }

        var apiKey = await Get(SyncSettings.Grist.ApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "Для проверки нужен ключ API Grist — он используется только здесь");
        }

        try
        {
            var table = await GetTableAsync();
            var emails = await ReadEmailsAsync(apiKey: apiKey);

            // Строк видно столько, сколько отдаёт правило доступа - обычно одну, свою,
            // даже владельцу документа. Это признак того, что правило работает, поэтому
            // так и пишем: иначе «людей в таблице: 1» выглядит как потеря данных.
            return (true,
                $"Документ доступен, таблица «{table}» читается. Правило доступа отдало " +
                $"строк: {emails.Count} — каждый видит только свою, это и нужно");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<List<string>> ReadEmailsAsync(string? token = null, string? apiKey = null)
    {
        using var http = _factory.CreateClient("grist");
        http.Timeout = TimeSpan.FromSeconds(20);

        var url = await UrlAsync();

        // Токен Grist передаётся параметром auth - так его принимает сам Grist; ключ API
        // администратора ходит заголовком, как обычно.
        if (!string.IsNullOrWhiteSpace(token))
        {
            url += "?auth=" + Uri.EscapeDataString(token);
        }
        else
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        var response = await http.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _log.LogWarning("Grist ответил {Status}: {Body}", (int)response.StatusCode, body);

            throw new InvalidOperationException(Hint(response.StatusCode));
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var emails = new List<string>();

        if (!doc.RootElement.TryGetProperty("records", out var records))
        {
            return emails;
        }

        foreach (var record in records.EnumerateArray())
        {
            if (record.TryGetProperty("fields", out var fields) &&
                fields.TryGetProperty("email", out var email))
            {
                var value = email.GetString();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    emails.Add(value.Trim());
                }
            }
        }

        return emails;
    }

    /// <summary>
    /// Адрес собираем сами из настроек, а не берём из запроса: иначе виджет (или кто угодно,
    /// кто до нас достучится) мог бы указать свой сервер, который на любой токен ответит
    /// нужной ему почтой — и хаб писал бы в чужой буфер.
    /// </summary>
    private async Task<string> UrlAsync()
    {
        var baseUrl = (await Get(SyncSettings.Grist.ServerUrl)).TrimEnd('/');
        var docId = await Get(SyncSettings.Grist.DocId);
        var table = await GetTableAsync();

        return $"{baseUrl}/api/docs/{docId}/tables/{table}/records";
    }

    private async Task<string> GetTableAsync()
    {
        var table = await Get(SyncSettings.Grist.IdentityTable);
        return string.IsNullOrWhiteSpace(table) ? DefaultTable : table.Trim();
    }

    private static string Hint(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.Unauthorized => "Grist не принял токен — возможно, он уже истёк",
            HttpStatusCode.Forbidden =>
                "Grist отказал в чтении таблицы пользователей: правило доступа должно " +
                "разрешать читать свою строку (user.Email == rec.email)",
            HttpStatusCode.NotFound =>
                "Grist не нашёл документ или таблицу пользователей — проверьте " +
                "идентификатор документа и имя таблицы с колонкой email",
            _ => $"Grist ответил {(int)status}"
        };
    }

    private async Task<string> Get(string name)
    {
        return await _settings.GetOrNullAsync(name) ?? "";
    }
}
