# П5. Порт ядра QLabelSDK на .NET 10 — разведка и план

Разведка 2026-10-29; код — с того же дня (что готово — «Ход реализации» ниже). Источник — декомпилят `C:\GGA1C\golabel-src\`
(вне git, только чтение), эталоны `C:\GGA1C\golden\<имя>\` (14 шаблонов пользователя + `sample2`).
Пути вида `QLabelSDK/X.cs:N` и просто `X.cs:N` — это `C:\GGA1C\golabel-src\QLabelSDK\QLabelSDK\X.cs`,
пути с `GoLabel/` и `GlobalInfo/` — от `C:\GGA1C\golabel-src\`. Опыты с отрисовкой делались одноразовыми скриптами во временной папке
вне репозитория (PowerShell 5.1 = .NET Framework, и консоль `net10.0-windows`), ниже — их итог.

## Главный вывод

**Растр `Q` повторяется на .NET 10 до пикселя, если задать режим сглаживания текста явно.**
Проверено на двух эталонах:

| Что | Чем рисует GoLabel | Опыт на .NET 10 | Итог |
|---|---|---|---|
| Подпись Code39 (`gkv1-vm-box`, «4021110124193», GlavkonGOST 24 B) | **Neodynamic** сам (`GetBarcodeImage`); судя по опыту, внутри — GDI+ `DrawString` | `TextRenderingHint.AntiAlias` + `StringFormat.GenericTypographic`, 96 dpi, порог G ≤ 192 | **0 расхождений** (164×22 точки) |
| `GdiWindowTextBox` (`gkv3-box`, «Лаборатория», Impact 12 → 38 pt) | GDI+ `DrawString` **без явного режима** (`SystemDefault`) | `ClearTypeGridFit` явно | **0 расхождений** (287×45) |
| то же | | `SystemDefault` на .NET 10 | **602 точки мимо** — `SystemDefault` в процессе .NET 10 даёт не то, что в GoLabel |
| `Image` (`gkv1-vm-box`, PNG → 135.88×59.56) | GDI+ `DrawImage` бикубикой, порог яркости 127.5, затем G ≤ 192 | тот же конвейер | **6 точек из 8 024 мимо**; x86 и x64 GDI+ дают одно и то же — расхождение в моём упрощении загрузки (`Image.ReDraw`/`SetRectangle`), не в GDI+ (не проверено до конца) |

Отсюда:

1. **System.Drawing (GDI+) на .NET 10 рисует так же, как в GoLabel** (на двух опытах; объяснение — один и тот же `gdiplus.dll`
   Windows, не проверено на всех объектах). Хаб на Windows Server — подходит.
2. **`SystemDefault` использовать нельзя.** GoLabel его не задаёт (в декомпиляте нет ни одного
   `TextRenderingHint`), поэтому текст шрифтом Windows зависит от настроек сглаживания сеанса, где
   запущен GoLabel. На стенде включён ClearType (`HKCU\Control Panel\Desktop`: `FontSmoothing=2`,
   `FontSmoothingType=2`) — эталоны сняты с ним. Порт задаёт `ClearTypeGridFit` явно для
   `WindowText*`/`GdiWindowTextBox` и `AntiAlias` для подписи штрихкода (так выходит у Neodynamic по опыту).
3. **Neodynamic не нужен.** Полосы Code39 у нас — чистая геометрия: узкий 1 точка, широкий 5,
   промежуток 1 (`gkv1-vm-box`: 15 знаков × 22 − 1 = 329 = `BoundRectWidth`). Свой кодер Code39
   (таблица на 44 знака) даёт те же полосы; ZXing.Net для печати **не годится** — у него отношение
   широкий:узкий фиксировано 2:1 (по памяти об исходниках `Code39Writer`, в этой сессии не проверено). ZXing — только для предпросмотра в редакторе (П12).
   Остаётся воспроизвести раскладку Neodynamic: где подпись относительно полос (`TopAndLeft`,
   `BottomAndCenter`, `Offset`), ширина картинки. Это выводится из 10 эталонов со штрихкодами —
   **отдельный риск шага 4**.

**Где можно не совпасть до байта** (каждое — со способом сверки):

| Риск | Почему | Как ловить |
|---|---|---|
| Раскладка подписи Neodynamic | закрытая библиотека; знаем только результат | подбор по 10 растрам; если не сойдётся — вопрос 4 ниже |
| Загрузка `Image` (6 точек) | дробный `BoundRect` после `ReDraw`/`SetRectangle` меняет выборку бикубики | шаг 3: повторить порядок загрузки `Image.cs:571–640` дословно |
| Сглаживание на рабочих местах | GoLabel у оператора может работать без ClearType или с масштабом 125 % — тогда **сам GoLabel** даёт другие байты, чем эталон | вопрос 3; порт фиксирует настройки стенда |
| Шрифты у учётки хаба | `GlavkonGOST.ttf` установлен **только в профиль `Administrator`** (`HKCU\…\Fonts`); `impact.ttf` — в `C:\Windows\Fonts`. Служба под другой учёткой GlavkonGOST не увидит | **снято 2026-10-29:** шрифт установлен для всех (решение 2); прогон под другой учёткой — шаг 7 |
| Дробная арифметика | GoLabel — x86 .NET Framework (x87), порт — x64 SSE; пороги `>= 127.5` и `(int)` от `float` | **проявилось на шаге 3**: бикубика 64-битного `gdiplus.dll` расходится с 32-битным на 1–13 точек логотипа («Ход реализации») |
| Дата ПК в растре | `DateTime.Now` прямо в `MyTimeDate.cs:132–313`; 5 эталонов нарисованы с «26» | в порту — внедряемые часы (`TimeProvider`), тест задаёт дату съёма 2026-10-28 |

**Запасной критерий** (только для диагностики, не вместо требования): тест при несовпадении
раскладывает оба задания на команды и растры, по каждому `Q` пишет число расходящихся точек и их
координаты. Зачёт — только побайтовое равенство `golabel.bin`.

## Решения пользователя (2026-10-29)

1. **NuGet `System.Drawing.Common` — разрешён.** Сборка автономная: все пакеты входят в вывод
   публикации, на целевой машине ничего не доставляется. Версия прибита точно (`10.0.13`, как пины
   в `common.props`).
2. **Шрифты — для всех пользователей.** GlavkonGOST установлен в `C:\Windows\Fonts\GlavkonGOST.ttf`
   + `HKLM\…\Fonts` «GlavkonGOST Type A (TrueType)» (хеш совпадает с профильной копией). Impact уже там.
3. **ClearType, масштаб, «толщина шрифта» на рабочих местах неизвестны** — определить по коду
   GoLabel и сравнению с эталонами; в порту режим сглаживания задаём явно. Вывод — ниже.
4. **Декомпилировать `Neodynamic.SDK.Barcode.dll` как справку о раскладке подписи — разрешено.**
   Декомпилят — вне репозитория (`C:\GGA1C\golabel-src\Neodynamic\`), код не копируем, кодер свой.
5. **`gkv3-box-var-mysql`:** 4 тестовые строки `a.morozov@test.local` из `my_labels` стенда снять
   в `data.csv` папки эталона (вне git) — разрешено, читать учёткой приложения (пароль —
   `C:\GGA1C\mysql-app.txt`, в вывод не печатать).
6. **Логотипы — вариант «В»: допуск, без x86-помощника и без перевода хаба в x86.** Правило
   эталонных тестов: допуск **только** для растров `Q` из объектов `Image` — не больше 16 точек
   мимо на картинку и не больше 0,5 % её точек; всё остальное строго байт в байт (команды,
   заголовки `Q`, текст шрифтами Windows, Code39, эллипсы, линии). Тест пишет по каждой картинке
   фактическое число точек мимо; эталоны без расхождений остаются «байт в байт».

### Что GoLabel берёт из системы (вопрос 3)

По коду (декомпилят): `TextRenderingHint` не задаётся нигде → текст шрифтом Windows рисуется
сглаживанием сеанса (`SystemDefault`; на стенде — ClearType). «Толщина шрифта» — `FontThick` из
`user.config` GoLabel (по умолчанию 0 → порог G ≤ 192) — меняет порог чёрной точки у **всех**
растров. `bUseRealDPI` («точность» мм → точки: 12 или 11.8 точки/мм при 300 dpi) — тоже из
`user.config`, по умолчанию `false`; на стенде обоих значений нет. Масштаб экрана: ядро рисует в
`Bitmap` (96 dpi у `new Bitmap`), DPI-awareness в командном режиме не проверена.

**Что это значит для печати с других машин:** порт печатает одинаково везде — настройки стенда
прибиты (ClearType там, где GoLabel берёт сглаживание сеанса; `FontThick` = 0; `bUseRealDPI` =
false). Сам GoLabel на рабочем месте с выключенным ClearType, другой «толщиной» или «точностью»
выдаст **другие** байты, чем эталон стенда, — такое расхождение «хаб против GoLabel оператора»
не ошибка порта. На живом рабочем месте не проверено.

## Ход реализации

| Шаг | Состояние (2026-10-29) | Эталоны байт в байт |
|---|---|---|
| 1 | готов: `Printing.Core` (`PrintValues`, `IPrinterDriver`), `Printing.GoLabel` (`EzpxReader` — разбор через `XDocument`, а не XmlSerializer: нужно меньше десятка классов из ~80; правки `InitLabel`) | шапка — **15 из 15** |
| 2 | готов: цикл наклейки, две в ряд, счётчики `-S`, переменные `-V`, `Text` (`AT`/`A`/`V`), `QRectangle`, `Line`, штрихкод командой; ветка `PrintNornal` — отказ (в эталонах её нет) | `sample2`; команды при замаскированных `Q` — **14 из 14** (кроме `gkv3-box-var-mysql`) |
| 3 | частично: `Raster1Bit`, `Image`, `Ellipse` (System.Drawing.Common 10.0.13) | + `gkv3-vm-product` |
| 4 | готов для Code39 без поворота, подпись `TopAndLeft`/`BottomAndCenter`; дата ПК в данных штрихкода — шаг 5 | + `gkv1-vm-box`, `gkv3-vm-box` (итого **4 из 15**) |
| 5 | готов: `GdiWindowTextBox` (перенос `WrapText`, межстрочный, подбор размера `Cal_FontSize`, `BoundingAlignment`), `WindowTextBox`, дата ПК в данных (`^D%y`, `^D±Y/M/Hnnnn`) от `TimeProvider` | с допуском `Image`: **12 из 14** (кроме печати из базы); не проходят 2 — строка Impact |
| 6–7 | не начаты | — |

**Шаг 5 — найдено.** (1) `WindowTextBox` перед печатью рисуется на экран (`Draw` из
`LinkedVar.UpdateLinkVariableData`) при `QLabel.ZoomRate = 0.1` — в командном режиме GoLabel его не
меняет — и ширина рамки становится `(int)(W · 0.1) / 0.1` (`gkv-avto-product`: 428 → 420, растр на
байт уже). (2) Объект без атрибута `X`/`Y` (`[DefaultValue(0)]`) берёт положение из `BoundRect`
(`gkv3-box`: `GdiWindowTextBox` с x = 0). (3) Текст `GlavkonGOST` всеми блоками и подписи Code39 с
датой — 0 точек мимо. (4) **Строка `Impact` 11 pt («Лаборатория Микроприборов», `gkv-avto-product`,
`gkv-avto-product-ru-korpus`) — 5888 точек мимо, и это снова разрядность `gdiplus.dll`:** PowerShell
x86 рисует её как эталон (14868 чёрных точек, столбцы 0–597), x64 — как порт (14410, 0–595); от
режима сглаживания не зависит, на 64 битах совпадает при размере +0.025 pt. Допуск решения 6 —
только для `Image`, поэтому эти 2 эталона зачёт не проходят (тест-страховка сверяет у них всё,
кроме этой строки). **Вопрос пользователю:** допуск для текста или иной путь.


**Шаг 4 — раскладка Code39 (выведена по 11 растрам эталонов, декомпилят Neodynamic — только
справка, он обфусцирован).** Ширина = сумма модулей Code39 (`*данные*`, узкий = `Narrow`, широкий =
`Width`, промежуток между знаками — узкий, контрольного знака нет). Высота растра =
`round(Height + Offset + h)`, где `h` — высота строки шрифта подписи (`FontCmd`) при 96 dpi
(`em · LineSpacing / EmHeight`); полосы — `ceil(Height)` точек без сглаживания. Подпись — GDI+
`AntiAlias` + `GenericTypographic`: `TopAndLeft` — в (0, 0), полосы снизу; `BottomAndCenter` — полосы
сверху, подпись по центру (ширина `MeasureString`) на `Height + Offset`. Совпало: `gkv1-vm-box`,
`gkv3-vm-box` целиком, штрихкод `gkv3-vm-box-102x152` (подпись снизу) — 0 точек мимо.

**Что осталось по шагам 6–7.** 6: печать из базы
(`Print(records)`), снимок `data.csv` для `gkv3-box-var-mysql` ещё не снят. 7: обвязка для хаба.

**Шаг строки многострочного `Text`** GoLabel берёт из `Font.Height` шрифта Windows из `FontCmd`
(не из принтера): `GlavkonGOST Type A` Bold 29 px → 37, 25 px → 32. Порт меряет тем же GDI+.

**Шаг 3 — найдено.** (1) Перед печатью GoLabel рисует `Image` на экран (`Image.Draw` из
`LinkedVar.UpdateLinkVariableData`), а там `CurrentImage.MakeTransparent(White)`: белые точки
картинки становятся прозрачными, и бикубика на краях даёт другой результат. С этим «6 точек мимо»
из разведки уходят. (2) **Остаток — разрядность `gdiplus.dll`:** уменьшение логотипа бикубикой в
64-битном GDI+ даёт 1–13 точек мимо на 9 картинках из 30, в 32-битном (GoLabel — x86) — 0
(PowerShell x86 и x64 на `gkv1-oem-product`, `Q136,124`: x86 — 0, x64 — 4). Эллипсы сходятся
везде. **Нужно решение пользователя:** рисовать `Image` в 32-битном процессе-помощнике
(самодостаточный x86-исполняемый файл в составе сборки) — или принять расхождение в несколько
точек логотипа.

## 1. Путь «.ezpx → байты» в декомпиляте

Эталоны сняты командой `GoLabel.exe -f … -i 127.0.0.1:<порт> -S0 …`. Этот путь:

| Шаг | Где | Что важно для порта |
|---|---|---|
| Разбор ключей, загрузка | `GoLabel/QLabelNet/Program.cs:569–583` → `GoLabel/DrawTools/DrawArea.cs:1136` `LoadFromXml` → `GlobalInfo/GlobalInfo/GlobalPrint.cs:1246` `InitLabel` → `QLabelSDK/QLabelTools.cs:50` `XMLLoad` | `InitLabel` дополняет `SerialFormat`/`VariableFormat` до 50/100 ячеек, правит переносы строк у `Text`, переводит единицы `Setup` |
| Порог «толщины шрифта» | `Program.cs:759` `Graphic.FontThick = Settings.Default.FontThick` | в `user.config` стенда значения нет → по умолчанию `0` (`GoLabel/QLabelNet.Properties/Settings.cs:1775`) → `FontThick_Mode.Default` |
| Значения `-S`/`-V` | `GlobalPrint.cs:782` `CheckSerialPrompt` (дополняет пробелами слева до длины формата), `:709` `CheckVariablePrompt` | окна запроса не переносим — значения даёт хаб |
| Выкидывание пустых объектов | `GlobalPrint.cs:392–571` `PrintJob_Execute` (удаление — `:570`) | объект нулевого размера без `^` в данных удаляется до печати |
| Выбор ветки | `GlobalPrint.cs:1168` `RunPrintJobThread`: источник данных → `PrintJob.Print(records)`; иначе `GlobalInfo/GlobalInfo/GlobalInfo.cs:3661` `UseNormalPrint` → `PrintNornal()` или `Print()` | по условиям (`HorAcross>1`, счётчики в тексте шрифтом Windows, подписи) наши шаблоны идут в `Print()` — **не проверено**, подтвердить на шаге 2 |
| Подготовка | `QLabelSDK/PrintJob.cs:301` `BeforePrintSetup` | при `DrawMode≠0` переносит `Image`/`Ellipse`/… в начало списка; ставит `^XSETCUT,DOUBLECUT,…` в команды начала задания (`:332`) |
| Шапка | `PrintJob.cs:607` `Print()` → `BeginJobCMDs`, затем `QLabelSDK/Setup.cs:322` `GetCommandEZPL` | `^Q`, `^W`, `^H`, `^P`, `^S`, `^AT`/`^AD`, `^C`, `^R`, `~Q`, `^O`, `^D`, `^E`, `~R` — у всех 15 эталонов |
| Наклейка | `QLabelSDK/QLabel.cs:3027` `PrintNormalCommandEZPL(lo, st)` | `^L` (`:4358` `SendBeginPage`), `D…`/`T…`, цикл по `HorAcross × VerAcross`, **объекты в обратном порядке**, пропуск вне страницы (`:6651` `OutOfPageArea` — в `gkv-avto-product`, по-видимому, так отпадает `Image` с Y=597: в байтах 3 картинки из 4), `E` (`:4406`) |
| Две в ряд | тот же цикл, смещение `lo[i].X/Y`; счётчик `+1` после каждой (`:5114` `UpdateSerialFormatAll`) | `gkv1-oem-product`: `^D086` и `^D087` |
| Отправка строки | `PrintJob.cs:2045` `SendStringToPrinter` — всегда UTF-8 | русский текст — UTF-8 как есть |
| Отправка растра | `PrintJob.cs:2086` → `:2148` `SendBitmapToPrinterEZPL` | заголовок `Q<x>,<y>,<байт>,<высота>\n` (`:2194`), хвост строки добивается единицами (`:2207`), инверсия `^0xFF` (вызывают с `Invese=1`), затем `\r\n`; `QA`+zlib только при `QLabel.bCompress` — в эталонах нет |
| Из базы | `PrintJob.cs:905` `Print(records)` → `QLabel.cs:3473` `PrintDBaseCommandEZPL`; чтение MySQL — `QLabel.cs:7141` `LoadMySql` | `gkv3-box-var-mysql`: 4 записи → 4 раза `^C1`, `^L`…`E` |

**Объекты, которые реально встречаются** (15 шаблонов):

| `xsi:type` | В каких | Путь в байты | Где |
|---|---|---|---|
| `Text` | `gkv1-*`, `gkv3-vm-box`, `gkv3-vm-product`, `gkv3-product`, `gkv3-box-var-mysql`, `sample2` | команда `ATA,…` (TrueType в принтере, флаг `E` = UTF-8) или `A…` | `QLabelSDK/Text.cs:3633` `MyToStringEZPL`, `:3960` `PrintMyToStringEZPL` |
| `BarCode` (Code39, подпись шрифтом Windows) | 10 шаблонов | **растр**: `bBuiltinFont=false` → `PrintBmpBarcode` | `BarCode.cs:3240` → `:3261` `PrintBmpBarcode` → `:1841` `Render2Bmp` → `:2075` `getBarcode`: подпись помещается под полосы → `return null` → `myBarcode.GetBarcodeImage(dpi)` (`:1934`) — **всё рисует Neodynamic** |
| `BarCode` встроенным шрифтом | `sample2` | команда `BA,…` | `BarCode.cs:3061` |
| `GdiWindowTextBox` | `gkv3-box`, `gkv3-vm-box-102x152`, `gkv-avto-*` | растр | `GdiWindowTextBox.cs:2093` → `:887` `GetRender2Bmp` → `:748` `myDraw2BmpTTFont`: на Windows 10+ — GDI+ `DrawString` (`:685` `myDrawString`), шрифт `Size × Dpi / 96 + 0.5` pt на картинке 96 dpi, `GenericTypographic`, высота строки `Font.Height` |
| `WindowTextBox` | `gkv1-oem-box`, `gkv-avto-product`, `-ru-korpus` | растр, та же схема | `WindowTextBox.cs:1286`, `:583–585` |
| `Image` (`LoadToDevice=false`) | все 14 | растр | `Image.cs:787`: `DrawImage` бикубикой в `BoundRect`, `ImageAccess.DitherImage` (`ImageAccess.cs:332`, `Default` = порог яркости 127.5), `AnyBitmapTo1Bits` |
| `Ellipse` | 8 шаблонов | растр | `Ellipse.cs:473`, отправка `(x + rectangle.X, y + rectangle.Y)` |
| `QRectangle`, `Line` | `sample2` | команды `R…`, `L…` | |
| Счётчики `^C00`, дата `^D…`, поля `^F00` | везде / `gkv3-box-var-mysql` | подстановка | `Graphic.cs:1952` `FillPrintingDataEZPL`; дата ПК — `Graphic.cs:2467` → `MyTimeDate.ConvDateTimeToStrEZPL` (`DateTime.Now` в `MyTimeDate.cs`); у `Text` с `UsePrinterClock=true` остаётся `^D` |

Общее для всех растров — `ImageAccess.AnyBitmapTo1Bits` (`ImageAccess.cs:122`): ширина
добивается до кратной 8, при `FontThick=Default` чёрная точка — **зелёный канал ≤ 192** (`:146–156` выбор порога, далее цикл);
у других режимов порог 128 или 254 по всем трём каналам.

`^XSET,UNICODE` GoLabel добавляет только моделям `EZ…` (`GlobalPrint.cs:342` `bNeedSendUtf8Cmd`) —
ZX430i не из них, в эталонах его нет.

## 2. Растр: чем и как рисует GoLabel

- **Всё — GDI+ (`System.Drawing`)**, картинки 24 бит, разрешение картинки 96 dpi (`new Bitmap` берёт
  dpi экрана; на стенде 96). `GoLabel.exe` в манифесте DPI-awareness закомментирован
  (`GoLabel/app.manifest:52–58`), но в ядре есть `WinAPI.SetDPIAware` (`QLabelSDK/WinAPI.cs:21`, `SetDPIAware`) —
  вызывается ли он в командном режиме, **не проверено**. На машине с масштабом 125 % GoLabel может
  рисовать иначе.
- **Сглаживание текста** не задаётся нигде → `SystemDefault` (см. «Главный вывод»).
  `SmoothingMode.None`, `InterpolationMode.HighQualityBicubic` — `Graphic.cs:2736` `GetGraphics`.
- **Порог** — `AnyBitmapTo1Bits`, зелёный ≤ 192 (ClearType даёт цветную кайму — порог по одному
  каналу как раз под неё).
- **Поворот** — `RotateFlip`/`GraphicsRotation` (`BarCode.cs:3288–3315` → `ImageAccess.cs:437` `GraphicsRotation`, `ImageAccess.cs:235` `RotateImage`); в
  эталонах всё `Angle0`, кроме `Text` `3BE` в `gkv3-vm-product` (это команда, не растр).
- **Шрифты на стенде:** `C:\Windows\Fonts\impact.ttf` есть; `GlavkonGOST.ttf` — только
  `C:\Users\Administrator\AppData\Local\Microsoft\Windows\Fonts\` (реестр `HKCU`). Хаб под
  `Administrator` его видит, служба под другой учёткой — нет.
- **System.Drawing.Common** на .NET 10 работает только на Windows (хаб — Windows Server 2025). Опыт
  П5 собран с `<UseWindowsForms>` (без NuGet, рантайм `Microsoft.WindowsDesktop.App 10.0.8` на стенде
  есть). Для библиотеки сервиса — либо NuGet `System.Drawing.Common` 10.0.x, либо
  `FrameworkReference Microsoft.WindowsDesktop.App` + `net10.0-windows` (вопрос 1).

## 3. Структура кода

```
src/GGA1C.Sync.Printing.Core        net10.0, без System.Drawing
    Model/       LabelJob (готовое задание: шапка + наклейки), PrintValues (счётчики, переменные,
                 строки данных, часы), PrinterTarget
    IPrinterDriver      Build(LabelTemplate, PrintValues) -> byte[]   (EZPL сейчас, CAB — П14)
    IPrinterTransport   Send(byte[]) / Query(...)                     (TCP 9100 — П6)
src/GGA1C.Sync.Printing.GoLabel     net10.0 (+ System.Drawing, Windows)
    Ezpx/        классы для XmlSerializer с теми же именами и [XmlInclude], EzpxReader
                 (XmlReaderSettings { CheckCharacters = false }), правки XMLLoad/InitLabel
    Ezpl/        EzplJobBuilder (Print/PrintNornal/Print(records)), SetupCommands (GetCommandEZPL),
                 LabelLoop (PrintNormalCommandEZPL, Layout, OutOfPageArea), объекты -> команда/растр
    Raster/      RasterWriter (Q), Bitmap1Bit (AnyBitmapTo1Bits, DitherImage), TextRaster
                 (WindowText*, явный TextRenderingHint), Code39Raster (свой кодер + подпись как у
                 Neodynamic), ImageRaster, EllipseRaster
    Data/        счётчики, переменные, дата (TimeProvider), ^F-поля из DataTable
test/GGA1C.Sync.Printing.Tests       эталонные тесты: каждая папка golden -> побайтово
```

| Часть декомпилята | Решение |
|---|---|
| Модель `.ezpx` (`PrintJob`, `QLabel`, `Setup`, `Layout`, `GraphicShape`, `Graphic`, классы объектов) | **почти как есть**: свойства и атрибуты XML; ручки, курсоры, `Draw*` для экрана — выкинуть |
| `Setup.GetCommandEZPL`, `QLabel.PrintNormalCommandEZPL`, `PrintDBaseCommandEZPL`, `MyToStringEZPL` объектов, `FillPrintingDataEZPL`, `MyTimeDate`, `Variable*`, `CheckDigit` | **почти как есть**, но статику (`PrintJob.gQLabel`, `Dpi`, 63 поля `QLabel`) — в поля задания; `DateTime.Now` → `TimeProvider`; `MessageBox` → исключения |
| `SendStringToPrinter`/`SendBitmapToPrinterEZPL` (пишут в порт по ходу) | **переписать**: писать в `MemoryStream`, задание целиком — `byte[]`; транспорт отдельно |
| Растр текста (`GdiWindowTextBox`, `WindowTextBox`) | **почти как есть** + явный `TextRenderingHint.ClearTypeGridFit`; ветку `myDrawStringGDI` (Windows < 10) — выкинуть |
| `BarCode` растром | **переписать**: свой Code39 + подпись `AntiAlias`/`GenericTypographic`; Neodynamic **не переносим** (запрещён); прочие символики — по мере появления в шаблонах |
| `GlobalPrint` (GlobalInfo), `Program.cs` | взять только логику `InitLabel`, `CheckSerialPrompt`, `PrintJob_Execute` (удаление пустых), `UseNormalPrint` |
| WinForms (`GlobalForm`, окна запроса, `RichTextBox`), EPL, `Trace.dll` (исключена из проекта — только LPT/COM/USB), `SocketSDK`, BLE, весы, RFID, ECHO, `ClipperLib`, `MyWordArt`, `LoadOleDb*`, `LoadMySql` | **не переносим** (данные даёт хаб) |

Код декомпилята переносится в `src/` впервые на этом шаге — с пометкой источника в заголовке
файла (решение о лицензии — ниже).

## 4. План реализации

Каждый шаг — отдельный заход «план → ок → код». Эталонный тест сравнивает весь `golabel.bin`; на
шагах до полного покрытия — ещё и «команды без растров» (растры `Q` маскируются), чтобы видеть
продвижение.

| # | Шаг | Готово, когда (побайтово) |
|---|---|---|
| 1 | Проекты `Printing.Core`/`Printing.GoLabel`, модель и чтение `.ezpx` (все 15), правки `InitLabel`; шапка `^XSETCUT` + `GetCommandEZPL` | все 15 читаются; **шапка (всё до первой наклейки) равна эталону у всех 15** |
| 2 | Цикл наклейки: `^L`, `D/T`, обратный порядок, `OutOfPageArea`, `Layout` (две в ряд), счётчики `-S`, `Text` (`AT`/`A`), `QRectangle`, `Line`, `BA`; выбор `Print`/`PrintNornal` по `UseNormalPrint` | **`sample2`** целиком; у остальных, кроме `gkv3-box-var-mysql`, совпадают все строки команд при замаскированных `Q` |
| 3 | Растр `Q`: `Bitmap1Bit`, `RasterWriter`, `Image`, `Ellipse`; дожать 6 точек `Image` | **`gkv3-vm-product`, `gkv3-product`, `gkv1-oem-product`, `gkv1-oem-product-nologo`** |
| 4 | Code39 растром: свой кодер + подпись как у Neodynamic (`TopAndLeft`, `BottomAndCenter`, `Offset`), счётчик в данных | **`gkv1-vm-box`, `gkv3-vm-box`** |
| 5 | Текст шрифтом Windows: `GdiWindowTextBox`, `WindowTextBox` (перенос строк, межстрочный, выравнивание), дата ПК в данных штрихкода (`^D+Y0000`, `^D%y`) от `TimeProvider` | **`gkv1-oem-box`, `gkv3-box`, `gkv3-vm-box-102x152`, `gkv-avto-box`, `gkv-avto-product`, `gkv-avto-product-noname`, `gkv-avto-product-ru-korpus`** |
| 6 | Печать из данных: `Print(records)`/`PrintDBaseCommandEZPL`, поля `^F00`/`^F01`, формат даты «октябрь 2026», `DataTable` от хаба | **`gkv3-box-var-mysql`** (нужен снимок 4 строк — вопрос 5) |
| 7 | Обвязка для хаба: задание без статики (два задания параллельно дают те же байты), явная культура, прогон тестов под другой учёткой Windows (шрифты!) | **все 15** в обычном прогоне, в параллельном и под другой учёткой |

**Оценка.** Переносимого декомпилята — порядка 8–12 тыс. строк (из 35 тыс. «модель + команды +
переменные» по П3: без EPL, баз, редактора, Neodynamic), своего нового кода — 1–2 тыс. (Code39,
обвязка, тесты). По заходам: шаги 1–2 — по одному, 3 — один, 4 — один-два (раскладка
Neodynamic), 5 — один-два, 6 — один, 7 — один: **7–10 заходов**. Самые неопределённые — 4 и 5.

## 5. Лицензия

Из `decisions.md` («Источник логики»): у GoLabel нет открытой лицензии («Copyright Godex»), EULA не
найдено; **портировать декомпилят для внутреннего использования — решение пользователя**, риск
назван один раз. Neodynamic (коммерческий, на ключе GoDEX) **не переносим**.

## Вопросы к пользователю

1. **NuGet `System.Drawing.Common` 10.0.x** в `Printing.GoLabel` (проще, `net10.0` остаётся) — или
   `FrameworkReference Microsoft.WindowsDesktop.App` с переводом цепочки проектов на
   `net10.0-windows`? Предлагаю NuGet.
2. **Шрифт GlavkonGOST на сервере хаба** установлен только для `Administrator`. Хаб будет работать
   службой (под какой учёткой) или под `Administrator`? Предлагаю установить шрифт для всех
   пользователей (нужно действие администратора).
3. **Рабочие места с GoLabel:** включён ли ClearType и какой масштаб экрана (100 %?), не менялась ли
   «толщина шрифта Windows» в настройках GoLabel? От этого зависит, совпадают ли байты GoLabel у
   оператора с эталоном стенда. Порт повторяет стенд: ClearType, 96 dpi, толщина по умолчанию.
4. Если раскладку подписи Neodynamic не удастся подобрать по эталонам: можно ли **читать**
   декомпилят `Neodynamic.SDK.Barcode.dll` как справку о геометрии (без переноса кода)?
5. **`gkv3-box-var-mysql`:** снять снимок 4 строк `my_labels` (`a.morozov@test.local`, как было
   2026-10-28 21:30) в `golden\gkv3-box-var-mysql\data.csv` чтением из базы стенда — или переснять
   эталон позже на фиксированных данных?

## Как проверялось (2026-10-29)

- Ссылки на файлы и строки — чтением декомпилята в этой сессии.
- Состав объектов — по `xsi:type` в `template.ezpx` всех 15 папок; команды — разбором `golabel.bin`.
- Растры: подпись Code39 и строка `GdiWindowTextBox` — отрисовка в PowerShell 5.1 x64 и на .NET 10 (`net10.0-windows`,
  `System.Drawing` из WindowsDesktop 10.0.8) против растров эталона (в PowerShell `SystemDefault` совпал с
  эталоном, на .NET 10 — нет);
  `Image` — на .NET 10 и в PowerShell 5.1 x64 и x86 (везде те же 6 точек мимо).
- Шрифты — `C:\Windows\Fonts`, `HKCU`/`HKLM …\Fonts`; сглаживание — `HKCU\Control Panel\Desktop`;
  `FontThick` — `user.config` GoLabel 2.1.9558.30383 (значения нет → по умолчанию 0).
