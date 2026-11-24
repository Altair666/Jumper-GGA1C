using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Веб-редактор наклеек (П12.1): точное превью макета, правка положения/размера/данных
/// объектов, EZPL-текст задания для отладки порта (П5), сохранение новой версией файла.
/// Только Sync.Admin. Свойства шрифтов — П12.2, счётчики — П12.3, добавление объектов — П12.4.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IPrintTemplateEditorAppService : IApplicationService
{
    /// <summary>Открыть шаблон: геометрия объектов, превью, EZPL, счётчики и переменные.</summary>
    Task<TemplateEditorDto> OpenAsync(int templateId);

    /// <summary>Применить правки и отдать свежее превью, не сохраняя в хаб.</summary>
    Task<TemplatePreviewDto> RenderPreviewAsync(TemplateEditorChangesDto input);

    /// <summary>Применить правки и сохранить новую версией файла шаблона.</summary>
    Task<TemplateEditorDto> SaveAsync(TemplateEditorChangesDto input);

    /// <summary>Содержимое текущего файла — для скачивания из редактора.</summary>
    Task<PrintTemplateFileDto> DownloadAsync(int templateId);
}

public class TemplateEditorDto
{
    public int TemplateId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Ширина наклейки в точках принтера — координаты объектов в тех же единицах.</summary>
    public int WidthPx { get; set; }

    /// <summary>Высота наклейки в точках принтера.</summary>
    public int HeightPx { get; set; }

    public float Dpi { get; set; }

    /// <summary>Превью макета в PNG (base64) — рисуется тем же кодом, что печать.</summary>
    public string PreviewPngBase64 { get; set; } = "";

    public List<TemplateEditorShapeDto> Shapes { get; set; } = [];

    /// <summary>Счётчики (C00…) с непустым форматом — «C00», «C01» и т.д.</summary>
    public List<string> Counters { get; set; } = [];

    /// <summary>Переменные (V00…) с непустым форматом.</summary>
    public List<string> Variables { get; set; } = [];

    /// <summary>
    /// EZPL-текст задания, собранный портом (П5): команды принтера, растровые Q-блоки заменены
    /// метками. Не собралось — причина: это и есть отладка П5.
    /// </summary>
    public string EzplText { get; set; } = "";

    public bool EzplOk { get; set; }

    /// <summary>Имя файла шаблона и его размер после последнего сохранения.</summary>
    public string FileName { get; set; } = "";

    public long Size { get; set; }
}

public class TemplateEditorShapeDto
{
    /// <summary>Позиция в списке объектов — ключ правок.</summary>
    public int Index { get; set; }

    /// <summary>xsi:type из шаблона — имя класса QLabelSDK (Text, WindowText, BarCode…).</summary>
    public string TypeName { get; set; } = "";

    /// <summary>Что за объект человеку: Текст принтера / Текст (Windows) / Штрихкод / Рамка /
    /// Линия / Картинка / Прочее.</summary>
    public string KindCaption { get; set; } = "";

    /// <summary>Положение в точках принтера.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Размер в точках; у «Текста» принтера размера нет — null.</summary>
    public float? Width { get; set; }

    public float? Height { get; set; }

    public string Data { get; set; } = "";

    public bool IsPrint { get; set; }
}

public class TemplateEditorChangesDto
{
    public int TemplateId { get; set; }

    public List<TemplateEditorShapeChangeDto> Changes { get; set; } = [];
}

public class TemplateEditorShapeChangeDto
{
    public int Index { get; set; }

    public float? X { get; set; }

    public float? Y { get; set; }

    public float? Width { get; set; }

    public float? Height { get; set; }

    /// <summary>Данные объекта: текст, код переменной, подпись штрихкода. null — не менять.</summary>
    public string? Data { get; set; }
}

public class TemplatePreviewDto
{
    /// <summary>Свежее превью после применения правок.</summary>
    public string PreviewPngBase64 { get; set; } = "";
}
