using System.Buffers;
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
    public async Task ReadPacketThrowsOnTruncatedHeader()
    {
        byte[] frame = WriteFrame(new PacketPing());
        using var stream = new MemoryStream(frame, 0, Connection.PacketHeaderSize - 1);

        PacketTruncatedException exception = await Assert.ThrowsExactlyAsync<PacketTruncatedException>(
            () => PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None).AsTask()
        );

        Assert.IsFalse(exception.IsPayload);
        Assert.AreEqual(Connection.PacketHeaderSize - 1, exception.BytesRead);
        Assert.AreEqual(Connection.PacketHeaderSize, exception.ExpectedBytes);
    }

    [TestMethod]
    public async Task ReadPacketReturnsNullAtCleanEof()
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
    public async Task ReadPacketThrowsOnTruncatedPayload()
    {
        byte[] frame = WriteFrame(new PacketPing());
        using var stream = new MemoryStream(frame, 0, frame.Length - 1);

        PacketTruncatedException exception = await Assert.ThrowsExactlyAsync<PacketTruncatedException>(
            () => PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None).AsTask()
        );

        Assert.IsTrue(exception.IsPayload);
        Assert.AreEqual(frame.Length - Connection.PacketHeaderSize - 1, exception.BytesRead);
        Assert.AreEqual(exception.ExpectedBytes, frame.Length - Connection.PacketHeaderSize);
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
    public async Task ReadPacketParsesMultipleFramesSequentially()
    {
        byte[] frameA = WriteFrame(new PacketPing());
        byte[] frameB = WriteFrame(new PacketPong());
        byte[] frameC = WriteFrame(new PacketSendChatMessage(default, "hello"));
        byte[] all = new byte[frameA.Length + frameB.Length + frameC.Length];
        frameA.CopyTo(all, 0);
        frameB.CopyTo(all, frameA.Length);
        frameC.CopyTo(all, frameA.Length + frameB.Length);
        using var stream = new MemoryStream(all);

        IContextualPacket? packet1 = await PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None);
        IContextualPacket? packet2 = await PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None);
        IContextualPacket? packet3 = await PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None);
        IContextualPacket? packet4 = await PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None);

        Assert.IsInstanceOfType<PacketPing>(packet1);
        Assert.IsInstanceOfType<PacketPong>(packet2);
        Assert.IsInstanceOfType<PacketSendChatMessage>(packet3);
        Assert.IsNull(packet4);
    }

    [TestMethod]
    public async Task ReadPacketWrapsDeserializationFailureInInvalidPacketDataException()
    {
        byte[] payload = { 0 }; // 只有 ChatChannel 字段,缺少字符串长度与内容
        byte[] frame = new byte[Connection.PacketHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(sizeof(ushort)),
            PacketRegistry.GetPacketID(new PacketSendChatMessage(default, string.Empty))
        );
        payload.CopyTo(frame.AsSpan(Connection.PacketHeaderSize));
        using var stream = new MemoryStream(frame);

        InvalidPacketDataException exception = await Assert.ThrowsExactlyAsync<InvalidPacketDataException>(
            () => PacketFraming.ReadPacketAsync(stream, context, CancellationToken.None).AsTask()
        );

        Assert.HasCount(1, exception.Payload);
        Assert.AreEqual((byte)0, exception.Payload[0]);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task CompletedPipeStillProcessesItsFinalBufferedPacket()
    {
        byte[] frame = WriteFrame(new PacketPing());
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();
        var receivedPackets = new List<IContextualPacket>();

        long leftover = await MiaoClientConnection.ProcessPacketsAsync(
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
        Assert.AreEqual(0L, leftover);
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

        long leftover = await MiaoClientConnection.ProcessPacketsAsync(
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
        Assert.AreEqual(0L, leftover);
    }

    [TestMethod]
    public async Task CompletedPipeReturnsLeftoverBytesForPartialFrame()
    {
        byte[] frame = WriteFrame(new PacketPing());
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame.AsMemory(0, frame.Length - 2));
        await pipe.Writer.CompleteAsync();
        var receivedPackets = new List<IContextualPacket>();

        long leftover = await MiaoClientConnection.ProcessPacketsAsync(
            pipe.Reader,
            context,
            (packet, _) =>
            {
                receivedPackets.Add(packet);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.HasCount(0, receivedPackets);
        Assert.AreEqual(frame.Length - 2, leftover);
    }

    [TestMethod]
    public void TryParsePacketParsesSingleFrame()
    {
        byte[] frame = WriteFrame(new PacketPing());
        ReadOnlySequence<byte> sequence = new(frame);

        bool parsed = MiaoClientConnection.TryParsePacket(ref sequence, out var packet, context);

        Assert.IsTrue(parsed);
        Assert.IsInstanceOfType<PacketPing>(packet);
        Assert.AreEqual(0L, sequence.Length);
    }

    [TestMethod]
    public void TryParsePacketReturnsFalseForIncompleteHeader()
    {
        byte[] frame = WriteFrame(new PacketPing());
        ReadOnlySequence<byte> sequence = new(frame.AsMemory(0, Connection.PacketHeaderSize - 1));

        bool parsed = MiaoClientConnection.TryParsePacket(ref sequence, out var packet, context);

        Assert.IsFalse(parsed);
        Assert.IsNull(packet);
    }

    [TestMethod]
    public void TryParsePacketReturnsFalseForIncompletePayload()
    {
        byte[] frame = WriteFrame(new PacketPing());
        ReadOnlySequence<byte> sequence = new(frame.AsMemory(0, frame.Length - 1));

        bool parsed = MiaoClientConnection.TryParsePacket(ref sequence, out var packet, context);

        Assert.IsFalse(parsed);
        Assert.IsNull(packet);
    }

    [TestMethod]
    public void TryParsePacketParsesMultipleFrames()
    {
        byte[] frameA = WriteFrame(new PacketPing());
        byte[] frameB = WriteFrame(new PacketPong());
        byte[] all = new byte[frameA.Length + frameB.Length];
        frameA.CopyTo(all, 0);
        frameB.CopyTo(all, frameA.Length);
        ReadOnlySequence<byte> sequence = new(all);

        bool parsed1 = MiaoClientConnection.TryParsePacket(ref sequence, out var packet1, context);
        Assert.IsTrue(parsed1);
        Assert.IsInstanceOfType<PacketPing>(packet1);

        bool parsed2 = MiaoClientConnection.TryParsePacket(ref sequence, out var packet2, context);
        Assert.IsTrue(parsed2);
        Assert.IsInstanceOfType<PacketPong>(packet2);

        Assert.AreEqual(0L, sequence.Length);
    }

    [TestMethod]
    public void TryParsePacketHandlesLargePayloadWithoutStackAllocation()
    {
        byte[] frame = WriteFrame(new PacketSendChatMessage(default, new string('a', 5000)));
        ReadOnlySequence<byte> sequence = new(frame);

        bool parsed = MiaoClientConnection.TryParsePacket(ref sequence, out var packet, context);

        Assert.IsTrue(parsed);
        Assert.IsInstanceOfType<PacketSendChatMessage>(packet);
        Assert.AreEqual(0L, sequence.Length);
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
