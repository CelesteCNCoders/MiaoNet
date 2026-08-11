namespace MiaoNet.Server;

public sealed class AdminPanelOptions
{
    public bool Enabled { get; set; } = false;

    public string ForumBaseUrl { get; set; } = "https://bbs.celemiao.com";

    public string? ClientID { get; set; }

    public string? ClientSecret { get; set; }

    public string RedirectUri { get; set; } = "http://localhost:21474/admin/callback";

    public int SessionHours { get; set; } = 12;

    /// <summary>
    /// Debug only: skip OAuth login and treat every admin request as authenticated.
    /// Never enable this when the listener is reachable from outside.
    /// </summary>
    public bool DebugSkipAuth { get; set; } = false;
}
