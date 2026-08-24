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
    public const string HasDataLeft
        = "Object {0} read finished but left {1} bytes not read.";
    public const string DeltasLengthMismatch
        = "Length of deltas {0} mismatched length {1}.";
}
