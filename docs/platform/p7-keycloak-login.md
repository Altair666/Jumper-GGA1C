# П7. Вход в хаб через Keycloak — разведка и план

Разведка 2026-10-28, кода нет. Карточка — `docs/roadmap.md`, П7. Проверено чтением кода
ветки `main` (`a17aea2`), декомпиляцией пакетов ABP 10.7.0 из кэша NuGet (`ilspycmd`) и
чтением клиентов реалма `test` через admin API (только GET). Что не проверено — помечено
«не проверено».

## Как устроено сейчас

- `SyncBlazorModule` подключает `AbpAccountWebOpenIddictModule`: сам хаб — сервер OpenIddict,
  вход — Razor-страница `/Account/Login` (тема LeptonX Lite), кука Identity
  (`IdentityConstants.ApplicationScheme`). `ConfigureAuthentication` только перекидывает
  Bearer на валидацию OpenIddict и включает `IsDynamicClaimsEnabled`. Внешних схем входа нет.
- В `OpenIddictDataSeedContributor` заводится только клиент Swagger. Пользователь один —
  `admin` из сеялки ABP.
- **Страницы `/labels`, `/keycloak`, `/altium-1c` закрыты только `[Authorize]`**, а
  `ConnectionsAppService` и `LabelClientsAppService` — только `[Authorize]`; своих разрешений
  в `SyncPermissions` нет. Сейчас это безопасно, потому что вошедший один — `admin`. **Как
  только войдёт любой человек из Keycloak, он увидит ключи, пароли печати и настройки
  Keycloak/Altium.** Значит, П7 без разрешения на админ-страницы делать нельзя.
- **Уже сейчас открыта самостоятельная регистрация.** `Abp.Account.IsSelfRegistrationEnabled`
  по умолчанию `true` (декомпиляция `AccountSettingDefinitionProvider`), в `AbpSettings`
  переопределений `Abp.Account.*` нет (запрос к базе), и `GET
  https://192.168.88.238:5443/Account/Register` на живом стенде отдаёт 200 с формой
  «Username / Email address». Значит, любой в сети может завести себе локальную учётку и с ней
  попасть на страницы под `[Authorize]`. Саму регистрацию (POST) не пробовал — не проверено,
  что она проходит до конца, но препятствий в коде не видно. Закрыть — в шаге 1 плана.
- Главная (`Index.razor`) — заглушка шаблона ABP; «кто я» нигде не показывается.
- Личность из Keycloak уже используется в другом месте: `KeycloakTokenVerifier` проверяет
  токен виджета (`gga1c-widget`) по JWKS реалма; адрес и реалм берёт из настроек
  `Sync.Keycloak.BaseUrl` / `Realm`.
- Люди — `LabelClient` с `KeycloakSub`; их заводит и выключает сверка `KeycloakAccountSync`
  (раз в час, `KeycloakSyncWorker`). Отдельного метода «сверить одного человека» нет —
  создание (`CreateFromUserAsync`) приватное.

## Что даёт ABP 10.7 для внешнего входа (проверено декомпиляцией)

- Кнопка на странице входа появляется сама: `LoginModel.GetExternalProviders()` берёт все схемы
  с непустым `DisplayName`. Достаточно `AddOpenIdConnect("Keycloak", "Keycloak", ...)` с
  `SignInScheme = IdentityConstants.ExternalScheme`.
- Пакет `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.11 **уже приходит
  транзитивно** от `Volo.Abp.AspNetCore` 10.7.0 (nuspec) — нового NuGet не нужно.
- Штатный `OnGetExternalLoginCallbackAsync` (в `LoginModel`; `OpenIddictSupportedLoginModel`
  его не переопределяет, она регистрируется как `[ExposeServices(typeof(LoginModel))]`):
  1. `ExternalLoginSignInAsync(provider, sub)` — если связь в `AbpUserLogins` есть, входит;
  2. иначе ищет пользователя **по email** и молча привязывает к нему внешний вход;
  3. иначе уводит на `/Account/Register?IsExternalLogin=true`, а регистрация требует
     включённой самостоятельной регистрации и нажатия кнопки человеком.

  Для нас пункт 2 опасен (связь у нас по `sub`, не по email; email в Keycloak правится, и
  `lmp.admin` носит почту владельца), пункт 3 не нужен. Поэтому колбэк **переопределяем**:
  свой класс-наследник `OpenIddictSupportedLoginModel` с
  `[ExposeServices(typeof(LoginModel), typeof(OpenIddictSupportedLoginModel))]` и
  `[Dependency(ReplaceServices = true)]`, переопределён только
  `OnGetExternalLoginCallbackAsync`. Разметку страницы не трогаем.
- `AbpSignInManager.PreSignInCheck` возвращает `NotAllowed`, если `IdentityUser.IsActive =
  false`; колбэк на это отвечает «Cannot proceed because user is not allowed!».
- Кука проверяется `AbpSecurityStampValidator` (зарегистрирован в
  `AbpIdentityAspNetCoreModule`): смена штампа безопасности выкидывает сессию при следующей
  проверке (интервал `SecurityStampValidatorOptions.ValidationInterval`, по умолчанию в
  ASP.NET Core — 30 минут; не проверено на стенде).
- Динамические утверждения (`IdentityDynamicClaimsPrincipalContributor`) выкидывают сессию
  только если пользователь **удалён**; `IsActive = false` они не проверяют.
- Для проверки сессий (`IIdentitySessionChecker`) в `Volo.Abp.Identity.Domain` нашлась только
  заглушка `NullIdentitySessionChecker`; что именно зарегистрировано в хабе — не проверено,
  на этот механизм не опираемся.

## Связь пользователя ABP с `LabelClient`

Новых столбцов не нужно: связь — запись в `AbpUserLogins` (провайдер `Keycloak`, ключ = `sub`),
а `LabelClient` ищется по тому же `sub` (`ILabelClientStore.FindBySubAsync`). В колбэке:

1. `sub` из внешнего входа (`ExternalLoginInfo.ProviderKey` — это `ClaimTypes.NameIdentifier`,
   куда обработчик OIDC кладёт `sub`; не проверено на стенде).
2. `LabelClient` по `sub`. Нет — сверить одного человека (вынести из `KeycloakAccountSync`
   публичный метод «завести/обновить по `KeycloakUser`»; данные — из утверждений токена или
   одним запросом `GET /admin/realms/test/users/{sub}` служебной учёткой `gga1c-sync`). Не
   вышло (нет фамилии для папки и т.п.) — отказ с понятным текстом.
3. `LabelClient.IsEnabled = false` — отказ («учётка отключена в хабе»), даже если Keycloak
   пустил.
4. Пользователь ABP по `FindByLoginAsync("Keycloak", sub)`. Нет — создать
   (`UserName` = `preferred_username`, `Email` из токена, `Name`/`Surname` из
   `given_name`/`family_name`, без пароля) и `AddLoginAsync`. Есть — обновить имя и почту.
   **По email к существующему не привязывать** (иначе `lmp.admin` с почтой владельца мог бы
   оказаться привязан к чужой учётке).
5. `SignInAsync`, сброс кэша динамических утверждений, возврат по `ReturnUrl`.

Локальный `admin` не трогается: форма логина/пароля остаётся (`Abp.Account.EnableLocalLogin`
по умолчанию включён), это и есть аварийный вход.

## «Отключён в Keycloak»

- **При входе.** Отключённому Keycloak не выдаёт код — штатное поведение Keycloak (на стенде
  не проверено: проверка требует выключить учётку в реалме, это правка Keycloak). Второй
  рубеж — шаг 3 колбэка: `LabelClient.IsEnabled`.
- **В открытой сессии.** Сверка (`KeycloakAccountSync`) при отключении `LabelClient` ставит
  связанному `IdentityUser` `IsActive = false` и меняет штамп безопасности; при возврате
  доступа сверкой — `IsActive = true`. Кука умирает на ближайшей проверке штампа; интервал
  проверки сократить до нескольких минут. Итоговая задержка — до часа сверки плюс интервал.
  Если это много — отдельным решением: чаще сверять или проверять человека в Keycloak при
  проверке куки.
- **Blazor Server.** Уже открытый «контур» (circuit) живёт на состоянии входа, полученном при
  подключении, и куку повторно не проверяет; в `Volo.Abp.AspNetCore.Components.Server`/`.Web`
  своего перепроверяющего `AuthenticationStateProvider` не нашлось (поиск по именам классов,
  другие сборки не смотрел). Для П7 достаточно, что следующая
  загрузка страницы уже не пустит; периодическая перепроверка контура — по желанию (не
  проверено).
- Выход из хаба гасит только куку хаба; сессия Keycloak остаётся, и повторный вход пройдёт без
  пароля. Полный выход (end_session Keycloak) — по желанию, нужен post-logout redirect URI.

## Клиент Keycloak

Клиенты реалма `test` (прочитано admin API 2026-10-28):

| Клиент | Тип | Потоки | Redirect URI |
|---|---|---|---|
| `gga1c-sync` | confidential | только служебная учётка | нет |
| `gga1c-widget` | public, PKCE S256 | standard flow | `…:5443/widget_report_grist/*` и http-варианты |
| `grist` | confidential | standard + служебная | `https://192.168.88.238:8444/*`, … |
| `docmost`, `test-app` | чужие | | |

Таймауты реалма: простой SSO-сессии 1800 с, максимум 36000 с, access token 300 с.

**Нужен новый клиент**, переиспользовать нельзя: `gga1c-sync` — служебная учётка с
`view-users`, включать ей браузерный вход значит смешать роли одного секрета;
`gga1c-widget` — публичный, его `azp` принимает `KeycloakTokenVerifier`, и redirect URI у него
открыт только под виджет. Предлагаемые настройки (создать — после согласия):

- `clientId`: `gga1c-hub`, confidential (Client authentication ON), Standard flow ON, Direct
  access grants OFF, Service accounts OFF, PKCE S256;
- Valid redirect URIs: `https://192.168.88.238:5443/signin-oidc`, для dev —
  `https://localhost:5444/signin-oidc` (и при нужде `https://192.168.88.238:5444/signin-oidc`);
- Valid post logout redirect URIs: `https://192.168.88.238:5443/*`, `https://localhost:5444/*`;
- Web origins: не нужны (поток идёт через сервер);
- scope: `openid profile email`;
- секрет — в файл вне репозитория (по образцу `/opt/grist/gga1c-sync-secret.txt`), в хаб —
  переменной окружения.

## Настройки хаба

Адрес и реалм — существующие `Sync.Keycloak.BaseUrl` / `Realm`. Новые:
`Sync.Keycloak.LoginClientId`, `Sync.Keycloak.LoginClientSecret` (шифруется), засев из
`Keycloak__LoginClientId` / `Keycloak__LoginClientSecret` тем же
`KeycloakSettingsDataSeedContributor`. Настройки ABP хранятся в `AbpSettings`
(ключ-значение) — **миграции не нужно**. Параметры OIDC читаются при первом обращении к схеме;
правка в UI применяется после перезапуска (так проще и предсказуемее, чем перечитывать
discovery на ходу).

## Миграции и пакеты

- **NuGet:** не нужен (см. выше). Если всё же захочется явной ссылки на
  `Microsoft.AspNetCore.Authentication.OpenIdConnect` — это новый пакет, спросить.
- **Миграции БД:** не нужны. `AbpUserLogins`, `AbpUsers`, `AbpSettings`,
  `AbpPermissionGrants` уже созданы миграцией `Initial`. Выдача нового разрешения роли `admin` —
  строка данных (засев), не схема.

## Разрешение на админ-страницы

Минимум, без которого вход людей открывает им всё: разрешение `Sync.Admin` в
`SyncPermissionDefinitionProvider`; `/labels`, `/keycloak`, `/altium-1c`,
`ConnectionsAppService`, `LabelClientsAppService` — `[Authorize(SyncPermissions.Admin)]`;
пункты меню — `requiredPermissionName`. Роль `admin` получает его засевом: ABP-шный
`PermissionDataSeedContributor` выдаёт роли `admin` все определённые разрешения (проверено
декомпиляцией), а засев в хабе запускает только DbMigrator (`SyncDbMigrationService`; сам
хаб при старте не сеет — проверено поиском). Человек из Keycloak без ролей видит только главную.

## Dev-проверка, не трогая живой стенд

Живой хаб — PID 13068, `C:\gg1ac\src\GGA1C.Sync.Blazor\bin\Release\...`, порты 5443/5080.
Порты 5444/5081 свободны (проверено `Get-NetTCPConnection`).

```
cd C:\gg1ac-wt\p7\src\GGA1C.Sync.Blazor
# переменные — как в CLAUDE.md: ConnectionStrings__Default, StringEncryption__DefaultPassPhrase,
# ASPNETCORE_Kestrel__Certificates__Default__Path / __Password
# плюс Keycloak__LoginClientId / Keycloak__LoginClientSecret (если засев ещё не прошёл)
dotnet run --no-launch-profile -c Debug --urls "https://0.0.0.0:5444;http://0.0.0.0:5081"
```

Грабли общей базы и одного хоста:

- **В worktree нет `openiddict.pfx`** (он в `.gitignore`, лежит только в
  `C:\gg1ac\src\GGA1C.Sync.Blazor\` — проверено). Без `ASPNETCORE_ENVIRONMENT=Development`
  модуль берёт `AddProductionEncryptionAndSigningCertificate("openiddict.pfx", …)` — файл
  скопировать в `C:\gg1ac-wt\p7\src\GGA1C.Sync.Blazor\` перед запуском (в git он не попадёт).

- **Куки не различают порты.** Хаб на `192.168.88.238:5443` и на `192.168.88.238:5444`
  пишут куку с тем же именем — вход в один перезаписывает куку другого (а прочесть чужую
  куку они, вероятно, не смогут: ключи Data Protection по умолчанию привязаны к каталогу
  приложения — не проверено). Поэтому dev открывать по
  **`https://localhost:5444`** (в SAN сертификата `gga1c-stand.crt` есть `localhost` —
  проверено) — другой хост, свои
  куки. Отсюда redirect URI `https://localhost:5444/signin-oidc`.
- **Фоновые работники запустятся дважды** на одной базе (сверка Keycloak, автосинхронизация
  Altium, уборка сессий GoLabel). Сверка идемпотентна; на время dev-проверки лучше выключить
  работников в dev-экземпляре флагом конфигурации (не проверено, нужна правка
  `OnPostApplicationInitializationAsync`).
- Засев (`KeycloakSettingsDataSeedContributor`, разрешения) идёт через DbMigrator — он пишет
  в общую базу, то есть и для живого стенда. Новые настройки безвредны для живого хаба
  (старый код их не читает), но запуск DbMigrator всё равно согласовать.
- Пользователи ABP, созданные при dev-входе, — в общей базе; живой хаб их увидит в
  «Пользователях», но войти ими не сможет (у него нет схемы Keycloak).

## План (после «ок»)

1. **Разрешение `Sync.Admin`** — `Application.Contracts/Permissions/SyncPermissions.cs`,
   `SyncPermissionDefinitionProvider.cs`; атрибуты в `Blazor/Components/Pages/Labels.razor`,
   `KeycloakPage.razor`, `Altium1C.razor`, `Application/Connections/ConnectionsAppService.cs`,
   `Application/Labels/LabelClientsAppService.cs`; меню `Blazor/Menus/SyncMenuContributor.cs`;
   выключить самостоятельную регистрацию (`Abp.Account.IsSelfRegistrationEnabled = false`
   засевом настройки в Domain).
2. **Настройки клиента входа** — `Domain/Settings/SyncSettings.cs`,
   `SyncSettingDefinitionProvider.cs`, `Domain/Keycloak/KeycloakSettingsDataSeedContributor.cs`;
   поля на странице `/keycloak` (`KeycloakPage.razor(.cs)`, `KeycloakSettingsDto`).
3. **Схема OIDC** — `Blazor/SyncBlazorModule.cs` (`ConfigureAuthentication`:
   `AddOpenIdConnect("Keycloak", "Keycloak", …)`, `SignInScheme = ExternalScheme`, code + PKCE,
   `GetClaimsFromUserInfoEndpoint`, scope `openid profile email`; параметры — из настроек через
   `IConfigureNamedOptions<OpenIdConnectOptions>` в новом файле `Blazor/Keycloak/`).
4. **Свой колбэк входа** — новый `Blazor/Pages/Account/SyncLoginModel.cs` (наследник
   `OpenIddictSupportedLoginModel`, только `OnGetExternalLoginCallbackAsync`, шаги из раздела
   «Связь»); для заведения по `sub` — публичный метод в `Domain/Keycloak/KeycloakAccountSync.cs`
   (+ `KeycloakClient.GetUserAsync(sub)`).
5. **Отзыв доступа** — `KeycloakAccountSync`: при отключении/включении `LabelClient`
   переключать `IdentityUser.IsActive` и штамп; `ValidationInterval` — в `SyncBlazorModule`.
6. **«Видит себя»** — карточка на `Blazor/Components/Pages/Index.razor(.cs)`: имя, логин,
   почта, папка буфера и состояние `LabelClient`.
7. **Проверка** — dev-экземпляр на `https://localhost:5444`: вход `a.morozov` → видит себя и
   не видит админ-страниц; `admin` по паролю — видит всё; отключённый в Keycloak (выключает
   пользователь или по его согласию) — не входит; после сверки открытая сессия гаснет.
   `dotnet build -c Release`, `dotnet test`.

## Что нужно согласовать до кода

1. Создать клиента `gga1c-hub` в реалме `test` с настройками выше (правка Keycloak).
2. Ввести разрешение `Sync.Admin`, закрыть им админ-страницы и выключить самостоятельную
   регистрацию (выходит за букву карточки, но без этого вход людей открывает им секреты, а
   регистрация открыта уже сейчас).
3. Новые настройки `Sync.Keycloak.LoginClientId` / `LoginClientSecret` и запуск DbMigrator на
   общей базе (засев настроек и разрешений; схема не меняется).
4. Человек без `LabelClient` при входе: заводить на лету (предложение) или отказывать до
   ближайшей сверки.
5. Задержка отзыва в открытой сессии «до часа + интервал проверки куки» — достаточно, или
   нужна частая сверка.
