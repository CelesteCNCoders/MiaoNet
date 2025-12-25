namespace MiaoNet.Shared;

public interface IPacketSerializationContext
{
    public PooledStringManager PooledStringManager { get; }
}