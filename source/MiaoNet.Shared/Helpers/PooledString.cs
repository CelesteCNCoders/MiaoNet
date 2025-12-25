using System.Diagnostics;

namespace MiaoNet.Shared;

/// <summary>
/// Used to optimize sizes in sending and receiving enum-like strings
/// (i.e. Animation names like <c>Walk</c>, <c>Jump</c>).
/// </summary>
[DebuggerDisplay("{Value}")]
public readonly struct PooledString : IContextualRefBinarySerializable<PooledString, PooledStringManager>
{
    private const int Int32HighestBitMask = 1 << 0x1F;

    public string Value { get; }

    public PooledString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public static implicit operator string(PooledString value)
        => value.Value;

    public static implicit operator PooledString(string value)
        => new(value);

    public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        if (pooledStringManager.GetOrCreateID(Value, out int id))
        {
            writer.Write(id);
        }
        else
        {
            writer.Write(id | Int32HighestBitMask);
            writer.Write(Value);
        }
    }

    public static PooledString Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        int sid = reader.ReadInt32();

        int id = sid & ~Int32HighestBitMask;
        bool hasValue = (sid & Int32HighestBitMask) == Int32HighestBitMask;

        return pooledStringManager.GetAndRecord(id, hasValue ? reader.ReadString() : null);
    }
}