# Карта ядра GoLabel II (QLabelSDK)

Опись к задаче П3, составлена 2026-10-28. Источник — `C:\Program Files (x86)\GoDEX\GoLabel II`,
`QLabelSDK.dll` 1.5 (в `.ezpx` пишется `QLabelSDKVersion`). Решения модуля — `decisions.md`.

**Декомпилят лежит вне репозитория:** `C:\GGA1C\golabel-src\` (`QLabelSDK`, `QlabelDlg`,
`GlobalInfo`, `GoLabel` — проекты ILSpy). Пересобрать: `dotnet tool install -g ilspycmd`, затем
`ilspycmd.cmd -p -o <папка> <сборка>` (в Git Bash шим `ilspycmd` не находится — звать `.cmd`).
Ниже пути вида `QLabelSDK/Text.cs` — относительно `C:\GGA1C\golabel-src\QLabelSDK\`.

## Сборки

| Сборка | Файлов / строк | Что внутри | Для порта |
|---|---|---|---|
| `QLabelSDK.dll` | 273 / 69 тыс. | Модель наклейки, `.ezpx`, сборка EZPL и EPL, отрисовка предпросмотра, транспорт | **Источник порта** |
| `QlabelDlg.dll` | 520 / 163 тыс. | Диалоги WinForms; из полезного — загрузка объектов и шрифтов в принтер, опрос памяти (`~MDIR`) | Только как справка о поведении |
| `GoLabel.exe` | 152 / 80 тыс. | Редактор (WinForms), печать из командной строки (`QLabelNet/Program.cs`) | Справка; командная строка — для эталонов |
| `GlobalInfo.dll` | 71 / 12 тыс. | Имя программы, расширение `.ezpx`, справочники, RFID | Не нужен |

Все четыре — .NET Framework 4.6.2, **x86** (`PlatformTarget`), без обфускации.

## Формат `.ezpx`

Обычный `XmlSerializer` над классом `PrintJob` (`QLabelSDK/ObjectXML.cs`,
`QLabelTools.XMLSave/XMLLoad`). Проверено на образцах `C:\Users\Public\Documents\Godex\GoLabel II\EZPX\`:

```xml
<PrintJob>
  <QLabelSDKVersion>1.0.0.8</QLabelSDKVersion>
  <Label>                       <!-- QLabel -->
    <SerialFormat>…</SerialFormat>      <!-- счётчики C00…, 50 ячеек -->
    <VariableFormat>…</VariableFormat>  <!-- переменные V00…, 100 ячеек -->
    <qlabel>
      <GraphicShape xsi:type="WindowText">…</GraphicShape>
      <GraphicShape xsi:type="QRectangle">…</GraphicShape>
```

- Объекты наклейки — список `GraphicShape` с `xsi:type` = имя класса. Значит, порт читает `.ezpx`
  тем же `XmlSerializer`, если имена классов и свойств сохранены (`[XmlInclude]`-списки — в
  `QLabel.cs` и `Setup.cs`).
- Зашифровано только поле `Label.Password` (флаг `EncryptPwd`): AES, ключ — SHA-256 от `GoDEX_SW`,
  IV — MD5 от `GoLabel` (`QLabelSDK/clsAES.cs`). Шаблонам без пароля это не нужно.
  `ObjectXML.EncryptSave/EncryptLoad` (весь файл в AES) GoLabel для `.ezpx` не использует.
- При загрузке `XMLLoad` правит старые файлы (`FormatVersion < 1`: переменные 30–38 переезжают в
  90–98) и разворачивает связи объектов (`ObjectLinkTransfer`) — это переносить вместе с чтением.
- `.ezp` — старый двоичный формат прежнего GoLabel; конвертирует его `GoLabel.exe`. **Не берём.**

## Как собирается задание (EZPL)

Точка входа — `PrintJob.Print()` (`QLabelSDK/PrintJob.cs:607`). Команды **не собираются в буфер
целиком**, а пишутся в порт по мере обхода, строка за строкой с `\r\n`:

1. `BeginJobCMDs` — команды «в начало задания» из настроек принтера (`PrinterSetup.cs` в QlabelDlg).
2. `Setup.GetCommandEZPL()` (`QLabelSDK/Setup.cs:322`) — шапка: `^Q`/`^QD` (длина, зазор, метка),
   `^W` (ширина), `^H` (плотность), `^P`/`^PI`, `^S` (скорость), `^AT`/`^AD` (термоперенос /
   прямая), `^C` (копии), `^R`, `~Q` (смещения), `^O` (отделитель), `^D`/`^Db` (отрез), `^E`, `~R`
   (поворот), `^RW`/`^RS` (RFID).
3. На каждую наклейку — `QLabel.PrintNormalCommandEZPL(Layout, Setup)` (`QLabel.cs:3027`):
   `^L` (+ `^XSET,DRAWMODE`), `D…`/`T…` (формат даты/времени), значения переменных и счётчиков
   (`UpdateVariableFormatAll`, `C00,…`), затем **объекты в обратном порядке списка**
   `GraphicShape.PrintMyToString(x, y)` и `E` в конце. Раскладка нескольких наклеек в ряд —
   `Layout` (`HorAcross × VerAcross`).
4. `EndJobCMDs`, отрез (`Set_Full_Cut`), закрытие порта.

Есть и «бумажная» версия той же логики — `QLabel.GetNormalCommandEZPL()` (`QLabel.cs:2248`)
собирает строки в `commands` для показа и записи в файл; объекты-картинки там дают пустую строку.
Печать из базы (`PrintDBaseCommandEZPL`, `QLabel.cs:3473`) — отдельная ветка на ~650 строк.

### Объекты: команда или картинка

Это главное для побайтовых тестов: часть объектов уходит **командой принтера**, часть GoLabel
**рисует сам** (GDI или Neodynamic) и шлёт картинкой `Q`/`QA` (`SendBitmapToPrinterEZPL`,
`PrintJob.cs:2148`: заголовок `Q<x>,<y>,<байт в строке>,<высота>`, затем 1-битный растр;
при `QLabel.bCompress` — `QA` и zlib).

| Класс (`xsi:type`) | Как печатается | Заметки |
|---|---|---|
| `Text` — «текст принтера» | **Команда**: встроенный шрифт `A<id>,…`; TrueType в принтере `AT<id>,x,y,w,h,…[B][T][S][U][I][E][M][P2/P3],<кодировка>,0,<данные>`; загруженный растровый `V<id>,…` | `Text.cs:3765`. Флаг **`E` = данные в UTF-8**. Это путь для русских шрифтов, загруженных в принтер |
| `WindowText`, `WindowTextBox`, `GdiWindowTextBox`, `RichTextImage*`, `WinWordArt`, `CurvedText` — шрифт Windows | **Картинка**: GDI рисует текст, `AnyBitmapTo1Bits`, поворот, `Q` | `WindowText.cs:1252`. Побайтовое совпадение требует того же растеризатора (GDI на Windows) и тех же шрифтов в системе |
| `BarCode` (1D) | **Команда**, если подпись встроенным шрифтом и не `GraphicMode`; иначе **картинка Neodynamic** (и всегда для `Code128CheckDigit`) | `BarCode.cs:3231`. Картинки Neodynamic мы не повторим — такие шаблоны перевести в режим команды |
| `QRCode`, `DataMatrix`, `Pdf417`, `MicroPdf417`, `Aztec`, `MaxiCode`, `GS1DataBar`, `GS1Composite`, `DotCode`, `HanXin`, `CodaBlockF`, `TLC39`, `HIBC*` | Как правило **команда** (`W…` у QR); у части есть ветка картинкой через Neodynamic | QR: `QRCode.cs:590`. В предпросмотре все рисует Neodynamic → у нас ZXing.Net |
| `QRectangle`, `Line`, `TableForm` | **Команда** (`R…`, `L…`) | |
| `Ellipse`, `RoundRect`, `ObliqueLine`, `VarShape` | **Картинка** | |
| `Image` | Ссылка на загруженную в принтер картинку (`Y…`) или **картинка** `Q` | `Image.cs:787` |
| `RFIDCode` | Команда | Не нужен |

Длина данных у некоторых команд считается в байтах (`PrintJob.ByteLen`: символ ≥ 256 → 2 байта),
а отправка всегда в UTF-8 (`SendStringToPrinter`) — для кириллицы это расходится. Как ведёт себя
принтер, покажут эталоны П2.

### Переменные, счётчики, дата

- Переменные `V00…V99`: формат в `Label.VariableFormat`, подстановка `^V00` в данных объекта.
  Обработка — `QLabelSDK.Variable/*`, `QLabel.UpdateVariableFormat*` (`QLabel.cs:5125–6276`):
  обрезка, выравнивание, формат чисел, вычисления (`QLabelSDK.VariableCal`).
- Счётчики `C00…`: `QLabel.UpdateSerialFormat` (`QLabel.cs:4905`).
- Дата/время — `MyTimeDate.cs`, команды `D`/`T`.
- Запрос значений у оператора — окна WinForms (`GlobalForm/FrmPrompt.cs` и др.). В хабе значения
  приходят из Grist — окна не переносим.

### Подводные камни для порта

- **Культура.** `Setup.GetCommandEZPL` вручную меняет десятичный разделитель на точку, а многие
  объекты склеивают `float` в строку без культуры (`QRCode.cs:620`: `x + _XInPixel`). На русской
  Windows дробная координата дала бы запятую. Порт — с явной культурой; эталоны снимать на машине с
  теми же региональными настройками, что у операторов.
- **Глобальное состояние.** `PrintJob.gQLabel`, `PrintJob.Dpi`, `PrintJob.printerLanguage`,
  транспорт `Printer` — статические; у `QLabel` 63 статических поля. В хабе печатают несколько
  человек сразу — в порту всё это становится полями экземпляра задания.
- **`MessageBox` внутри ядра** — 53 вызова (ошибки ввода-вывода, «Label Validator…»). В порту —
  исключения или результат.
- **System.Drawing** — в 84 файлах из 273: и для предпросмотра, и для объектов-картинок. В .NET 10
  `System.Drawing.Common` работает только на Windows — хабу подходит (он на Windows), но это новый
  NuGet-пакет: спросить в П5.
- **EPL** (второй язык GoDEX) идёт параллельными методами `…EPL` почти в каждом классе — не берём.

## Транспорт и `Trace.dll`

`QLabelSDK.PrinterControl/Printer.cs`: выбор по `CommunicationType`.

| Путь | Чем | Используется ли `Trace.dll` |
|---|---|---|
| `Network` (IP:порт, 9100) | `SocketSDK/SocketSDK.cs` — свой managed TCP-клиент | **Нет** |
| `USB`, `COM`, `LPT` | `Trace.dll`: `openport`/`closeport`, `OpenUSB`, `FindFirstUSB`/`FindNextUSB`, `sendbuf`, `RcvBuf`, `setbaudrate_CtsRts` | Да — как «труба» для байтов |
| `Driver` | `winspool.drv`: `OpenPrinter`/`WritePrinter` (`API_Driver.cs`) | Нет |
| `PrintToFile` | `MemoryStream` (`Printer_ToFile`) → `Printer.TempPrintBuf` | Нет |
| `BLE` | `LibBLE.dll` | Нет |

Плюс `EnableLog`, `SetLogPath`, `SetIoErrorMode` из `Trace.dll` вызываются при каждом открытии
порта. Из 79 экспортов `Trace.dll` (есть `OpenNet`, `ecTextOut`, `Bar_QRcode`, `putimage`…)
**QLabelSDK объявляет только эти 11** (`API_EZio.cs`): команды EZPL `Trace.dll` для GoLabel не строит.

**Что это значит для П4.** Байты, прошедшие через `Trace.dll`, — те же байты, что собрал
QLabelSDK; посредник над `Trace.dll` проверяет транспорт, а не сборку команд. А при печати по
Ethernet GoLabel `Trace.dll` вообще не трогает. Эталоном для порта служит сам QLabelSDK — через
GoLabel (П2) или, если понадобится, через x86-процесс, который загружает оригинальный
`QLabelSDK.dll` и печатает в `PrintToFile` (не проверено: ядро зовёт `MessageBox` и опирается на
статику, которую обычно готовит `GoLabel.exe`). Пересмотреть карточку П4 до начала работ.

## Печать из командной строки — для эталонов П2

`GoLabel.exe -f <файл.ezpx> -i 127.0.0.1:9100` печатает по сети без редактора
(`QLabelNet/Program.cs`, справка — `CmdlineHelp.txt` рядом с `GoLabel.exe`). Есть `-V00 <значение>`
для переменных, `-S0 <значение>` для счётчиков, `-c` копии, `-dark`, `-speed`, а
`-view <файл> -save <bmp>` сохраняет предпросмотр картинкой — эталон для предпросмотра в хабе.

Проверено 2026-10-28 на `Sample2.ezpx` из установки и слушателе на `127.0.0.1:9100`:

- без значений GoLabel **остановился на окне «Serial Number Prompt»** и ничего не отправил —
  в образце счётчик с запросом;
- с `-S0 100 -V00 TEST` — отработал без окон, вышел с кодом 0, пришло 312 байт:

```
^XSETCUT,DOUBLECUT,0      ← команда «в начало задания»
^Q100,3  ^W102  ^H5  ^P1  ^S4  ^AD  ^C1  ^R0  ~Q+0  ^O0  ^D0  ^E15  ~R200
^L
Dy2-me-dd  Th:m:s
R120,104,696,546,12,12  R138,122,680,530,5,5
BA,182,192,2,5,88,0,1,mmm33100816
Lo,142,340,677,344
AF,299,136,1,1,0,0E,product ID
AF,231,465,1,1,0,0E,TEST
AF,297,396,1,1,0,0E,     100
E
```

(каждая команда — отдельная строка с `\r\n`). То есть эталоны П2 снимаются скриптом: каждому
шаблону — значения всех переменных и счётчиков в командной строке. Флаг `E` у текста стоит и
здесь — кодировку кириллицы покажет шаблон с русским текстом.

## Шрифты в принтере

Загрузка и опрос — в `QlabelDlg/LibView/FrmObjectDownload.cs`, `FrmObjectMaintain.cs`:
`~MDIR` — список объектов в памяти принтера (ответ с `KB free`), `~MDELF,<имя>` — удалить шрифт,
`~EB,<имя>,<размер>` — загрузить картинку, `~MDELD`/`~L,DBASE` — базы. Отсюда хаб может сам
спрашивать принтер, какие шрифты в нём есть, вместо ручного справочника — проверить в П6.

## Что портируем

| Часть | Файлы | Решение |
|---|---|---|
| Модель и `.ezpx` | `PrintJob`, `QLabel` (свойства и загрузка), `Setup`, `Layout`, `GraphicShape`, `Graphic`, классы объектов, `ObjectXML`, `QLabelTools.XMLLoad`, `clsAES` | **Портировать**, имена для `XmlSerializer` не менять |
| Сборка EZPL | `Setup.GetCommandEZPL`, `QLabel.PrintNormalCommandEZPL`, `MyToStringEZPL`/`PrintMyToString` объектов, `SendBitmapToPrinterEZPL`, zlib | **Портировать**, сначала то, что встречается в наших шаблонах (П2) |
| Переменные, счётчики, дата | `QLabelSDK.Variable`, `QLabelSDK.VariableCal`, `MyTimeDate`, `CheckDigit`, `clsFormat` | **Портировать** |
| Объекты-картинки | `WindowText*`, `RichTextImage*`, `Ellipse`, `RoundRect`, `ImageAccess` (1 бит, поворот) | **Портировать, если есть в шаблонах**, на System.Drawing (Windows) |
| Штрихкоды картинкой | всё через `Neodynamic.SDK.Barcode` | **Не портировать**; предпросмотр — ZXing.Net, печать — командой |
| Транспорт | `SocketSDK`, `Printer` | Заменить своим TCP 9100 (П6); логику ожидания ответа (`QueryReplyData`, `CheckPortReply`) — посмотреть при П6 |
| Печать из баз | `QLabel.LoadOleDb*`, `LoadMySql`, `PrintDBaseCommand*` | **Не портировать**: данные даёт хаб |
| EPL | методы `…EPL` | Не портировать |
| Интерфейс | `QLabelSDK.Component`, `GlobalForm`, `QLabelSDK.GlobalForm`, `QLabelSDK.Command` (undo/redo), ручки и курсоры в `GraphicShape`/`Graphic` | Не портировать; undo/redo — образец для веб-редактора П12 |
| Прочее | `ClipperLib`, `MyWordArt` (фигурный текст), весы (`ScaleSetup`, `TCPPortCtrl`), RFID, камера ECHO, BLE | Не портировать |

## Объём

Вся «модель + команды + переменные» в декомпиляте — около **35 тыс. строк**; интерфейс — 5 тыс.,
транспорт — 3 тыс., Clipper и WordArt — 5 тыс. Если взять только EZPL, без баз, EPL, ручек
редактора и Neodynamic, переносить порядка **10–15 тыс. строк** декомпилята (оценка по размерам
файлов, не по построчному разбору). Порядок работ для П5:

1. Модель + чтение `.ezpx` + `Setup` + `Text`, `QRectangle`, `Line`, штрихкоды командой — первые
   эталоны совпадают побайтово.
2. Переменные и счётчики — шаблоны с данными из Grist.
3. Объекты-картинки (`WindowText` и др.) — только если они есть в наших шаблонах.

Точный список классов даст П2: какие `xsi:type` встречаются в настоящих шаблонах.
