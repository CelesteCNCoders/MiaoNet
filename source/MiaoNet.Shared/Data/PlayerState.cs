using System.Diagnostics;
using System.Text.Json.Serialization;
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
    public Vector2 Position { get; set; }

    public byte Dashes { get; set; }

    // not serialized
    public bool Dashing { get; set; }

    public float DeltaTime { get; set; }

    // TODO some packets that update this property
    public PlayerSpriteMode PlayerSpriteMode { get; set; }

    public bool Dead { get; set; }

    public PlayerState(Vector2 position, byte dashes, float deltaTime)
    {
        Position = position;
        Dashes = dashes;
        DeltaTime = deltaTime;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Position);
        writer.Write(Dashes);
        writer.Write(DeltaTime);
        writer.Write((byte)PlayerSpriteMode);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadVector2(), reader.ReadByte(), reader.ReadSingle())
        {
            PlayerSpriteMode = (PlayerSpriteMode)reader.ReadByte()
        };

    public override string ToString()
        => $"({Position.X}, {Position.Y})";

    [DebuggerHidden]
    private string DebuggerDisplay => ToString();
}