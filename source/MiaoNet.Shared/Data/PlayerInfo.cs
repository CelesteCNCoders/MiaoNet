namespace MiaoNet.Shared;

public sealed class PlayerInfo : IRefBinarySerializable<PlayerInfo>
{
    public int ID { get; set; }

    public string Name { get; set; }

    public PlayerInfo(int id, string name)
    {
        ID = id;
        Name = name;
    }

    public override string ToString()
        => $"P-{Name}:{ID}";

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ID);
        writer.Write(Name);
    }

    public static PlayerInfo Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadString());
}