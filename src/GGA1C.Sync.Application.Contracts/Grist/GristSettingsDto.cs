namespace GGA1C.Sync.Grist;

/// <summary>
/// Документ Grist, по которому хаб опознаёт людей: виджет присылает токен от самого Grist,
/// хаб читает им таблицу с адресами почты.
///
/// Ключ API нужен только кнопке «Проверить связь». Как и прочие секреты, наружу он не
/// отдаётся: вместо него приходит маска той же длины
/// (<see cref="GGA1C.Sync.Secrets.SecretMask"/>). При сохранении: одни точки — оставить
/// как было, пусто — удалить, иное — новое значение.
/// </summary>
public class GristSettingsDto
{
    /// <summary>Адрес Grist, например http://192.168.88.238:8484</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Идентификатор документа — тот, что в адресной строке Grist.</summary>
    public string DocId { get; set; } = "";

    /// <summary>Маска сохранённого ключа при чтении; что ввели — при сохранении.</summary>
    public string ApiKey { get; set; } = "";

    public bool HasApiKey { get; set; }

    /// <summary>Таблица с колонкой email, по умолчанию LabelIdentity.</summary>
    public string IdentityTable { get; set; } = "";
}
