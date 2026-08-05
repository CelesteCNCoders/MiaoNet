namespace MiaoNet.Shared;

// I'm bad at naming
public readonly struct PlayerMovedInitialData : IContextualRefBinarySerializable<PlayerMovedInitialData, PooledStringManager>
{
    public int PlayerID { get; }

    public PlayerState InitialState { get; }

    public PlayerMovedInitialData(int playerID, PlayerState initialState)
    {
        PlayerID = playerID;
        InitialState = initialState;
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(PlayerID);
        writer.Write(InitialState, pooledStringManager);
    }

    public static PlayerMovedInitialData Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new PlayerMovedInitialData(
            reader.ReadInt32(),
            reader.Read<PlayerState, PooledStringManager>(pooledStringManager)
        );
}