using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace GGA1C.Sync.Diagnostics;

public class RequestLogStore : DomainService, IRequestLogStore
{
    /// <summary>
    /// Журнал пишется на каждый запрос, поэтому растёт быстро. Держим ограниченное окно:
    /// он нужен для разбора «что прислал клиент только что», а не как архив.
    /// </summary>
    private const int KeepLastRecords = 2000;

    private readonly IRepository<RequestLogItem, int> _repo;

    public RequestLogStore(IRepository<RequestLogItem, int> repo)
    {
        _repo = repo;
    }

    public async Task SaveAsync(RequestLogItem item)
    {
        await _repo.InsertAsync(item, autoSave: true);

        // Подрезаем хвост не на каждой записи, а изредка - иначе на каждый запрос
        // приходился бы ещё и COUNT со удалением.
        if (item.Id % 200 != 0)
        {
            return;
        }

        var total = await _repo.GetCountAsync();
        if (total <= KeepLastRecords)
        {
            return;
        }

        var query = await _repo.GetQueryableAsync();
        var stale = query
            .OrderByDescending(x => x.Id)
            .Skip(KeepLastRecords)
            .ToList();

        if (stale.Count > 0)
        {
            await _repo.DeleteManyAsync(stale, autoSave: true);
        }
    }

    public async Task<List<RequestLogItem>> GetAsync(
        int maxCount = 100,
        string? source = null,
        string? pathFilter = null)
    {
        var query = await _repo.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(x => x.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(pathFilter))
        {
            query = query.Where(x => x.Path.Contains(pathFilter));
        }

        return query
            .OrderByDescending(x => x.Id)
            .Take(maxCount)
            .ToList();
    }

    public async Task ClearAsync()
    {
        await _repo.DeleteDirectAsync(x => true);
    }
}
