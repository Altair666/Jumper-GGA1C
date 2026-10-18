using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Запись этикеток в буфер конкретной учётной записи. Пишет сюда виджет Grist через
/// GristLabelsController, предъявляя ключ; читает GoLabel2 - но не через этот сервис,
/// а напрямую из файла буфера.
///
/// <para>
/// RemoteService отключён намеренно. ABP по соглашению публикует сервисы приложения как
/// REST API (настроено в SyncBlazorModule через ConventionalControllers). Для этого сервиса
/// такая публикация недопустима: эндпоинты получались анонимными, и любой в сети мог
/// читать и вытирать буфер мимо проверки ключа. Проверено вживую - GET отвечал 200.
/// Единственная HTTP-дверь к буферу - GristLabelsController.
/// </para>
/// </summary>
[RemoteService(IsEnabled = false)]
public interface ILabelsAppService : IApplicationService
{
    /// <summary>
    /// Опознать учётку по ключу. Возвращает null, если ключ неизвестен. Отключённая
    /// учётка возвращается с IsEnabled = false - чтобы в ответе можно было отличить
    /// "ключ не тот" от "ключ отозван".
    /// </summary>
    Task<LabelClientDto?> ResolveClientAsync(string? key);

    /// <summary>
    /// Опознать человека по короткоживущему токену, который виджет получил у самого Grist.
    /// Хаб спрашивает Grist, чья это сессия, и ищет учётку по адресу почты — секретов в
    /// документе при этом не хранится.
    /// </summary>
    Task<GristClientResolution> ResolveClientByGristTokenAsync(string? token);

    Task<List<LabelRecord>> GetAllAsync(string slug);

    Task<int> UpsertManyAsync(string slug, List<LabelRecord> records);

    /// <summary>Заменить буфер учётки целиком - основной режим виджета.</summary>
    Task<int> ReplaceAllAsync(string slug, List<LabelRecord> records);

    Task<bool> DeleteAsync(string slug, string externalId);

    Task ClearAllAsync(string slug);

    /// <summary>Отметить успешное использование ключа.</summary>
    Task TouchAsync(int clientId);
}