using System.Collections.Generic;
using System.Threading.Tasks;

namespace GGA1C.Sync.Diagnostics;

public interface IRequestLogStore
{
    Task SaveAsync(RequestLogItem item);

    /// <summary>Последние записи, новые сверху. Фильтры повторяют прежнюю версию сервиса.</summary>
    Task<List<RequestLogItem>> GetAsync(int maxCount = 100, string? source = null, string? pathFilter = null);

    Task ClearAsync();
}
