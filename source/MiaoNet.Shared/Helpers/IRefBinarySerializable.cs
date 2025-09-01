namespace MiaoNet.Shared;

public interface IRefBinarySerializable
{
    public abstract void Serialize(ref RefBinaryWriter writer);
}

public interface IRefBinarySerializable<out T> : IRefBinarySerializable
{
    public abstract static T Deserialize(ref RefBinaryReader reader);
}