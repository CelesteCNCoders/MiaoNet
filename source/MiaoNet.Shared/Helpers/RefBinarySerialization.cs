namespace MiaoNet.Shared;

public static class RefBinarySerialization
{
    public static byte[] Serialize<T>(T value, int capacity = 128)
        where T : IRefBinarySerializable
    {
        ByteArrayBufferWriter buffer = new(capacity);
        RefBinaryWriter writer = new(buffer);
        value.Serialize(ref writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> data)
        where T : IRefBinarySerializable<T>
    {
        RefBinaryReader reader = new(data);
        return reader.Read<T>();
    }
}
