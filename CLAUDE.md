# GGA1C.Sync

Интеграционный хаб на Windows Server 2025 рядом с Altium on-prem, SOLIDWORKS PDM и 1С.
Действует **от имени конкретного человека**, не общей учётки: личность даёт Keycloak (связь
по `sub`), отключили человека в Keycloak — у него закрыт доступ везде.

Модули, строго в этом порядке: **1. Печать** этикеток и наклеек → **2. SolidWorks / PDM** →
**3. Altium ↔ 1С**. Сквозная часть — учётка человека с привязанными учётками других систем и
журнал «кто что сделал». Цель и решения — `docs/summary.md`, задачи — `docs/roadmap.md`.

## Стек

ABP Framework 10.7 (NuGet-пакеты, не форк) · Blazor Server, тема MudBlazor (страницы входа —
LeptonX Lite) · EF Core 10 · **MySQL 8.4**, одна база `gga1c` и для ABP, и для буфера этикеток ·
Elsa Workflows 3.9 — запланирована, **не подключена**.

```
src/  Domain.Shared  Domain  Application.Contracts  Application  EntityFrameworkCore
      HttpApi (приём от виджета Grist)  HttpApi.Client  Blazor (UI + точка запуска)  DbMigrator
test/ Domain.Tests  Application.Tests  EntityFrameworkCore.Tests  TestBase  ConsoleTestApp
```

## Собрать, запустить, проверить

Переменные окружения обязательны — в `appsettings.json` секретов нет намеренно:
`ConnectionStrings__Default` (пароль — `C:\GGA1C\mysql-app.txt`),
`StringEncryption__DefaultPassPhrase` (`C:\GGA1C\stringencryption.key`),
`ASPNETCORE_Kestrel__Certificates__Default__Path` / `__Password` (`C:\GGA1C\tls\`).

```
dotnet build -c Release                       # в корне: 0 ошибок, 0 NU1902/NU1903
dotnet test                                   # в корне
cd src\GGA1C.Sync.DbMigrator && dotnet run -c Release
cd src\GGA1C.Sync.Blazor && dotnet run --no-launch-profile -c Release --urls "https://0.0.0.0:5443;http://0.0.0.0:5080"
```

Вход в хаб пока локальный: `admin` (сеялка ABP). Стенд — `docs/platform/stand.md`.

## Правила проекта

1. Ответы пользователю — **на русском**. Он тестирует на реальном железе сразу после правки.
2. **В репозитории только сервис.** Виджет Grist, Grist, Keycloak, Docmost, скрипты стенда —
   вне git (`.local-tools/`, `wwwroot/widget/` в `.gitignore`).
3. **Виджет Grist (`widget_report_grist`) правит отдельный чат.** Сами его не трогаем —
   пишем для того чата промпт с правкой.
4. Базы и секреты — вне репозитория и папки сборки, в `C:\GGA1C\`. Пароли и ключи в документы
   не пишем — только путь к файлу, где они лежат.
5. Перед коммитом — `dotnet build -c Release` без ошибок и без `NU1902`/`NU1903`.
6. Версия одна на решение — `<Version>` в `common.props`.
7. Проверка правки — dev-запуском на стенде, а не сборкой релиза.

## Как работаем

- Одна задача из `docs/roadmap.md` = одна сессия. Сначала прочитать карточку и файлы из «что
  читать». Широкий поиск по коду — субагентом Explore.
- До кода — план из 3–7 шагов с файлами, ждать «ок». Только то, что в карточке, без попутного
  рефакторинга и новых абстракций.
- **Остановиться и спросить** перед: новым NuGet-пакетом, миграцией БД, правами или глобальными
  настройками MySQL, удалением файлов, правками в Keycloak/Grist/IIS.
- Нужно действие человека (DevTools, доступ, принтер) — остановиться и сказать, что именно.
- **Перед словом «готово» — самопроверка** скиллом `superpowers:verification-before-completion`:
  каждое утверждение отчёта и docs подтверждено свежей командой или чтением файла в этой же
  сессии, предположения помечены «не проверено», свои документы перечитаны на противоречия. В
  отчёте — раздел «Самопроверка»: что проверил, чем, что нашёл и исправил. Касается и субагентов.
- В конце: обновить карточку в `roadmap.md`, новые грабли — в docs модуля или сюда, коммит.
- **Сделал коммит — сразу пуш своей ветки**, не спрашивая (`GIT_TERMINAL_PROMPT=0 git push -u
  origin <ветка>`, чтобы без учётных данных падал, а не висел). Работа — в своей ветке, не в
  `main`; слияние в `main` решает пользователь.

## Грабли кода (каждая уже стоила времени)

- **`--no-launch-profile` обязателен**: без него `dotnet run` берёт адрес из
  `launchSettings.json` (`https://localhost:44397` — по нему и видно, что флаг забыли) и молча
  игнорирует `--urls` и `ASPNETCORE_URLS`.
- **Сначала погасить старый `GGA1C.Sync.Blazor`**: живой процесс держит dll, сборка падает, а
  «сайт отвечает» — отвечает прежний процесс. Выглядит как «изменения не применились».
- **Фоновые работники ABP не открывают единицу работы** — репозитории в них падают с
  `ObjectDisposedException`. Открывать явно через `IUnitOfWorkManager` (см. `KeycloakSyncWorker`).
- **Шифрованную настройку расшифровывает только `ISettingProvider`**; `ISettingManager`
  отдаёт шифротекст (пишет при этом нормально).
- **Ключ `StringEncryption` в git не возвращать** — шаблон ABP кладёт его в `appsettings.json`.
- Секреты в UI — маской своей длины (`SecretMask`): одни точки — не менять, пусто — удалить,
  иное — новое значение.
- `MySql.EntityFrameworkCore` (Oracle) работает только с MySQL: на MariaDB падает при миграции
  с `InvalidCastException: DBNull -> Int64`.
- Пины 10.0.13 закрывают уязвимости шаблона: `System.Security.Cryptography.*` в `common.props`,
  `Microsoft.AspNetCore.Components.WebAssembly.Server` в Blazor-проекте — при обновлении ABP
  проверять заново.

## Где что описано

| Тема | Файл |
|---|---|
| Цель, что работает, решения, отказы | `docs/summary.md` |
| Задачи и статусы | `docs/roadmap.md` |
| Стенд: адреса, https, IIS, сертификат | `docs/platform/stand.md` |
| Учётки из Keycloak, опознание виджета | `docs/platform/accounts.md` |
| Настройки, секреты, окружение сборки | `docs/platform/environment.md` |
| Печать: решения модуля 1 | `docs/printing/decisions.md` |
| Печать: текущий поток Grist → MySQL → GoLabel | `docs/printing/golabel-mysql.md` |
| SolidWorks / PDM | `docs/solidworks/README.md` |
| Altium ↔ 1С, реверс API Altium, тупики | `docs/altium-1c/README.md` |
