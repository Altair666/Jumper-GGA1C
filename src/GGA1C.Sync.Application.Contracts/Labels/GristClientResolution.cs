namespace GGA1C.Sync.Labels;

/// <summary>
/// Чем закончилась попытка опознать человека по токену Grist.
///
/// Причина отказа нужна целиком: её видит оператор в виджете, и она должна говорить, что
/// делать — истёк токен, нет строки в таблице пользователей, нет учётки в хабе. Прежний
/// ответ «ключ не известен» в этой схеме бесполезен: ключа больше нет.
/// </summary>
public class GristClientResolution
{
    public LabelClientDto? Client { get; set; }

    public string? Error { get; set; }
}
