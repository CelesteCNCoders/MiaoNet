namespace MiaoNet.Shared;

// I'm bad at naming
public readonly struct PlayerMovedInitialData : IContextualRefBinarySerializable<PlayerMovedInitialData, PooledStringManager>
{
    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public PlayerState InitialState { get; }

    public PlayerMovedInitialData(uint playerEpoch, uint playerSequence, PlayerState initialState)
        => (PlayerEpoch, PlayerSequence, InitialState) = (playerEpoch, playerSequence, initialState);

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write(InitialState, pooledStringManager);
    }

    public static PlayerMovedInitialData Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
        => new(
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.Read<PlayerState, PooledStringManager>(pooledStringManager)
        );
}
