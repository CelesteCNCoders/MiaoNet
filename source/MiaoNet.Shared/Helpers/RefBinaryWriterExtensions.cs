using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;

namespace MiaoNet.Shared;

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

    public static void Write(this ref RefBinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining), StackTraceHidden, DebuggerHidden]
    public static void Write<T>(this ref RefBinaryWriter writer, T value) where T : IRefBinarySerializable<T>
        => value.Serialize(ref writer);

    [MethodImpl(MethodImplOptions.AggressiveInlining), StackTraceHidden, DebuggerHidden]
    public static void Write<T, TContext>(this ref RefBinaryWriter writer, T value, TContext context)
        where T : IContextualRefBinarySerializable<T, TContext>
        => value.Serialize(ref writer, context);

    public static void Write(this ref RefBinaryWriter writer, IReadOnlyCollection<string> strings)
    {
        if (strings.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(strings));
        writer.Write((ushort)strings.Count);
        foreach (var item in strings)
            writer.Write(item);
    }

    public static void Write<T>(this ref RefBinaryWriter writer, IReadOnlyCollection<T> values)
        where T : IRefBinarySerializable<T>
    {
        if (values.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(values));
        writer.Write((ushort)values.Count);
        foreach (var item in values)
            item.Serialize(ref writer);
    }

    public static void Write<T, TContext>(this ref RefBinaryWriter writer, IReadOnlyCollection<T> values, TContext context)
        where T : IContextualRefBinarySerializable<T, TContext>
    {
        if (values.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(values));
        writer.Write((ushort)values.Count);
        foreach (var item in values)
            item.Serialize(ref writer, context);
    }

    public static void WriteSmall<T>(this ref RefBinaryWriter writer, IReadOnlyCollection<T> values)
    where T : IRefBinarySerializable<T>
    {
        if (values.Count > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(values));
        writer.Write((byte)values.Count);
        foreach (var item in values)
            item.Serialize(ref writer);
    }

    public static void WriteSmall<T, TContext>(this ref RefBinaryWriter writer, IReadOnlyCollection<T> values, TContext context)
        where T : IContextualRefBinarySerializable<T, TContext>
    {
        if (values.Count > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(values));
        writer.Write((byte)values.Count);
        foreach (var item in values)
            item.Serialize(ref writer, context);
    }

    public static void Write(this ref RefBinaryWriter writer, DateTime dateTime)
        => writer.Write(dateTime.Ticks);
}