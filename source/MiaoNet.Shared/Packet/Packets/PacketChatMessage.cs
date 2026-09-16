namespace MiaoNet.Shared;

public enum ChatMessageType : byte
{
    Chat,
    MapChat,
    ChannelChat,
    PrivateMessage,
    Server,
    ServerChat
}

public sealed class PacketChatMessage : IContextlessPacket<PacketChatMessage>
{
    public DateTime DateTime { get; set; }

    public ChatMessageType Type { get; set; }

    public int? SourcePlayer { get; set; }

    public string Content { get; set; }

    public PacketChatMessage(DateTime dateTime, ChatMessageType type, int? sourcePlayer, string content)
    {
        if (type is not ChatMessageType.Server)
            SafeGuard.Assert(sourcePlayer is not null);
        DateTime = dateTime;
        Type = type;
        SourcePlayer = sourcePlayer;
        Content = content;
    }

    public static PacketChatMessage Deserialize(ref RefBinaryReader reader)
        => new(
            reader.ReadDateTime(),
            (ChatMessageType)reader.ReadByte(),
            reader.ReadBoolean() ? reader.Read7BitEncodedInt() : null,
            reader.ReadString()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(DateTime);
        writer.Write((byte)Type);
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
    public ChatChannel ChatChannel { get; }

    public string Content { get; }

    public PacketSendChatMessage(ChatChannel chatChannel, string content)
    {
        ChatChannel = chatChannel;
        Content = content;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)ChatChannel);
        writer.Write(Content);
    }

    public static PacketSendChatMessage Deserialize(ref RefBinaryReader reader)
        => new((ChatChannel)reader.ReadByte(), reader.ReadString());
}

public sealed class PacketSendPrivateChatMessage :
    PacketRequest<PacketSendPrivateChatMessageResponse>,
    IContextlessPacket<PacketSendPrivateChatMessage>
{
    public int TargetPlayerID { get; }

    public string Content { get; }

    public PacketSendPrivateChatMessage(int targetPlayerID, string content)
    {
        TargetPlayerID = targetPlayerID;
        Content = content;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(RequestID);
        writer.Write7BitEncodedInt(TargetPlayerID);
        writer.Write(Content);
    }

    public static PacketSendPrivateChatMessage Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.Read7BitEncodedInt();
        return new(reader.Read7BitEncodedInt(), reader.ReadString()) { RequestID = reqID };
    }
}

public sealed class PacketSendPrivateChatMessageResponse :
    PacketResponse,
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

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(RequestID);
        writer.Write(DateTime);
        writer.Write((byte)Result);
    }

    public static PacketSendPrivateChatMessageResponse Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.Read7BitEncodedInt();
        return new(reader.ReadDateTime(), (SendResult)reader.ReadByte()) { RequestID = reqID };
    }
}
