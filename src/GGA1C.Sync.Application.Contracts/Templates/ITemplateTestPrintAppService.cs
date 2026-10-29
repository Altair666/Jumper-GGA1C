using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace GGA1C.Sync.Templates;

/// <summary>
/// ВРЕМЕННО (П9, решение пользователя 2026-10-29): отладочная печать шаблона из хаба
/// администратором. Когда связка виджет ↔ хаб с редактированием заработает, печать переедет в
/// виджет — этот сервис, его реализация и TestPrintDialog удаляются целиком.
///
/// Наклеек столько, сколько выбрано строк данных (как GoLabel: наклейка на запись); копии —
/// из настройки самого шаблона. Поля «копии» нет намеренно.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface ITemplateTestPrintAppService : IApplicationService
{
    /// <summary>Учётки с их строками буфера (my_labels) — откуда брать данные.</summary>
    Task<List<TestPrintSourceDto>> GetSourcesAsync();

    /// <summary>Собрать задание и показать размер или отказ порта; на принтер ничего не уходит.</summary>
    Task<TestPrintBuildResultDto> BuildAsync(TestPrintInput input);

    /// <summary>Собрать и отправить на принтер; запись в журнал: кто, шаблон, принтер, строки, байты.</summary>
    Task<TestPrintBuildResultDto> PrintAsync(TestPrintInput input);
}

public class TestPrintInput
{
    public int TemplateId { get; set; }

    /// <summary>Учётка, чьи строки берутся (для шаблона без базы — не нужна).</summary>
    public int? ClientId { get; set; }

    /// <summary>Id строк AppLabels в порядке печати.</summary>
    public List<long> RowIds { get; set; } = [];

    public string PrinterHost { get; set; } = TestPrintDefaults.Host;

    public int PrinterPort { get; set; } = TestPrintDefaults.Port;
}

public static class TestPrintDefaults
{
    /// <summary>ZX430i стенда.</summary>
    public const string Host = "192.168.55.127";

    public const int Port = 9100;
}

public class TestPrintSourceDto
{
    public int ClientId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<TestPrintRowDto> Rows { get; set; } = [];
}

public class TestPrintRowDto
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ModeCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Qty { get; set; } = "";
    public string ExternalId { get; set; } = "";
}

public class TestPrintBuildResultDto
{
    public bool Ok { get; set; }

    /// <summary>Отказ порта или ошибка отправки — текстом для человека.</summary>
    public string? Error { get; set; }

    public int Bytes { get; set; }

    /// <summary>Сколько наклеек в задании (команд ^L).</summary>
    public int Labels { get; set; }

    public bool UsesDatabase { get; set; }

    public bool Sent { get; set; }
}
