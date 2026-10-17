using System;
using Volo.Abp.Domain.Entities;

namespace GGA1C.Sync.Diagnostics;

/// <summary>
/// Запись о входящем HTTP-запросе: метод, путь, заголовки и тело.
///
/// Это не дубль аудита ABP. Аудит пишет, какой пользователь вызвал какой метод приложения,
/// а здесь нужен сырой запрос целиком — именно так был раскопан недокументированный API
/// Altium Enterprise Server: сервис ставился «перед» клиентом, и было видно, какие запросы
/// тот реально шлёт. Для продолжения реверс-инжиниринга (нерешённая пагинация компонентов)
/// этот журнал нужен в прежнем виде.
/// </summary>
public class RequestLogItem : Entity<int>
{
    public DateTime Timestamp { get; set; }

    /// <summary>[Browser] | [AES] | [TC/EDA] | [OAuth] — определяется по пути и User-Agent.</summary>
    public string Source { get; set; } = "";

    public string Method { get; set; } = "";

    public string Path { get; set; } = "";

    public string Headers { get; set; } = "";

    public string Body { get; set; } = "";
}
