using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Templates;

/// <summary>П9: файлы шаблонов в папке Templates:Folder; ссылка — только имя файла.</summary>
public class FolderTemplateFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gga1c-templates-" + Guid.NewGuid().ToString("N"));

    private FolderTemplateFileStore Store() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Templates:Folder"] = _folder })
        .Build());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public async Task Save_Open_Delete()
    {
        var store = Store();
        var content = Encoding.UTF8.GetBytes("<PrintJob />");
        var fileRef = await store.SaveAsync(new MemoryStream(content), "Наклейка ГКВ-3.EZPX");

        fileRef.ShouldEndWith(".ezpx");
        fileRef.ShouldNotContain("ГКВ");
        Directory.GetFiles(_folder).ShouldHaveSingleItem();

        await using (var stream = await store.OpenAsync(fileRef))
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory);
            memory.ToArray().ShouldBe(content);
        }

        await store.DeleteAsync(fileRef);
        Directory.GetFiles(_folder).ShouldBeEmpty();
        await store.DeleteAsync(fileRef); // повторно — не ошибка
        await Should.ThrowAsync<FileNotFoundException>(() => store.OpenAsync(fileRef));
    }

    [Theory]
    [InlineData(@"..\..\GGA1C\stringencryption.key")]
    [InlineData("../x.ezpx")]
    [InlineData("")]
    [InlineData(".ezpx")]
    public async Task Ref_Cannot_Leave_Folder(string fileRef)
    {
        await Should.ThrowAsync<ArgumentException>(() => Store().OpenAsync(fileRef));
    }

    [Fact]
    public void Default_Folder_Is_Outside_Repository()
    {
        new FolderTemplateFileStore(new ConfigurationBuilder().Build()).Folder.ShouldBe(FolderTemplateFileStore.DefaultFolder);
    }
}
