using System.Buffers;

namespace MiaoNet.Shared;

public sealed class ByteArrayBufferWriter : IBufferWriter<byte>
{
    private const int DefaultInitialCapacity = 256;

    private byte[] buffer;
    private int written;

    public ByteArrayBufferWriter()
        => buffer = [];

    public ByteArrayBufferWriter(int initialCapacity)
        => buffer = new byte[initialCapacity];

    public int WrittenCount => written;

    public int Capacity => buffer.Length;

    public int FreeCapacity => buffer.Length - written;

    public Span<byte> WrittenSpan => buffer.AsSpan(0, written);

    public Memory<byte> WrittenMemory => buffer.AsMemory(0, written);

    public void Clear() => written = 0;

    /// <inheritdoc/>
    public void Advance(int count)
    {
        if ((uint)count > (uint)FreeCapacity)
            throw new ArgumentOutOfRangeException(nameof(count));
        written += count;
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return buffer.AsSpan(written);
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return buffer.AsMemory(written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        if (sizeHint == 0)
            sizeHint = 1;

        if (sizeHint > FreeCapacity)
        {
            int growBy = Math.Max(sizeHint, buffer.Length);
            if (buffer.Length == 0)
                growBy = Math.Max(growBy, DefaultInitialCapacity);
            Array.Resize(ref buffer, checked(buffer.Length + growBy));
        }
    }
}
