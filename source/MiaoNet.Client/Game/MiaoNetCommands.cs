namespace Celeste.Mod.MiaoNet;

public static class MiaoNetCommands
{
    private static MiaoNetContext Context => MiaoNetModule.Instance.MiaoNetContext;

    [Command("con", "Connect to MiaoNet.")]
    public static void Connect(string? server = null, string? port = null)
    {
        if (server is not null)
            Context.TargetServer = server;
        if (port is not null && int.TryParse(port, out var num))
            Context.TargetPort = num;
        Context.Connect();
    }

    [Command("dc", "Disconnect from MiaoNet.")]
    public static void Disconnect()
    {
        Context.Disconnect();
    }
}