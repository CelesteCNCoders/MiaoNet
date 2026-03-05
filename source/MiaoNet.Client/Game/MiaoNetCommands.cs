namespace Celeste.Mod.MiaoNet;

public static class MiaoNetCommands
{
    [Command("con", "Connect to MiaoNet.")]
    public static void Connect(string? server = null, string? port = null)
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        if (server is not null)
           ctx.TargetServer = server;
        if (port is not null && int.TryParse(port, out var num) && num is > 0 and <= 65535)
            ctx.TargetPort = num;
        ctx.Connect();
    }

    [Command("dc", "Disconnect from MiaoNet.")]
    public static void Disconnect()
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        ctx.Disconnect();
    }

    [Command("trust_all", "Trust all certificates when connecting to MiaoNet.")]
    public static void TrustAllCertificates()
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        ctx.TrustAllCertificates = true;
    }

    [Command("distrust_all", "Do not trust all certificates when connecting to MiaoNet.")]
    public static void DistrustAllCertificates()
    {
        var ctx = MiaoNetModule.Instance.MiaoNetContext;
        ctx.TrustAllCertificates = false;
    }
}