using System.Threading.Tasks;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Сколько секунд работает MySQL (<c>SHOW GLOBAL STATUS LIKE 'Uptime'</c>) — по нему работник
/// печати замечает перезапуск сервера (<see cref="MySqlRestartDetector"/>).
/// </summary>
public interface IMySqlServerUptime
{
    Task<long> GetUptimeSecondsAsync();
}
