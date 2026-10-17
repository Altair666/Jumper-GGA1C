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

    public const string AutoSyncEnabled = Prefix + ".AutoSyncEnabled";
    public const string IntervalMinutes = Prefix + ".IntervalMinutes";

    /// <summary>Update | New | Skip - что делать с проектами по умолчанию.</summary>
    public const string DefaultSyncAction = Prefix + ".DefaultSyncAction";
}