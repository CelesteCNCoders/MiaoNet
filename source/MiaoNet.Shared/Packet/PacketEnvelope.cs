namespace MiaoNet.Shared;

[Flags]
public enum PacketEnvelopeFlags : byte
{
    None = 0,
    HasSender = 1 << 0,
    HasRequestID = 1 << 1,
    IsResponse = 1 << 2
}

public readonly struct PacketEnvelope
{
    private readonly int senderPlayerID;
    private readonly int requestID;

    public int SenderPlayerID
        => HasSender
            ? senderPlayerID
            : throw new InvalidOperationException(SR.EnvelopeHasNoSenderID);

    public int RequestID
        => HasRequestID
            ? requestID
            : throw new InvalidOperationException(SR.EnvelopeHasNoRequestID);

    public PacketEnvelopeFlags Flags { get; }

    public bool HasSender => (Flags & PacketEnvelopeFlags.HasSender) != 0;

    public bool HasRequestID => (Flags & PacketEnvelopeFlags.HasRequestID) != 0;

    public bool IsResponse => (Flags & PacketEnvelopeFlags.IsResponse) != 0;

    private PacketEnvelope(int senderPlayerID, int requestID, PacketEnvelopeFlags flags)
    {
        this.senderPlayerID = senderPlayerID;
        this.requestID = requestID;
        Flags = flags;
    }

    public static PacketEnvelope FromSender(int senderPlayerID)
        => new(senderPlayerID, 0, PacketEnvelopeFlags.HasSender);

    public static PacketEnvelope FromRequest(int requestID)
        => new(0, requestID, PacketEnvelopeFlags.HasRequestID);

    public static PacketEnvelope ReplyTo(int requestID)
        => new(0, requestID, PacketEnvelopeFlags.HasRequestID | PacketEnvelopeFlags.IsResponse);

    public void WriteOptional(ref RefBinaryWriter writer)
    {
        if (HasSender)
            writer.Write7BitEncodedInt(SenderPlayerID);
        if (HasRequestID)
            writer.Write7BitEncodedInt(RequestID);
    }

    public static PacketEnvelope ReadOptional(ref RefBinaryReader reader, PacketEnvelopeFlags flags)
    {
        int senderPlayerID = (flags & PacketEnvelopeFlags.HasSender) != 0 ? reader.Read7BitEncodedInt() : 0;
        int requestID = (flags & PacketEnvelopeFlags.HasRequestID) != 0 ? reader.Read7BitEncodedInt() : 0;

        return new(senderPlayerID, requestID, flags);
    }
}
