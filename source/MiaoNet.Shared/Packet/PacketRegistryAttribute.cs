namespace MiaoNet.Shared;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PacketRegistryAttribute : Attribute
{
    public Type Type { get; }

    public PacketRegistryAttribute(Type type)
    {
        Type = type;
    }
}