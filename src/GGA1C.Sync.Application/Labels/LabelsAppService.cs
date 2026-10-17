using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;

namespace GGA1C.Sync.Labels;

// Атрибут обязателен ИМЕННО на классе: ABP ищет его на типе реализации, и пометки
// только на интерфейсе недостаточно - проверено, GET /api/app/labels продолжал
// отдавать содержимое буфера анонимно.
[RemoteService(IsEnabled = false)]
public class LabelsAppService : ApplicationService, ILabelsAppService
{
    private readonly ILabelsBuffer _buffer;
    private readonly ISettingManager _settingManager;

    public LabelsAppService(ILabelsBuffer buffer, ISettingManager settingManager)
    {
        _buffer = buffer;
        _settingManager = settingManager;
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

    // Ключ возвращаем и меняем только вошедшим пользователям. Публикация этого сервиса
    // как REST API отключена (см. ILabelsAppService), но [Authorize] оставлен вторым
    // рубежом: если публикацию когда-нибудь включат обратно, ключ не утечёт анонимно.

    [Authorize]
    public async Task<string?> GetApiKeyAsync()
    {
        return await _settingManager.GetOrNullGlobalAsync(SyncSettings.GristApiKey);
    }

    [Authorize]
    public async Task<string> RegenerateApiKeyAsync()
    {
        var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
        await _settingManager.SetGlobalAsync(SyncSettings.GristApiKey, key);
        return key;
    }
}
