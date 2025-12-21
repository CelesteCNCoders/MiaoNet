#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
using MiaoNet.Shared;

namespace MiaoNet.Shared;

public enum HoldableType
{
    None,
    Custom,
    Theo,
    Jelly
}

public struct HoldableInfo : IRefBinarySerializable<HoldableInfo>
{
    public HoldableType Type { get; set; }

    /* Jelly only */
    public PooledString Animation { get; set; }

    public ushort AnimationFrame { get; }

    public Vector2 Scale { get; }

    public float Rotation { get; }
    /* Jelly only */

    // *other fields that support HoldableType.Custom*

    /// <summary>For <see cref="HoldableType.Theo"/> and possible others only.</summary>
    public HoldableInfo(HoldableType type)
    {
        Type = type;
    }

    /// <summary>For <see cref="HoldableType.Jelly"/> only.</summary>
    public HoldableInfo(
        HoldableType type, 
        PooledString animation, ushort animationFrame, 
        Vector2 scale, float rotation
    )
    {
        Type = type;
        Animation = animation;
        AnimationFrame = animationFrame;
        Scale = scale;
        Rotation = rotation;
    }

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Type);
        if (Type == HoldableType.Jelly)
        {
            writer.Write(Animation);
            writer.Write(AnimationFrame);
            writer.Write(Scale);
            writer.Write(Rotation);
        }
    }

    public static HoldableInfo Deserialize(ref RefBinaryReader reader)
    {
        HoldableType type = (HoldableType)reader.ReadByte();
        if (type is HoldableType.Jelly)
            return new(
                type, 
                reader.Read<PooledString>(), reader.ReadUInt16(),
                reader.ReadVector2(), reader.ReadSingle()
            );
        if (type is HoldableType.Theo)
            return new(type);
        return new(type);
    }
}