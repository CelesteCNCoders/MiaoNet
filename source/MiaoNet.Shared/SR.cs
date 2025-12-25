namespace MiaoNet.Shared;

internal static class SR
{
    public const string MissingPooledString 
        = "A pooled string is missing but doesn't contain value.";
    public const string PooledStringValueNotMatch
        = "A pooled string is found locally but remote provided a different value.";
    public const string TypeMustAtLeaseImplContextualPacket
        = "A packet type being registered must at lease implement IContextualPacket.";
}