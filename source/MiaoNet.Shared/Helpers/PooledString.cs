using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace MiaoNet.Shared;

// TODO if client received a PooledString but do not resolve it
// then client don't cache this string but server think it does
// is it ok?
// actually, no, why will you not resolve it?

/// <summary>
/// Used to optimize the size of enum-like strings
/// (i.e. Animation names like <c>Walk</c>, <c>Jump</c>).
/// Always used with <see cref="PooledStringManager"/> at the same time.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct PooledString : IRefBinarySerializable<PooledString>
{
    private const int Int32HighestBitMask = 1 << 0x1F;

    public int ID { get; }

    public string? Value { get; }

    private string DebuggerDisplay => Value is null ? $"ID = {ID}" : $"Value = {Value}, ID = {ID}";

    // bad that we don't have class-level friend access in .net

    /// <summary><b>Do not</b> use this, use <see cref="PooledStringManager"/> instead.</summary>
    public PooledString(int id, string? value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ID = id;
        Value = value;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        if (Value is not null)
        {
            writer.Write(ID | Int32HighestBitMask);
            writer.Write(Value);
        }
        else
        {
            writer.Write(ID);
        }
    }

    public static PooledString Deserialize(ref RefBinaryReader reader)
    {
        int sid = reader.ReadInt32();

        int id = sid & ~Int32HighestBitMask;
        bool hasValue = (sid & Int32HighestBitMask) == Int32HighestBitMask;

        if (hasValue)
            return new(id, reader.ReadString());
        else
            return new(id, null);
    }
}