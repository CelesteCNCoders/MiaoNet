using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchEntityIDHolder
{
    public string Level { get; }

    public int ID { get; }

    public WatchEntityIDHolder(string level, int id)
    {
        Level = level;
        ID = id;
    }
}

internal static class WatchEntityIDTable<TEntity> where TEntity : class
{
    private static readonly ConditionalWeakTable<TEntity, WatchEntityIDHolder> ids = new();

    public static void Set(TEntity entity, string level, int id)
        => ids.AddOrUpdate(entity, new WatchEntityIDHolder(level, id));

    public static bool TryGet(TEntity entity, string level, out int id)
    {
        if (ids.TryGetValue(entity, out WatchEntityIDHolder? holder)
            && StringComparer.Ordinal.Equals(holder.Level, level))
        {
            id = holder.ID;
            return true;
        }

        id = default;
        return false;
    }

    public static void Clear()
        => ids.Clear();
}

internal static class WatchSyntheticEntityIDTable<TEntity> where TEntity : class
{
    private static readonly ConditionalWeakTable<TEntity, WatchEntityIDHolder> ids = new();

    public static void Set(TEntity entity, int id)
        => ids.AddOrUpdate(entity, new WatchEntityIDHolder(string.Empty, id));

    public static bool TryGet(TEntity entity, out int id)
    {
        if (ids.TryGetValue(entity, out WatchEntityIDHolder? holder))
        {
            id = holder.ID;
            return true;
        }

        id = default;
        return false;
    }

    public static void Clear()
        => ids.Clear();
}

internal static class WatchEntityPayloadCodec
{
    public static void WriteSingle(Span<byte> payload, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            payload[offset..],
            BitConverter.SingleToInt32Bits(value)
        );

    public static float ReadSingle(ReadOnlySpan<byte> payload, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]));

    public static void WriteUInt16(Span<byte> payload, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(payload[offset..], value);

    public static ushort ReadUInt16(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
}
