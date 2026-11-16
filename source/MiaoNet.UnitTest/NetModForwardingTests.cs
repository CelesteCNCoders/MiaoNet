#pragma warning disable CA1861

using System.Buffers;
using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class NetModForwardingTests
{
    private readonly TestSerializationContext context = new();

    [TestMethod]
    public void ClientNetModPacketBecomesARoutableCarrier()
    {
        byte[] payload = [0x11, 0x22, 0x33];
        byte[] frame = BuildClientNetModPacket(
            netModID: 2, localID: 4, scope: PacketScope.Players, targets: [7, 8], payload: payload
        );

        ReadOnlySequence<byte> sequence = new(frame);
        Assert.IsTrue(MiaoClientConnection.TryParsePacket(ref sequence, out EnvelopedPacket parsed, context));
        Assert.AreEqual(0L, sequence.Length);

        PacketNetMod carried = (PacketNetMod)parsed.Packet;
        Assert.AreEqual(2, carried.NetModID);
        Assert.AreEqual(4, carried.LocalID);
        Assert.AreEqual(PacketScope.Players, carried.Route.Scope);
        CollectionAssert.AreEqual(new[] { 7, 8 }, carried.Route.Targets.ToArray());
        CollectionAssert.AreEqual(payload, carried.Payload.ToArray());
    }

    [TestMethod]
    public void BroadcastNetModPacketCarriesNoTargets()
    {
        byte[] frame = BuildClientNetModPacket(0, 1, PacketScope.Map, [], [0xAB]);
        ReadOnlySequence<byte> sequence = new(frame);

        Assert.IsTrue(MiaoClientConnection.TryParsePacket(ref sequence, out EnvelopedPacket parsed, context));

        PacketNetMod carried = (PacketNetMod)parsed.Packet;
        Assert.AreEqual(PacketScope.Map, carried.Route.Scope);
        Assert.IsEmpty(carried.Route.Targets);
    }

    [TestMethod]
    public async Task ForwardingRewritesTheIndexAndKeepsThePayloadBytes()
    {
        byte[] payload = [0x06, 0x03, 0x01, 0x04];
        byte[] frame = BuildClientNetModPacket(
            netModID: 3, localID: 9, scope: PacketScope.Player, targets: [1234], payload: payload
        );

        ReadOnlySequence<byte> sequence = new(frame);
        Assert.IsTrue(MiaoClientConnection.TryParsePacket(ref sequence, out EnvelopedPacket parsed, context));
        PacketNetMod carried = (PacketNetMod)parsed.Packet;

        Assert.AreEqual(3, carried.NetModID);

        ByteArrayBufferWriter output = new(64);
        PacketFraming.WritePacket(
            output,
            PacketEnvelope.FromSender(4321),
            PacketNetModDelivery.From(233, carried.LocalID, carried.Payload),
            context
        );

        using MemoryStream stream = new(output.WrittenSpan.ToArray());
        EnvelopedPacket? read = await PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None);

        Assert.IsNotNull(read);
        Assert.AreEqual(4321, read.Value.Envelope.SenderPlayerID);

        PacketNetModDelivery forwarded = (PacketNetModDelivery)read.Value.Packet;
        Assert.AreEqual(233, forwarded.NetModID);
        Assert.AreEqual(9, forwarded.LocalID);
        CollectionAssert.AreEqual(payload, forwarded.Payload.ToArray());
    }

    [TestMethod]
    public void EveryReceiverOfOneForwardedPacketSharesItsBytes()
    {
        byte[] payload = [0x01, 0x02];

        PacketNetModDelivery first = PacketNetModDelivery.From(0, 7, payload);
        PacketNetModDelivery second = PacketNetModDelivery.From(1, 7, payload);

        Assert.AreEqual(0, first.NetModID);
        Assert.AreEqual(1, second.NetModID);
        Assert.IsTrue(first.Payload.Equals(second.Payload));
    }

    private byte[] BuildClientNetModPacket(
        byte netModID,
        byte localID,
        PacketScope scope,
        int[] targets,
        byte[] payload
    )
    {
        ByteArrayBufferWriter output = new(64);
        PacketFraming.WritePacket(
            output,
            default,
            PacketNetMod.FromPacket(
                netModID, localID,
                Route(scope, targets),
                new RawBytesPacket { Bytes = payload }
            ),
            context
        );
        return output.WrittenSpan.ToArray();
    }

    private static PacketRoute Route(PacketScope scope, int[] targets)
        => scope switch
        {
            PacketScope.Player => PacketRoute.Unicast(targets[0]),
            PacketScope.Players => PacketRoute.Multicast(targets),
            _ => PacketRoute.Broadcast(scope)
        };

    private sealed class TestSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }

    private sealed class RawBytesPacket : IContextualPacket<RawBytesPacket>
    {
        public byte[] Bytes { get; set; } = [];

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
            => writer.WriteSpan(Bytes);

        public static RawBytesPacket Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
            => new() { Bytes = reader.ReadSpan(reader.BytesLeft).ToArray() };
    }
}
