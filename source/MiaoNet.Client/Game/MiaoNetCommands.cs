namespace Celeste.Mod.MiaoNet;

public static class MiaoNetCommands
{
    [Command("con", "Connect to MiaoNet.")]
    public static void Connect(string? server = null, string? port = null)
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        if (server is not null)
            ctx.TargetServer = server;
        if (port is not null && int.TryParse(port, out var num))
            ctx.TargetPort = num;
        ctx.Connect();
    }

    [Command("dc", "Disconnect from MiaoNet.")]
    public static void Disconnect()
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        ctx.Disconnect();
    }

#if !USE_CELEMIAO_AUTH
    [Command("mn_avatar", "Set the avatar of miaonet")]
    public static void SetAvatar(string? url)
    {
        MiaoNetModule.Settings.AvatarUrl = url;
    }
#endif

#if DEBUG
    [Command("mn_status", "Show a MiaoNet status message.")]
    public static void ShowStatus(string text, bool spin = false)
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        ctx.StatusComponent.ShowStatusMessage(text, spin);
    }
#endif
}