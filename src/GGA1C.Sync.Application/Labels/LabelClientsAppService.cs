using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Labels;

// Атрибут нужен и на классе: ABP ищет его на типе реализации, пометки на интерфейсе
// недостаточно - проверено, эндпоинт оставался анонимным.
[RemoteService(IsEnabled = false)]
[Authorize]
public class LabelClientsAppService : ApplicationService, ILabelClientsAppService
{
    private readonly ILabelClientStore _clients;
    private readonly ILabelsBufferFactory _buffers;

    public LabelClientsAppService(ILabelClientStore clients, ILabelsBufferFactory buffers)
    {
        _clients = clients;
        _buffers = buffers;
    }

    public async Task<List<LabelClientDto>> GetListAsync()
    {
        var clients = await _clients.GetAllAsync();

        return clients.Select(c => new LabelClientDto
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            KeyPrefix = c.KeyPrefix,
            IsEnabled = c.IsEnabled,
            LastUsedAt = c.LastUsedAt,
            BufferPath = _buffers.PathFor(c.Slug),
            RowCount = _buffers.For(c.Slug).Count()
        }).ToList();
    }

    public async Task<string> CreateAsync(string name, string slug)
    {
        var (client, key) = await _clients.CreateAsync(name, slug);

        // Создаём файл буфера сразу, чтобы путь для DSN в GoLabel2 существовал ещё до
        // первой синхронизации - иначе источник ODBC не настроить.
        _buffers.For(client.Slug);

        return key;
    }

    public async Task<string> RegenerateKeyAsync(int id)
    {
        return await _clients.RegenerateKeyAsync(id);
    }

    public async Task<string?> RevealKeyAsync(int id)
    {
        return await _clients.RevealKeyAsync(id);
    }

    public async Task SetEnabledAsync(int id, bool enabled)
    {
        await _clients.SetEnabledAsync(id, enabled);
    }

    public async Task DeleteAsync(int id)
    {
        await _clients.DeleteAsync(id);
    }

    public async Task ClearBufferAsync(int id)
    {
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        _buffers.For(client.Slug).ClearAll();
    }
}
