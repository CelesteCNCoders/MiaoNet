namespace MiaoNet.Shared;

// TODO this implement is ugly
public interface IHasPooledString<TPacket> where TPacket : IPacket<TPacket>
{
    public object ResolveAllPooledString(PooledStringManager manager);

    public void RepackWith(object storageObject, PooledStringManager manager);
}