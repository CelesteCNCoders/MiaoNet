using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// TODO this impl is ugly
public sealed class SerializedPacket
{
    private int clientCount;
    private readonly ArrayPool<byte> arrayPool;
    private readonly ArraySegment<byte> arraySegment;
    [ThreadStatic] private static MemoryStream? memoryStream;

    public ArraySegment<byte> ArraySegment => arraySegment;

    public SerializedPacket(
        ArrayPool<byte> arrayPool,
        IContextualPacket packet,
        IPacketSerializationContext context,
        int clientCount = 1
    )
    {
        memoryStream ??= new(512);
        memoryStream.Seek(sizeof(ushort), SeekOrigin.Begin);

        RefBinaryWriter writer = new(memoryStream);
        ushort id = PacketRegistry.GetPacketID(packet);
        writer.Write(id);
        packet.Serialize(ref writer, context);
        if (memoryStream.Position > ushort.MaxValue + sizeof(ushort))
        {
            memoryStream.Seek(sizeof(ushort), SeekOrigin.Begin);
            throw new ArgumentOutOfRangeException(nameof(packet));
        }
        ushort size = (ushort)memoryStream.Position;
        memoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write((ushort)(size - 2 * sizeof(ushort)));
        var array = arrayPool.Rent(size);
        memoryStream.GetBuffer().AsSpan()[0..size].CopyTo(array.AsSpan()[0..size]);

        var arraySegment = new ArraySegment<byte>(array, 0, size);

        this.clientCount = clientCount;
        this.arrayPool = arrayPool;
        this.arraySegment = arraySegment;
    }

    public SerializedPacket(ArrayPool<byte> arrayPool, IContextlessPacket packet, int clientCount = 1)
        : this(arrayPool, packet, null!, clientCount) // is passing null ok...?
    {
    }

    private void Dispose(bool disposing = true)
    {
        Debug.Assert(arraySegment.Array is not null);
        arrayPool.Return(arraySegment.Array);
#pragma warning disable CA1816
        if (disposing)
            GC.SuppressFinalize(this);
#pragma warning restore CA1816
    }

    public void OnConsumed()
    {
        int v = Interlocked.Decrement(ref clientCount);
        if (v < 0)
            throw new ArgumentOutOfRangeException();
        if (v == 0)
            Dispose();
    }

    public void OnConsumed(int count)
    {
        int v = Interlocked.Add(ref clientCount, -count);
        if (v < 0)
            throw new ArgumentOutOfRangeException(nameof(count)); // TODO message
        if (v == 0)
            Dispose();
    }

    // TODO TODO TODO this should not be used
    ~SerializedPacket()
    {
        Dispose(false);
    }
}