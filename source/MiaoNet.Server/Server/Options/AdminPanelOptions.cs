namespace MiaoNet.Server;

public sealed class AdminPanelOptions
{
    public bool Enabled { get; set; } = false;

    public string ForumBaseUrl { get; set; } = "https://bbs.celemiao.com";

    public string? ClientID { get; set; }

    public string? ClientSecret { get; set; }

    public string RedirectUri { get; set; } = "http://localhost:21474/admin/callback";

    /// <summary>
    /// OAuth scope(s) to request on login, space-separated. The forum's oauth-center
    /// guards /api/miaonet/admin-user with a scope, so the token must include it.
    /// </summary>
    public string? Scope { get; set; }

    public int SessionHours { get; set; } = 12;

    /// <summary>
    /// Debug only: skip OAuth login and treat every admin request as authenticated.
    /// Never enable this when the listener is reachable from outside.
    /// </summary>
    public bool DebugSkipAuth { get; set; } = false;
}
