namespace MiaoNet.Shared;

public sealed class PlayerPresenceMessage : IRefBinarySerializable<PlayerPresenceMessage>
{
    public string PlayerJoined { get; }

    public string PlayerLeft { get; }

    public PlayerPresenceMessage(string playerJoined, string playerLeft)
    {
        ArgumentNullException.ThrowIfNull(playerJoined);
        ArgumentNullException.ThrowIfNull(playerLeft);

        PlayerJoined = playerJoined;
        PlayerLeft = playerLeft;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerJoined);
        writer.Write(PlayerLeft);
    }

    public static PlayerPresenceMessage Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadString(), reader.ReadString());
    }
}