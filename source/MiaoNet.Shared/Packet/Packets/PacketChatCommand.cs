namespace MiaoNet.Shared;

public sealed class PacketChatCommand : PacketRequest<PacketChatCommandResponse>, IPacket<PacketChatCommand>
{
    public int CommandID { get; set; }

    public CommandSegment[] Segments { get; set; }

    public PacketChatCommand(int commandID, CommandSegment[] segments)
    {
        CommandID = commandID;
        Segments = segments;
    }

    public static PacketChatCommand Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadArray<CommandSegment>());

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(CommandID);
        writer.Write(Segments);
    }
}

public sealed class PacketChatCommandResponse : PacketResponse, IPacket<PacketChatCommandResponse>
{
    public CommandSegment[] Segments { get; set; }

    public PacketChatCommandResponse(int requestID, CommandSegment[] segments)
        : base(requestID)
    {
        Segments = segments;
    }

    public override void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(RequestID);
        writer.Write(Segments);
    }

    public static PacketChatCommandResponse Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadArray<CommandSegment>());
}