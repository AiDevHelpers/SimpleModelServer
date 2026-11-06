namespace SimpleModelServer.Contracts.Configuration;

/// <summary>
///     One switchable model configuration from <c>models.configurations</c> (e.g. "Fast &amp; Responsive").
///     Selecting one is done with <c>global.configurationId</c>; the server applies it on restart.
/// </summary>
/// <param name="Id">Stable configuration id, the value <c>global.configurationId</c> refers to.</param>
/// <param name="Nickname">Human-readable name shown in clients.</param>
/// <param name="IsDefault">True when this is the profile used if no id is selected.</param>
public sealed record ModelProfile(int Id, string Nickname, bool IsDefault);
