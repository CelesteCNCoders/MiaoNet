#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif

namespace MiaoNet.Shared;

public struct Vector2S : IRefBinarySerializable<Vector2S>, IEquatable<Vector2S>
{
    public short X;
    public short Y;

    public Vector2S(short x, short y)
    {
        X = x;
        Y = y;
    }

    public readonly void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
    }

    public static Vector2S Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadInt16(), reader.ReadInt16());
    }

    public static implicit operator Vector2(Vector2S vec)
        => new(vec.X, vec.Y);

    public static explicit operator Vector2S(Vector2 vec)
        => new((short)vec.X, (short)vec.Y);

    public readonly bool Equals(Vector2S other)
        => X == other.X && Y == other.Y;

    public readonly override bool Equals(object? obj) 
        => obj is Vector2S vec && Equals(vec);

    public static bool operator ==(Vector2S left, Vector2S right) 
        => left.Equals(right);

    public static bool operator !=(Vector2S left, Vector2S right) 
        => !(left == right);

    public readonly override int GetHashCode()
        => HashCode.Combine(X, Y);
}