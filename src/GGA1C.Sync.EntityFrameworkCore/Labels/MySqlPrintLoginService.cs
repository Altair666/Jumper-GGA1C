using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.EntityFrameworkCore.Labels;

/// <summary>
/// Логины печати в MySQL. Живёт в слое данных, потому что работает прямым SQL: создание
/// пользователей и выдача прав через EF невыразимы.
///
/// Логин — это почта человека, та же, под которой он входит в Keycloak и Grist: заводить
/// ему второе имя незачем. Пароль при этом свой: MySQL Community умеет проверять только
/// свои пароли, внешней аутентификации (LDAP, Kerberos) в нём нет — серверные плагины для
/// этого есть лишь в Enterprise, в нашей сборке только клиентские части.
///
/// Если почты нет или она не влезает в 32 знака (предел MySQL), логин собирается из имени
/// папки с приставкой <c>lbl_</c>.
/// </summary>
public class MySqlPrintLoginService : IPrintLoginService, ITransientDependency
{
    private const string Prefix = "lbl_";

    /// <summary>Предел MySQL на имя пользователя.</summary>
    private const int MaxLoginLength = 32;
    private const string ViewName = "my_labels";

    /// <summary>
    /// Пустая заглушка под запрос, который GoLabel II шлёт при обновлении данных.
    /// Имя не наше: так называется первое представление information_schema.
    /// </summary>
    private const string ProbeViewName = "administrable_role_authorizations";

    private readonly IConfiguration _configuration;
    private readonly ILogger<MySqlPrintLoginService> _log;

    public MySqlPrintLoginService(
        IConfiguration configuration,
        ILogger<MySqlPrintLoginService> log)
    {
        _configuration = configuration;
        _log = log;
    }

    public string LoginFor(string slug, string? email)
    {
        var mail = (email ?? "").Trim().ToLowerInvariant();

        return mail.Length is > 0 and <= MaxLoginLength && IsValidLogin(mail)
            ? mail
            : Normalize(Prefix + slug);
    }

    public async Task<string> EnsureAsync(string slug, string? email, string password)
    {
        var login = LoginFor(slug, email);

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new BusinessException("GGA1C.Sync:EmptyPrintPassword")
                .WithData("message", "Пароль для доступа к базе печати не может быть пустым");
        }

        await EnsureViewAsync();

        await using var db = await OpenAsync();

        // Логин может уже существовать - тогда просто меняем пароль. Отдельной ветки
        // "создать или сменить" в MySQL нет, поэтому две команды подряд.
        await ExecuteAsync(db, $"CREATE USER IF NOT EXISTS '{login}'@'%' IDENTIFIED BY @pwd",
            ("@pwd", password));
        await ExecuteAsync(db, $"ALTER USER '{login}'@'%' IDENTIFIED BY @pwd",
            ("@pwd", password));

        // Права только на представление: таблица с чужими строками человеку недоступна.
        await ExecuteAsync(db, $"GRANT SELECT ON {Database()}.{ViewName} TO '{login}'@'%'");

        // И на заглушку - см. пояснение к EnsureViewAsync.
        await ExecuteAsync(db, $"GRANT SELECT ON {Database()}.{ProbeViewName} TO '{login}'@'%'");

        _log.LogInformation("Логин печати {Login} готов", login);

        return login;
    }

    public async Task DropAsync(string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return;
        }

        var safe = Normalize(login);

        await using var db = await OpenAsync();
        await ExecuteAsync(db, $"DROP USER IF EXISTS '{safe}'@'%'");

        _log.LogInformation("Логин печати {Login} удалён", safe);
    }

    public async Task EnsureViewAsync()
    {
        await using var db = await OpenAsync();

        // SESSION_USER(), а не CURRENT_USER(): внутри представления с правами создателя
        // CURRENT_USER() возвращает создателя, то есть фильтр отобрал бы строки не того
        // человека. SESSION_USER() - это тот, кто подключился.
        //
        // Хост отрезаем по ПОСЛЕДНЕЙ собачке: логин у нас - почта, и в нём своя собачка
        // уже есть. SUBSTRING_INDEX(..., '@', 1) вернул бы "a.morozov" вместо
        // "a.morozov@test.local" и никого бы не нашёл.
        var sql = $@"
CREATE OR REPLACE SQL SECURITY DEFINER VIEW {ViewName} AS
SELECT
    l.Date         AS date,
    l.ProductCode  AS product_code,
    l.ModeCode     AS mode_code,
    l.Article      AS article,
    l.Name         AS name,
    l.Manufacturer AS manufacturer,
    l.Barcode      AS barcode,
    l.Qty          AS qty,
    l.Extra1       AS extra1,
    l.Extra2       AS extra2,
    l.Extra3       AS extra3,
    l.ExternalId   AS external_id,
    l.UpdatedAt    AS updated_at
FROM AppLabels l
JOIN AppLabelClients c ON c.Slug = l.Slug
WHERE c.PrintLogin = LEFT(
        SESSION_USER(),
        LENGTH(SESSION_USER()) - LENGTH(SUBSTRING_INDEX(SESSION_USER(), '@', -1)) - 1)
  AND c.IsEnabled = 1";

        await ExecuteAsync(db, sql);

        // GoLabel II при обновлении читает список таблиц и пробует прочитать одно из
        // системных имён БЕЗ указания схемы - проверено по журналу запросов MySQL:
        //
        //   SELECT ... FROM INFORMATION_SCHEMA.TABLES
        //   SELECT * from administrable_role_authorizations      <- без имени базы
        //
        // MySQL ищет это имя в нашей базе, не находит и отвечает "SELECT command denied"
        // (так он отвечает и когда объекта нет - чтобы не раскрывать содержимое базы).
        // Оператор видит окно с ошибкой на каждое обновление. Спрятать information_schema
        // нельзя - она видна всем по устройству СУБД, поэтому подставляем пустышку с тем
        // же именем: запрос проходит молча и возвращает ноль строк.
        await ExecuteAsync(db, $@"
CREATE OR REPLACE SQL SECURITY DEFINER VIEW {ProbeViewName} AS
SELECT NULL AS GRANTEE, NULL AS GRANTEE_HOST, NULL AS ROLE_NAME,
       NULL AS ROLE_HOST, NULL AS IS_GRANTABLE, NULL AS IS_DEFAULT, NULL AS IS_MANDATORY
FROM DUAL WHERE FALSE");
    }

    public async Task<bool> ExistsAsync(string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return false;
        }

        await using var db = await OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM mysql.user WHERE user = @login", db);
        command.Parameters.AddWithValue("@login", Normalize(login));

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(ConnectionString());
        await connection.OpenAsync();

        return connection;
    }

    private static async Task ExecuteAsync(
        MySqlConnection db, string sql, params (string Name, string Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, db);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private string ConnectionString()
    {
        var value = _configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessException("GGA1C.Sync:NoConnectionString")
                .WithData("message", "Не задана строка подключения к базе");
        }

        return value;
    }

    private string Database()
    {
        return new MySqlConnectionStringBuilder(ConnectionString()).Database;
    }

    /// <summary>
    /// Имя пользователя подставляется в SQL текстом — параметром в MySQL его передать
    /// нельзя. Поэтому проверка строгая: латиница, цифры и знаки, которые бывают в почте.
    /// </summary>
    private static string Normalize(string value)
    {
        var trimmed = (value ?? "").Trim().ToLowerInvariant();

        if (!IsValidLogin(trimmed))
        {
            throw new BusinessException("GGA1C.Sync:InvalidPrintLogin")
                .WithData("message",
                    "Логин печати может содержать только латиницу, цифры, точку, дефис, " +
                    "подчёркивание и собачку");
        }

        return trimmed;
    }

    private static bool IsValidLogin(string value)
    {
        return value.Length is > 0 and <= MaxLoginLength
            && value.All(c => char.IsAsciiLetterOrDigit(c)
                              || c is '_' or '.' or '-' or '@');
    }

    public string GeneratePassword()
    {
        // Без похожих друг на друга знаков: пароль диктуют и вписывают руками в GoLabel.
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(20);

        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
