using MiaoNet.Shared;

namespace MiaoNet.CelesteNetBridge;

/// <summary>
/// One CelesteNet DataType payload carried over the NetMod pipeline.
/// <para>
/// The payload is relayed verbatim: the bridge does not parse or rewrite CelesteNet data. Who sent
/// it is not in here either -- the MiaoNet server stamps the sender into the envelope, the same way
/// it does for every other NetMod packet.
/// </para>
/// </summary>
internal sealed class PacketBridgeFrame : IContextualPacket<PacketBridgeFrame> {
    public byte[] Payload { get; }

    public PacketBridgeFrame(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context) {
        writer.Write7BitEncodedInt(Payload.Length);
        writer.WriteSpan(Payload);
    }

    public static PacketBridgeFrame Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context) {
        int length = reader.Read7BitEncodedInt();
        if (length <= 0 || length > reader.BytesLeft)
            throw new InvalidDataException("invalid bridge frame length");

        return new(reader.ReadSpan(length).ToArray());
    }
}
