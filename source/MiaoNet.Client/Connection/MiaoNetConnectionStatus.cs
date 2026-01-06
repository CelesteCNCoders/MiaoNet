namespace Celeste.Mod.MiaoNet;

public enum MiaoNetConnectionStatus
{
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Cancelled,
    ConnectFailedWithException,
    ConnectionAborted,
    ConnectionAbortedWithException,
    DisconnectedWithReason
}