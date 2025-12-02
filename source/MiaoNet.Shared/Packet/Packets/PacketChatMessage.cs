namespace MiaoNet.Shared;

public enum ChatMessageType : byte
{
    Chat,
    PrivateMessage,
    Server,
    ServerChat
}

public sealed class PacketChatMessage : IPacket<PacketChatMessage>
{
    public ChatMessageType Type { get; set; }

    public int? SourcePlayer { get; set; }

    public string Content { get; set; }

    public PacketChatMessage(ChatMessageType type, int? sourcePlayer, string content)
    {
        // can we introduce ChatMessageType.General that indicates a raw chat message?
        if (type is not ChatMessageType.Server)
            SafeGuard.Assert(sourcePlayer is not null);
        Type = type;
        SourcePlayer = sourcePlayer;
        Content = content;
    }

    public static PacketChatMessage Deserialize(ref RefBinaryReader reader)
        => new(
            (ChatMessageType)reader.ReadByte(), 
            reader.ReadBoolean() ? reader.ReadInt32() : null,
            reader.ReadString()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Type);
        if (SourcePlayer.HasValue)
        {
            writer.Write(true);
            writer.Write((int)SourcePlayer);
        }
        else
        {
            writer.Write(false);
        }
        writer.Write(Content);
    }
}