namespace MiaoNet.Shared;

public sealed class PacketEmoteText : PacketPlayerNotification, IPacket<PacketEmoteText>
{
    public string Text { get; }

    public PacketEmoteText(int playerID, string text) : base(playerID)
        => Text = text;

    public override void Serialize(ref RefBinaryWriter writer)
    {
        base.Serialize(ref writer);
        writer.Write(Text);
    }

    public static PacketEmoteText Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadString());
}

public sealed class PacketSendEmoteText : IPacket<PacketSendEmoteText>
{
    public string Text { get; }

    public PacketSendEmoteText(string text)
        => Text = text;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Text);

    public static PacketSendEmoteText Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
}