namespace MiaoNet.Shared;

public readonly struct EnvelopedPacket
{
    public PacketEnvelope Envelope { get; }

    public IContextualPacket Packet { get; }

    public EnvelopedPacket(PacketEnvelope envelope, IContextualPacket packet)
    {
        Envelope = envelope;
        Packet = packet;
    }

    public void Deconstruct(out PacketEnvelope envelope, out IContextualPacket packet)
        => (envelope, packet) = (Envelope, Packet);
}
