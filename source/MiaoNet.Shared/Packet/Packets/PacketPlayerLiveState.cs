namespace MiaoNet.Shared;

public sealed class PacketPlayerLiveState : IContextlessPacket<PacketPlayerLiveState>
{
    public bool IsDie { get; }

    /// <summary>Die direction when <see cref="IsDie"/>, or respawn position.</summary>
    public Vector2 Vector2 { get; }

    public PacketPlayerLiveState(bool isDie, Vector2 vector2)
    {
        IsDie = isDie;
        Vector2 = vector2;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(IsDie);
        writer.Write(Vector2);
    }

    public static PacketPlayerLiveState Deserialize(ref RefBinaryReader reader)
        => new PacketPlayerLiveState(reader.ReadBoolean(), reader.ReadVector2());
}