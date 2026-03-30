namespace MiaoNet.Shared;

public enum ChatMessageType : byte
{
    Chat,
    PrivateMessage,
    MapChat,
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
            reader.ReadBoolean() ? reader.ReadInt32() : null,
            reader.ReadString()
        );

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(DateTime);
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

public sealed class PacketSendChatMessage : IContextlessPacket<PacketSendChatMessage>
{
    public string Content { get; }

    public PacketSendChatMessage(string content)
        => Content = content;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Content);

    public static PacketSendChatMessage Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
}

public sealed class PacketSendMapChatMessage : IContextlessPacket<PacketSendMapChatMessage>
{
    public string Content { get; }

    public PacketSendMapChatMessage(string content)
        => Content = content;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Content);

    public static PacketSendMapChatMessage Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadString());
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
        writer.Write(RequestID);
        writer.Write(TargetPlayerID);
        writer.Write(Content);
    }

    public static PacketSendPrivateChatMessage Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.ReadInt32();
        return new(reader.ReadInt32(), reader.ReadString()) { RequestID = reqID };
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
        writer.Write(RequestID);
        writer.Write(DateTime);
        writer.Write((byte)Result);
    }

    public static PacketSendPrivateChatMessageResponse Deserialize(ref RefBinaryReader reader)
    {
        int reqID = reader.ReadInt32();
        return new(reader.ReadDateTime(), (SendResult)reader.ReadByte()) { RequestID = reqID };
    }
}