using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Labels;

public class LabelsAppService : ApplicationService, ILabelsAppService
{
    private readonly ILabelsBuffer _buffer;

    public LabelsAppService(ILabelsBuffer buffer)
    {
        _buffer = buffer;
    }

    public async Task<bool> IsApiKeyValidAsync(string? providedKey)
    {
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return false;
        }

        var expected = await SettingProvider.GetOrNullAsync(SyncSettings.GristApiKey);

        // Пустая настройка = доступ закрыт. Иначе незаполненный ключ открывал бы буфер всем.
        return !string.IsNullOrWhiteSpace(expected) && providedKey == expected;
    }

    public Task<List<LabelRecord>> GetAllAsync()
    {
        return Task.FromResult(_buffer.GetAll());
    }

    public Task<int> UpsertManyAsync(List<LabelRecord> records)
    {
        _buffer.UpsertMany(records);
        return Task.FromResult(records.Count);
    }

    public Task<int> ReplaceAllAsync(List<LabelRecord> records)
    {
        _buffer.ReplaceAll(records);
        return Task.FromResult(records.Count);
    }

    public Task<bool> DeleteAsync(string externalId)
    {
        return Task.FromResult(_buffer.Delete(externalId));
    }

    public Task ClearAllAsync()
    {
        _buffer.ClearAll();
        return Task.CompletedTask;
    }
}
