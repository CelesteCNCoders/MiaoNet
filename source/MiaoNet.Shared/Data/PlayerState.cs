namespace MiaoNet.Shared;

/// <summary>
/// Player's position, dashes and so on.
/// </summary>
public sealed class PlayerState : IRefBinarySerializable<PlayerState>
{
    public float X;

    public float Y;

    public byte Dashes;

    public PlayerState(float x, float y, byte dashes)
    {
        (X, Y) = (x, y);
        Dashes = dashes;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Dashes);
    }

    public static PlayerState Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte());

    public override string ToString()
        => $"({X}, {Y}), Dashes = {Dashes}";
}