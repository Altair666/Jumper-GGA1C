namespace GGA1C.Sync.Labels;

/// <summary>
/// Что ставится новому человеку по умолчанию. Пароль простой намеренно: учётка появляется
/// сама при сверке с Keycloak, человеку её сразу диктуют, а сменить можно в карточке.
/// Доступ он даёт только к своим строкам через представление my_labels.
/// </summary>
public static class PrintLoginDefaults
{
    public const string Password = "1234";
}
