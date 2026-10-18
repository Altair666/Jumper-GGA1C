using System.Collections.Generic;
using System.Threading.Tasks;
using GGA1C.Sync.Keycloak;
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
    private readonly KeycloakTokenVerifier _keycloakTokens;

    public LabelsAppService(
        ILabelsBufferFactory buffers,
        ILabelClientStore clients,
        KeycloakTokenVerifier keycloakTokens)
    {
        _buffers = buffers;
        _clients = clients;
        _keycloakTokens = keycloakTokens;
    }

    public async Task<GristClientResolution> ResolveClientByKeycloakTokenAsync(string? token)
    {
        var (sub, email, error) = await _keycloakTokens.VerifyAsync(token);

        if (error != null)
        {
            return new GristClientResolution { Error = error };
        }

        // Сначала по sub: он не меняется, когда человеку правят почту или имя. Почта -
        // запасной путь для учёток, заведённых руками и ещё не привязанных к Keycloak.
        var client = sub == null ? null : await _clients.FindBySubAsync(sub);

        if (client == null && !string.IsNullOrWhiteSpace(email))
        {
            client = await _clients.FindByEmailAsync(email);
        }

        if (client == null)
        {
            return new GristClientResolution
            {
                Error = $"В хабе нет учётной записи для {email ?? sub} — " +
                        "запустите сверку с Keycloak"
            };
        }

        return new GristClientResolution { Client = ToDto(client) };
    }

    public async Task<LabelClientDto?> ResolveClientAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var client = await _clients.FindByKeyAsync(key);

        return client == null ? null : ToDto(client);
    }

    /// <summary>
    /// Путь к буферу и счётчик строк здесь намеренно не заполняются: это горячий путь
    /// каждого запроса от виджета, а подсчёт означал бы открытие файла буфера.
    /// </summary>
    private static LabelClientDto ToDto(LabelClient client)
    {
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