using System.Collections.Generic;

namespace GGA1C.Sync.Keycloak;

/// <summary>
/// Итог сверки учёток с Keycloak. Показывается в интерфейсе, чтобы было видно не только
/// «готово», но и что именно изменилось — без этого непонятно, сработала сверка или нет.
/// </summary>
public class KeycloakSyncReport
{
    public bool Ok { get; set; } = true;

    /// <summary>Сообщение, если сверка не выполнялась или упала.</summary>
    public string? Message { get; set; }

    public int Checked { get; set; }

    /// <summary>Заведено новых учёток (каждой сразу выписан ключ).</summary>
    public int Created { get; set; }

    /// <summary>Обновлены имя, email или логин.</summary>
    public int Updated { get; set; }

    /// <summary>Привязано к Keycloak учёток, ранее созданных руками.</summary>
    public int Linked { get; set; }

    /// <summary>Отключено — человеку сняли доступ в Keycloak.</summary>
    public int Disabled { get; set; }

    /// <summary>Включено обратно — доступ вернули, и отключала их именно сверка.</summary>
    public int Restored { get; set; }

    /// <summary>Пропущено: нет email, а без него Grist не отдаст виджету ключ.</summary>
    public int SkippedWithoutEmail { get; set; }

    /// <summary>Подробные строки для показа в интерфейсе.</summary>
    public List<string> Details { get; set; } = new();
}
