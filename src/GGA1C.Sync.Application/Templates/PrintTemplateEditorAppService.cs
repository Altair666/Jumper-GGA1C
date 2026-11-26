// Веб-редактор наклеек (П12). Рабочая область — по модели GoLabel: канва в мм от физического
// размера, зум слайдером, линейки мм/см/дюймы/пиксели, отрисовка вектором в браузере. Сервер
// отдаёт геометрию и растры объектов тем же кодом, что печать (LabelPreview.RenderShapePng
// поверх RasterShapeBitmaps), поэтому растровые объекты на канве — ровно то, что уйдёт на
// принтер; текст шрифтом принтера и рамки рисует браузер, как предпросмотр самого GoLabel.
//
// Сохранение — новой версией файла: прежний файл в хранилище остаётся, запись шаблона
// переключается на новую ссылку. Так правку можно откатить вручную без механизма версий.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using GGA1C.Sync.Permissions;
using GGA1C.Sync.Printing;
using GGA1C.Sync.Printing.GoLabel;
using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using GGA1C.Sync.Printing.GoLabel.Preview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace GGA1C.Sync.Templates;

[RemoteService(IsEnabled = false)]
[Authorize(SyncPermissions.Admin)]
public class PrintTemplateEditorAppService : SyncAppService, IPrintTemplateEditorAppService
{
    private readonly IRepository<PrintTemplate, int> _templates;
    private readonly ITemplateFileStore _files;

    public PrintTemplateEditorAppService(
        IRepository<PrintTemplate, int> templates,
        ITemplateFileStore files)
    {
        _templates = templates;
        _files = files;
    }

    public async Task<TemplateEditorDto> OpenAsync(int templateId)
    {
        var template = await GetLabelTemplateAsync(templateId);
        var file = await ReadFileAsync(template);
        var editor = EzpxEditor.Load(new MemoryStream(file));

        return ToDto(template, editor.Template, file);
    }

    public async Task<TemplatePreviewDto> RenderPreviewAsync(TemplateEditorChangesDto input)
    {
        var (editor, _) = await LoadEditableAsync(input.TemplateId);
        editor.Apply(ToEdits(input.Changes));

        // Растры обновляем только тем объектам, чьи данные правили: у остальных картинка
        // не зависит от правки.
        var editedData = input.Changes.Where(c => c.Data != null).Select(c => c.Index).ToHashSet();
        var rasters = new Dictionary<int, string>();

        foreach (var (shape, index) in editor.Template.Label.Shapes.Select((s, i) => (s, i)))
        {
            if (!editedData.Contains(index))
            {
                continue;
            }

            if (LabelPreview.RenderShapePng(shape, editor.Template.Setup.Dpi) is { } raster)
            {
                rasters[index] = Convert.ToBase64String(raster.Png);
            }
        }

        return new TemplatePreviewDto { Rasters = rasters };
    }

    public async Task<TemplateEditorDto> SaveAsync(TemplateEditorChangesDto input)
    {
        var template = await GetLabelTemplateAsync(input.TemplateId);
        var file = await ReadFileAsync(template);
        var editor = EzpxEditor.Load(new MemoryStream(file));
        if ((input.Setup?.WidthMm ?? editor.Template.Setup.LabelWidth) > PrintTemplateConsts.MaxPageWidthMm)
        {
            throw new UserFriendlyException($"Ширина страницы не может превышать {PrintTemplateConsts.MaxPageWidthMm} мм.");
        }
        editor.Apply(ToEdits(input.Changes));

        // Настройки страницы — до сохранения: размер канвы в ответе пересчитается сам.
        if (input.Setup != null)
        {
            editor.ApplySetup(new EzpxSetupEdit(
                input.Setup.WidthMm,
                input.Setup.HeightMm,
                input.Setup.GapMm,
                input.Setup.LeftMarginDots,
                input.Setup.RightMarginDots,
                input.Setup.TopMarginDots,
                input.Setup.BottomMarginDots,
                input.Setup.HorAcross,
                input.Setup.VerAcross,
                input.Setup.HorGapMm,
                input.Setup.VerGapMm,
                input.Setup.AcrossType,
                input.Setup.Shape,
                input.Setup.LabelType,
                input.Setup.FeedMm,
                input.Setup.BlackMarkMm,
                input.Setup.BlackMarkAfter,
                input.Setup.Speed,
                input.Setup.Darkness,
                input.Setup.Copies,
                input.Setup.DirectThermal,
                input.Setup.BlackMarkPositionMm,
                input.Setup.LabelMode));
        }

        using var saved = new MemoryStream();
        editor.Save(saved);
        var content = PrintTemplatesAppService.StripDatabaseCredentials(saved.ToArray());

        string fileRef;
        using (var stream = new MemoryStream(content, writable: false))
        {
            fileRef = await _files.SaveAsync(stream, template.FileName);
        }

        template.FileRef = fileRef;
        template.Size = content.Length;
        template.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))
            .ToLowerInvariant();
        await _templates.UpdateAsync(template, autoSave: true);

        // Прежний файл остаётся в хранилище: правку можно откатить вручную, а механизма
        // версий нет (ФАЙЛ отдаст версионирование позже).

        Logger.LogInformation(
            "Шаблон отредактирован: {User} — {TemplateId} «{Name}», новая версия {File}, {Size} байт",
            CurrentUser.UserName, template.Id, template.Name, template.FileName, template.Size);

        return ToDto(template, editor.Template, content);
    }

    public async Task<PrintTemplateFileDto> DownloadAsync(int templateId)
    {
        var template = await GetLabelTemplateAsync(templateId);
        var file = await ReadFileAsync(template);
        return new PrintTemplateFileDto
        {
            FileName = template.FileName,
            Content = PrintTemplatesAppService.StripDatabaseCredentials(file),
        };
    }

    // ── Внутреннее ────────────────────────────────────────────────────────────

    private async Task<PrintTemplate> GetLabelTemplateAsync(int id)
    {
        var template = await _templates.GetAsync(id);

        if (template.Kind == PrintTemplateKind.Pdf)
        {
            throw new UserFriendlyException("PDF в редакторе наклеек не открывается.");
        }

        return template;
    }

    private async Task<byte[]> ReadFileAsync(PrintTemplate template)
    {
        await using var stream = await _files.OpenAsync(template.FileRef);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private async Task<(EzpxEditor Editor, byte[] File)> LoadEditableAsync(int templateId)
    {
        var template = await GetLabelTemplateAsync(templateId);
        var file = await ReadFileAsync(template);
        return (EzpxEditor.Load(new MemoryStream(file)), file);
    }

    private TemplateEditorDto ToDto(PrintTemplate template, EzpxTemplate parsed, byte[] file)
    {
        var dpi = parsed.Setup.Dpi;

        var shapes = parsed.Label.Shapes.Select((s, index) =>
        {
            var dto = new TemplateEditorShapeDto
            {
                Index = index,
                TypeName = s.TypeName,
                KindCaption = KindCaption(s),
                X = s.X,
                Y = s.Y,
                Width = SizeOf(s).Width,
                Height = SizeOf(s).Height,
                Data = s is EzpxGraphic g ? g.Data : "",
                IsPrint = s.IsPrint,
                FontHeightPoints = s is EzpxText text ? text.FontHeight : 1f,
            };

            if (LabelPreview.RenderShapePng(s, dpi) is { } raster)
            {
                dto.IsRaster = true;
                dto.RasterPngBase64 = Convert.ToBase64String(raster.Png);
                dto.RasterWidthDots = raster.WidthDots;
                dto.RasterHeightDots = raster.HeightDots;
            }

            return dto;
        }).ToList();

        string ezplText;
        bool ezplOk;
        try
        {
            using var stream = new MemoryStream(file, writable: false);
            byte[] bytes = new GoLabelDriver(new MarkerRasterRenderer()).Build(
                stream, new PrintValues { SerialDefaultsFromTemplate = true });
            ezplText = Encoding.UTF8.GetString(bytes);
            ezplOk = true;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException
            or FormatException or ArgumentException or UserFriendlyException)
        {
            ezplText = "Задание не собралось — это и есть отладка П5:\n" + ex.Message;
            ezplOk = false;
        }

        return new TemplateEditorDto
        {
            TemplateId = template.Id,
            Name = template.Name,
            WidthMm = parsed.Setup.LabelWidth,
            HeightMm = parsed.Setup.MediaLengthToMm(parsed.Setup.LabelLength),
            DotsPerMm = parsed.Setup.DotsPerMm,
            Dpi = dpi,
            Setup = ToSetupDto(parsed.Setup),
            Shapes = shapes,
            Counters = Indexed(parsed.Label.SerialFormat, "C"),
            Variables = Indexed(parsed.Label.VariableFormat, "V"),
            EzplText = ezplText,
            EzplOk = ezplOk,
            FileName = template.FileName,
            Size = template.Size,
        };
    }

    private static TemplateSetupDto ToSetupDto(EzpxSetup s) => new()
    {
        WidthMm = s.LabelWidth,
        HeightMm = s.MediaLengthToMm(s.LabelLength),
        GapMm = s.MediaLengthToMm(s.GapLength),
        DotsPerMm = s.PageSetupDotsPerMm,
        LeftMarginDots = s.LeftMargin,
        RightMarginDots = s.Layout.RightMargin,
        TopMarginDots = s.TopMargin,
        BottomMarginDots = s.Layout.BottomMargin,
        LabelMode = s.Layout.LabelMode,
        HorAcross = s.Layout.PageHorAcross,
        VerAcross = s.Layout.PageVerAcross,
        HorGapMm = s.Layout.PageHorGap,
        VerGapMm = s.Layout.PageVerGap,
        AcrossType = (int)s.Layout.AcrossType,
        Shape = s.Layout.Shape,
        LabelType = s.LabelType,
        FeedMm = s.MediaLengthToMm(s.FeedLength),
        BlackMarkMm = s.MediaLengthToMm(s.BlackMark),
        BlackMarkPositionMm = s.MediaLengthToMm(s.Position),
        BlackMarkAfter = s.ZSign != '-',
        Speed = s.Speed,
        Darkness = s.Darkness,
        Copies = s.Copy,
        DirectThermal = s.PrintMode == 0,
    };

    private static EzpxShapeEdit[] ToEdits(List<TemplateEditorShapeChangeDto> changes)
    {
        // Удаление — первым: индексы правок даны в исходном списке, после удаления они
        // сдвигаются.
        var ordered = changes.OrderByDescending(c => c.Remove).ThenBy(c => c.Index).ToList();

        return ordered.Select(c => new EzpxShapeEdit(
            c.Index, c.X, c.Y, c.Width, c.Height, c.Data, c.Remove)).ToArray();
    }

    private static (float? Width, float? Height) SizeOf(EzpxShape s) => s switch
    {
        EzpxRasterShape r => (r.BoundRect.Width, r.BoundRect.Height),
        EzpxBarCode b => (b.Width, b.Height),
        EzpxRectangle rect => (rect.Width, rect.Height),
        EzpxLine line => (line.Width, line.Height),
        _ => (null, null),
    };

    private static string KindCaption(EzpxShape s) => s switch
    {
        EzpxText => "Текст (шрифт принтера)",
        EzpxBarCode b when b.IsRaster => "Штрихкод (картинка)",
        EzpxBarCode => "Штрихкод (команда)",
        EzpxRectangle => "Рамка",
        EzpxLine => "Линия",
        EzpxRasterShape r when r.TypeName == "Image" => "Картинка",
        EzpxRasterShape => "Текст (Windows)",
        _ => "Прочее",
    };

    private static List<string> Indexed(string?[] formats, string prefix)
    {
        var result = new List<string>();
        for (int i = 0; i < formats.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(formats[i]))
            {
                result.Add($"{prefix}{i:D2}: {formats[i]}");
            }
        }
        return result;
    }
}

/// <summary>
/// Рендерер для отладочного текста задания: вместо двоичного Q-блока пишет одну строку-метку,
/// чтобы весь EZPL читался как текст. Задание с ним не печатается — только для показа.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MarkerRasterRenderer : IRasterRenderer
{
    public void Render(RasterRequest request, EzplWriter output)
    {
        string data = request.Shape is EzpxGraphic graphic
            ? request.Job.FillPrintingData(graphic, bformat: false)
            : "";
        output.WriteLine(
            $"Q; {request.Shape.TypeName}; @x={request.OffsetX + request.Shape.X:0},y={request.OffsetY + request.Shape.Y:0}; данные: {data}");
    }
}
