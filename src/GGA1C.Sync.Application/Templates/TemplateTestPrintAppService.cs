// ВРЕМЕННО (П9): отладочная печать шаблона из хаба. Удаляется целиком вместе с
// ITemplateTestPrintAppService и Components/Templates/TestPrintDialog.razor, когда печать
// переедет в виджет (решение пользователя 2026-10-29).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using GGA1C.Sync.Permissions;
using GGA1C.Sync.Printing;
using GGA1C.Sync.Printing.GoLabel;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace GGA1C.Sync.Templates;

[RemoteService(IsEnabled = false)]
[Authorize(SyncPermissions.Admin)]
public class TemplateTestPrintAppService : SyncAppService, ITemplateTestPrintAppService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly IRepository<PrintTemplate, int> _templates;
    private readonly ITemplateFileStore _files;
    private readonly ILabelClientStore _clients;
    private readonly IRepository<LabelRow, long> _rows;

    public TemplateTestPrintAppService(
        IRepository<PrintTemplate, int> templates,
        ITemplateFileStore files,
        ILabelClientStore clients,
        IRepository<LabelRow, long> rows)
    {
        _templates = templates;
        _files = files;
        _clients = clients;
        _rows = rows;
    }

    public async Task<List<TestPrintSourceDto>> GetSourcesAsync()
    {
        var clients = await _clients.GetAllAsync();
        var query = await _rows.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(r => r.Id));
        return clients
            .OrderBy(c => c.Name)
            .Select(c => new TestPrintSourceDto
            {
                ClientId = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                Rows = rows.Where(r => r.Slug == c.Slug).Select(r => new TestPrintRowDto
                {
                    Id = r.Id,
                    Date = r.Date,
                    ProductCode = r.ProductCode,
                    ModeCode = r.ModeCode,
                    Name = r.Name,
                    Qty = r.Qty,
                    ExternalId = r.ExternalId,
                }).ToList(),
            })
            .ToList();
    }

    public async Task<TestPrintBuildResultDto> BuildAsync(TestPrintInput input)
    {
        var (result, _, _) = await BuildJobAsync(input);
        return result;
    }

    public async Task<TestPrintBuildResultDto> PrintAsync(TestPrintInput input)
    {
        if (string.IsNullOrWhiteSpace(input.PrinterHost) || input.PrinterPort is <= 0 or > 65535)
        {
            throw new UserFriendlyException("Укажите принтер: адрес и порт (1–65535).");
        }

        var (result, bytes, template) = await BuildJobAsync(input);
        if (!result.Ok || bytes == null)
        {
            return result;
        }

        try
        {
            using var cts = new CancellationTokenSource(SendTimeout);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(input.PrinterHost.Trim(), input.PrinterPort, cts.Token);
            await using var stream = tcp.GetStream();
            await stream.WriteAsync(bytes, cts.Token);
            await stream.FlushAsync(cts.Token);
            result.Sent = true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            result.Ok = false;
            result.Error = $"Принтер {input.PrinterHost}:{input.PrinterPort} не принял задание: {ex.Message}";
        }

        Logger.LogInformation(
            "Отладочная печать: {User} — шаблон {TemplateId} «{Template}», принтер {Host}:{Port}, строк {Rows}, наклеек {Labels}, байт {Bytes}, отправлено {Sent}{Error}",
            CurrentUser.UserName, template!.Id, template.Name, input.PrinterHost, input.PrinterPort, input.RowIds.Count,
            result.Labels, result.Bytes, result.Sent, result.Error == null ? "" : " — " + result.Error);
        return result;
    }

    private async Task<(TestPrintBuildResultDto Result, byte[]? Bytes, PrintTemplate? Template)> BuildJobAsync(TestPrintInput input)
    {
        var template = await _templates.GetAsync(input.TemplateId);
        if (template.Kind == PrintTemplateKind.Pdf)
        {
            throw new UserFriendlyException("PDF-шаблон этой печатью не печатается — только наклейки GoLabel.");
        }

        byte[] file;
        await using (var stream = await _files.OpenAsync(template.FileRef))
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory);
            file = memory.ToArray();
        }

        var result = new TestPrintBuildResultDto();
        EzpxTemplate parsed;
        using (var probe = new MemoryStream(file, writable: false))
        {
            parsed = EzpxReader.Load(probe);
        }
        result.UsesDatabase = parsed.Label.DataBaseFormat != "None";

        List<IReadOnlyList<object?>>? records = null;
        if (result.UsesDatabase)
        {
            records = await LoadRecordsAsync(input);
        }

        try
        {
            var values = new PrintValues
            {
                SerialDefaultsFromTemplate = true,
                Records = records,
                PrinterAlignsText = input.PrinterAlignsText,
            };
            using var templateStream = new MemoryStream(file, writable: false);
            byte[] bytes = new GoLabelDriver().Build(templateStream, values);
            result.Ok = true;
            result.Bytes = bytes.Length;
            result.Labels = CountLabels(bytes);
            return (result, bytes, template);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FormatException or ArgumentException)
        {
            result.Ok = false;
            result.Error = "Порт не собрал задание: " + ex.Message;
            return (result, null, template);
        }
    }

    /// <summary>Строки AppLabels учётки в порядке столбцов my_labels (поля ^F00, ^F01 …).</summary>
    private async Task<List<IReadOnlyList<object?>>> LoadRecordsAsync(TestPrintInput input)
    {
        if (input.ClientId == null || input.RowIds.Count == 0)
        {
            throw new UserFriendlyException("Шаблон берёт данные из базы — выберите человека и строку (строки) его буфера.");
        }
        var client = await _clients.FindAsync(input.ClientId.Value)
            ?? throw new UserFriendlyException("Учётка не найдена.");
        var query = await _rows.GetQueryableAsync();
        var ids = input.RowIds.ToList();
        var rows = await AsyncExecuter.ToListAsync(query.Where(r => r.Slug == client.Slug && ids.Contains(r.Id)));
        if (rows.Count != ids.Distinct().Count())
        {
            throw new UserFriendlyException("Часть выбранных строк не найдена у этой учётки — обновите страницу.");
        }
        var byId = rows.ToDictionary(r => r.Id);
        return ids.Select(id => (IReadOnlyList<object?>)ToRecord(byId[id])).ToList();
    }

    /// <summary>Столбцы представления my_labels по порядку.</summary>
    internal static object?[] ToRecord(LabelRow r) =>
    [
        r.Date, r.ProductCode, r.ModeCode, r.Article, r.Name, r.Manufacturer, r.Barcode, r.Qty,
        r.Extra1, r.Extra2, r.Extra3, r.ExternalId, r.UpdatedAt,
    ];

    /// <summary>Наклеек в задании — сколько команд печати «E» (данные растров Q пропускаются).</summary>
    internal static int CountLabels(byte[] job)
    {
        int count = 0;
        int i = 0;
        while (i < job.Length)
        {
            int lf = Array.IndexOf(job, (byte)10, i);
            int end = lf < 0 ? job.Length : lf;
            int textEnd = end > i && job[end - 1] == (byte)13 ? end - 1 : end;
            string line = Encoding.ASCII.GetString(job, i, textEnd - i);
            i = lf < 0 ? job.Length : lf + 1;
            if (line == "E")
            {
                count++;
            }
            else if (line.StartsWith('Q') && line[1..].Split(',') is { Length: 4 } q
                && int.TryParse(q[2], out int rowBytes) && int.TryParse(q[3], out int rows))
            {
                i += rowBytes * rows + 2;
            }
        }
        return count;
    }
}
