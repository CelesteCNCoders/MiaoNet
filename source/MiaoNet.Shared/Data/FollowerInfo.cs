namespace MiaoNet.Shared;

public enum FollowerType
{
    Strawberry,
    StrawberrySeed,
    Key,
    Custom
}

// TODO i'm lazy to minimalize the size...
public readonly struct FollowerInfo : IContextualRefBinarySerializable<FollowerInfo, PooledStringManager>
{
    public FollowerType Type { get; }

    public PooledString SpriteID { get; }

    public PooledString AnimationID { get; }

    public ushort AnimationFrame { get; }

    public Vector2S Offset { get; }

    public FollowerInfo(
        FollowerType type,
        PooledString spriteID,
        PooledString animationID, ushort animationFrame,
        Vector2S offset
    )
    {
        Type = type;
        SpriteID = spriteID;
        AnimationID = animationID;
        AnimationFrame = animationFrame;
        Offset = offset;
    }

    public readonly void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write((byte)Type);
        writer.Write(SpriteID, pooledStringManager);
        writer.Write(AnimationID, pooledStringManager);
        writer.Write(AnimationFrame);
        writer.Write(Offset);
    }

    public static FollowerInfo Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        return new FollowerInfo(
            (FollowerType)reader.ReadByte(),
            reader.Read<PooledString, PooledStringManager>(pooledStringManager),
            reader.Read<PooledString, PooledStringManager>(pooledStringManager),
            reader.ReadUInt16(),
            reader.Read<Vector2S>()
        );
    }
}

public readonly struct FollowerInfoDelta : IContextualRefBinarySerializable<FollowerInfoDelta, PooledStringManager>
{
    public PooledString AnimationID { get; }

    public ushort AnimationFrame { get; }

    public Vector2S Offset { get; }

    public FollowerInfoDelta(string animationID, ushort animationFrame, Vector2S offset)
    {
        AnimationID = animationID;
        AnimationFrame = animationFrame;
        Offset = offset;
    }

    public readonly void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write(AnimationID, pooledStringManager);
        writer.Write(AnimationFrame);
        writer.Write(Offset);
    }

    public static FollowerInfoDelta Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        return new FollowerInfoDelta(
            animationID: reader.Read<PooledString, PooledStringManager>(pooledStringManager),
            animationFrame: reader.ReadUInt16(),
            reader.Read<Vector2S>()
        );
    }
}