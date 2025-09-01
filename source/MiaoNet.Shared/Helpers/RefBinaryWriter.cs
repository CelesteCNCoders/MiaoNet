using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BP = System.Buffers.Binary.BinaryPrimitives;

namespace MiaoNet.Shared;

public readonly ref struct RefBinaryWriter
{
    private readonly Stream stream;

    public RefBinaryWriter(Stream stream)
        => this.stream = stream;

    public void WriteSpan(ReadOnlySpan<byte> span)
        => stream.Write(span);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteSpanInlined(ReadOnlySpan<byte> span)
        => stream.Write(span);

    public void Write(byte value)
        => stream.WriteByte(value);

#pragma warning disable IDE0049
    public void Write(Boolean value)
        => stream.WriteByte(value ? (byte)1 : (byte)0);

    public void Write(Int16 value)
    { Span<byte> span = stackalloc byte[sizeof(Int16)]; BP.WriteInt16LittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(Int32 value)
    { Span<byte> span = stackalloc byte[sizeof(Int32)]; BP.WriteInt32LittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(Int64 value)
    { Span<byte> span = stackalloc byte[sizeof(Int64)]; BP.WriteInt64LittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(Single value)
    { Span<byte> span = stackalloc byte[sizeof(Single)]; BP.WriteSingleLittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(Double value)
    { Span<byte> span = stackalloc byte[sizeof(Double)]; BP.WriteDoubleLittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(Half value)
    { Span<byte> span = stackalloc byte[Marshal.SizeOf<Half>()]; BP.WriteHalfLittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(UInt16 value)
    { Span<byte> span = stackalloc byte[sizeof(UInt16)]; BP.WriteUInt16LittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(UInt32 value)
    { Span<byte> span = stackalloc byte[sizeof(UInt32)]; BP.WriteUInt32LittleEndian(span, value); WriteSpanInlined(span); }

    public void Write(UInt64 value)
    { Span<byte> span = stackalloc byte[sizeof(UInt64)]; BP.WriteUInt64LittleEndian(span, value); WriteSpanInlined(span); }
#pragma warning restore
}

public static class RefBinaryWriterExtensions
{
    public static void Write(this ref RefBinaryWriter writer, Version version)
    {
        writer.Write((ushort)version.Major);
        writer.Write((ushort)version.Minor);
        writer.Write((ushort)version.Build);
    }

    public static void Write(this ref RefBinaryWriter writer, string value, Encoding encoding)
    {
        int length = encoding.GetByteCount(value);
        if (length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        writer.Write((ushort)length);

        // TODO stackalloc
        Span<byte> span = stackalloc byte[length];
        encoding.GetBytes(value, span);
        writer.WriteSpan(span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(this ref RefBinaryWriter writer, string value)
        => writer.Write(value, Encoding.UTF8);

    public static void Write(this ref RefBinaryWriter writer, Color value)
    {
        writer.Write(value.R);
        writer.Write(value.G);
        writer.Write(value.B);
        writer.Write(value.A);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(this ref RefBinaryWriter writer, T value) where T : IRefBinarySerializable<T>
        => value.Serialize(ref writer);

    public static void Write<T>(this ref RefBinaryWriter writer, IReadOnlyCollection<T> value) where T : IRefBinarySerializable<T>
    {
        if (value.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        writer.Write((ushort)value.Count);
        foreach (var item in value)
            item.Serialize(ref writer);
    }

    public static void Write<T>(this ref RefBinaryWriter writer, T? value) where T : struct, IRefBinarySerializable<T>
    {
        if (value.HasValue)
        {
            writer.Write(true);
            writer.Write((T)value.Value);
        }
        else
        {
            writer.Write(false);
        }
    }
}