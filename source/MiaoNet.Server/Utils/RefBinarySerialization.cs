using MiaoNet.Shared;

namespace MiaoNet.Server;

public static class RefBinarySerialization
{
    // TODO avoid using memory streams
    public static byte[] Serialize<T>(T value, int capacity = 32)
        where T : IRefBinarySerializable
    {
        MemoryStream ms = new(capacity);
        RefBinaryWriter w = new(ms);
        value.Serialize(ref w);
        return ms.GetBuffer().AsSpan()[..(int)ms.Position].ToArray();
    }

    public static T Deserialize<T>(byte[] data)
        where T : IRefBinarySerializable<T>
    {
        RefBinaryReader r = new(data);
        var value = r.Read<T>();
        return value;
    }
}
