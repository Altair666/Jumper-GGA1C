# AltiumSync — контекст для продолжения работы (Claude Code CLI)

## Что это

**AltiumSync** — сервис-прокладка на C#/.NET 8, который:
1. Синхронизирует компоненты и BOM из **Altium Enterprise Server 7.2.6.5** (on-prem) в **1С:Бухгалтерия 8.3** через OData.
2. Отдаёт данные для печати этикеток на принтере **ZX430i** через приложение **GoLabel2**, наполняясь из **Grist** (таблица product_code/mode_code/date).

Технически — один ASP.NET Core процесс (порт 5000), который:
- отдаёт веб-интерфейс (обычный HTML/JS, без фреймворков, `wwwroot/index.html`)
- является REST API
- является fake Teamcenter EDA Gateway (`/tc/...`) и OAuth2 provider (`/oauth/...`) — экспериментальная, сейчас не используемая ветка (см. ниже)

## Стек

- C# / .NET 8, ASP.NET Core
- SQLite + Dapper — **два отдельных файла**:
  - `sync.db` — компоненты, папки, настройки, логи, oauth-коды
  - `labels.db` — отдельная таблица `labels` для Grist → GoLabel2 (физически отдельный файл, чтобы GoLabel2 подключался по ODBC только к нему, read-only)
- Windows Server 2025, разворачивается через `deploy.bat` (останавливает старый процесс на порту 5000, `dotnet publish`, запускает).

## Структура проекта

```
AltiumSync/
├── Controllers/
│   ├── ApiController.cs         — основной REST API + диагностика + admin-эндпоинты Grist labels
│   ├── EdaGatewayController.cs  — fake Teamcenter EDA Gateway на /tc (эксперимент, см. "Тупиковые ветки")
│   └── GristLabelsController.cs — /api/grist/* — приём данных от Grist (auth по X-Api-Key)
├── Services/
│   ├── AltiumService.cs         — HTTP-клиент к Altium Enterprise Server, парсинг ответов
│   ├── SyncService.cs           — оркестрация синхронизации, ReadOnlyMode флаг
│   ├── OneCService.cs           — клиент 1С OData
│   ├── AutoSyncBackgroundService.cs — фоновая синхронизация по расписанию
│   └── RequestLoggerMiddleware.cs   — логирует все входящие запросы (для реверс-инжиниринга)
├── Data/
│   ├── Database.cs        — CRUD для sync.db (компоненты/папки/настройки/логи)
│   └── LabelsDatabase.cs  — CRUD для labels.db (таблица labels)
├── Models/Models.cs       — все DTO/модели, включая AppSettings и LabelRecord
├── wwwroot/
│   ├── index.html         — весь UI (вкладки: Дашборд, Компоненты, Проекты, Папки,
│   │                        Запросы AES, Журнал, Grist→GoLabel2, Диагностика, Настройки)
│   └── tc/eda/             — статические файлы для fake Teamcenter EDA client (jar/xml)
├── appsettings.json        — DatabasePath = C:\AltiumSync\sync.db, порт 5000
└── deploy.bat              — стоп процесса на порту 5000 → dotnet publish → запуск
```

Текущая версия в UI: **v0.36** (см. `<div class="sub">Sync Service v0.XX</div>` в index.html — **всегда увеличивать на +0.01 после каждого изменения**, это правило соблюдалось весь проект).

## Обнаруженный API Altium (реверс-инжиниринг через DevTools, НЕ документирован)

- `GET /components/api/tags` — дерево тегов/папок: `{tags:[{guid,name,parentGuid,subTags,count}], totalCount}`
- `GET /components/api/components?fields[]=Id&fields[]=Comment&...&text=&tag={guid}&orderby=&start=0&limit=50`
  — компоненты, жёсткий лимит 50 на страницу. Параметры точно скопированы из реального запроса браузера.
- Структура компонента: `Id (R_...)`, `ItemGUID`, `Comment` (артикул), `Description`, `Manufacturer`, `FootprintName1`,
  плюс кастомные поля `Наименование 1С`, `Наименование`, `Код`.
- Авторизация: form-login `POST /Account/Login` → cookie `.AFSAUTH` + `ASP.NET_SessionId`.

## НЕРЕШЁННАЯ ключевая проблема — пагинация компонентов

**Симптом:** программные запросы с `tag=` и `start=` **всегда** возвращают первые 50 компонентов,
игнорируя оба параметра — даже для тегов с 3 компонентами (Audio).

**Диагностика:**
- С реальными куками браузера (скопированными вручную из DevTools) те же запросы **работают
  корректно** (Audio → 3 компонента, не 50). Подтверждено эндпоинтом `/api/diag/raw-request`.
- Значит проблема не в URL/параметрах, а в серверном сессионном контексте.
- Кука `A365_LIBRARYCOMPONENTSAPI_DOWLOAD_SESSION` создаётся браузером (видимо JS SPA при
  первой загрузке `/components`), не через обычный HTTP Set-Cookie на GET-запрос.
- Пробовали: warmup-запросы к `/librarycomponentsapi/api/v1/...` — кука НЕ появляется.
- Пробовали: ручная установка этой куки со случайным GUID — не помогает (или помогало
  частично, финальный статус не подтверждён — последний тест пагинации после этого фикса
  показывал те же 50/50, нужно перепроверить на актуальной сборке).
- Текущий **временный обход**: в Настройках есть поле "Куки браузера" (`AltiumBrowserCookies`
  в `AppSettings`) — пользователь вручную копирует Cookie-заголовок из DevTools раз в ~24ч
  (пока жива ASP.NET сессия), вставляет в поле. `AltiumService.GetAsync()` при заданном этом
  поле шлёт запросы с этими куками напрямую вместо использования собственной программной
  сессии (см. `GetBrowserSession()` / блок в начале `GetAsync`).

**Следующий шаг для решения "по-настоящему":** открыть DevTools, Network, hard refresh
`/components` с нуля, посмотреть **первые 10-15 XHR-запросов** до того как начинает грузиться
список компонентов — один из них должен инициализировать сессионный контекст на сервере.
Особое внимание на `/librarycomponentsapi/...` эндпоинты — предположительно один из них
(view конкретного компонента? download?) триггерит установку куки на сервере.

## Тупиковые/приостановленные ветки (для контекста, не переоткрывать без причины)

1. **PLM Integration через Windchill** — в AES 7.2.6.5 в дропдауне Driver только "Teamcenter EDA",
   Windchill пропал (был в 6.0.5.28). Лицензии на Windchill PLM Addon не нашли. Приостановлено.
2. **PLM Integration через Teamcenter EDA (fake gateway)** — реализован `EdaGatewayController.cs`
   на `/tc/...`, эмулирует Teamcenter EDA Gateway, принимает `admin/admin` логин, отдаёт файлы
   `altiumLibrary_edadef.xml`/`altiumPcb_edadef.xml`/JAR из `wwwroot/tc/eda/`. **Тупик**: AES
   требует физически установленный EDA Gateway CLIENT (проприетарный Java-клиент Siemens) на
   машине сервера AES + лицензию Teamcenter PLM Addon — это НЕ решается через сетевой fake-сервер,
   AES проверяет локальную установку до любого сетевого запроса ("EDA Client location is not set").
   **Оставлено в коде на случай если пригодится, но синхронизация идёт напрямую через
   `/components/api/...`, не через PLM Integration.**
3. **OAuth2 provider** (`/oauth/authorize`, `/oauth/token`) — реализован для попытки подружиться
   с Windchill-подобным PLM интерфейсом который требовал OAuth Provider при создании инстанса.
   Не пригодился в итоге (Windchill недоступен), но код рабочий и не мешает.

## Grist → GoLabel2 (новая, отдельная от Altium фича)

**Задача:** сервис держит третью БД-прокладку (`labels.db`) для печати этикеток на принтере
ZX430i через GoLabel2.

**Разделение прав:**
- **Grist** → пишет через наш HTTP API (`POST /api/grist/labels/upsert`, `/replace-all`,
  `DELETE /api/grist/labels/{externalId}`) с заголовком `X-Api-Key` (ключ хранится в
  `AppSettings.GristApiKey`, автогенерируется, можно перевыпустить в UI).
- **GoLabel2** → читает `labels.db` напрямую через **ODBC** (SQLite ODBC Driver,
  ch-werner sqliteodbc), DSN настроен с галкой **Read Only** — физически не может писать.

**Схема таблицы `labels`** (см. `LabelsDatabase.cs`):
```
id, external_id (UNIQUE, = Grist row id), article, name, manufacturer,
barcode, qty, extra1, extra2, extra3, updated_at
```

**⚠️ НЕСООТВЕТСТВИЕ ПОЛЕЙ (требует решения):** реальный Grist-виджет уже существующий (отдельно
от нашего API) использует другие названия колонок в самой Grist-таблице:
```
date          → JS: formatRuDate(state.selectedDate)   — дата в формате "август 2026"
product_code  → JS: product.productCode                — Products.product_code (Text)
mode_code     → JS: modMap.get(product.modeId)?.code   — Modifications.mode_code (Text)
```
Наша схема `LabelRecord`/`labels` таблицы (article/name/manufacturer/barcode/qty) была
придумана заранее как generic-набор и **не факт что соответствует** реальным нуждам этого
конкретного виджета/этикетки. **Нужно уточнить у пользователя** маппинг: либо переименовать
поля в `LabelRecord` под `date`/`product_code`/`mode_code` напрямую, либо добавить эти три
как отдельные колонки (сохранив generic-поля для других сценариев), либо использовать
`extra1`/`extra2`/`extra3` как есть для этих трёх значений. **Уточнить перед правкой,
не переделывать наугад.**

Сам Custom Widget для Grist (JS-файл, который реально шлёт `POST /api/grist/labels/upsert`
на каждое изменение строки, используя `grist.onRecords()`) **ещё не написан** — было только
обсуждение какие поля нужны на панели настроек виджета (Server URL, API Key, Endpoint,
маппинг колонок). Если продолжать — это следующий кусок работы.

## Правила которые соблюдались весь проект (сохранить в CLI-сессии)

1. **После каждого изменения — бампать версию** в `wwwroot/index.html`
   (`<div class="sub">Sync Service v0.XX</div>`), +0.01.
2. **Перед каждой сборкой** — проверять баланс фигурных скобок в JS-блоке `index.html`
   вручную (был неоднократный баг: осиротевшие функции без заголовка/с лишней `}` после
   правок через `str_replace`). Пример проверочного скрипта — см. историю чата, считает
   depth по `{`/`}` вне строк построчно и печатает первую отрицательную позицию.
3. **`deploy.bat`** должен: убить процесс на порту 5000 (`netstat -ano | findstr :5000`
   → `taskkill /PID .. /F`), подождать 4 сек, удалить папку `publish`, `dotnet publish -c
   Release -o publish --nologo`, при ошибке сборки — `pause` и не запускать. НЕ использовать
   `wmic` (на реальном Windows Server у пользователя это обрывало скрипт без объяснений) и
   НЕ использовать кириллицу/юникод-символы в `.bat` файле (ломает кодировку cmd, был баг
   `chcp 65001` тоже ломал — убрали).
4. **БД (`sync.db`, `labels.db`) хранится вне папки `publish`** — путь абсолютный
   (`C:\AltiumSync\sync.db` в `appsettings.json` через `DatabasePath`), чтобы настройки не
   терялись при каждом `deploy.bat` (папка `publish` удаляется и пересоздаётся).
5. Все ответы пользователю — **на русском**, он работает в проде на реальном железе, тестирует
   сразу после каждой правки, часто копипастит консольный вывод построчно.

## Как продолжить в CLI

1. Распакуй актуальный `AltiumSync.zip` (или клонируй репозиторий, если уже запушен на GitHub —
   см. отдельный вопрос про репозиторий "Jumper GGA1C" в этом же треде).
2. `cd AltiumSync && dotnet build` — проверить что всё компилируется (в чат-версии сборка
   не проверяется реальным компилятором, только вручную по балансу скобок — возможны опечатки).
3. Первая задача: уточнить у пользователя маппинг полей Grist-виджета (см. раздел выше) и
   поправить `LabelRecord`/`labels`/`GristLabelsController` под реальные колонки.
4. Вторая задача (если пользователь захочет вернуться): дожать автоматическое получение
   сессионных кук Altium без ручного копипаста — см. раздел "НЕРЕШЁННАЯ проблема" выше.
