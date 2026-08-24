namespace MiaoNet.Server;

public sealed class AnnouncementsStrings
{
    public string PlayerJoined { get; }

    public string PlayerLeft { get; }

    public string PlayerJoinMessage { get; }

    public AnnouncementsStrings(string playerJoined, string playerLeft, string playerJoinMessage)
    {
        PlayerJoined = playerJoined;
        PlayerLeft = playerLeft;
        PlayerJoinMessage = playerJoinMessage;
    }
}
