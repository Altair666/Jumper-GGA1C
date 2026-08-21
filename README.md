# Altium ↔ 1С Sync Service

## Требования

- Windows Server 2025
- .NET 8 SDK → https://dotnet.microsoft.com/download/dotnet/8.0
  (скачать "SDK x64 Installer", не Runtime)

---

## Сборка и запуск (шаг за шагом)

### Шаг 1 — Распаковать архив

Распаковать `AltiumSync.zip` в любую папку, например `C:\AltiumSync`

### Шаг 2 — Открыть командную строку

```
Win + R → cmd → Enter
cd C:\AltiumSync
```

### Шаг 3 — Собрать и опубликовать

```cmd
dotnet publish -c Release -o C:\AltiumSync\publish
```

Это скачает зависимости и соберёт приложение в папку `publish`.

### Шаг 4 — Запустить для теста

```cmd
cd C:\AltiumSync\publish
dotnet AltiumSync.dll
```

Открыть браузер: http://localhost:5000

Если нужен другой порт — поменять в `appsettings.json`:
```json
"Urls": "http://0.0.0.0:8080"
```

### Шаг 5 — Установить как Windows Service (для продакшн)

Запустить PowerShell от администратора:

```powershell
New-Service `
  -Name "AltiumSync" `
  -BinaryPathName "C:\AltiumSync\publish\AltiumSync.exe" `
  -DisplayName "Altium 1C Sync Service" `
  -StartupType Automatic

Start-Service AltiumSync
```

Проверить статус:
```powershell
Get-Service AltiumSync
```

Остановить / перезапустить:
```powershell
Stop-Service AltiumSync
Restart-Service AltiumSync
```

Удалить сервис:
```powershell
Stop-Service AltiumSync
Remove-Service AltiumSync
```

---

## Настройка после запуска

Открыть http://[сервер]:5000 → вкладка **Настройки**:

1. Ввести URL и токен Altium Enterprise Server
2. Ввести URL, имя базы, логин/пароль 1С
3. Нажать "Проверить" рядом с каждым подключением
4. Сохранить

---

## Настройка 1С

1. Запустить базу через IIS или Apache (веб-публикация)
2. Конфигуратор → Администрирование → Публикация на веб-сервере
   → включить **OData**
3. Создать пользователя `sync_user`:
   - Права: чтение Номенклатуры, Спецификаций, НоменклатурныхГрупп
   - Если нужно создание — добавить право записи

Проверить вручную в браузере:
```
http://[сервер]/[база]/odata/standard.odata/Catalog_Номенклатура?$format=json&$top=1
```

---

## Настройка Altium Enterprise Server

1. Войти в Admin Panel
2. API → Tokens → Create Token
3. Скопировать токен в настройки сервиса

---

## Параметры компонентов в Altium

Добавить в карточку компонента (Component Parameters):

| Параметр   | Значение    | Смысл                         |
|------------|-------------|-------------------------------|
| `Sync_1C`  | `Yes` / `No`| Синхронизировать или нет      |

Компоненты с Lifecycle State ≠ `Released` пропускаются автоматически.

---

## Параметры проекта (.PcbPrj)

Project → Project Parameters → добавить:

| Параметр          | Пример              | Смысл                         |
|-------------------|---------------------|-------------------------------|
| `Sync_Action`     | `New` / `Update` / `Skip` | Действие синхронизации  |
| `1C_ProductName`  | `Контроллер МК-01`  | Имя номенклатуры в 1С         |
| `Drawing_Number`  | `АБВГ.123456.001`   | Номер чертежа                 |

---

## Структура файлов

```
AltiumSync/
├── Models/Models.cs                    модели данных
├── Data/Database.cs                    SQLite, все запросы
├── Services/
│   ├── AltiumService.cs                клиент Altium REST API
│   ├── OneCService.cs                  клиент 1С OData
│   ├── SyncService.cs                  логика синхронизации
│   └── AutoSyncBackgroundService.cs    фоновая служба
├── Controllers/ApiController.cs        REST API (UI + Add-In)
├── wwwroot/index.html                  веб-интерфейс
├── Program.cs                          точка входа
└── appsettings.json                    порт, путь к БД
```

База данных `sync.db` создаётся автоматически рядом с exe при первом запуске.

---

## API для Altium Add-In

### Компонент

```
POST /api/addin/push
{
  "type": "component",
  "altiumId": "uuid",
  "altiumName": "Резистор 10k",
  "altiumPath": "folder-uuid",
  "syncAction": "Update",
  "parameters": {
    "Manufacturer Part Number": "RC0402FR-0710KL"
  }
}
```

### Проект / BOM

```
POST /api/addin/push
{
  "type": "project",
  "altiumId": "proj-uuid",
  "altiumName": "Контроллер МК-01",
  "syncAction": "New",
  "parameters": { "Revision": "2.0" },
  "bomLines": [
    { "altiumId": "comp-uuid", "name": "Резистор", "quantity": 5, "unit": "шт" }
  ]
}
```

---

## TODO

- [ ] Уточнить endpoint-ы Altium API под версию вашего сервера
- [ ] Маппинг единиц измерения Altium → 1С  
- [ ] Добавить авторизацию в веб-интерфейс
- [ ] Написать Altium Designer Add-In (следующий этап)
