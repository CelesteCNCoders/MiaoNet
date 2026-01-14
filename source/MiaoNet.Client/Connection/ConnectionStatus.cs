namespace Celeste.Mod.MiaoNet;

public static class ConnectionStatus
{
    private static string Base => "miaonet_connection_status_{0}";

    public static string Connecting => Dialog.Get(string.Format(Base, "connecting"));

    public static string VersionNotMatch(Version local, Version remote)
        => Dialog.Get(string.Format(Base, "version_not_match"))
            .Replace("(0)", local.ToString(3))
            .Replace("(1)", remote.ToString(3));

    public static string DisconnectedExceptionally => Dialog.Get(string.Format(Base, "disconnected_exceptionally"));

    public static string Connected => Dialog.Get(string.Format(Base, "connected"));

    public static string Disconnected => Dialog.Get(string.Format(Base, "disconnected"));

    public static string Cancelled => Dialog.Get(string.Format(Base, "cancelled"));

    public static string ConnectFailedWithReason(string reason)
        => Dialog.Get(string.Format(Base, "connect_failed_with_reason"))
            .Replace("(0)", reason);

    public static string DisconnectedWithReason(string reason)
        => Dialog.Get(string.Format(Base, "disconnected_exceptionally_with_reason"))
            .Replace("(0)", reason);
}
