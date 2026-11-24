namespace MiaoNet.Shared;

public enum ChatMessageType : byte
{
    Channel,
    PrivateMessage,
    Server,
    ServerChat
}

public sealed class PacketChatMessage : IContextlessPacket<PacketChatMessage>
{
    public DateTime DateTime { get; }

    public ChatMessageType Type { get; }

    public int? ChatChannelID { get; }

    public int? SourcePlayer { get; }

    public string Content { get; }

    public PacketChatMessage(
        DateTime dateTime,
        ChatMessageType type,
        int? chatChannelID,
        int? sourcePlayer,
        string content
    )
    {
        if (type is ChatMessageType.Channel)
            SafeGuard.Assert(chatChannelID is not null);
        if (type is ChatMessageType.PrivateMessage)
            SafeGuard.Assert(sourcePlayer is not null);
        DateTime = dateTime;
        Type = type;
        ChatChannelID = chatChannelID;
        SourcePlayer = sourcePlayer;
        Content = content;
    }

    public static PacketChatMessage Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadDateTime(),
            (ChatMessageType)reader.ReadByte(),
            reader.ReadBoolean() ? reader.Read7BitEncodedInt() : null,
            reader.ReadBoolean() ? reader.Read7BitEncodedInt() : null,
            reader.ReadString()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(DateTime);
        writer.Write((byte)Type);
        if (ChatChannelID.HasValue)
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(ChatChannelID.Value);
        }
        else
        {
            writer.Write(false);
        }
        if (SourcePlayer.HasValue)
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(SourcePlayer.Value);
        }
        else
        {
            writer.Write(false);
        }
        writer.Write(Content);
    }
}

public sealed class PacketSendChatMessage : IContextlessPacket<PacketSendChatMessage>
{
    public int ChatChannelID { get; }

    public string Content { get; }

    public PacketSendChatMessage(int chatChannelID, string content)
    {
        ChatChannelID = chatChannelID;
        Content = content;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(ChatChannelID);
        writer.Write(Content);
    }

    public static PacketSendChatMessage Deserialize(ref RefBinaryReader reader)
        => new(reader.Read7BitEncodedInt(), reader.ReadString());
}

public sealed class PacketSendPrivateChatMessage :
    IPacketRequest<PacketSendPrivateChatMessageResponse>,
    IContextlessPacket<PacketSendPrivateChatMessage>
{
    public int TargetPlayerID { get; }

    public string Content { get; }

    public PacketSendPrivateChatMessage(int targetPlayerID, string content)
    {
        TargetPlayerID = targetPlayerID;
        Content = content;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(TargetPlayerID);
        writer.Write(Content);
    }

    public static PacketSendPrivateChatMessage Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.Read7BitEncodedInt(), reader.ReadString());
    }
}

public sealed class PacketSendPrivateChatMessageResponse :
    IPacketResponse,
    IContextlessPacket<PacketSendPrivateChatMessageResponse>
{
    public enum SendResult
    {
        Success,
        NoSuchPlayer,
        Denied
    }

    public DateTime DateTime { get; }

    public SendResult Result { get; }

    public PacketSendPrivateChatMessageResponse(DateTime dateTime, SendResult result)
    {
        DateTime = dateTime;
        Result = result;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(DateTime);
        writer.Write((byte)Result);
    }

    public static PacketSendPrivateChatMessageResponse Deserialize(ref RefBinaryReader reader)
    {
        return new(reader.ReadDateTime(), (SendResult)reader.ReadByte());
    }
}
