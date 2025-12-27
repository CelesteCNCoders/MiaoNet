namespace MiaoNet.Shared;

public sealed class PacketPing : PacketRequest<PacketPong>
{
    public override void Serialize(ref RefBinaryWriter writer) { }
}

public sealed class PacketPong : PacketResponse
{
    public override void Serialize(ref RefBinaryWriter writer) { }
}