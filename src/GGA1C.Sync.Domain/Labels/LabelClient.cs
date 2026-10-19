using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Учётная запись, от имени которой виджет Grist пишет этикетки. Разделение идёт по ключу:
/// ключ и есть удостоверение, по нему хаб понимает, в чей буфер писать.
///
/// Почему так, а не через Keycloak: Grist не отдаёт виджету личность пользователя —
/// в plugin API нет getUser, а сессионная кука помечена HttpOnly и SameSite=Lax, то есть
/// из JS не читается и кросс-сайтом не уходит. Ключ — единственное, что виджет может
/// предъявить достоверно.
///
/// Ключ хранится в двух видах. <see cref="KeyHash"/> — SHA-256, по нему идёт поиск при
/// каждом запросе: так учётка находится одним сравнением, а украденная база не отдаёт
/// рабочих ключей. <see cref="KeyEncrypted"/> — тот же ключ, зашифрованный ключом
/// StringEncryption (он лежит вне репозитория), чтобы администратор мог посмотреть
/// значение повторно, а не выпускать новое каждый раз, когда потерялась бумажка.
/// </summary>
public class LabelClient : AuditedEntity<int>
{
    /// <summary>Человекочитаемое имя: «Иванов, участок сборки», «Печать ОТК».</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Имя папки буфера внутри Widget/Labels-корня. Только латиница, цифры, дефис и
    /// подчёркивание — значение попадает в путь на диске и в адрес шары для GoLabel2.
    /// </summary>
    public string Slug { get; set; } = "";

    /// <summary>SHA-256 ключа в hex. По нему ищем учётку.</summary>
    public string KeyHash { get; set; } = "";

    /// <summary>Первые символы ключа — чтобы различать учётки в интерфейсе, не раскрывая ключ.</summary>
    public string KeyPrefix { get; set; } = "";

    /// <summary>Ключ целиком, зашифрованный. Нужен только для показа администратору.</summary>
    public string KeyEncrypted { get; set; } = "";

    /// <summary>
    /// Отключённая учётка получает 401, но не удаляется: буфер и история остаются,
    /// и включить обратно можно без перевыпуска ключа.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Когда ключом последний раз успешно пользовались. Помогает найти мёртвые учётки.</summary>
    public DateTime? LastUsedAt { get; set; }

    // ── Связь с Keycloak ──────────────────────────────────────────────────────

    /// <summary>
    /// Идентификатор пользователя в Keycloak (sub). Связь держим именно по нему, а не по
    /// email или логину: email и имя человек меняет, sub — нет. Пусто у учёток, заведённых
    /// руками.
    /// </summary>
    public string? KeycloakSub { get; set; }

    public string? KeycloakUsername { get; set; }

    /// <summary>
    /// Email из Keycloak. Нужен не для связи, а для правил доступа в Grist: именно по
    /// user.Email Grist отдаёт виджету строку с его ключом.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Когда учётку отключила синхронизация, а не человек. Различать обязательно: иначе
    /// следующая же синхронизация включит обратно того, кого администратор выключил руками,
    /// и отзыв доступа молча отменится.
    /// </summary>
    public DateTime? SyncDisabledAt { get; set; }

    /// <summary>Когда последний раз сверялись с Keycloak.</summary>
    public DateTime? SyncedAt { get; set; }

    // ── Учётные данные во внешних системах ────────────────────────────────────

    /// <summary>
    /// Логин человека в Altium Designer (Altium Enterprise Server). Нужен, чтобы выгрузка
    /// компонентов шла от его имени: в журнале Altium остаётся он, а не одна служебная
    /// учётка на весь завод.
    /// </summary>
    public string? AltiumLogin { get; set; }

    /// <summary>
    /// Пароль Altium, зашифрованный ключом StringEncryption (он лежит вне репозитория).
    /// Наружу не отдаётся никогда — ни в интерфейс, ни в API: в интерфейсе видно только,
    /// задан он или нет.
    /// </summary>
    public string? AltiumPasswordEncrypted { get; set; }

    /// <summary>
    /// Логин в хранилище SOLIDWORKS PDM. Хранить его приходится потому, что PDM не умеет
    /// ни SAML, ни OIDC, а значит через Keycloak в него не войти; способа действовать от
    /// имени человека без его пароля PDM тоже не даёт.
    /// </summary>
    public string? PdmLogin { get; set; }

    /// <summary>Пароль PDM, зашифрованный тем же ключом. См. оговорку у <see cref="PdmLogin"/>.</summary>
    public string? PdmPasswordEncrypted { get; set; }

    /// <summary>
    /// Логин этого человека в MySQL, которым подключается GoLabel II. Пусто, пока логин
    /// не выдан. Пароль у нас не хранится вовсе: он показывается один раз при выдаче —
    /// как ключ виджета. Забыл — выдаётся новый.
    /// </summary>
    public string? PrintLogin { get; set; }

    /// <summary>
    /// Пароль этого человека к MySQL, зашифрованный ключом StringEncryption. В отличие от
    /// ключа виджета и паролей Altium/PDM, его **можно посмотреть** в карточке: человек
    /// вписывает его в GoLabel на машине с принтером, и администратору приходится его
    /// диктовать. По умолчанию при создании учётки ставится 1234 — меняется там же.
    /// </summary>
    public string? PrintPasswordEncrypted { get; set; }

    /// <summary>Когда учётные данные последний раз меняли. Видно в интерфейсе, чтобы было
    /// понятно, не устарел ли пароль после смены в домене.</summary>
    public DateTime? CredentialsUpdatedAt { get; set; }
}
