using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GGA1C.Sync.Permissions;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace GGA1C.Sync.Templates;

/// <summary>Шаблоны печати (П9). Файл — в <see cref="ITemplateFileStore"/>, в базе только ссылка.</summary>
[RemoteService(IsEnabled = false)]
[Authorize(SyncPermissions.Admin)]
public class PrintTemplatesAppService : SyncAppService, IPrintTemplatesAppService
{
    private readonly IRepository<PrintTemplate, int> _templates;
    private readonly ITemplateFileStore _files;

    public PrintTemplatesAppService(IRepository<PrintTemplate, int> templates, ITemplateFileStore files)
    {
        _templates = templates;
        _files = files;
    }

    public async Task<List<PrintTemplateDto>> GetListAsync(int? basePlatformId = null, int? modificationId = null)
    {
        var query = await _templates.GetQueryableAsync();
        if (basePlatformId.HasValue)
        {
            query = query.Where(t => t.BasePlatformId == basePlatformId.Value);
        }
        if (modificationId.HasValue)
        {
            query = query.Where(t => t.ModificationId == modificationId.Value);
        }
        var list = await AsyncExecuter.ToListAsync(query
            .OrderBy(t => t.BasePlatformId).ThenBy(t => t.ModificationId).ThenBy(t => t.Name));
        return list.Select(ToDto).ToList();
    }

    public async Task<PrintTemplateDto> UploadAsync(PrintTemplateUploadDto input)
    {
        Validate(input);
        CheckFile(input.Kind, input.FileName, input.Content);
        if (input.Kind != PrintTemplateKind.Pdf)
        {
            input.Content = StripDatabaseCredentials(input.Content);
        }

        string fileRef;
        using (var content = new MemoryStream(input.Content, writable: false))
        {
            fileRef = await _files.SaveAsync(content, input.FileName);
        }

        var template = new PrintTemplate
        {
            Name = input.Name.Trim(),
            Kind = input.Kind,
            BasePlatformId = input.BasePlatformId,
            ModificationId = input.ModificationId,
            FileRef = fileRef,
            FileName = Path.GetFileName(input.FileName),
            Size = input.Content.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(input.Content)).ToLowerInvariant(),
        };
        try
        {
            await _templates.InsertAsync(template, autoSave: true);
        }
        catch
        {
            // Строка не записалась — файл-сирота не нужен.
            await _files.DeleteAsync(fileRef);
            throw;
        }

        Logger.LogInformation("Шаблон загружен: {Name} ({Kind}), платформа {Platform}, исполнение {Modification}, {File}, {Size} байт, {User}",
            template.Name, template.Kind, template.BasePlatformId, template.ModificationId, template.FileName, template.Size, CurrentUser.UserName);
        return ToDto(template);
    }

    public async Task<PrintTemplateDto> UpdateAsync(int id, PrintTemplateEditDto input)
    {
        Validate(input);
        var template = await _templates.GetAsync(id);
        if ((template.Kind == PrintTemplateKind.Pdf) != (input.Kind == PrintTemplateKind.Pdf))
        {
            throw new UserFriendlyException("Вид PDF ↔ наклейка меняется только новой загрузкой файла.");
        }
        template.Name = input.Name.Trim();
        template.Kind = input.Kind;
        template.BasePlatformId = input.BasePlatformId;
        template.ModificationId = input.ModificationId;
        await _templates.UpdateAsync(template, autoSave: true);
        return ToDto(template);
    }

    public async Task DeleteAsync(int id)
    {
        var template = await _templates.FindAsync(id);
        if (template == null)
        {
            return;
        }
        await _templates.DeleteAsync(template, autoSave: true);
        await _files.DeleteAsync(template.FileRef);
        Logger.LogInformation("Шаблон удалён: {Name} ({File}), {User}", template.Name, template.FileName, CurrentUser.UserName);
    }

    public async Task<PrintTemplateFileDto> DownloadAsync(int id)
    {
        var template = await _templates.GetAsync(id);
        await using var stream = await _files.OpenAsync(template.FileRef);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return new PrintTemplateFileDto
        {
            FileName = template.FileName,
            ContentType = template.Kind == PrintTemplateKind.Pdf ? "application/pdf" : "application/xml",
            Content = memory.ToArray(),
        };
    }

    private static void Validate(PrintTemplateEditDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > PrintTemplateConsts.MaxNameLength)
        {
            throw new UserFriendlyException($"Название шаблона — от 1 до {PrintTemplateConsts.MaxNameLength} символов.");
        }
        if (input.BasePlatformId <= 0)
        {
            throw new UserFriendlyException("Укажите базовую платформу (rowId из Grist).");
        }
        if (input.ModificationId is <= 0)
        {
            throw new UserFriendlyException("Исполнение — rowId из Grist (больше нуля) или пусто.");
        }
        if (!Enum.IsDefined(input.Kind))
        {
            throw new UserFriendlyException("Неизвестный вид шаблона.");
        }
    }

    /// <summary>Наклейка — .ezpx, который разбирает порт GoLabel; PDF — по сигнатуре %PDF.</summary>
    internal static void CheckFile(PrintTemplateKind kind, string fileName, byte[] content)
    {
        if (content.Length == 0 || content.Length > PrintTemplateConsts.MaxFileBytes)
        {
            throw new UserFriendlyException($"Файл пустой или больше {PrintTemplateConsts.MaxFileBytes / 1024 / 1024} МБ.");
        }
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName).Length > PrintTemplateConsts.MaxFileNameLength)
        {
            throw new UserFriendlyException("Нет имени файла или оно слишком длинное.");
        }
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (kind == PrintTemplateKind.Pdf)
        {
            if (extension != ".pdf" || content.Length < 5 || content[0] != '%' || content[1] != 'P' || content[2] != 'D' || content[3] != 'F')
            {
                throw new UserFriendlyException("Для вида PDF нужен файл .pdf.");
            }
            return;
        }
        if (extension != ".ezpx")
        {
            throw new UserFriendlyException("Наклейка — файл GoLabel .ezpx.");
        }
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            EzpxReader.Load(stream);
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException("Файл не читается как шаблон GoLabel (.ezpx): " + ex.Message);
        }
    }

    /// <summary>
    /// Логин и пароль базы, сохранённые GoLabel в шаблоне, хабу не нужны (данные он даёт сам) —
    /// не храним их: значения UserID, Password, EncryptPwd вычищаются, как при съёме эталонов.
    /// </summary>
    internal static byte[] StripDatabaseCredentials(byte[] content)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        var cleaned = text;
        foreach (var tag in new[] { "UserID", "Password", "EncryptPwd" })
        {
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "<" + tag + ">[^<]*</" + tag + ">", "<" + tag + " />");
        }
        if (cleaned == text)
        {
            return content;
        }
        // GetString сохраняет BOM знаком U+FEFF, GetBytes пишет его обратно теми же байтами.
        return System.Text.Encoding.UTF8.GetBytes(cleaned);
    }

    private static PrintTemplateDto ToDto(PrintTemplate t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Kind = t.Kind,
        BasePlatformId = t.BasePlatformId,
        ModificationId = t.ModificationId,
        FileName = t.FileName,
        Size = t.Size,
        Sha256 = t.Sha256,
        CreationTime = t.CreationTime,
        LastModificationTime = t.LastModificationTime,
    };
}
