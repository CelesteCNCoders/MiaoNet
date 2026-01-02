namespace Celeste.Mod.MiaoNet;

public enum MiaoNetConnectionStatus
{
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    ConnectFailedWithException,
    ConnectionAborted,
    ConnectionAbortedWithException,
    DisconnectedWithReason
}