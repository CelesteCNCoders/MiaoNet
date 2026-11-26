using MiaoNet.Shared;

namespace MiaoNet.Server;

public enum ChatChannelScope : byte
{
    Global,
    PlayerChannel,
    PlayerMap,
    ReadOnly
}

public sealed class ServerChatChannel
{
    public const int PublicID = 0;
    public const int ChannelID = 1;
    public const int MapID = 2;

    public int ID { get; }

    public string Key { get; }

    public ChatChannelScope Scope { get; }

    public Color Color { get; }

    public ChatChannelFlags Flags { get; }

    public string DefaultName { get; }

    public string DefaultPrefix { get; }

    public bool CanSend => Flags.HasFlag(ChatChannelFlags.CanSend);

    public ServerChatChannel(
        int id,
        string key,
        ChatChannelScope scope,
        Color color,
        ChatChannelFlags flags,
        string defaultName,
        string defaultPrefix
    )
    {
        ID = id;
        Key = key;
        Scope = scope;
        Color = color;
        Flags = flags;
        DefaultName = defaultName;
        DefaultPrefix = defaultPrefix;
    }

    public ChatChannelInfo ToInfo(IReadOnlyDictionary<string, ChatChannelStrings> strings)
    {
        strings.TryGetValue(Key, out var localized);
        return new(ID, Key, localized?.Name ?? DefaultName, localized?.Prefix ?? DefaultPrefix, Color, Flags);
    }

    public static IReadOnlyList<ServerChatChannel> CreateDefaultChannels() =>
    [
        new(
            PublicID,
            "public",
            ChatChannelScope.Global,
            Color.White,
            ChatChannelFlags.CanSend,
            "Public",
            string.Empty
        ),
        new(
            ChannelID,
            "channel",
            ChatChannelScope.PlayerChannel,
            new Color(211, 211, 211),
            ChatChannelFlags.CanSend,
            "Channel",
            "[Channel Chat]"
        ),
        new(
            MapID,
            "map",
            ChatChannelScope.PlayerMap,
            new Color(0, 255, 255),
            ChatChannelFlags.CanSend,
            "Map",
            "[Map Chat]"
        )
    ];
}
