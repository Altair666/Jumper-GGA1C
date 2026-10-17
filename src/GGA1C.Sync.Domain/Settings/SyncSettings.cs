namespace GGA1C.Sync.Settings;

public static class SyncSettings
{
    private const string Prefix = "Sync";

    /// <summary>
    /// Ключ, которым виджет Grist подписывает запросы к буферу этикеток.
    /// Хранится шифрованным (см. SyncSettingDefinitionProvider) и клиентам не отдаётся.
    /// </summary>
    public const string GristApiKey = Prefix + ".GristApiKey";
}