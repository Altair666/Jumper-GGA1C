# Стенд 192.168.88.238

Отладочный стенд на этой же машине. В бою его заменят рабочий Grist (db.mp-lab.ru), Keycloak,
федерованный с AD, и нормальный сертификат.

## Адреса

| Адрес | Что | Чем терминируется TLS |
|---|---|---|
| `https://192.168.88.238:8444` | Grist | IIS, сайт `grist-https` → `http://127.0.0.1:8484` |
| `https://192.168.88.238:8443` | Keycloak, реалм `test` | IIS, сайт `kc-https` → `http://127.0.0.1:8080` |
| `https://192.168.88.238:5443` | хаб и копия виджета | сам Kestrel, сертификат из переменных окружения |

Старые http-адреса (`:8484`, `:8080`, `:5080`) живут для скриптов стенда. Хаб с http отвечает
307 на https, Grist по http уводит на тот же вход.

**Порт 443 оставлен Docmost.** Пока Grist висел на 443, голый `http://192.168.88.238` (адрес
Docmost) браузеры превращали в https и уводили в Grist. Поэтому Grist на 8444.

Документы Grist: **LMP-test-report** (`bAfvnQAFcaMe…`) — облегчённая копия только для отладки
виджета (перенесены нужные виджету столбцы); полный «LMP данные изделий» развивается на боевом
сервере.

## Зачем https

Виджет считает PKCE-хеш через `crypto.subtle`, а он есть только в защищённом контексте — для
фрейма защищёнными должны быть **и фрейм, и все страницы над ним**, то есть сам Grist. По http
вход падал с `Cannot read properties of undefined (reading 'digest')`.

В бою то же самое с другой стороны: Grist по https, а хаб на голом IP без сертификата — браузер
режет запрос как mixed content ещё до отправки (проверено: `Failed to fetch`, в логе хаба пусто).

## Сертификат

Самоподписанный, SAN на `192.168.88.238`, `127.0.0.1`, `localhost`, имя машины; срок 5 лет.
`C:\GGA1C\tls\gga1c-stand.pfx` (для Kestrel; пароль — у владельца стенда) и `gga1c-stand.crt`
(PEM, для доверия). Внесён в доверенные корневые **только этой машины** — на другой браузер
ругается, пока не импортировать `crt`.

## Grist

Поднимать **только** `/opt/grist/grist-up.sh` (иначе легко запустить без тома `grist-data` и
получить пустую базу), снапшот — `/opt/grist/grist-backup.sh`. Контейнер `grist-local` в WSL.

Переменные: `APP_HOME_URL=https://192.168.88.238:8444`, `GRIST_OIDC_SP_HOST=https://192.168.88.238:8444`
(без порта адрес уходит на 443, то есть в Docmost),
`GRIST_OIDC_IDP_ISSUER=https://192.168.88.238:8443/realms/test`. В контейнер монтируется
`/opt/grist/stand-ca.crt` и задаётся `NODE_EXTRA_CA_CERTS` — **без этого Grist не поднимается**:
node не доверяет сертификату, discovery OIDC отваливается, в логе `RestartShell: initial spawn
failed`.

**Грабля bash:** комментарий внутри многострочной команды (`docker run ... \`) обрывает её —
`grist-up.sh` падал с `docker run requires at least 1 argument`. Пояснения — только перед командой.

## Keycloak

`/opt/keycloak-stack/docker-compose.yml`: `KC_PROXY_HEADERS=xforwarded` и
`KC_HOSTNAME=https://192.168.88.238:8443`. Без первого Keycloak не верит `X-Forwarded-Proto`,
отдаёт discovery с http-адресами, и issuer в токене расходится с тем, что ждёт хаб.
Issuer — `https://192.168.88.238:8443/realms/test`, настройка хаба `Sync.Keycloak.BaseUrl` туда же.

Клиенты: у `grist` добавлен `https://192.168.88.238:8444/*`, у `gga1c-widget` —
`https://192.168.88.238:5443/widget_report_grist/*` и web origins. Старые http-адреса оставлены
на время перехода. `gga1c-hub` (с 28.10.2026) — вход людей в сам хаб: confidential, PKCE,
redirect `https://192.168.88.238:5443/signin-oidc` и `https://localhost:5444/signin-oidc`
(dev), секрет — `C:\GGA1C\keycloak-hub-secret.txt` (см. `docs/platform/p7-keycloak-login.md`).

Настройки виджета в документе (секция 70): адрес
`https://192.168.88.238:5443/widget_report_grist/index.html`,
`syncServerUrl=https://192.168.88.238:5443`, `syncKcIssuer=https://192.168.88.238:8443/realms/test`,
ключ пуст.

## IIS

Сайты на папках `C:\inetpub\grist-proxy` и `C:\inetpub\kc-proxy`, правила ARR в `web.config`
(образец — сайт Docmost). Две ловушки:

- серверные переменные `HTTP_X_FORWARDED_*` надо разрешить сайту
  (`appcmd set config <сайт> -section:system.webServer/rewrite/allowedServerVariables
  /+[name='HTTP_X_FORWARDED_PROTO'] /commit:apphost`), иначе **500.50 URL Rewrite Module Error**;
- `X-Forwarded-For` от ARR содержит порт (`IP:порт`) — во всех правилах он переопределяется на
  `{REMOTE_ADDR}` (на Docmost это ломало вход).

## Брандмауэр

Входящие правила на 443, 8443, 8444, 5443 («GGA1C stand https N»). Без правила на 8443 Grist из
контейнера не доставал до Keycloak — запрос просто истекал. **3306 закрыт** — открыть для
рабочего места с GoLabel, если источником выбран MySQL.

## MySQL

Служба `MySQL`, распакован в `C:\GGA1C\mysql`, данные `C:\GGA1C\mysql-data`, настройки
`C:\GGA1C\mysql\my.ini`. База `gga1c`, пользователь приложения `gga1c_app` (у него `CREATE USER`
и `GRANT OPTION` на `gga1c.*` — хаб сам выдаёт логины печати). Пароли — `C:\GGA1C\mysql-app.txt`,
`C:\GGA1C\mysql-root.txt`. Бэкап до перехода с SQLite — `C:\GGA1C\backup\sqlite-before-mysql-20261018.zip`.

## Прежний сервис

Сервис AltiumSync на `:5000` свёрнут 18.10.2026 — хаб перехватил приём от виджета и раздачу
виджета. Удалены `C:\gg1ac\publish`, `C:\gg1ac\wwwroot`, `C:\AltiumSync` (там лежали пароли
Altium, 1С, ключ Grist и куки браузера); всё сохранено в
`C:\GGA1C\backup\altiumsync-legacy-20261018.zip` (38 МБ, 260 файлов). Автозапуска, правил
брандмауэра и портпрокси у него не было — других следов не осталось. Исходники прежнего эскиза —
`C:\altiumsync-sketch` и история git (коммит `ec1cc49` и раньше). Из эскиза работало одно звено —
мост виджет → буфер этикеток; его контракт с виджетом перенесён в хаб как есть.
