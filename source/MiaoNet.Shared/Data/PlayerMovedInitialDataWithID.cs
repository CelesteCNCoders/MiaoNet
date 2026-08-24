namespace MiaoNet.Shared;

// A PlayerMovedInitialData paired with the owning player's ID, used in
// multi-player snapshots (e.g. PacketPlayerLocationChangedResponse.Players).
// Single-player notifications don't need it - they carry the ID on the packet
// itself (PacketPlayerNotification.PlayerID) and use the bare payload.
public readonly struct PlayerMovedInitialDataWithID
    : IContextualRefBinarySerializable<PlayerMovedInitialDataWithID, PooledStringManager>
{
    public int PlayerID { get; }

    public PlayerMovedInitialData InitialData { get; }

    public PlayerMovedInitialDataWithID(int playerID, PlayerMovedInitialData initialData)
    {
        PlayerID = playerID;
        InitialData = initialData;
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(PlayerID);
        writer.Write(InitialData, pooledStringManager);
    }

    public static PlayerMovedInitialDataWithID Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new(reader.ReadInt32(), reader.Read<PlayerMovedInitialData, PooledStringManager>(pooledStringManager));
}
