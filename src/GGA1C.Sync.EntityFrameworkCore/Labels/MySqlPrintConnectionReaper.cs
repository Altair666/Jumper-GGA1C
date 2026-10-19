using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.EntityFrameworkCore.Labels;

/// <summary>
/// Уборка подключений GoLabel II: у каждого печатающего остаётся самое свежее, прежние
/// спящие закрываются.
///
/// Закрываются только сессии с командой <c>Sleep</c> — то есть те, где сейчас ничего не
/// выполняется. Работающий запрос не обрывается, иначе можно уронить печать на середине.
/// Подключения хаба (<c>gga1c_app</c>) не трогаются вовсе: отбор идёт строго по именам
/// логинов печати из нашей же таблицы.
/// </summary>
public class MySqlPrintConnectionReaper : IPrintConnectionReaper, ITransientDependency
{
    /// <summary>
    /// Не трогаем совсем свежие: GoLabel успевает открыть соединение и сразу выполнить
    /// запрос, и было бы обидно закрыть его в этот момент.
    /// </summary>
    private const int MinIdleSeconds = 30;

    private readonly IConfiguration _configuration;
    private readonly ILogger<MySqlPrintConnectionReaper> _log;

    public MySqlPrintConnectionReaper(
        IConfiguration configuration,
        ILogger<MySqlPrintConnectionReaper> log)
    {
        _configuration = configuration;
        _log = log;
    }

    public async Task<int> ReapAsync()
    {
        var connectionString = _configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return 0;
        }

        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync();

        var extra = await FindExtraAsync(db);

        foreach (var id in extra)
        {
            try
            {
                await using var kill = new MySqlCommand($"KILL CONNECTION {id}", db);
                await kill.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                // Сессия могла закрыться сама между выборкой и KILL - это не беда.
                _log.LogDebug(ex, "Подключение {Id} уже закрыто", id);
            }
        }

        return extra.Count;
    }

    /// <summary>
    /// Номера лишних сессий: для каждого логина печати оставляем самую новую (у MySQL
    /// номер растёт), остальные спящие — в отбой.
    /// </summary>
    private async Task<List<long>> FindExtraAsync(MySqlConnection db)
    {
        const string sql = @"
SELECT p.ID
FROM information_schema.PROCESSLIST p
JOIN AppLabelClients c
  ON c.PrintLogin = SUBSTRING_INDEX(p.USER, '@', 1) OR c.PrintLogin = p.USER
WHERE p.COMMAND = 'Sleep'
  AND p.TIME >= @idle
  AND p.ID <> (
      SELECT MAX(p2.ID)
      FROM information_schema.PROCESSLIST p2
      WHERE p2.USER = p.USER)";

        await using var command = new MySqlCommand(sql, db);
        command.Parameters.AddWithValue("@idle", MinIdleSeconds);

        var ids = new List<long>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }
}
