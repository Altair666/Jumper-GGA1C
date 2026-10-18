using System;
using System.Collections.Generic;
using System.IO;
using GGA1C.Sync.Labels;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Проверки на чистых классах, без хоста ABP: транслитерация имени папки и работа фабрики
/// буферов. Именно эти два места легко ломаются незаметно — имя папки попадает в путь на
/// диске, а удаление буфера упирается в пул соединений SQLite.
/// </summary>
public class LabelsBufferTests : IDisposable
{
    private readonly string _root;
    private readonly LabelsBufferFactory _factory;

    public LabelsBufferTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gga1c-tests-" + Guid.NewGuid().ToString("N")[..8]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Labels:BuffersRoot"] = _root
            })
            .Build();

        _factory = new LabelsBufferFactory(configuration);
    }

    [Theory]
    [InlineData("Иванов Пётр", "ivanov")]
    [InlineData("Иванов, участок сборки", "ivanov")]
    [InlineData("Щербаков", "shcherbakov")]
    [InlineData("Кузнецова Елена Сергеевна", "kuznetsova")]
    [InlineData("Volkov", "volkov")]
    [InlineData("  Морозов  ", "morozov")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("...", "")]
    public void Имя_папки_выводится_из_фамилии(string? name, string expected)
    {
        SlugGenerator.FromName(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("a.morozov@test.local", "amorozov")]
    [InlineData("tester@test.local", "tester")]
    [InlineData("p_ivanov@mp-lab.ru", "p_ivanov")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Имя_папки_выводится_из_email(string? email, string expected)
    {
        SlugGenerator.FromEmail(email).ShouldBe(expected);
    }

    [Theory]
    // фамилия в приоритете: папка с фамилией понятнее тому, кто настраивает ODBC
    [InlineData("Иванов Пётр", "p.ivanov@mp-lab.ru", "ivanov")]
    // из имени выжать нечего - берём email
    [InlineData("...", "p.ivanov@mp-lab.ru", "pivanov")]
    [InlineData("", "tester@test.local", "tester")]
    // нет ни того, ни другого - пусто, вызывающий обязан сказать об этом человеку
    [InlineData("", "", "")]
    public void Имя_папки_из_фамилии_или_почты(string? name, string? email, string expected)
    {
        SlugGenerator.FromNameOrEmail(name, email).ShouldBe(expected);
    }

    [Fact]
    public void Буфер_создаётся_в_папке_учётки()
    {
        var buffer = _factory.For("ivanov");

        buffer.DatabasePath.ShouldBe(Path.Combine(_root, "ivanov", "labels.db"));
        File.Exists(buffer.DatabasePath).ShouldBeTrue();
        buffer.Count().ShouldBe(0);
    }

    [Fact]
    public void Буферы_разных_учёток_не_смешиваются()
    {
        var first = _factory.For("ivanov");
        var second = _factory.For("petrov");

        first.ReplaceAll([new LabelRecord { ExternalId = "A", ProductCode = "A-1" }]);
        second.ReplaceAll([
            new LabelRecord { ExternalId = "B", ProductCode = "B-1" },
            new LabelRecord { ExternalId = "C", ProductCode = "B-2" }
        ]);

        first.Count().ShouldBe(1);
        second.Count().ShouldBe(2);

        // Полная перезапись одного не должна задевать другого — ровно та проблема,
        // из-за которой и вводилось разделение по учёткам.
        first.ReplaceAll([new LabelRecord { ExternalId = "D", ProductCode = "A-2" }]);

        first.GetAll().ShouldHaveSingleItem().ProductCode.ShouldBe("A-2");
        second.Count().ShouldBe(2);
    }

    [Fact]
    public void Удаление_буфера_убирает_папку_с_файлом()
    {
        var buffer = _factory.For("ivanov");
        buffer.ReplaceAll([new LabelRecord { ExternalId = "A", ProductCode = "A-1" }]);

        var dir = Path.GetDirectoryName(buffer.DatabasePath)!;
        Directory.Exists(dir).ShouldBeTrue();

        _factory.Delete("ivanov");

        // Если бы не сброс пула соединений SQLite, файл остался бы открытым и папка
        // не удалилась бы — поэтому проверка именно на отсутствие каталога.
        Directory.Exists(dir).ShouldBeFalse();
    }

    [Fact]
    public void Удаление_несуществующего_буфера_не_падает()
    {
        _factory.Delete("neverexisted");
    }

    [Theory]
    [InlineData("../../GGA1C")]
    [InlineData("..")]
    [InlineData("имя")]
    [InlineData("with space")]
    [InlineData("")]
    public void Недопустимое_имя_папки_отвергается(string slug)
    {
        // Иначе значение увело бы запись за пределы корня буферов — туда, где лежат
        // Sync.db и ключ шифрования.
        Should.Throw<BusinessException>(() => _factory.For(slug));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // временная папка — если не удалилась, пусть её уберёт система
        }
    }
}
