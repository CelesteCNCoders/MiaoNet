using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

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

    public static TEntity? Find(IEnumerable<TEntity> entities, string level, int id)
    {
        foreach (TEntity entity in entities)
            if (TryGet(entity, level, out int candidateID) && candidateID == id)
                return entity;
        return null;
    }

    public static TEntity? Find(Level level, int id)
        => Find(level.Entities.OfType<TEntity>(), level.Session.Level, id);

    public static TEntity? Find(Level level, string room, int id)
        => Find(level.Entities.OfType<TEntity>(), room, id);

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
    public static void WriteInt32(Span<byte> payload, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(payload[offset..], value);

    public static int ReadInt32(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);

    public static void WriteSingle(Span<byte> payload, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            payload[offset..],
            BitConverter.SingleToInt32Bits(value)
        );

    public static float ReadSingle(ReadOnlySpan<byte> payload, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]));

    public static void WriteVector2(Span<byte> payload, int offset, Vector2 value)
    {
        WriteSingle(payload, offset, value.X);
        WriteSingle(payload, offset + sizeof(float), value.Y);
    }

    public static Vector2 ReadVector2(ReadOnlySpan<byte> payload, int offset)
        => new(ReadSingle(payload, offset), ReadSingle(payload, offset + sizeof(float)));

    public static void WriteUInt16(Span<byte> payload, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(payload[offset..], value);

    public static ushort ReadUInt16(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
}
