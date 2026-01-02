namespace MiaoNet.Shared;

public enum DisconnectReason : byte
{
    PlayerRequested,
    Kicked,
    InvalidPacket,
    InvalidPacketWithState,
    Timeout
}

// server -> client
public sealed class PacketDisconnected : IContextlessPacket<PacketDisconnected>
{
    public DisconnectReason Reason { get; set; }

    public string? Message { get; set; }

    public PacketDisconnected(DisconnectReason reason, string? message = null)
    {
        Reason = reason;
        Message = message;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Reason);
        if (Message is not null)
        {
            writer.Write(true);
            writer.Write(Message);
        }
        else
        {
            writer.Write(false);
        }
    }

    public static PacketDisconnected Deserialize(ref RefBinaryReader reader)
    {
        DisconnectReason reason = (DisconnectReason)reader.ReadByte();
        bool hasMessage = reader.ReadBoolean();
        string? message;
        message = hasMessage ? reader.ReadString() : null;
        return new(reason, message);
    }
}