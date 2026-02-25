using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace MiaoNet.Shared;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining), StackTraceHidden, DebuggerHidden]
    public static T Read<T>(this ref RefBinaryReader reader) where T : IRefBinarySerializable<T>
        => T.Deserialize(ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining), StackTraceHidden, DebuggerHidden]
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

    public static T[] ReadSmallArray<T>(this ref RefBinaryReader reader) where T : IRefBinarySerializable<T>
    {
        int count = reader.ReadByte();
        T[] list = new T[count];
        for (int i = 0; i < count; i++)
            list[i] = Read<T>(ref reader);
        return list;
    }

    public static T[] ReadSmallArray<T, TContext>(this ref RefBinaryReader reader, TContext context)
        where T : IContextualRefBinarySerializable<T, TContext>
    {
        int count = reader.ReadByte();
        T[] list = new T[count];
        for (int i = 0; i < count; i++)
            list[i] = Read<T, TContext>(ref reader, context);
        return list;
    }

    public static DateTime ReadDateTime(this ref RefBinaryReader reader)
        => new DateTime(reader.ReadInt64());
}