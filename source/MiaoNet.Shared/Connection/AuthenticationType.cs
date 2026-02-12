namespace MiaoNet.Shared;

public enum AuthenticationType : byte
{
    /// <summary>
    /// first login, or refresh_token is expired
    /// </summary>
    Authorize,
    /// <summary>
    /// second login, no external request.
    /// fallback to <see cref="SyncRefresh"/> if access_token is expired
    /// </summary>
    QuickLogin,
    /// <summary>
    /// second login, but with external requests to update sth.
    /// will update access_token.
    /// </summary>
    SyncRefresh
}