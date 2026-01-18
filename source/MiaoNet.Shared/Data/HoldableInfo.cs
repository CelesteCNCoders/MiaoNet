namespace MiaoNet.Shared;

public enum HoldableType
{
    None,
    Theo,
    Jelly,
    Player,
    /// <summary>Same fields with <see cref="Theo"/.></summary>
    Custom,
    /// <summary>Same fields with <see cref="Jelly"/.></summary>
    CustomExtended
}

// TODO Use flags to indicate which flags are custom holdable types needed?
public struct HoldableInfo : IContextualRefBinarySerializable<HoldableInfo, PooledStringManager>
{
    public HoldableType Type { get; }

    public Vector2? Offset { get; set; }

    #region Extended Fields

    public PooledString Animation { get; }

    public ushort AnimationFrame { get; }

    public Vector2 Scale { get; }

    public float Rotation { get; }

    #endregion

    // *other fields that support HoldableType.Custom*

    public HoldableInfo(HoldableType type, Vector2? offset)
    {
        if (HasExtendedFields(type))
            throw new ArgumentException(null, nameof(type));
        Type = type;
        Offset = offset;
    }

    public HoldableInfo(
        HoldableType type, Vector2? offset,
        PooledString animation, ushort animationFrame,
        Vector2 scale, float rotation
    )
    {
        if (!HasExtendedFields(type))
            throw new ArgumentException(null, nameof(type));
        Type = type;
        Offset = offset;
        Animation = animation;
        AnimationFrame = animationFrame;
        Scale = scale;
        Rotation = rotation;
    }

    public readonly void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
    {
        writer.Write((byte)Type);
        if (Type is HoldableType.None)
            return;

        if (Offset is Vector2 offset)
        {
            writer.Write(true);
            writer.Write(offset);
        }
        else
        {
            writer.Write(false);
        }
        if (HasExtendedFields(Type))
        {
            writer.Write(Animation, pooledStringManager);
            writer.Write(AnimationFrame);
            writer.Write(Scale);
            writer.Write(Rotation);
        }
    }

    public static HoldableInfo Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
    {
        HoldableType type = (HoldableType)reader.ReadByte();
        if (type is HoldableType.None)
            return new(type, null);

        Vector2? offset = null;
        bool hasOffset = reader.ReadBoolean();
        if (hasOffset)
            offset = reader.ReadVector2();
        if (HasExtendedFields(type))
        {
            return new HoldableInfo(
                type, offset,
                reader.Read<PooledString, PooledStringManager>(pooledStringManager), reader.ReadUInt16(),
                reader.ReadVector2(), reader.ReadSingle()
            );
        }
        else
        {
            return new HoldableInfo(type, offset);
        }
    }

    private static bool HasExtendedFields(HoldableType type)
        => type is HoldableType.Jelly or HoldableType.CustomExtended;
}