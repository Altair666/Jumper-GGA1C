using System;
using Volo.Abp.Domain.Entities;

namespace GGA1C.Sync.Altium;

/// <summary>
/// Журнал синхронизации: что именно было сделано с каждой сущностью.
///
/// Это не замена аудиту ABP: аудит пишет, кто вызвал какой метод, а здесь видно
/// содержательный итог по каждому компоненту и папке — что создано, что обновлено,
/// что пропущено и с какой ошибкой. Без этого разбирать неудачную синхронизацию
/// на несколько тысяч компонентов невозможно.
/// </summary>
public class SyncLogEntry : Entity<int>
{
    public DateTime Timestamp { get; set; }

    /// <summary>Component | Folder | Project</summary>
    public string EntityType { get; set; } = "";

    public string EntityId { get; set; } = "";

    public string EntityName { get; set; } = "";

    /// <summary>Create | Update | Import | Skip | Error</summary>
    public string Action { get; set; } = "";

    /// <summary>OK | Error</summary>
    public string Status { get; set; } = "";

    public string? Message { get; set; }
}
