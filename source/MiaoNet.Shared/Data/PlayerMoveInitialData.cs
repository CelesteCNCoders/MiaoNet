namespace MiaoNet.Shared;

// I'm bad at naming
public readonly struct PlayerMovedInitialData : IContextualRefBinarySerializable<PlayerMovedInitialData, PooledStringManager>
{
    public int PlayerID { get; }

    public PlayerState InitialState { get; }

    public PlayerGraphicsInfo? GraphicsInfo { get; }

    public PlayerMovedInitialData(int playerID, PlayerState initialState, PlayerGraphicsInfo? graphicsInfo)
    {
        PlayerID = playerID;
        InitialState = initialState;
        GraphicsInfo = graphicsInfo;
    }

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(PlayerID);
        writer.Write(InitialState, pooledStringManager);
        if (GraphicsInfo is not null)
        {
            writer.Write(true);
            writer.Write(GraphicsInfo);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static PlayerMovedInitialData Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new PlayerMovedInitialData(
            reader.ReadInt32(),
            reader.Read<PlayerState, PooledStringManager>(pooledStringManager),
            reader.ReadBoolean() ? reader.Read<PlayerGraphicsInfo>() : null
        );
}