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
    [JsonInclude]
    public Vector2 Position;

    [JsonInclude]
    public byte Dashes;

    [JsonInclude]
    public bool Dashing; // not serialized

    [JsonInclude]
    public float TimeRate = 1.0f;

    // TODO some packets update this field
    [JsonInclude]
    public PlayerSpriteMode PlayerSpriteMode;

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