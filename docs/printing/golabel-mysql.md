# Текущий поток: Grist → буфер в MySQL → GoLabel II

Работает сейчас. Остаётся на переходный период, пока хаб не печатает наклейки сам
(см. `decisions.md`).

## Путь данных

Виджет Grist шлёт партию этикеток в хаб (`POST /api/grist/labels/replace-all`; есть ещё
`upsert`, `DELETE labels/{externalId}`, `DELETE labels` — `GristLabelsController`), опознание —
по токену Keycloak. Хаб кладёт строки в таблицу `AppLabels` базы `gga1c`, GoLabel II забирает
их **прямо из MySQL** по логину и паролю конкретного человека. На этикетку идут `date`,
`product_code`, `mode_code`.

## В GoLabel II нет ODBC

Проверено 18.10.2026 по сборкам в `C:\Program Files (x86)\GoDEX\GoLabel II`: строка `ODBC` не
встречается ни в `GoLabel.exe`, ни в `QlabelDlg.dll`, ни в `QLabelSDK.dll`. В диалоге
«Подключить базу» — CSV, Microsoft Access, Excel и подключение **по логину и паролю** к MySQL,
SQL Server и Oracle; к MySQL GoLabel идёт своим `MySqlConnector.dll`. На машине с принтером
ничего ставить не нужно.

**Настройка в GoLabel II:** «Подключить базу» → MySQL, сервер — адрес хаба, порт 3306, база
`gga1c`, логин и пароль — из карточки человека в хабе, таблица — `my_labels`.

## Разделение по людям держит СУБД

Логин человека имеет право `SELECT` **только на представление** `my_labels`, прав на таблицу нет:

```sql
CREATE OR REPLACE SQL SECURITY DEFINER VIEW my_labels AS
SELECT l.Date AS date, l.ProductCode AS product_code, ...
FROM AppLabels l JOIN AppLabelClients c ON c.Slug = l.Slug
WHERE c.PrintLogin = SUBSTRING_INDEX(SESSION_USER(), '@', 1) AND c.IsEnabled = 1
```

**`SESSION_USER()`, а не `CURRENT_USER()`** — внутри представления с правами создателя
`CURRENT_USER()` возвращает создателя. Проверено: двое записали по 2 этикетки, логин видит свои
2 из 4, `SELECT` по таблице — `ERROR 1142`. Отключённая учётка не видит даже свои.

## Логин и пароль печати

Создаются сами при сверке с Keycloak, как только у человека есть учётка и почта: **логин —
почта** (если почты нет или она длиннее 32 знаков — `lbl_<папка>`), пароль **1234**, права
только на `my_labels`. Кнопки «выдать» нет — администратор диктует почту и 1234.

Пароль хранится зашифрованным и **виден** в карточке («глаз») — его диктуют оператору, и
«показывается один раз» означало бы перевыпуск при каждой потере бумажки. Там же меняется:
кнопка со стрелками подставляет случайный пароль, применяется при сохранении (`ALTER USER`),
чтобы случайное нажатие не отрезало печать.

## CSV — второй источник (убрано 2026-10-29)

Была выгрузка `Labels:ExportRoot` (`C:\GGA1C\labels\<папка>\labels.csv`). **Убрана 2026-10-29**
(решение пользователя: печать уходит из хаба напрямую, операторы входят под личными учётками AD).
Код `LabelsCsvExport`, настройка и удаление файлов при удалении учётки удалены; папка
`C:\GGA1C\labels` на диске оставлена — что с ней делать, решит пользователь. Источник для GoLabel —
только MySQL `my_labels`.

## Две странности GoLabel II, вылеченные со стороны базы

**Окно с ошибкой на каждое обновление.** По журналу запросов MySQL GoLabel при обновлении делает:

```
SELECT ... FROM INFORMATION_SCHEMA.TABLES
SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='gga1c' AND TABLE_TYPE='VIEW'
SELECT * from administrable_role_authorizations        <- БЕЗ имени схемы
```

Последний MySQL ищет в нашей базе и отвечает `SELECT command denied`. Поэтому `EnsureViewAsync`
создаёт рядом с `my_labels` пустую заглушку с тем же именем и выдаёт на неё право. Цена — две
строки в списке таблиц у человека.

**Подключения не закрываются.** На каждое обновление GoLabel открывает новое соединение и бросает
прежнее. `MAX_USER_CONNECTIONS` не годится — сервер откажет новому соединению. Поэтому прежние
сессии закрываются **в момент появления новой**: сервер на входе выполняет `init_connect` с
вызовом `gga1c.kill_old_sessions` (`SQL SECURITY DEFINER` = `gga1c_app`, у него `PROCESS` и
`CONNECTION_ADMIN`). Закрываются только спящие сессии того же логина; подключения хаба не
затрагиваются (для пользователей с `CONNECTION_ADMIN` сервер `init_connect` пропускает).

**Обработчик ошибок в процедуре обязателен** (`DECLARE CONTINUE HANDLER FOR SQLEXCEPTION BEGIN
END`): если процедура из `init_connect` падает, MySQL разрывает подключение — человек не войдёт.

Процедуру и права заводит хаб. Саму настройку включить **один раз при установке** из-под root —
хаб намеренно не трогает глобальные переменные:

```sql
SET PERSIST init_connect = 'CALL gga1c.kill_old_sessions()';
```

`PrintConnectionWorker` — сеть безопасности: раз в две минуты добирает лишние спящие сессии.

## Перед выходом на рабочее место

Порт 3306 закрыт брандмауэром — открыть для места с принтером и проверить подключение
(MySQL 8.4 — `caching_sha2_password`, MySqlConnector умеет, соединение по TLS сервера).
