using System.Diagnostics;

namespace MiaoNet.Shared;

/// <summary>
/// Used to optimize sizes in sending and receiving enum-like strings
/// (i.e. Animation names like <c>Walk</c>, <c>Jump</c>).
/// </summary>
[DebuggerDisplay("{Value}")]
public readonly struct PooledString : IContextualRefBinarySerializable<PooledString, PooledStringManager>
{
    private const int HasValueFlagMask = 1;

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
            writer.Write7BitEncodedInt(id << 1);
        }
        else
        {
            writer.Write7BitEncodedInt((id << 1) | HasValueFlagMask);
            writer.Write(Value);
        }
    }

    public static PooledString Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        int sid = reader.Read7BitEncodedInt();

        int id = sid >> 1;
        bool hasValue = (sid & HasValueFlagMask) != 0;

        return pooledStringManager.GetAndRecord(id, hasValue ? reader.ReadString() : null);
    }
}
