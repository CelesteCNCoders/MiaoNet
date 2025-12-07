using System.Diagnostics;
#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
namespace MiaoNet.Shared;

/// <summary>
/// Player's position, dashes and so on.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class PlayerState : IRefBinarySerializable<PlayerState>
{
    public Vector2 Position;

    public byte Dashes;

    public bool Dashing; // not serialized

    public float TimeRate = 1.0f;

#if MIAO_CLIENT
    // only used to initialize players who are suddenly in debug map
    public PlayerState(Vector2 position)
    {
        Position = position;
    }
#endif

    public PlayerState(Vector2 position, byte dashes, float timeRate)
    {
        Position = position;
        Dashes = dashes;
        TimeRate = timeRate;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Position);
        writer.Write(Dashes);
        writer.Write(TimeRate);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadVector2(), reader.ReadByte(), reader.ReadSingle());

    public override string ToString()
        => $"({Position.X}, {Position.Y}), Dashes = {Dashes}, TimeRate = {TimeRate:F2}";

    [DebuggerHidden]
    private string DebuggerDisplay => ToString();
}