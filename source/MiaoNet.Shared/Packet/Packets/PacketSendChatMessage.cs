namespace MiaoNet.Shared;

public sealed class PacketSendChatMessage : IPacket<PacketSendChatMessage>
{
    public string Content { get; }

    public PacketSendChatMessage(string content)
    {
        Content = content;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Content);
    }

    public static PacketSendChatMessage Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
}