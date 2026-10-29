# Окружение, настройки, секреты

## Данные и секреты

Всё вне репозитория и папки сборки, в `C:\GGA1C\`:

| Что | Где |
|---|---|
| Пароль `gga1c_app` / root MySQL | `C:\GGA1C\mysql-app.txt`, `mysql-root.txt` |
| Ключ шифрования настроек | `C:\GGA1C\stringencryption.key` |
| Сертификат стенда | `C:\GGA1C\tls\` |
| Выгрузка CSV для GoLabel | `C:\GGA1C\labels\<папка>\labels.csv` (`Labels:ExportRoot`) |
| Значок сайта (П16) | `C:\GGA1C\branding\site-icon.(png|svg|ico)` (`Branding:Folder`; нет файла — значок из сборки) |
| Бэкапы | `C:\GGA1C\backup\` |

**`StringEncryption:DefaultPassPhrase` в `appsettings.json` пустой намеренно.** Этим ключом ABP
шифрует значения настроек (пароли Altium, 1С, PDM, секрет Keycloak). Шаблон кладёт ключ прямо в
файл, который уходит в git, — пароли в базе зашифрованы, а ключ рядом в репозитории. Реальное
значение — переменная `StringEncryption__DefaultPassPhrase` и файл выше. **В репозиторий не
возвращать.**

## Секреты в интерфейсе

**Маска своей длины** (`SecretMask` в Domain.Shared), а не пустое поле с галочкой «сохранено».
Видно, что секрет есть и какой он длины; его можно выделить и стереть. При сохранении: одни
точки — не менять, пусто — удалить, иное — новое значение. Прежний вариант («пусто = оставить»)
не позволял удалить секрет.

**Шифрованную настройку расшифровывает только `ISettingProvider`.** `ISettingManager`
(`GetOrNullGlobalAsync`) отдаёт шифротекст (пишет при этом с шифрованием). Вылезло на масках:
128 точек вместо 86. Читать секреты только через `ISettingProvider`.

## Сборка и пакеты

**ABP CLI не умеет докачивать** — шаблон тянется одним запросом, при обрыве SSL падает, ничего
не оставляя в кэше (канал этой машины рвётся на длинных загрузках, как с Docker Hub). Шаблон
скачан `curl --retry 5 --retry-all-errors` и лежит в `C:\abp-template\app-10.7.0.zip`;
переигрывать через `-ts` на этот файл.

**Уязвимости из шаблона закрыты пинами.** Шаблон тянет `System.Security.Cryptography.Pkcs` и
`.Xml` 10.0.11 (уязвимости высокой серьёзности), Blazor-проект — уязвимый
`Microsoft.AspNetCore.Components.WebAssembly.Server`. Пины на 10.0.13 — в `common.props` (его
импортируют 13 из 14 проектов) и в Blazor-проекте. Предупреждений 4 вместо 76. **При обновлении
ABP проверять заново.**

**Проектов 14 — цена шаблона ABP.** `HttpApi.Client`, `ConsoleTestApp` и лишние тестовые можно
подрезать, когда станет видно, что не используются.

## Elsa (когда дойдёт)

Пакеты переименованы: актуальные — `Elsa.Persistence.EFCore` и провайдер под нужную СУБД.
Старые имена `Elsa.EntityFrameworkCore.*` брошены на 3.2.4, а поиск по NuGet выдаёт именно их —
не подключать. Эталонный набор ссылок для хоста «сервер плюс Studio» —
`C:\elsa-core\apps\src\Elsa.Server.Studio.BlazorServer\*.csproj`.

Справочные клоны (только чтение): `C:\elsa-core`, `C:\elsa-studio`. Песочница Elsa «сервер плюс
конструктор» — порт 5055 (учётка — у владельца стенда).

## Фоновые работники

`KeycloakSyncWorker` (сверка учёток, раз в час), `AutoSyncWorker` (автосинхронизация Altium),
`PrintConnectionWorker` (раз в две минуты добирает брошенные сессии GoLabel). Единицу работы
открывают явно через `IUnitOfWorkManager` — сами работники ABP её не открывают.

## Грабли кода (каждая уже стоила времени)

- **Запуск хаба из worktree** (`C:\gg1ac-wt\...`): там нет `openiddict.pfx` и `wwwroot/libs`
  (оба в `.gitignore`) — без первого хаб не стартует, без второго страница входа отдаёт 500.
  Скопировать из `C:\gg1ac\src\GGA1C.Sync.Blazor\` до запуска. Второй экземпляр на общей
  базе — с `Workers__Enabled=false` и по `https://localhost:<порт>` (куки не различают порты).
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

