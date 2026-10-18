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
        // Папку буфера можно не указывать: выведем из имени, взяв фамилию и
        // транслитерировав её. Кириллица в пути и в имени сетевой шары для GoLabel2
        // неуместна, поэтому именно транслитерация, а не имя как есть.
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = SlugGenerator.FromName(name);

            if (string.IsNullOrWhiteSpace(slug))
            {
                throw new BusinessException("GGA1C.Sync:CannotDeriveSlug")
                    .WithData("message",
                        "Не удалось вывести имя папки из имени учётной записи — укажите его вручную");
            }
        }

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
        var client = await _clients.FindAsync(id);
        if (client == null)
        {
            return;
        }

        var slug = client.Slug;

        await _clients.DeleteAsync(id);

        // Файл буфера удаляем вместе с учёткой, чтобы не копить мусор на сервере.
        // Порядок именно такой: сначала запись, потом файлы. Если удаление файлов
        // упрётся в занятый файл, учётка уже отозвана - ключ не работает, и повторное
        // удаление доберёт папку.
        _buffers.Delete(slug);
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
