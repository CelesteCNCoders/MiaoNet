namespace MiaoNet.Shared;

public interface IContextualRefBinarySerializable<in TContext>
{
    public abstract void Serialize(ref RefBinaryWriter writer, TContext context);
}

public interface IContextualRefBinarySerializable<out T, in TContext>
    : IContextualRefBinarySerializable<TContext>
{
    public abstract static T Deserialize(ref RefBinaryReader reader, TContext context);
}