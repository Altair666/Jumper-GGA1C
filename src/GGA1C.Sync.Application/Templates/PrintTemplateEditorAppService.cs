// Веб-редактор наклеек (П12.1). Превью рисуется тем же кодом, что печать
// (LabelPreview поверх RasterShapeBitmaps), поэтому то, что видит администратор, — то, что
// уйдёт на принтер; расхождение возможно только у объектов-команд (текст шрифтом принтера),
// которые принтер рисует сам, — там превью рисует GDI, как и предпросмотр GoLabel.
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

        var dto = ToDto(template, editor.Template, file);
        return dto;
    }

    public async Task<TemplatePreviewDto> RenderPreviewAsync(TemplateEditorChangesDto input)
    {
        var (editor, _) = await LoadEditableAsync(input.TemplateId);
        editor.Apply(ToEdits(input.Changes));

        return new TemplatePreviewDto
        {
            PreviewPngBase64 = RenderPngSafe(editor.Template),
        };
    }

    public async Task<TemplateEditorDto> SaveAsync(TemplateEditorChangesDto input)
    {
        var template = await GetLabelTemplateAsync(input.TemplateId);
        var file = await ReadFileAsync(template);
        var editor = EzpxEditor.Load(new MemoryStream(file));
        editor.Apply(ToEdits(input.Changes));

        using var saved = new MemoryStream();
        editor.Save(saved);
        var content = saved.ToArray();

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
            Content = file,
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
        var dto = new TemplateEditorDto
        {
            TemplateId = template.Id,
            Name = template.Name,
            WidthPx = Math.Max(1, parsed.Setup.WidthInPixel),
            HeightPx = Math.Max(1, parsed.Setup.HeightInPixel),
            Dpi = parsed.Setup.Dpi,
            FileName = template.FileName,
            Size = template.Size,
            PreviewPngBase64 = RenderPngSafe(parsed),
            Shapes = parsed.Label.Shapes.Select((s, i) => new TemplateEditorShapeDto
            {
                Index = i,
                TypeName = s.TypeName,
                KindCaption = KindCaption(s),
                X = s.X,
                Y = s.Y,
                Width = SizeOf(s).Width,
                Height = SizeOf(s).Height,
                Data = s is EzpxGraphic g ? g.Data : "",
                IsPrint = s.IsPrint,
            }).ToList(),
            Counters = Indexed(parsed.Label.SerialFormat, "C"),
            Variables = Indexed(parsed.Label.VariableFormat, "V"),
        };

        // EZPL-текст для отладки П5: что порт шлёт принтеру. Растровые Q-блоки — двоичные,
        // поэтому вместо них метки; само задание собирается тем же GoLabelDriver.
        try
        {
            using var stream = new MemoryStream(file, writable: false);
            byte[] bytes = new GoLabelDriver(new MarkerRasterRenderer()).Build(
                stream, new PrintValues { SerialDefaultsFromTemplate = true });
            dto.EzplText = Encoding.UTF8.GetString(bytes);
            dto.EzplOk = true;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException
            or FormatException or ArgumentException or UserFriendlyException)
        {
            dto.EzplText = "Задание не собралось — это и есть отладка П5:\n" + ex.Message;
            dto.EzplOk = false;
        }

        return dto;
    }

    /// <summary>Превью: сбой отрисовки одного объекта — не повод не открывать редактор.</summary>
    private static string RenderPngSafe(EzpxTemplate parsed)
    {
        try
        {
            return Convert.ToBase64String(LabelPreview.RenderPng(parsed));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return "";
        }
    }

    private static EzpxShapeEdit[] ToEdits(List<TemplateEditorShapeChangeDto> changes) =>
        changes.Select(c => new EzpxShapeEdit(c.Index, c.X, c.Y, c.Width, c.Height, c.Data)).ToArray();

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
