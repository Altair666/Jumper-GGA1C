using System;
using System.Globalization;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Volo.Abp.DependencyInjection;

namespace GGA1C.Sync.EntityFrameworkCore.Labels;

/// <summary>Uptime MySQL учёткой приложения: особых прав <c>SHOW GLOBAL STATUS</c> не требует.</summary>
public class MySqlServerUptime : IMySqlServerUptime, ITransientDependency
{
    private readonly IConfiguration _configuration;

    public MySqlServerUptime(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<long> GetUptimeSecondsAsync()
    {
        var connectionString = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Не задана строка подключения Default");
        }

        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync();

        await using var command = new MySqlCommand("SHOW GLOBAL STATUS LIKE 'Uptime'", db);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("MySQL не вернул Uptime");
        }

        return long.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
    }
}
