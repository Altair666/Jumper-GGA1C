namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Пользователь реалма Keycloak — ровно те поля, что нужны для учёток виджета.
/// </summary>
public class KeycloakUser
{
    /// <summary>Идентификатор пользователя (он же sub в токене). Устойчив, по нему и связываем.</summary>
    public string Id { get; set; } = "";

    public string Username { get; set; } = "";

    public string? Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>
    /// Снятый флаг означает, что человеку закрыли доступ. Для нас это сигнал отключить
    /// его учётку виджета — ключ начнёт отвечать 401.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Имя для учётки: «Фамилия Имя», если они заданы, иначе логин. Фамилия идёт первой
    /// намеренно — из неё выводится имя папки буфера.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = new[] { LastName, FirstName };
            var name = string.Join(" ", System.Array.FindAll(parts, p => !string.IsNullOrWhiteSpace(p)));
            return string.IsNullOrWhiteSpace(name) ? Username : name;
        }
    }
}
