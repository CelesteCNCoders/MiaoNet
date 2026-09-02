namespace MiaoNet.Shared;

public enum LiveStateType
{
    Die,
    Respawn,
    RespawnFromSL,
    DeathWipe,
}

public sealed class PacketPlayerLiveState : IContextlessPacket<PacketPlayerLiveState>
{
    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public LiveStateType Type { get; }

    /// <summary>
    /// Death direction for <see cref="LiveStateType.Die"/>, respawn position for
    /// the respawn variants, or zero for <see cref="LiveStateType.DeathWipe"/>.
    /// </summary>
    public Vector2 Vector2 { get; }

    public PacketPlayerLiveState(uint playerEpoch, uint playerSequence, LiveStateType type, Vector2 vector2)
    {
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        Type = type;
        Vector2 = vector2;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write((byte)Type);
        writer.Write(Vector2);
    }

    public static PacketPlayerLiveState Deserialize(ref RefBinaryReader reader)
        => new PacketPlayerLiveState(
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            (LiveStateType)reader.ReadByte(),
            reader.ReadVector2()
        );
}
