using System.Buffers.Binary;
using System.IO.Pipelines;
using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PacketFramingTests
{
    private readonly TestPacketSerializationContext context = new();

    [TestMethod]
    public void WritePacketRejectsPayloadLargerThanProtocolLimit()
    {
        string largeName = new('a', 33_000);
        string largePrefix = new('b', 33_000);
        var packet = new PacketClientInitial(
            channelID: 1,
            playerID: 2,
            new PlayerInfo(3, largeName, largePrefix, string.Empty, Color.White),
            Array.Empty<PacketClientInitial.Channel>(),
            Array.Empty<PacketClientInitial.Player>(),
            new PlayerPresenceMessage(string.Empty, string.Empty),
            string.Empty
        );
        using var stream = new MemoryStream();

        PacketTooLargeException exception = Assert.ThrowsExactly<PacketTooLargeException>(
            () => PacketFraming.WritePacket(stream, packet, context)
        );

        Assert.AreEqual(packet.GetType(), exception.PacketType);
        Assert.IsGreaterThan(Connection.MaxPayloadSize, exception.PayloadSize);
        Assert.AreEqual(Connection.MaxPayloadSize, exception.MaxPayloadSize);
    }

    [TestMethod]
    public void WritePacketAcceptsPayloadAtProtocolLimit()
    {
        const int packetFieldsSize = sizeof(byte) + sizeof(ushort);
        var packet = new PacketSendChatMessage(
            default,
            new string('a', Connection.MaxPayloadSize - packetFieldsSize)
        );
        using var stream = new MemoryStream();

        PacketFraming.WritePacket(stream, packet, context);

        Assert.AreEqual(
            Connection.MaxPayloadSize,
            BinaryPrimitives.ReadUInt16LittleEndian(stream.GetBuffer())
        );
        Assert.AreEqual(
            Connection.PacketHeaderSize + Connection.MaxPayloadSize,
            stream.Length
        );
    }

    [TestMethod]
    public async Task ReadPacketReturnsNullForPartialHeader()
    {
        byte[] frame = WriteFrame(new PacketPing());
        using var stream = new MemoryStream(frame, 0, Connection.PacketHeaderSize - 1);

        IContextualPacket? packet = await PacketFraming.ReadPacketAsync(
            stream,
            context,
            CancellationToken.None
        );

        Assert.IsNull(packet);
    }

    [TestMethod]
    public async Task ReadPacketReturnsNullAtFrameBoundaryEof()
    {
        using var stream = new MemoryStream();

        IContextualPacket? packet = await PacketFraming.ReadPacketAsync(
            stream,
            context,
            CancellationToken.None
        );

        Assert.IsNull(packet);
    }

    [TestMethod]
    public async Task ReadPacketReturnsNullForPartialPayload()
    {
        byte[] frame = WriteFrame(new PacketPing());
        using var stream = new MemoryStream(frame, 0, frame.Length - 1);

        IContextualPacket? packet = await PacketFraming.ReadPacketAsync(
            stream,
            context,
            CancellationToken.None
        );

        Assert.IsNull(packet);
    }

    [TestMethod]
    public async Task ReadPacketPreservesExistingWireFormat()
    {
        byte[] frame = WriteFrame(new PacketPing());
        Assert.AreEqual(sizeof(int), BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.AreEqual(
            PacketRegistry.GetPacketID(new PacketPing()),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(sizeof(ushort)))
        );
        using var stream = new MemoryStream(frame);

        IContextualPacket? packet = await PacketFraming.ReadPacketAsync(
            stream,
            context,
            CancellationToken.None
        );

        Assert.IsInstanceOfType<PacketPing>(packet);
    }

    [TestMethod]
    public async Task CompletedPipeStillProcessesItsFinalBufferedPacket()
    {
        byte[] frame = WriteFrame(new PacketPing());
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();
        var receivedPackets = new List<IContextualPacket>();

        await MiaoClientConnection.ProcessPacketsAsync(
            pipe.Reader,
            context,
            (packet, _) =>
            {
                receivedPackets.Add(packet);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.HasCount(1, receivedPackets);
        Assert.IsInstanceOfType<PacketPing>(receivedPackets[0]);
    }

    [TestMethod]
    public async Task CompletedPipeProcessesMaximumPayloadWithoutLargeStackAllocation()
    {
        const int packetFieldsSize = sizeof(byte) + sizeof(ushort);
        byte[] frame = WriteFrame(new PacketSendChatMessage(
            default,
            new string('a', Connection.MaxPayloadSize - packetFieldsSize)
        ));
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: long.MaxValue,
            resumeWriterThreshold: long.MaxValue - 1
        ));
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();
        int received = 0;

        await MiaoClientConnection.ProcessPacketsAsync(
            pipe.Reader,
            context,
            (packet, bytesConsumed) =>
            {
                Assert.IsInstanceOfType<PacketSendChatMessage>(packet);
                Assert.AreEqual(frame.Length, bytesConsumed);
                received++;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.AreEqual(1, received);
    }

    private byte[] WriteFrame(IContextualPacket packet)
    {
        using var stream = new MemoryStream();
        PacketFraming.WritePacket(stream, packet, context);
        return stream.ToArray();
    }

    private sealed class TestPacketSerializationContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
