namespace MiaoNet.Shared;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PacketRegistryAttribute : Attribute
{
    public Type[] Types { get; }

    public PacketRegistryAttribute(Type[] types)
    {
        Types = types;
    }
}