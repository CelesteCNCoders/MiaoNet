namespace MiaoNet.Shared;

// I'm bad at naming
public readonly struct PlayerMovedInitialData : IContextualRefBinarySerializable<PlayerMovedInitialData, PooledStringManager>
{
    public PlayerState InitialState { get; }

    public PlayerMovedInitialData(PlayerState initialState)
        => InitialState = initialState;

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
        => writer.Write(InitialState, pooledStringManager);

    public static PlayerMovedInitialData Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new(reader.Read<PlayerState, PooledStringManager>(pooledStringManager));
}