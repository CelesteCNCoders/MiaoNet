using System.Buffers;
using System.Buffers.Binary;

namespace MiaoNet.Shared;

public static class PacketFraming
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    public static void WritePacket(
        ByteArrayBufferWriter output,
        IContextualPacket packet,
        IPacketSerializationContext context
    )
    {
        ushort packetID = PacketRegistry.GetPacketID(packet);
        int frameStart = output.WrittenCount;

        RefBinaryWriter writer = new(output);
        writer.Write((ushort)0);
        writer.Write(packetID);
        packet.Serialize(ref writer, context);
        writer.Flush();

        int payloadSize = output.WrittenCount - frameStart - Connection.PacketHeaderSize;
        if (payloadSize > Connection.MaxPayloadSize)
        {
            throw new PacketTooLargeException(
                packet.GetType(),
                payloadSize,
                Connection.MaxPayloadSize
            );
        }

        Span<byte> frame = output.WrittenSpan[frameStart..];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payloadSize);
        BinaryPrimitives.WriteUInt16LittleEndian(frame[sizeof(ushort)..], packetID);
    }

    public static void WriteSizePrefixed<T>(ByteArrayBufferWriter output, T value)
        where T : IRefBinarySerializable<T>
    {
        int frameStart = output.WrittenCount;

        RefBinaryWriter writer = new(output);
        writer.Write((ushort)0);
        value.Serialize(ref writer);
        writer.Flush();

        int size = output.WrittenCount - frameStart - sizeof(ushort);
        if (size > Connection.MaxPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(value));

        Span<byte> frame = output.WrittenSpan[frameStart..];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)size);
    }

    public static async ValueTask<IContextualPacket?> ReadPacketAsync(
        Stream stream,
        IPacketSerializationContext context,
        CancellationToken cancellationToken
    )
    {
        byte[] headerBuffer = pool.Rent(Connection.PacketHeaderSize);
        try
        {
            return await ReadPacketAsync(
                stream,
                headerBuffer.AsMemory(0, Connection.PacketHeaderSize),
                context,
                cancellationToken
            );
        }
        finally
        {
            pool.Return(headerBuffer);
        }
    }

    internal static async ValueTask<IContextualPacket?> ReadPacketAsync(
        Stream stream,
        Memory<byte> headerMemory,
        IPacketSerializationContext context,
        CancellationToken cancellationToken
    )
    {
        if (headerMemory.Length < Connection.PacketHeaderSize)
            throw new ArgumentException(SR.PacketHeaderBufferTooSmall, nameof(headerMemory));

        headerMemory = headerMemory[..Connection.PacketHeaderSize];
        int headerBytesRead = await stream.ReadAtLeastAsync(
            headerMemory,
            Connection.PacketHeaderSize,
            throwOnEndOfStream: false,
            cancellationToken
        );
        if (headerBytesRead < Connection.PacketHeaderSize)
        {
            if (headerBytesRead > 0)
                throw new PacketTruncatedException(isPayload: false, headerBytesRead, Connection.PacketHeaderSize);
            return null;
        }

        ushort payloadSize = BinaryPrimitives.ReadUInt16LittleEndian(headerMemory.Span);
        ushort packetID = BinaryPrimitives.ReadUInt16LittleEndian(headerMemory.Span[sizeof(ushort)..]);

        byte[] payloadBuffer = pool.Rent(payloadSize);
        try
        {
            Memory<byte> payloadMemory = payloadBuffer.AsMemory(0, payloadSize);
            if (payloadSize > 0)
            {
                int payloadBytesRead = await stream.ReadAtLeastAsync(
                    payloadMemory,
                    payloadSize,
                    throwOnEndOfStream: false,
                    cancellationToken
                );
                if (payloadBytesRead < payloadSize)
                    throw new PacketTruncatedException(isPayload: true, payloadBytesRead, payloadSize);
            }

            try
            {
                RefBinaryReader reader = new(payloadMemory.Span);
                RefBinaryPacketReadHandler readHandler = PacketRegistry.GetPacketReader(packetID);
                return readHandler(ref reader, context);
            }
            catch (Exception exception)
            {
                throw new InvalidPacketDataException(payloadMemory.ToArray(), exception);
            }
        }
        finally
        {
            pool.Return(payloadBuffer);
        }
    }
}
