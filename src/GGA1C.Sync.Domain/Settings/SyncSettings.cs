namespace GGA1C.Sync.Settings;

public static class SyncSettings
{
    private const string Prefix = "Sync";

    /// <summary>
    /// Ключ, которым виджет Grist подписывает запросы к буферу этикеток.
    /// Хранится шифрованным и клиентам не отдаётся.
    /// </summary>
    public const string GristApiKey = Prefix + ".GristApiKey";

    public static class Altium
    {
        private const string AltiumPrefix = Prefix + ".Altium";

        public const string ServerUrl = AltiumPrefix + ".ServerUrl";
        public const string User = AltiumPrefix + ".User";
        public const string Password = AltiumPrefix + ".Password";

        /// <summary>
        /// Заголовок Cookie, скопированный из DevTools браузера. Временный обход:
        /// программная сессия не даёт правильной пагинации компонентов (см. HANDOFF).
        /// Если заполнено - запросы идут с этими куками вместо собственной авторизации.
        /// </summary>
        public const string BrowserCookies = AltiumPrefix + ".BrowserCookies";

        public const string ApiComponents = AltiumPrefix + ".ApiComponents";
        public const string ApiFolders = AltiumPrefix + ".ApiFolders";
        public const string ApiProjects = AltiumPrefix + ".ApiProjects";
        public const string LifecycleFilter = AltiumPrefix + ".LifecycleFilter";
    }

    public static class OneC
    {
        private const string OneCPrefix = Prefix + ".OneC";

        public const string ServerUrl = OneCPrefix + ".ServerUrl";
        public const string BaseName = OneCPrefix + ".BaseName";
        public const string User = OneCPrefix + ".User";
        public const string Password = OneCPrefix + ".Password";
    }

    public static class Keycloak
    {
        private const string KeycloakPrefix = Prefix + ".Keycloak";

        /// <summary>Базовый адрес Keycloak, без /realms. Например http://kc.example.local:8080</summary>
        public const string BaseUrl = KeycloakPrefix + ".BaseUrl";

        public const string Realm = KeycloakPrefix + ".Realm";

        /// <summary>
        /// Клиент со включённой служебной учётной записью (client credentials).
        /// Ему нужна роль view-users из realm-management, иначе Admin API ответит 403.
        /// </summary>
        public const string ClientId = KeycloakPrefix + ".ClientId";

        public const string ClientSecret = KeycloakPrefix + ".ClientSecret";

        /// <summary>
        /// Публичный клиент, которым виджет в Grist доказывает, кто перед ним. Секрета у
        /// него нет и доступа ни к чему он не даёт — только подписанное удостоверение
        /// человека. Хаб принимает токены лишь этого клиента.
        /// </summary>
        public const string WidgetClientId = KeycloakPrefix + ".WidgetClientId";

        /// <summary>
        /// Клиент, которым люди входят в сам хаб (confidential, код + PKCE). Отдельный от
        /// служебного <see cref="ClientId"/>: у того роль view-users и нет браузерного входа,
        /// смешивать их в одном секрете нельзя. Пусто — входа через Keycloak в хабе нет.
        /// Читается при старте хаба: после правки нужен перезапуск.
        /// </summary>
        public const string LoginClientId = KeycloakPrefix + ".LoginClientId";

        public const string LoginClientSecret = KeycloakPrefix + ".LoginClientSecret";
    }

    public static class Setup
    {
        /// <summary>
        /// Когда через страницу первого запуска создан администратор. Пока значение пусто и
        /// в роли admin никого нет, страница /setup открыта; после — закрыта навсегда, и
        /// открыть её снова можно только стерев строку в AbpSettings.
        /// </summary>
        public const string FirstAdminCreatedAt = Prefix + ".Setup.FirstAdminCreatedAt";
    }

    public const string AutoSyncEnabled = Prefix + ".AutoSyncEnabled";
    public const string IntervalMinutes = Prefix + ".IntervalMinutes";

    /// <summary>Update | New | Skip - что делать с проектами по умолчанию.</summary>
    public const string DefaultSyncAction = Prefix + ".DefaultSyncAction";
}