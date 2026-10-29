# GGA1C.Sync

Интеграционный хаб на Windows Server 2025 рядом с Altium on-prem, SOLIDWORKS PDM и 1С.
Действует **от имени конкретного человека**: личность даёт Keycloak (связь по `sub`), к учётке
человека привязаны его учётки в Altium, PDM, 1С и печати; отключили в Keycloak — закрыто везде.

Модули по порядку: **1. Печать** этикеток и наклеек → **2. SolidWorks / PDM** → **3. Altium ↔ 1С**
(последним). Цель и решения — `docs/summary.md`, задачи — `docs/roadmap.md`.

## Стек

ABP 10.7 (NuGet, не форк) · Blazor Server + MudBlazor (вход — LeptonX Lite) · EF Core 10 ·
**MySQL 8.4**, одна база `gga1c` · печать — `src/GGA1C.Sync.Printing.*` (порт ядра GoLabel) ·
Elsa 3.9 — запланирована, не подключена.

## Собрать, запустить, проверить

Секретов в `appsettings.json` нет: `ConnectionStrings__Default` (`C:\GGA1C\mysql-app.txt`),
`StringEncryption__DefaultPassPhrase` (`C:\GGA1C\stringencryption.key`), сертификат Kestrel
(`C:\GGA1C\tls\`). Живой хаб запускает `C:\GGA1C\start-hub.ps1` (задача `GGA1C-Hub-Autostart`).

```
dotnet build -c Release     # 0 ошибок, 0 NU1902/NU1903 — в C:\gg1ac-wt\build, не в C:\gg1ac
dotnet test                 # там же; эталоны печати: GGA1C_GOLDEN_DIR=C:\GGA1C\golden
cd src\GGA1C.Sync.Blazor && dotnet run --no-launch-profile -c Release --urls "https://0.0.0.0:5443;http://0.0.0.0:5080"
```

Вход: сотрудники — кнопкой «Keycloak», аварийный — `admin` по паролю, на чистой базе первого
администратора создаёт `/setup`. Подробности — `docs/platform/p7-keycloak-login.md`, стенд —
`docs/platform/stand.md`.

## Правила проекта

1. Ответы пользователю — **на русском**. Он тестирует на реальном железе сразу после правки.
2. **В репозитории только сервис** — то, что идёт в сборку, и тесты самого сервиса. Виджет, Grist,
   Keycloak, Docmost, скрипты стенда и **отладочные утилиты, даже на C#** (фейк-принтер, съём
   эталонов) — вне git: `.local-tools/`. Эталоны и шаблоны печати — `C:\GGA1C\golden`, не в git.
3. **Виджет Grist правит отдельный чат** — пишем ему промпт с правкой.
4. Базы и секреты — в `C:\GGA1C\`; в документах только путь к файлу, не значение.
5. Версия одна на решение — `<Version>` в `common.props`. Сборка автономная: все пакеты в выводе.

## Как работаем

- Одна задача из `roadmap.md` = одна сессия; читать карточку и файлы из «Читать». Широкий поиск —
  субагентом Explore. До кода — план 3–7 шагов. Только то, что в карточке.
- **Спросить перед:** новым NuGet-пакетом, миграцией БД, правами/глобальными настройками MySQL,
  удалением файлов, правками Keycloak/Grist/IIS. Нужно действие человека — сказать, какое.
- **Вся работа в `main`** в `C:\gg1ac`; сборка и тесты — в `C:\gg1ac-wt\build`
  (`git -C C:\gg1ac-wt\build checkout --detach <коммит>`). Коммит → сразу
  `GIT_TERMINAL_PROMPT=0 git push origin main`.
- **Не больше одной-двух задач разом**, код правит только одна.
- **После слияния кода — перезапустить живой хаб самому** (остановить цепочку `start-hub.ps1` и
  `Start-ScheduledTask GGA1C-Hub-Autostart`), проверить :5443 и журнал `Logs\logs.txt`.
- **Перед «готово» — самопроверка** скиллом `superpowers:verification-before-completion`, в отчёте
  раздел «Самопроверка»; непроверенное помечать «не проверено». Касается и субагентов.
- В конце: карточка в `roadmap.md`, новые грабли — в docs модуля.
- **Живая схема** — https://claude.ai/artifact/Dm1hmtUDMhaavNtNwR4wTy (`nodes`, `edges`, `inbox`;
  инструмент `ArtifactData`). Поменял промпт или структуру — обнови схему в той же сессии;
  в начале сессии разбери `inbox` и пометь `done: true` с ответом.

## Грабли кода — коротко (подробно в `docs/platform/environment.md`)

- `dotnet run` — только с `--no-launch-profile`; сначала погасить старый `GGA1C.Sync.Blazor`.
- Хаб из worktree: скопировать `openiddict.pfx` и `wwwroot/libs`; второй экземпляр — с
  `Workers__Enabled=false` и по `https://localhost:<порт>`.
- Фоновые работники ABP: единицу работы открывать явно (`IUnitOfWorkManager`).
- Секреты читать только через `ISettingProvider`; ключ `StringEncryption` в git не класть.
- MySQL, не MariaDB; пины 10.0.13 против уязвимостей шаблона ABP.
- GoLabel входит в MySQL без TLS — пароли логинов печати хаб держит в кэше MySQL (БАГ-1).

## Где что описано

| Тема | Файл |
|---|---|
| Цель, решения, отказы, открытые вопросы | `docs/summary.md` |
| Задачи и статусы | `docs/roadmap.md` |
| Стенд; учётки и вход; окружение и грабли | `docs/platform/stand.md`, `accounts.md`, `environment.md` |
| Задача шлюзу db.mp-lab.ru (`/sync`) | `docs/platform/fastapi-gateway-task.md` |
| Печать: решения, поток GoLabel, порт ядра | `docs/printing/decisions.md`, `golabel-mysql.md`, `p5-plan.md` |
| SolidWorks / PDM; Altium ↔ 1С | `docs/solidworks/README.md`, `docs/altium-1c/README.md` |
