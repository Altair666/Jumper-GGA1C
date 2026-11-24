using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Веб-редактор наклеек (П12): рабочая область по модели GoLabel — канва в миллиметрах от
/// физического размера, линейки мм/см/дюймы/пиксели, отрисовка вектором; растровые объекты
/// принтера (логотипы, штрихкоды-картинки, текст Windows) приходят готовыми картинками из того
/// же кода, что печать. Правка положения/размера/данных, EZPL-текст задания для отладки П5,
/// сохранение новой версией файла. Только Sync.Admin.
/// Свойства шрифтов — П12.2, счётчики — П12.3, добавление объектов — П12.4.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IPrintTemplateEditorAppService : IApplicationService
{
    /// <summary>Открыть шаблон: геометрия, растры объектов, EZPL, счётчики и переменные.</summary>
    Task<TemplateEditorDto> OpenAsync(int templateId);

    /// <summary>
    /// Пересобрать растры объектов после правки данных: правка текста «шрифтом Windows»
    /// меняет его картинку. Идентичность (X/Y/размер) канва считает сама — она векторная.
    /// </summary>
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

    /// <summary>Ширина наклейки в миллиметрах (как хранится в шаблоне).</summary>
    public float WidthMm { get; set; }

    /// <summary>Высота наклейки в миллиметрах.</summary>
    public float HeightMm { get; set; }

    /// <summary>
    /// Сколько точек принтера в одном миллиметре — тот же коэффициент, которым порт собирает
    /// задание (у GoLabel при 200 dpi это 7.99, не ровно dpi/25.4). Координаты объектов в DTO —
    /// в этих точках; мм = точки / <see cref="DotsPerMm"/>.
    /// </summary>
    public float DotsPerMm { get; set; }

    /// <summary>DPI из шаблона — для показа.</summary>
    public float Dpi { get; set; }

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

    /// <summary>Что за объект человеку: Текст (шрифт принтера) / Текст (Windows) / Штрихкод / …</summary>
    public string KindCaption { get; set; } = "";

    /// <summary>Положение в точках принтера (см. <see cref="TemplateEditorDto.DotsPerMm"/>).</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Размер в точках; у «Текста» принтера размера нет — null.</summary>
    public float? Width { get; set; }

    public float? Height { get; set; }

    public string Data { get; set; } = "";

    public bool IsPrint { get; set; }

    /// <summary>
    /// Высота знака «текста принтера» в пунктах (FontHeight шаблона). В предпросмотре GoLabel
    /// она рисуется как FontHeight × Dpi/72 точек (Text.Draw, QLabelSDK/Text.cs:2860), в мм это
    /// FontHeight × 25.4/72.
    /// </summary>
    public float FontHeightPoints { get; set; } = 1f;

    /// <summary>
    /// Объект печатается растром: картинка ниже — ровно то, что уйдёт на принтер. Векторные
    /// (текст принтера, рамки, штрихкод командой) канва рисует сама.
    /// </summary>
    public bool IsRaster { get; set; }

    /// <summary>Растр объекта в PNG (base64); размеры в точках — <see cref="RasterWidthDots"/> и высота.</summary>
    public string? RasterPngBase64 { get; set; }

    public int RasterWidthDots { get; set; }

    public int RasterHeightDots { get; set; }
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
    /// <summary>Свежие растры тех объектов, чьи данные правили: индекс → PNG base64.</summary>
    public Dictionary<int, string> Rasters { get; set; } = new();
}
