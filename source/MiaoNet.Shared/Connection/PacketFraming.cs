using System.Buffers;
using System.Buffers.Binary;

namespace MiaoNet.Shared;

public static class PacketFraming
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    public static void WritePacket(
        Stream stream,
        IContextualPacket packet,
        IPacketSerializationContext context
    )
    {
        ushort packetID = PacketRegistry.GetPacketID(packet);
        long frameStart = stream.Position;
        stream.Seek(Connection.PacketHeaderSize, SeekOrigin.Current);
        long payloadStart = stream.Position;

        RefBinaryWriter writer = new(stream);
        packet.Serialize(ref writer, context);

        long payloadSize = stream.Position - payloadStart;
        if (payloadSize > Connection.MaxPayloadSize)
        {
            throw new PacketTooLargeException(
                packet.GetType(),
                payloadSize,
                Connection.MaxPayloadSize
            );
        }

        long frameEnd = stream.Position;
        stream.Position = frameStart;
        writer.Write((ushort)payloadSize);
        writer.Write(packetID);
        stream.Position = frameEnd;
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
            throw new ArgumentException("The packet header buffer is too small.", nameof(headerMemory));

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
