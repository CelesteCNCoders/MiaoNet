using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif

namespace MiaoNet.Shared;

/// <summary>
/// A ByRefLike <see cref="BinaryReader"/>.
/// It's suggested that always pass it as a reference (that is <see langword="ref"/> <see cref="RefBinaryReader"/>).
/// </summary>
public ref struct RefBinaryReader
{
    private ReadOnlySpan<byte> span;

    public RefBinaryReader(ReadOnlySpan<byte> span)
        => this.span = span;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> Move(int v)
    {
        ReadOnlySpan<byte> p = span;
        span = span[v..];
        return p;
    }

    public byte ReadByte() => Move(1)[0];

    public ReadOnlySpan<byte> ReadSpan(int length) => Move(length)[..length];

    // from System.IO.BinaryReader
    public int Read7BitEncodedInt()
    {
        uint result = 0;
        byte byteReadJustNow;
        const int MaxBytesWithoutOverflow = 4;
        for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
        {
            byteReadJustNow = ReadByte();
            result |= (byteReadJustNow & 0x7Fu) << shift;
            if (byteReadJustNow <= 0x7Fu)
                return (int)result;
        }
        byteReadJustNow = ReadByte();
        if (byteReadJustNow > 0b_1111u)
            throw new FormatException();
        result |= (uint)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
        return (int)result;
    }

    // from System.IO.BinaryReader
    public long Read7BitEncodedInt64()
    {
        ulong result = 0;
        byte byteReadJustNow;
        const int MaxBytesWithoutOverflow = 9;
        for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
        {
            byteReadJustNow = ReadByte();
            result |= (byteReadJustNow & 0x7Ful) << shift;
            if (byteReadJustNow <= 0x7Fu)
                return (long)result;
        }
        byteReadJustNow = ReadByte();
        if (byteReadJustNow > 0b_1u)
            throw new FormatException();
        result |= (ulong)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
        return (long)result;
    }

#pragma warning disable IDE0049
    public Boolean ReadBoolean() => Move(1)[0] != 0;
    public Int16 ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Move(sizeof(Int16)));
    public Int32 ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Move(sizeof(Int32)));
    public Int64 ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Move(sizeof(Int64)));
    public Single ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Move(sizeof(Single)));
    public Double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Move(sizeof(Double)));
    public UInt16 ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Move(sizeof(UInt16)));
    public UInt32 ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Move(sizeof(UInt32)));
    public UInt64 ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Move(sizeof(UInt64)));
    public Half ReadHalf() => BinaryPrimitives.ReadHalfLittleEndian(Move(Marshal.SizeOf<Half>()));
#pragma warning restore
}

public static class RefBinaryReaderExtensions
{
    public static Version ReadVersion(this ref RefBinaryReader reader)
    {
        ushort major = reader.ReadUInt16();
        ushort minor = reader.ReadUInt16();
        ushort build = reader.ReadUInt16();
        return new Version(major, minor, build);
    }

    public static string ReadString(this ref RefBinaryReader reader, Encoding encoding)
    {
        ushort length = reader.ReadUInt16();
        ReadOnlySpan<byte> bytes = reader.ReadSpan(length);
        return encoding.GetString(bytes);
    }

    public static string ReadString(this ref RefBinaryReader reader)
        => ReadString(ref reader, Encoding.UTF8);

    public static Color ReadColor(this ref RefBinaryReader reader)
    {
        byte r, g, b, a;
        r = reader.ReadByte();
        g = reader.ReadByte();
        b = reader.ReadByte();
        a = reader.ReadByte();
        return new(r, g, b, a);
    }

    public static Vector2 ReadVector2(this ref RefBinaryReader reader)
        => new Vector2(reader.ReadSingle(), reader.ReadSingle());

    public static T Read<T>(this ref RefBinaryReader reader) where T : IRefBinarySerializable<T>
        => T.Deserialize(ref reader);

    public static T Read<T, TContext>(this ref RefBinaryReader reader, TContext context)
        where T : IContextualRefBinarySerializable<T, TContext>
        => T.Deserialize(ref reader, context);

    public static string[] ReadStringArray(this ref RefBinaryReader reader)
    {
        int count = reader.ReadUInt16();
        string[] list = new string[count];
        for (int i = 0; i < count; i++)
            list[i] = ReadString(ref reader);
        return list;
    }

    public static T[] ReadArray<T>(this ref RefBinaryReader reader) where T : IRefBinarySerializable<T>
    {
        int count = reader.ReadUInt16();
        T[] list = new T[count];
        for (int i = 0; i < count; i++)
            list[i] = Read<T>(ref reader);
        return list;
    }

    public static T[] ReadArray<T, TContext>(this ref RefBinaryReader reader, TContext context)
        where T : IContextualRefBinarySerializable<T, TContext>
    {
        int count = reader.ReadUInt16();
        T[] list = new T[count];
        for (int i = 0; i < count; i++)
            list[i] = Read<T, TContext>(ref reader, context);
        return list;
    }
}