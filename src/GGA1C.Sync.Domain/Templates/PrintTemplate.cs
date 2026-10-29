using Volo.Abp.Domain.Entities.Auditing;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Шаблон печати (П9): PDF или наклейка GoLabel, привязанная к базовой платформе из Grist и,
/// по желанию, к исполнению. На одну платформу — сколько угодно шаблонов.
///
/// Сам файл в базе не хранится (решение пользователя): здесь только ссылка
/// <see cref="FileRef"/>, которую выдал <see cref="ITemplateFileStore"/> — сейчас папка на
/// диске, потом FastAPI-сервис файлов на NAS.
/// </summary>
public class PrintTemplate : AuditedEntity<int>
{
    public string Name { get; set; } = "";

    public PrintTemplateKind Kind { get; set; }

    /// <summary>rowId базовой платформы в Grist (Basic_platforms).</summary>
    public int BasePlatformId { get; set; }

    /// <summary>rowId исполнения в Grist (Modifications); пусто — шаблон для всей платформы.</summary>
    public int? ModificationId { get; set; }

    /// <summary>Непрозрачная ссылка хранилища файлов.</summary>
    public string FileRef { get; set; } = "";

    /// <summary>Имя файла, как его загрузили (для скачивания и показа).</summary>
    public string FileName { get; set; } = "";

    public long Size { get; set; }

    /// <summary>SHA-256 содержимого в hex — видно, менялся ли файл.</summary>
    public string Sha256 { get; set; } = "";
}
