using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace GGA1C.Sync.Branding;

/// <summary>
/// П16: значок сайта — только PNG, SVG без скриптов или ICO, не больше 256 КБ; файл в папке
/// Branding:Folder, один, сброс возвращает значок по умолчанию.
/// </summary>
public class SiteIconTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Ico = [0, 0, 1, 0, 1, 0, 16, 16];
    private static readonly byte[] Svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"/></svg>");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gga1c-site-icon-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void Типы_определяются_по_содержимому()
    {
        SiteIcon.Validate("logo.PNG", Png).ShouldBe(".png");
        SiteIcon.Validate("favicon.ico", Ico).ShouldBe(".ico");
        SiteIcon.Validate("icon.svg", Svg).ShouldBe(".svg");
    }

    [Theory]
    [InlineData("logo.svg")] // PNG под чужим расширением
    [InlineData("logo.gif")]
    [InlineData("logo")]
    public void Расширение_должно_совпасть_с_содержимым(string name)
    {
        Should.Throw<UserFriendlyException>(() => SiteIcon.Validate(name, Png));
    }

    [Fact]
    public void Не_картинка_и_пустой_файл_отклоняются()
    {
        Should.Throw<UserFriendlyException>(() => SiteIcon.Validate("a.png", []));
        Should.Throw<UserFriendlyException>(() => SiteIcon.Validate("a.png", Encoding.UTF8.GetBytes("hello")));
        Should.Throw<UserFriendlyException>(() => SiteIcon.Validate("a.jpg", [0xFF, 0xD8, 0xFF, 0xE0]));
    }

    [Fact]
    public void Слишком_большой_отклоняется()
    {
        var big = new byte[SiteIcon.MaxBytes + 1];
        Png.CopyTo(big, 0);
        Should.Throw<UserFriendlyException>(() => SiteIcon.Validate("a.png", big));
    }

    [Theory]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    [InlineData("<svg onload=\"alert(1)\"></svg>")]
    [InlineData("<svg><a href=\"javascript:alert(1)\"/></svg>")]
    [InlineData("<svg><image href=\"https://evil.example/x.png\"/></svg>")]
    [InlineData("<svg><foreignObject><div/></foreignObject></svg>")]
    public void SVG_со_скриптами_и_внешними_ссылками_отклоняется(string svg)
    {
        Should.Throw<UserFriendlyException>(() => SiteIcon.Validate("a.svg", Encoding.UTF8.GetBytes(svg)));
    }

    [Fact]
    public async Task Хранилище_держит_один_файл_и_сбрасывается()
    {
        var store = new SiteIconStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Branding:Folder"] = _folder })
            .Build());

        store.Find().ShouldBeNull();
        store.Reset(); // папки ещё нет — не падает

        await store.SaveAsync("a.png", Png);
        store.Find()!.ContentType.ShouldBe("image/png");

        await store.SaveAsync("a.svg", Svg);
        var found = store.Find()!;
        found.ContentType.ShouldBe("image/svg+xml");
        Directory.GetFiles(_folder).Length.ShouldBe(1);

        await Should.ThrowAsync<UserFriendlyException>(() => store.SaveAsync("b.png", [1, 2, 3]));
        store.Find()!.Path.ShouldBe(found.Path);

        store.Reset();
        store.Find().ShouldBeNull();
    }

    [Fact]
    public void Папка_по_умолчанию_вне_репозитория()
    {
        new SiteIconStore(new ConfigurationBuilder().Build()).Folder.ShouldBe(SiteIconStore.DefaultFolder);
    }
}
