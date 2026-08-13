namespace MiaoNet.Shared;

// A PlayerPresenceData paired with the owning player's ID, used in multi-player
// snapshots (e.g. PacketPlayerChannelMovedResponse.ChannelPlayers).
// Single-player notifications don't need it - they carry the ID on the packet
// itself (PacketPlayerNotification.PlayerID) and use the bare payload.
public readonly struct PlayerPresenceDataWithID : IRefBinarySerializable<PlayerPresenceDataWithID>
{
    public int PlayerID { get; }

    public PlayerPresenceData Data { get; }

    public PlayerPresenceDataWithID(int playerID, PlayerPresenceData data)
    {
        PlayerID = playerID;
        Data = data;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
        writer.Write(Data);
    }

    public static PlayerPresenceDataWithID Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<PlayerPresenceData>());
}
