namespace MiaoNet.Shared;

internal static class SR
{
    public const string MissingPooledString 
        = "A pooled string is missing but doesn't contain value.";
    public const string PooledStringValueNotMatch
        = "A pooled string is found locally(\"{0}\") but remote provided a different value(\"{1}\").";
    public const string TypeMustAtLeaseImplContextualPacket
        = "A packet type being registered must at lease implement IContextualPacket.";
    public const string TypeIsNotRegisteredAsAPacket
        = "Type \"{0}\" is not registered as a packet.";
    public const string PacketNotFoundByID
        = "Packet type with id {0} is not found.";
    public const string PacketTooLarge
        = "Packet \"{0}\" is too large with size {1}.";
    public const string PacketHasDataLeft
        = "Packet id {0} read finished but left {1} bytes not read.";
    public const string HasDataLeft
        = "Object {0} read finished but left {1} bytes not read.";
}