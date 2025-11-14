using System.Diagnostics;

namespace MiaoNet.Shared;

/// <summary>
/// Player's position, dashes and so on.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class PlayerState : IRefBinarySerializable<PlayerState>
{
    public float X;

    public float Y;

    public byte Dashes;

    public bool Dashing;

    public float TimeRate = 1.0f;

    public PlayerState(float x, float y, byte dashes, float timeRate)
    {
        (X, Y) = (x, y);
        Dashes = dashes;
        TimeRate = timeRate;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Dashes);
        writer.Write(TimeRate);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadSingle());

    public override string ToString()
        => $"({X}, {Y}), Dashes = {Dashes}, TimeRate = {TimeRate:F2}";

    [DebuggerHidden]
    private string DebuggerDisplay => ToString();
}