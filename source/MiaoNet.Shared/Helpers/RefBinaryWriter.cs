using System.Buffers;
using System.Runtime.CompilerServices;
using BP = System.Buffers.Binary.BinaryPrimitives;

namespace MiaoNet.Shared;

/// <summary>
/// A ByRefLike <see cref="BinaryWriter"/>.
/// Always pass it as a reference (that is <see langword="ref"/> <see cref="RefBinaryWriter"/>).
/// </summary>
public ref struct RefBinaryWriter
{
    public const int WindowSize = 4096;

    // sizeof(Half)
    private const int HalfSize = 2;

    private readonly IBufferWriter<byte> output;
    private Span<byte> window;
    private int offset;

    public RefBinaryWriter(IBufferWriter<byte> output)
    {
        this.output = output;
        window = default;
        offset = 0;
    }

    public void Flush()
    {
        if (offset != 0)
        {
            output.Advance(offset);
            offset = 0;
        }
    }

    private void NextWindow(int sizeHint)
    {
        Flush();
        Span<byte> span = output.GetSpan(Math.Max(sizeHint, WindowSize));
        window = span.Length > WindowSize ? span[..WindowSize] : span;
        offset = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(byte value)
    {
        if (offset == window.Length)
            NextWindow(1);
        window[offset++] = value;
    }

    public void WriteSpan(scoped ReadOnlySpan<byte> span)
    {
        if (span.Length <= window.Length - offset)
        {
            span.CopyTo(window[offset..]);
            offset += span.Length;
            return;
        }

        Flush();

        if (span.Length >= WindowSize)
        {
            span.CopyTo(output.GetSpan(span.Length));
            output.Advance(span.Length);
            window = default;
            return;
        }

        Span<byte> next = output.GetSpan(WindowSize);
        window = next.Length > WindowSize ? next[..WindowSize] : next;
        span.CopyTo(window);
        offset = span.Length;
    }

    // from System.IO.BinaryWriter
    /// <inheritdoc cref="BinaryWriter.Write7BitEncodedInt(int)"/>
    public void Write7BitEncodedInt(int value)
    {
        uint uValue = (uint)value;
        while (uValue > 0x7Fu)
        {
            Write((byte)(uValue | ~0x7Fu));
            uValue >>= 7;
        }
        Write((byte)uValue);
    }

    // from System.IO.BinaryWriter
    /// <inheritdoc cref="BinaryWriter.Write7BitEncodedInt64(long)"/>
    public void Write7BitEncodedInt64(long value)
    {
        ulong uValue = (ulong)value;
        while (uValue > 0x7Fu)
        {
            Write((byte)((uint)uValue | ~0x7Fu));
            uValue >>= 7;
        }
        Write((byte)uValue);
    }

#pragma warning disable IDE0049
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Boolean value)
        => Write(value ? (byte)1 : (byte)0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Int16 value)
    {
        if (window.Length - offset < sizeof(Int16))
            NextWindow(sizeof(Int16));
        BP.WriteInt16LittleEndian(window[offset..], value);
        offset += sizeof(Int16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Int32 value)
    {
        if (window.Length - offset < sizeof(Int32))
            NextWindow(sizeof(Int32));
        BP.WriteInt32LittleEndian(window[offset..], value);
        offset += sizeof(Int32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Int64 value)
    {
        if (window.Length - offset < sizeof(Int64))
            NextWindow(sizeof(Int64));
        BP.WriteInt64LittleEndian(window[offset..], value);
        offset += sizeof(Int64);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Single value)
    {
        if (window.Length - offset < sizeof(Single))
            NextWindow(sizeof(Single));
        BP.WriteSingleLittleEndian(window[offset..], value);
        offset += sizeof(Single);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Double value)
    {
        if (window.Length - offset < sizeof(Double))
            NextWindow(sizeof(Double));
        BP.WriteDoubleLittleEndian(window[offset..], value);
        offset += sizeof(Double);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Half value)
    {
        if (window.Length - offset < HalfSize)
            NextWindow(HalfSize);
        BP.WriteHalfLittleEndian(window[offset..], value);
        offset += HalfSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(UInt16 value)
    {
        if (window.Length - offset < sizeof(UInt16))
            NextWindow(sizeof(UInt16));
        BP.WriteUInt16LittleEndian(window[offset..], value);
        offset += sizeof(UInt16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(UInt32 value)
    {
        if (window.Length - offset < sizeof(UInt32))
            NextWindow(sizeof(UInt32));
        BP.WriteUInt32LittleEndian(window[offset..], value);
        offset += sizeof(UInt32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(UInt64 value)
    {
        if (window.Length - offset < sizeof(UInt64))
            NextWindow(sizeof(UInt64));
        BP.WriteUInt64LittleEndian(window[offset..], value);
        offset += sizeof(UInt64);
    }
#pragma warning restore
}
