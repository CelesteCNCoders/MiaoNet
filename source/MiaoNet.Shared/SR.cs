namespace MiaoNet.Shared;

internal static class SR
{
    public const string MissingPooledString
        = "A pooled string(id {0}) is missing but doesn't contain value.";
    public const string PooledStringValueNotMatch
        = "A pooled string is found locally(\"{0}\") but remote provided a different value(\"{1}\").";
    public const string InvalidPooledStringID
        = "A pooled string id must be positive, but remote provided {0}.";
    public const string UnexpectedPooledStringID
        = "A new pooled string has id {0}, but the next expected id is {1}.";
    public const string PooledStringEntryLimitExceeded
        = "The remote pooled string entry limit has been exceeded.";
    public const string PooledStringValueTooLarge
        = "A remote pooled string uses {0} UTF-8 bytes, exceeding the limit of {1}.";
    public const string PooledStringTotalBytesExceeded
        = "The remote pooled string UTF-8 byte limit has been exceeded.";
    public const string TypeMustAtLeaseImplContextualPacket
        = "A packet type being registered must at lease implement IContextualPacket.";
    public const string TooManyPacketsRegistered
        = "At most 255 packets can be registered.";
    public const string TypeIsNotRegisteredAsAPacket
        = "Type \"{0}\" is not registered as a packet.";
    public const string PacketNotFoundByID
        = "Packet type with id {0} is not found.";
    public const string PacketTooLarge
        = "Packet \"{0}\" is too large with payload size {1}; the maximum is {2}.";
    public const string PacketTruncated
        = "Packet {0} is truncated: read {1} of {2} bytes.";
    public const string PacketHeaderBufferTooSmall
        = "The packet header buffer is too small.";
    public const string PacketPartHeader
        = "header";
    public const string PacketPartPayload
        = "payload";
    public const string PacketHasDataLeft
        = "Packet id {0} read finished but left {1} bytes not read.";
    public const string DeltasLengthMismatch
        = "Length of deltas {0} mismatched length {1}.";
    public const string EnvelopeHasNoSenderID
        = "The packet envelope has no sender id, but the sender id was requested.";
    public const string EnvelopeHasNoRequestID
        = "The packet envelope has no request id, but the request id was requested.";
    public const string TooManyMulticastTargets
        = "A multicast packet can address at most {0} players.";
    public const string UnknownPacketScope
        = "A route carries unknown packet scope {0}.";
    public const string NetModIDOutOfRange
        = "The sender declared {0} net-mod(s), but referenced index {1}.";
    public const string NetModDeclarationTooLong
        = "A client declared {0} net-mods, but at most {1} can be indexed on the wire.";
    public const string NetModDeclarationIsUnnamed
        = "Net-mod declaration {0} carries no name.";
    public const string UnknownNetModPacket
        = "No net-mod is registered at index {0}, so packet id {1} cannot be decoded.";
    public const string NetModAlreadyRegistered
        = "A net-mod named \"{0}\" is already registered.";
    public const string NetModPacketTypeAlreadyRegistered
        = "Net-mod \"{0}\" declares the packet type \"{1}\" twice.";
    public const string NetModIDSpaceExhausted
        = "At most {0} net-mods can be registered.";
    public const string NetModPacketIDSpaceExhausted
        = "Net-mod \"{0}\" registered more than {1} packet types.";
    public const string NetModUnregistered
        = "Net-mod \"{0}\" is not registered, so its packets cannot be sent.";
    public const string NetModPacketNotDeclared
        = "Net-mod \"{0}\" never declared a packet of type \"{1}\", so it cannot send it.";
    public const string NetModNotConnected
        = "A net-mod packet was sent while no connection is installed.";
    public const string NetModSendNotAllowed
        = "Net-mod \"{0}\" packet \"{1}\" was declared with the {2} scope.";
    public const string BroadcastScopeRequired
        = "A broadcast route requires a broadcast scope, but {0} was given.";
    public const string NetModPacketHasNoSender
        = "A net-mod packet arrived without a sender.";
}
