namespace MiaoNet.Server;

public sealed class ChatChannelStrings
{
    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;
}

public sealed class AnnouncementsStrings
{
    public string PlayerJoined { get; set; } = string.Empty;

    public string PlayerLeft { get; set; } = string.Empty;

    public string PlayerJoinMessage { get; set; } = string.Empty;

    public Dictionary<string, ChatChannelStrings>? ChatChannels { get; set; }

    public AnnouncementsStrings()
    {
    }

    public AnnouncementsStrings(string playerJoined, string playerLeft, string playerJoinMessage)
    {
        PlayerJoined = playerJoined;
        PlayerLeft = playerLeft;
        PlayerJoinMessage = playerJoinMessage;
    }
}
