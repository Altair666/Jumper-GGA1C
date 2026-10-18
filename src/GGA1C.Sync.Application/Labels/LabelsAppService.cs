using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Labels;

// Атрибут нужен и на классе: ABP ищет его на типе реализации, пометки на интерфейсе
// недостаточно - проверено, GET /api/app/labels продолжал отдавать содержимое буфера
// анонимно.
[RemoteService(IsEnabled = false)]
public class LabelsAppService : ApplicationService, ILabelsAppService
{
    private readonly ILabelsBufferFactory _buffers;
    private readonly ILabelClientStore _clients;

    public LabelsAppService(ILabelsBufferFactory buffers, ILabelClientStore clients)
    {
        _buffers = buffers;
        _clients = clients;
    }

    public async Task<LabelClientDto?> ResolveClientAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var client = await _clients.FindByKeyAsync(key);
        if (client == null)
        {
            return null;
        }

        // Путь и счётчик здесь намеренно не заполняем: это горячий путь каждого запроса
        // от виджета, а подсчёт строк означал бы открытие файла буфера.
        return new LabelClientDto
        {
            Id = client.Id,
            Name = client.Name,
            Slug = client.Slug,
            KeyPrefix = client.KeyPrefix,
            IsEnabled = client.IsEnabled,
            LastUsedAt = client.LastUsedAt
        };
    }

    public Task<List<LabelRecord>> GetAllAsync(string slug)
    {
        return Task.FromResult(_buffers.For(slug).GetAll());
    }

    public Task<int> UpsertManyAsync(string slug, List<LabelRecord> records)
    {
        _buffers.For(slug).UpsertMany(records);
        return Task.FromResult(records.Count);
    }

    public Task<int> ReplaceAllAsync(string slug, List<LabelRecord> records)
    {
        _buffers.For(slug).ReplaceAll(records);
        return Task.FromResult(records.Count);
    }

    public Task<bool> DeleteAsync(string slug, string externalId)
    {
        return Task.FromResult(_buffers.For(slug).Delete(externalId));
    }

    public Task ClearAllAsync(string slug)
    {
        _buffers.For(slug).ClearAll();
        return Task.CompletedTask;
    }

    public async Task TouchAsync(int clientId)
    {
        await _clients.TouchAsync(clientId);
    }
}