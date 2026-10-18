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
/// Имя логина собирается из папки учётки с приставкой <c>lbl_</c> — так его не спутать со
/// служебными учётными записями самой базы, и человек по имени «root» не получит логин root.
/// </summary>
public class MySqlPrintLoginService : IPrintLoginService, ITransientDependency
{
    private const string Prefix = "lbl_";
    private const string ViewName = "my_labels";

    private readonly IConfiguration _configuration;
    private readonly ILogger<MySqlPrintLoginService> _log;

    public MySqlPrintLoginService(
        IConfiguration configuration,
        ILogger<MySqlPrintLoginService> log)
    {
        _configuration = configuration;
        _log = log;
    }

    public string LoginFor(string slug)
    {
        return Prefix + Normalize(slug);
    }

    public async Task<string> IssueAsync(string slug)
    {
        var login = LoginFor(slug);
        var password = GeneratePassword();

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

        _log.LogInformation("Выдан логин печати {Login}", login);

        return password;
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
WHERE c.PrintLogin = SUBSTRING_INDEX(SESSION_USER(), '@', 1)
  AND c.IsEnabled = 1";

        await ExecuteAsync(db, sql);
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
    /// нельзя. Поэтому проверка строгая: только латиница, цифры и подчёркивание.
    /// </summary>
    private static string Normalize(string value)
    {
        var trimmed = (value ?? "").Trim().ToLowerInvariant();

        if (trimmed.Length == 0 || trimmed.Length > 32 ||
            !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new BusinessException("GGA1C.Sync:InvalidPrintLogin")
                .WithData("message",
                    "Логин печати может содержать только латиницу, цифры и подчёркивание");
        }

        return trimmed;
    }

    private static string GeneratePassword()
    {
        // Без похожих друг на друга знаков: пароль диктуют и вписывают руками в GoLabel.
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(20);

        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
