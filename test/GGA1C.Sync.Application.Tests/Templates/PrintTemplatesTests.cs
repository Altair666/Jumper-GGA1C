using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GGA1C.Sync.Labels;
using GGA1C.Sync.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace GGA1C.Sync.Templates;

/// <summary>
/// П9: шаблоны печати и временная отладочная печать. Файлы — в подменённом хранилище в
/// памяти. Сборка и печать сверяются с эталоном test-print (C:\GGA1C\golden, GGA1C_GOLDEN_DIR) —
/// без эталона эти два теста ничего не проверяют и проходят.
/// </summary>
public abstract class PrintTemplatesTests<TStartupModule> : SyncApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly MemoryFileStore _files = new();
    private readonly IPrintTemplatesAppService _templates;
    private readonly ITemplateTestPrintAppService _testPrint;
    private readonly ILabelClientStore _clients;
    private readonly IRepository<LabelRow, long> _rows;

    protected PrintTemplatesTests()
    {
        _templates = GetRequiredService<IPrintTemplatesAppService>();
        _testPrint = GetRequiredService<ITemplateTestPrintAppService>();
        _clients = GetRequiredService<ILabelClientStore>();
        _rows = GetRequiredService<IRepository<LabelRow, long>>();
    }

    protected override void AfterAddApplication(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ITemplateFileStore>(_files));
    }

    private static string? GoldenDir(string name)
    {
        var root = Environment.GetEnvironmentVariable("GGA1C_GOLDEN_DIR");
        var dir = string.IsNullOrEmpty(root) ? null : Path.Combine(root, name);
        return dir != null && File.Exists(Path.Combine(dir, "template.ezpx")) ? dir : null;
    }

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");

    [Fact]
    public async Task Pdf_Upload_List_Delete()
    {
        var dto = await _templates.UploadAsync(new PrintTemplateUploadDto
        {
            Name = "Паспорт", Kind = PrintTemplateKind.Pdf, BasePlatformId = 4, ModificationId = 7,
            FileName = "passport.pdf", Content = Pdf,
        });
        dto.Size.ShouldBe(Pdf.Length);
        _files.Files.Count.ShouldBe(1);

        (await _templates.GetListAsync(4)).Select(t => t.Id).ShouldContain(dto.Id);
        (await _templates.GetListAsync(5)).ShouldBeEmpty();
        (await _templates.GetListAsync(4, 7)).ShouldHaveSingleItem();

        (await _templates.DownloadAsync(dto.Id)).Content.ShouldBe(Pdf);

        await _templates.DeleteAsync(dto.Id);
        (await _templates.GetListAsync()).ShouldNotContain(t => t.Id == dto.Id);
        _files.Files.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PrintTemplateKind.Pdf, "a.ezpx")]
    [InlineData(PrintTemplateKind.ProductLabel, "a.pdf")]
    [InlineData(PrintTemplateKind.BoxLabel, "a.ezpx")] // не XML GoLabel
    public async Task Wrong_File_Is_Rejected(PrintTemplateKind kind, string fileName)
    {
        await Should.ThrowAsync<UserFriendlyException>(() => _templates.UploadAsync(new PrintTemplateUploadDto
        {
            Name = "x", Kind = kind, BasePlatformId = 1, FileName = fileName, Content = Pdf,
        }));
        _files.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Platform_Is_Required()
    {
        await Should.ThrowAsync<UserFriendlyException>(() => _templates.UploadAsync(new PrintTemplateUploadDto
        {
            Name = "x", Kind = PrintTemplateKind.Pdf, BasePlatformId = 0, FileName = "a.pdf", Content = Pdf,
        }));
    }

    [Fact]
    public void Database_Credentials_Are_Not_Stored()
    {
        var xml = "\uFEFF<PrintJob><DataBase><UserID>a.morozov@test.local</UserID><Password>secret</Password><EncryptPwd>xx</EncryptPwd></DataBase></PrintJob>";
        var cleaned = Encoding.UTF8.GetString(PrintTemplatesAppService.StripDatabaseCredentials(Encoding.UTF8.GetBytes(xml)));
        cleaned.ShouldNotContain("secret");
        cleaned.ShouldNotContain("a.morozov");
        cleaned.ShouldContain("<UserID />");
        cleaned.ShouldStartWith("\uFEFF");
    }

    [Fact]
    public void Admin_Only()
    {
        typeof(PrintTemplatesAppService).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ShouldContain(SyncPermissions.Admin);
        typeof(TemplateTestPrintAppService).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ShouldContain(SyncPermissions.Admin);
    }

    [Fact]
    public void Labels_Are_Counted_By_Print_Command_Skipping_Rasters()
    {
        // Растр, внутри которого байты «\nE\r\n», — не наклейка.
        byte[] job = [.. Encoding.ASCII.GetBytes("^L\r\nQ0,0,1,4\n"), 10, (byte)'E', 13, 10, .. Encoding.ASCII.GetBytes("\r\nE\r\n^L\r\nE\r\n")];
        TemplateTestPrintAppService.CountLabels(job).ShouldBe(2);
    }

    /// <summary>
    /// Отладочная печать «тестовой печати» по 4 строкам буфера a.morozov (data.csv эталона) даёт
    /// ровно байты GoLabel (golabel.bin): строки буфера → поля ^F00, ^F01 в порядке my_labels.
    /// </summary>
    [Fact]
    public async Task Test_Print_Matches_GoLabel_Golden()
    {
        var dir = GoldenDir("test-print-2");
        if (dir == null)
        {
            return;
        }
        var (template, clientId, rowIds) = await SeedGoldenAsync(dir);

        // Байт в байт с GoLabel — в режиме «принтер понимает P2/P3».
        var built = await _testPrint.BuildAsync(new TestPrintInput { TemplateId = template.Id, ClientId = clientId, RowIds = rowIds, PrinterAlignsText = true });
        built.Ok.ShouldBeTrue(built.Error);
        built.UsesDatabase.ShouldBeTrue();
        built.Labels.ShouldBe(4);
        var golden = File.ReadAllBytes(Path.Combine(dir, "golabel.bin"));
        built.Bytes.ShouldBe(golden.Length);

        // Печать — на свой слушатель TCP вместо принтера.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var memory = new MemoryStream();
            await client.GetStream().CopyToAsync(memory);
            return memory.ToArray();
        });
        var printed = await _testPrint.PrintAsync(new TestPrintInput
        {
            TemplateId = template.Id, ClientId = clientId, RowIds = rowIds, PrinterHost = "127.0.0.1", PrinterPort = port,
            PrinterAlignsText = true,
        });
        printed.Sent.ShouldBeTrue(printed.Error);
        var bytes = await received.WaitAsync(TimeSpan.FromSeconds(30));
        bytes.SequenceEqual(golden).ShouldBeTrue("отправленные байты не равны эталону GoLabel");
    }

    [Fact]
    public async Task Database_Template_Needs_Rows_And_Unreachable_Printer_Is_Reported()
    {
        var dir = GoldenDir("test-print-2");
        if (dir == null)
        {
            return;
        }
        var (template, clientId, rowIds) = await SeedGoldenAsync(dir);

        await Should.ThrowAsync<UserFriendlyException>(() => _testPrint.BuildAsync(new TestPrintInput { TemplateId = template.Id }));

        // Порт, на котором никто не слушает.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var result = await _testPrint.PrintAsync(new TestPrintInput
        {
            TemplateId = template.Id, ClientId = clientId, RowIds = rowIds, PrinterHost = "127.0.0.1", PrinterPort = deadPort,
        });
        result.Sent.ShouldBeFalse();
        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    private async Task<(PrintTemplateDto Template, int ClientId, List<long> RowIds)> SeedGoldenAsync(string dir)
    {
        var template = await _templates.UploadAsync(new PrintTemplateUploadDto
        {
            Name = "Тестовая печать", Kind = PrintTemplateKind.BoxLabel, BasePlatformId = 4,
            FileName = "тестовая печать.ezpx", Content = File.ReadAllBytes(Path.Combine(dir, "template.ezpx")),
        });

        int clientId = 0;
        var rowIds = new List<long>();
        await WithUnitOfWorkAsync(async () =>
        {
            var slug = "golden" + Guid.NewGuid().ToString("N")[..6];
            var (client, _) = await _clients.CreateAsync("Эталон", slug);
            clientId = client.Id;
            foreach (var line in File.ReadLines(Path.Combine(dir, "data.csv"), Encoding.UTF8).Skip(1).Where(l => l.Length > 0))
            {
                var v = line.Split(',');
                var row = await _rows.InsertAsync(new LabelRow
                {
                    Slug = slug, Date = v[0], ProductCode = v[1], ModeCode = v[2], Article = v[3], Name = v[4],
                    Manufacturer = v[5], Barcode = v[6], Qty = v[7], Extra1 = v[8], Extra2 = v[9], Extra3 = v[10],
                    ExternalId = v[11], UpdatedAt = DateTime.Parse(v[12], System.Globalization.CultureInfo.InvariantCulture),
                }, autoSave: true);
                rowIds.Add(row.Id);
            }
        });
        return (template, clientId, rowIds);
    }

    private sealed class MemoryFileStore : ITemplateFileStore
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();

        public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var key = Guid.NewGuid().ToString("N") + Path.GetExtension(fileName);
            Files[key] = memory.ToArray();
            return key;
        }

        public Task<Stream> OpenAsync(string fileRef, CancellationToken cancellationToken = default) =>
            Files.TryGetValue(fileRef, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new FileNotFoundException(fileRef);

        public Task DeleteAsync(string fileRef, CancellationToken cancellationToken = default)
        {
            Files.TryRemove(fileRef, out _);
            return Task.CompletedTask;
        }
    }
}
