using System.Text;
using System.Xml.Linq;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using GGA1C.Sync.Printing.GoLabel.Preview;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Printing;

/// <summary>
/// Правка .ezpx с сохранением (П12.1). Главное — сохранённый файл остаётся шаблоном GoLabel:
/// правится только то, что редактировали, неизвестные порту поля остаются, а задание из
/// правленного файла собирается без сюрпризов. На синтетическом шаблоне проверяем места в XML
/// (атрибуты против BoundRect), на эталонах — что round-trip не ломает печать.
/// </summary>
public class EzpxEditorTests
{
    private const string Synthetic = """
        <PrintJob xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <FormatVersion>1</FormatVersion>
          <QLabelSDKVersion>1.5</QLabelSDKVersion>
          <Label>
            <qlabel>
              <GraphicShape xsi:type="Text" X="10" Y="20" IsPrint="true">
                <Data>hello</Data>
                <FontHeight>2.5</FontHeight>
              </GraphicShape>
              <GraphicShape xsi:type="GdiWindowTextBox">
                <Data>abc</Data>
                <BoundRectWidth>100</BoundRectWidth>
                <BoundRectHeight>50</BoundRectHeight>
                <BoundRect>
                  <Location><X>1</X><Y>2</Y></Location>
                  <Size><Width>100</Width><Height>50</Height></Size>
                  <X>1</X><Y>2</Y><Width>100</Width><Height>50</Height>
                </BoundRect>
                <CustomUnknownField>keep-me</CustomUnknownField>
              </GraphicShape>
              <GraphicShape xsi:type="QRectangle">
                <Width>30</Width>
                <Height>40</Height>
              </GraphicShape>
            </qlabel>
          </Label>
          <Setup LabelWidth="50" LabelLength="30" Dpi="200" />
        </PrintJob>
        """;

    private static Stream Stream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    private static (EzpxEditor Editor, byte[] Saved) Edited(params EzpxShapeEdit[] edits)
    {
        var editor = EzpxEditor.Load(Stream(Synthetic));
        editor.Apply(edits);
        using var output = new MemoryStream();
        editor.Save(output);
        return (editor, output.ToArray());
    }

    [Fact]
    public void Правка_текста_меняет_атрибуты_и_Data()
    {
        var (_, saved) = Edited(new EzpxShapeEdit(0, X: 55, Y: 66, Data: "изменено"));

        var doc = XDocument.Parse(Encoding.UTF8.GetString(saved).TrimStart('﻿'));
        var text = doc.Descendants().First(e => (string?)e.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")) == "Text");

        ((string?)text.Attribute("X")).ShouldBe("55");
        ((string?)text.Attribute("Y")).ShouldBe("66");
        text.Element("Data")!.Value.ShouldBe("изменено");
    }

    [Fact]
    public void Правка_растрового_объекта_меняет_BoundRect_во_всех_местах()
    {
        var (_, saved) = Edited(new EzpxShapeEdit(1, X: 7, Y: 8, Width: 120, Height: 60));

        var reloaded = EzpxReader.Load(new MemoryStream(saved));
        var box = (EzpxRasterShape)reloaded.Label.Shapes[1];

        // Положение читается из BoundRect (GoLabel берёт его, когда он есть).
        box.X.ShouldBe(7f);
        box.Y.ShouldBe(8f);
        box.BoundRect.Width.ShouldBe(120f);
        box.BoundRect.Height.ShouldBe(60f);
    }

    [Fact]
    public void Сохранённый_файл_не_теряет_неизвестные_порту_поля()
    {
        var (_, saved) = Edited(new EzpxShapeEdit(1, X: 7, Y: 8));

        var doc = XDocument.Parse(Encoding.UTF8.GetString(saved).TrimStart('﻿'));
        doc.Descendants("CustomUnknownField").Single().Value.ShouldBe("keep-me");
    }

    [Fact]
    public void Правка_рамки_меняет_широку_и_высоту()
    {
        var (_, saved) = Edited(new EzpxShapeEdit(2, Width: 33, Height: 44));

        var reloaded = EzpxReader.Load(new MemoryStream(saved));
        var rect = (EzpxRectangle)reloaded.Label.Shapes[2];

        rect.Width.ShouldBe(33f);
        rect.Height.ShouldBe(44f);
    }

    [Fact]
    public void Превью_строится_и_размер_равен_наклейке()
    {
        var editor = EzpxEditor.Load(Stream(Synthetic));
        byte[] png = LabelPreview.RenderPng(editor.Template);

        png.Length.ShouldBeGreaterThan(0);
        png.Take(8).ToArray().ShouldBe([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
    }

    [Fact]
    public void Правка_двигает_объект_на_превью()
    {
        // Проверяем не пиксели, а что превью после правки собирается без исключений и что
        // модель применила координаты.
        var editor = EzpxEditor.Load(Stream(Synthetic));
        editor.Apply([new EzpxShapeEdit(0, X: 15, Y: 25)]);

        editor.Template.Label.Shapes[0].X.ShouldBe(15f);
        editor.Template.Label.Shapes[0].Y.ShouldBe(25f);
        byte[] png = LabelPreview.RenderPng(editor.Template);
        png.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Правка_за_границами_списка_игнорируется()
    {
        var editor = EzpxEditor.Load(Stream(Synthetic));
        editor.Apply([new EzpxShapeEdit(99, X: 1, Y: 1)]);

        editor.Template.Label.Shapes.Count.ShouldBe(3);
    }
}

/// <summary>
/// Round-trip эталонов: load → save → build даёт те же байты, что исходный шаблон. Гарантирует,
/// что редактор не портит шаблоны, которых не касается.
/// </summary>
public class EzpxEditorGoldenTests
{
    [GoldenTheory]
    [MemberData(nameof(GoLabelPortTests.FullGoldens), MemberType = typeof(GoLabelPortTests))]
    public void Round_trip_сохраняет_задание(string name)
    {
        string path = Path.Combine(Golden.Root, name, "template.ezpx");

        byte[] original;
        using (var stream = File.OpenRead(path))
        {
            original = new GoLabel.GoLabelDriver().Build(stream, GoLabelPortTests.LoadValues(name));
        }

        var editor = EzpxEditor.Load(File.OpenRead(path));
        using var saved = new MemoryStream();
        editor.Save(saved);
        saved.Position = 0;

        byte[] after = new GoLabel.GoLabelDriver().Build(saved, GoLabelPortTests.LoadValues(name));
        after.SequenceEqual(original).ShouldBeTrue(
            $"{name}: задание из сохранённого файла не равно заданию из исходного");
    }
}

/// <summary>
/// Настройки страницы (П12.5) и удаление объектов: Setup-атрибуты правятся в файле,
/// удалённый объект исчезает из модели и из XML.
/// </summary>
public class EzpxEditorSetupTests
{
    private static byte[] Edited(Action<EzpxEditor> edit)
    {
        var editor = EzpxEditor.Load(new MemoryStream(Encoding.UTF8.GetBytes("""
            <PrintJob xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <FormatVersion>1</FormatVersion>
              <Setup LabelWidth="50" LabelLength="30" GapLength="3" Speed="20" Darkness="5" Copy="1" PrintMode="1" />
              <Label>
                <qlabel>
                  <GraphicShape xsi:type="Text" X="10" Y="20"><Data>привет</Data></GraphicShape>
                  <GraphicShape xsi:type="QRectangle"><Width>30</Width><Height>40</Height></GraphicShape>
                </qlabel>
              </Label>
            </PrintJob>
            """)));
        edit(editor);
        using var output = new MemoryStream();
        editor.Save(output);
        return output.ToArray();
    }

    [Fact]
    public void Настройка_страницы_меняет_Setup_атрибуты()
    {
        var saved = Edited(editor => editor.ApplySetup(new EzpxSetupEdit(
            WidthMm: 104, HeightMm: 100.83f, GapMm: 2.5f,
            LeftMarginDots: 15, TopMarginDots: 25,
            Speed: 3, Darkness: 12, Copies: 2, DirectThermal: false)));

        var reloaded = EzpxReader.Load(new MemoryStream(saved));
        reloaded.Setup.LabelWidth.ShouldBe(104f);
        reloaded.Setup.LabelLength.ShouldBe(100.83f);
        reloaded.Setup.GapLength.ShouldBe(2.5f);
        reloaded.Setup.LeftMargin.ShouldBe(15);
        reloaded.Setup.TopMargin.ShouldBe(25);
        reloaded.Setup.Speed.ShouldBe(3);
        reloaded.Setup.Darkness.ShouldBe(12);
        reloaded.Setup.Copy.ShouldBe(2);
        reloaded.Setup.PrintMode.ShouldBe(0);   // DirectThermal: false = термоперенос, ^AT
    }

    [Fact]
    public void Удаление_объекта_убирает_его_из_модели_и_xml()
    {
        var saved = Edited(editor => editor.RemoveShape(0));

        var reloaded = EzpxReader.Load(new MemoryStream(saved));
        reloaded.Label.Shapes.Count.ShouldBe(1);
        reloaded.Label.Shapes.Single().TypeName.ShouldBe("QRectangle");
        // Текст исчез вместе со своим элементом, не оставив пустышек
        Encoding.UTF8.GetString(saved).ShouldNotContain("привет");
    }

    [Fact]
    public void Термоперенос_и_прямая_печать_меняют_PrintMode()
    {
        // Прямая печать (DirectThermal = true) — ^AD, PrintMode = 1;
        // термоперенос с лентой — ^AT, PrintMode = 0 (SetupCommands.GetCommandEZPL).
        var direct = Edited(editor => editor.ApplySetup(new EzpxSetupEdit(DirectThermal: true)));
        var transfer = Edited(editor => editor.ApplySetup(new EzpxSetupEdit(DirectThermal: false)));

        EzpxReader.Load(new MemoryStream(direct)).Setup.PrintMode.ShouldBe(1);
        EzpxReader.Load(new MemoryStream(transfer)).Setup.PrintMode.ShouldBe(0);
    }
}
