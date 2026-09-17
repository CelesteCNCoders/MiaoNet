namespace MiaoNet.Shared;

public sealed class PacketEmoteText : IContextlessPacket<PacketEmoteText>
{
    public string Text { get; }

    public PacketEmoteText(string text)
        => Text = text;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Text);

    public static PacketEmoteText Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
}
