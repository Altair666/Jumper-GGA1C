using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Шаблоны печати (П9): список, загрузка, правка описания, удаление, скачивание. Только
/// Sync.Admin; API для виджета — позже (виджет и шлюз отложены).
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IPrintTemplatesAppService : IApplicationService
{
    /// <summary>Шаблоны; фильтр по платформе и исполнению — по желанию.</summary>
    Task<List<PrintTemplateDto>> GetListAsync(int? basePlatformId = null, int? modificationId = null);

    Task<PrintTemplateDto> UploadAsync(PrintTemplateUploadDto input);

    Task<PrintTemplateDto> UpdateAsync(int id, PrintTemplateEditDto input);

    Task DeleteAsync(int id);

    /// <summary>Содержимое файла — для скачивания.</summary>
    Task<PrintTemplateFileDto> DownloadAsync(int id);
}

public class PrintTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public PrintTemplateKind Kind { get; set; }
    public int BasePlatformId { get; set; }
    public int? ModificationId { get; set; }
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }

    /// <summary>Наклейка GoLabel (.ezpx) — для неё доступна отладочная печать.</summary>
    public bool IsLabel => Kind != PrintTemplateKind.Pdf;
}

public class PrintTemplateEditDto
{
    public string Name { get; set; } = "";
    public PrintTemplateKind Kind { get; set; }
    public int BasePlatformId { get; set; }
    public int? ModificationId { get; set; }
}

public class PrintTemplateUploadDto : PrintTemplateEditDto
{
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = [];
}

public class PrintTemplateFileDto
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
}
