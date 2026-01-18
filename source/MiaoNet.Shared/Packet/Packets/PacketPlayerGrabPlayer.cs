namespace MiaoNet.Shared;

public sealed class PacketPlayerGrabPlayer : IContextlessPacket<PacketPlayerGrabPlayer>
{
    // TODO broadcast to all players

    /// <summary>
    /// client -> server: The player that the client grabbed
    /// <br/>
    /// server -> client: The player that grabbed the client
    /// </summary>
    public int PlayerID { get; }

    public bool IsRelease { get; }

    public Vector2 Force { get; }

    public PacketPlayerGrabPlayer(int playerID)
    {
        PlayerID = playerID;
        IsRelease = false;
    }

    public PacketPlayerGrabPlayer(int playerID, Vector2 force)
    {
        PlayerID = playerID;
        IsRelease = true;
        Force = force;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
        if (!IsRelease)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(Force);
        }
    }

    public static PacketPlayerGrabPlayer Deserialize(ref RefBinaryReader reader)
    {
        int playerID = reader.ReadInt32();
        bool isRelease = reader.ReadBoolean();
        if (!isRelease)
            return new(playerID);
        else
            return new(playerID, reader.ReadVector2());
    }
}