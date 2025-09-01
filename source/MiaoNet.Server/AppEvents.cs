using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public static class AppEvents
{
    public static readonly EventId Connection = new(10, "Connection");
    public static readonly EventId Game = new(11, "Game");
    public static readonly EventId Channel = new(12, "Channel");
    public static readonly EventId Chat = new(13, "Chat");
    public static readonly EventId Command = new(14, "Command");
}