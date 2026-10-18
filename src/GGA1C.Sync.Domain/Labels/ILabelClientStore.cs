using System.Collections.Generic;
using System.Threading.Tasks;

namespace GGA1C.Sync.Labels;

public interface ILabelClientStore
{
    /// <summary>
    /// Найти учётку по предъявленному ключу. Поиск идёт по SHA-256, то есть одним
    /// сравнением и без расшифровки. Проверку IsEnabled делает вызывающий — чтобы можно
    /// было различить «ключ неизвестен» и «учётка отключена».
    /// </summary>
    Task<LabelClient?> FindByKeyAsync(string key);

    Task<List<LabelClient>> GetAllAsync();

    Task<LabelClient?> FindAsync(int id);

    /// <summary>
    /// Создать учётку. Возвращает её вместе с ключом в открытом виде — обычно это
    /// единственный момент, когда ключ существует вне базы.
    /// <paramref name="existingKey"/> нужен только для переноса: при обновлении сервиса
    /// прежний общий ключ сохраняется, иначе уже настроенные виджеты сразу отвалятся.
    /// </summary>
    Task<(LabelClient Client, string Key)> CreateAsync(string name, string slug, string? existingKey = null);

    /// <summary>Выпустить новый ключ. Старый сразу перестаёт работать.</summary>
    Task<string> RegenerateKeyAsync(int id);

    /// <summary>Показать ключ администратору (расшифровать сохранённую копию).</summary>
    Task<string?> RevealKeyAsync(int id);

    Task SetEnabledAsync(int id, bool enabled);

    Task DeleteAsync(int id);

    /// <summary>Отметить успешное использование ключа.</summary>
    Task TouchAsync(int id);

    // ── Для сверки с Keycloak ─────────────────────────────────────────────────

    /// <summary>Найти по идентификатору пользователя Keycloak (sub) — основная связь.</summary>
    Task<LabelClient?> FindBySubAsync(string sub);

    /// <summary>
    /// Найти по email. Нужно один раз: чтобы привязать к Keycloak учётку, которую до
    /// этого завели руками, а не плодить вторую на того же человека.
    /// </summary>
    Task<LabelClient?> FindByEmailAsync(string email);

    Task UpdateAsync(LabelClient client);

    /// <summary>
    /// Свободное имя папки на основе предложенного: при совпадении добавляет число.
    /// Две фамилии Иванов — обычное дело, и второй не должен ломать синхронизацию.
    /// </summary>
    Task<string> MakeUniqueSlugAsync(string baseSlug);

    /// <summary>
    /// Записать учётные данные человека во внешних системах. Пароли шифруются здесь —
    /// в открытом виде они не попадают ни в базу, ни обратно в интерфейс.
    /// </summary>
    Task SetCredentialsAsync(int id, AccountCredentials input);

    /// <summary>
    /// Учётные данные в открытом виде. Расшифровка живёт здесь, потому что ключ шифрования
    /// есть только у хранилища. Слой приложения наружу отдаёт не эти значения, а маски их
    /// длины; сами значения понадобятся потокам, которые ходят в Altium и PDM от имени
    /// человека.
    /// </summary>
    Task<AccountCredentials> RevealCredentialsAsync(int id);
}
