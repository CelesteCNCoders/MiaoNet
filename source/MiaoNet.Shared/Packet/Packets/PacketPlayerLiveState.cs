namespace MiaoNet.Shared;

public enum LiveStateType { Die, Respawn, RespawnFromSL }

public sealed class PacketPlayerLiveState : IContextlessPacket<PacketPlayerLiveState>
{

    public LiveStateType Type { get; }

    /// <summary>Die direction when <see cref="IsDie"/>, or respawn position.</summary>
    public Vector2 Vector2 { get; }

    public PacketPlayerLiveState(LiveStateType type, Vector2 vector2)
    {
        Type = type;
        Vector2 = vector2;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Type);
        writer.Write(Vector2);
    }

    public static PacketPlayerLiveState Deserialize(ref RefBinaryReader reader)
        => new PacketPlayerLiveState((LiveStateType)reader.ReadByte(), reader.ReadVector2());
}