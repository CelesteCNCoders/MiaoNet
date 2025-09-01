namespace MiaoNet.Shared;

public sealed class PlayerStats : IRefBinarySerializable<PlayerStats>
{
    public float X;

    public float Y;

    public byte Dashes;

    public PlayerStats(float x, float y, byte dashes)
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

    public static PlayerStats Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte());
}