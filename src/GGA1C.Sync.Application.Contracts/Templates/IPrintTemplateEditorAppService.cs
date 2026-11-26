using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Веб-редактор наклеек (П12): рабочая область по модели GoLabel — канва в миллиметрах от
/// физического размера, линейки мм/см/дюймы/пиксели, мышь (перетаскивание, маркеры),
/// настройки страницы в трёх вкладках как в диалоге GoLabel (размер носителя, размещение,
/// тип бумаги), EZPL-текст задания для отладки П5, сохранение новой версией файла.
/// Только Sync.Admin. Свойства шрифтов — П12.2, счётчики — П12.3.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface IPrintTemplateEditorAppService : IApplicationService
{
    /// <summary>Открыть шаблон: геометрия, растры объектов, настройки страницы, EZPL.</summary>
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

    /// <summary>Настройки страницы (Setup) — как в диалоге GoLabel.</summary>
    public TemplateSetupDto Setup { get; set; } = new();

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

/// <summary>
/// Настройки страницы — те же поля, что в диалоге GoLabel «Настройки страницы»:
/// размер носителя (вкладка 1), размещение на листе (вкладка 2), тип бумаги (вкладка 3).
/// Единицы хранения: размеры в мм, отступы и координаты объектов — точки принтера.
/// </summary>
public class TemplateSetupDto
{
    // ── Вкладка 1: размер носителя ────────────────────────────────────────────

    /// <summary>Ширина наклейки в мм.</summary>
    public float WidthMm { get; set; }

    /// <summary>Высота наклейки в мм.</summary>
    public float HeightMm { get; set; }

    /// <summary>Отступ слева, точек принтера.</summary>
    public int LeftMarginDots { get; set; }

    /// <summary>Отступ справа, точек принтера (Layout.RightMargin).</summary>
    public int RightMarginDots { get; set; }

    /// <summary>Отступ сверху, точек принтера.</summary>
    public int TopMarginDots { get; set; }

    /// <summary>Отступ снизу, точек принтера (Layout.BottomMargin).</summary>
    public int BottomMarginDots { get; set; }

    // ── Вкладка 2: размещение ─────────────────────────────────────────────────

    /// <summary>0 — печать по отдельным этикеткам, 1 — печать на целом листе (Layout.LabelMode).</summary>
    public int LabelMode { get; set; }

    /// <summary>Коэффициент перевода для измерений в настройках страницы.</summary>
    public float? DotsPerMm { get; set; }

    /// <summary>Сколько наклеек по горизонтали в выбранном режиме.</summary>
    public int HorAcross { get; set; } = 1;

    /// <summary>Сколько наклеек по вертикали на листе.</summary>
    public int VerAcross { get; set; } = 1;

    /// <summary>Зазор между наклейками по горизонтали, мм.</summary>
    public float HorGapMm { get; set; }

    /// <summary>Зазор между наклейками по вертикали, мм.</summary>
    public float VerGapMm { get; set; }

    /// <summary>Порядок печати: 0 — копировать, 1 — слева направо, 2 — сверху вниз
    /// (AcrossType из шаблона).</summary>
    public int AcrossType { get; set; }

    /// <summary>Форма этикетки на листе: 0 — квадрат, 1 — круг, 2 — скруглённый
    /// (Layout.Shape из шаблона).</summary>
    public int Shape { get; set; }

    // ── Вкладка 3: тип бумаги ─────────────────────────────────────────────────

    /// <summary>Тип носителя: 0 — этикетка с зазором, 1 — непрерывный, 2 — черная отметка
    /// (LabelType из шаблона).</summary>
    public int LabelType { get; set; }

    /// <summary>Ширина зазора в мм (носитель «с зазором»).</summary>
    public float GapMm { get; set; }

    /// <summary>Длина подачи в мм (непрерывный носитель).</summary>
    public float FeedMm { get; set; }

    /// <summary>Ширина черной отметки в мм.</summary>
    public float BlackMarkMm { get; set; }

    /// <summary>Положение черной отметки в мм.</summary>
    public float BlackMarkPositionMm { get; set; }

    /// <summary>Черная отметка за этикеткой (true, ZSign = +) или внутри этикетки
    /// (false, ZSign = −).</summary>
    public bool BlackMarkAfter { get; set; } = true;

    // ── Общие ─────────────────────────────────────────────────────────────────

    /// <summary>Скорость печати (единицы GoLabel, 1–6).</summary>
    public int Speed { get; set; } = 20;

    /// <summary>Плотность (темнота) печати, 0–19.</summary>
    public int Darkness { get; set; } = 5;

    /// <summary>Копий каждой наклейки.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Термоперенос (лента, ^AT) — true; прямая печать (^AD) — false.</summary>
    public bool DirectThermal { get; set; }
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
    /// Объект печатается растром: картинка ниже — ровно то, что уйдёт на принтер. Векторные
    /// (текст принтера, рамки, штрихкод командой) канва рисует сама.
    /// </summary>
    public bool IsRaster { get; set; }

    /// <summary>Растр объекта в PNG (base64); размеры в точках — <see cref="RasterWidthDots"/> и высота.</summary>
    public string? RasterPngBase64 { get; set; }

    public int RasterWidthDots { get; set; }

    public int RasterHeightDots { get; set; }

    /// <summary>
    /// Высота знака «текста принтера» в пунктах (FontHeight шаблона). В предпросмотре GoLabel
    /// она рисуется как FontHeight × Dpi/72 точек (Text.Draw, QLabelSDK/Text.cs:2860), в мм это
    /// FontHeight × 25.4/72.
    /// </summary>
    public float FontHeightPoints { get; set; } = 1f;
}

public class TemplateEditorChangesDto
{
    public int TemplateId { get; set; }

    public List<TemplateEditorShapeChangeDto> Changes { get; set; } = [];

    /// <summary>Правка настроек страницы; null — не менять.</summary>
    public TemplateSetupChangeDto? Setup { get; set; }
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

    /// <summary>Удалить объект. Индексы остальных сдвигаются — правки одного запроса
    /// применяются по порядку, поэтому удаление ставят последним.</summary>
    public bool Remove { get; set; }
}

/// <summary>Правка настроек страницы; null-поля не меняются.</summary>
public class TemplateSetupChangeDto
{
    public float? WidthMm { get; set; }

    public float? HeightMm { get; set; }

    public float? GapMm { get; set; }

    public int? LeftMarginDots { get; set; }

    public int? RightMarginDots { get; set; }

    public int? TopMarginDots { get; set; }

    public int? BottomMarginDots { get; set; }

    /// <summary>Режим размещения: 0 — отдельные этикетки, 1 — целый лист.</summary>
    public int? LabelMode { get; set; }

    public int? HorAcross { get; set; }

    public int? VerAcross { get; set; }

    public float? HorGapMm { get; set; }

    public float? VerGapMm { get; set; }

    /// <summary>Порядок печати: 0 — копировать, 1 — слева направо, 2 — сверху вниз.</summary>
    public int? AcrossType { get; set; }

    /// <summary>Форма этикетки: 0 — квадрат, 1 — круг, 2 — скруглённый.</summary>
    public int? Shape { get; set; }

    /// <summary>Тип носителя: 0 — с зазором, 1 — непрерывный, 2 — черная отметка.</summary>
    public int? LabelType { get; set; }

    public float? FeedMm { get; set; }

    public float? BlackMarkMm { get; set; }

    public float? BlackMarkPositionMm { get; set; }

    /// <summary>Черная отметка за этикеткой (+) — true; внутри этикетки (−) — false.</summary>
    public bool? BlackMarkAfter { get; set; }

    public int? Speed { get; set; }

    public int? Darkness { get; set; }

    public int? Copies { get; set; }

    public bool? DirectThermal { get; set; }
}

public class TemplatePreviewDto
{
    /// <summary>Свежие растры тех объектов, чьи данные правили: индекс → PNG base64.</summary>
    public Dictionary<int, string> Rasters { get; set; } = new();
}
