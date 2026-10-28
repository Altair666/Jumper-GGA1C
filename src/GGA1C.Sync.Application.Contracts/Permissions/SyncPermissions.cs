namespace GGA1C.Sync.Permissions;

public static class SyncPermissions
{
    public const string GroupName = "Sync";

    /// <summary>
    /// Администрирование хаба: учётки людей с их ключами и паролями печати, подключения
    /// Keycloak, Altium и 1С. Пока вошедший был один (<c>admin</c>), хватало
    /// <c>[Authorize]</c>; с входом через Keycloak в хаб попадает любой сотрудник, и без
    /// отдельного права он увидел бы чужие секреты. Роль <c>admin</c> получает право
    /// засевом DbMigrator (ABP выдаёт ей все определённые права).
    /// </summary>
    public const string Admin = GroupName + ".Admin";
}
