namespace GGA1C.Sync.Labels;

/// <summary>
/// Учётные данные человека во внешних системах — то, что вводят в карточке учётки.
///
/// Пустой пароль означает «оставить прежний», а не «стереть»: наружу пароль не отдаётся,
/// поэтому форма приходит с пустым полем, и трактовать пустоту как очистку значило бы
/// терять пароль при каждом сохранении формы. Логины так не ведут себя — они не секретны,
/// подставляются в форму как есть, и пустой логин действительно стирает прежний.
/// </summary>
public record AccountCredentials(
    string? AltiumLogin,
    string? AltiumPassword,
    string? PdmLogin,
    string? PdmPassword,
    string? OneCLogin = null,
    string? OneCPassword = null);
