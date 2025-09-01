using System.Collections.Frozen;
using System.Diagnostics;

namespace MiaoNet.Shared;

public static class PacketRegistry
{
    private delegate T RefBinaryReadHandler<out T>(ref RefBinaryReader reader);
    private static readonly FrozenDictionary<ushort, RefBinaryReadHandler<IPacket>> idToReader;
    private static readonly FrozenDictionary<Type, ushort> typeToId;

    static PacketRegistry()
    {
        var asm = typeof(PacketRegistry).Assembly;

        var infoAttrs = (PacketRegistryAttribute[])Attribute.GetCustomAttributes(asm, typeof(PacketRegistryAttribute));

        List<(ushort id, Type type, RefBinaryReadHandler<IPacket> reader)> list =
            infoAttrs.Select((a, id) =>
            {
                var type = a.Type;
                var map = type.GetInterfaceMap(typeof(IRefBinarySerializable<>).MakeGenericType(type));
                var readerIndex = Array.FindIndex(
                    map.InterfaceMethods,
                    m => m.Name is nameof(IRefBinarySerializable<IPacket>.Deserialize)
                );
                Debug.Assert(readerIndex is 0 or 1);

                var reader = (RefBinaryReadHandler<IPacket>)map.TargetMethods[readerIndex]
                    .CreateDelegate(typeof(RefBinaryReadHandler<>).MakeGenericType(type));
                return ((ushort)(id + 1), type, reader); // 0 reserved
            }).ToList();

        idToReader = list.ToFrozenDictionary(t => t.id, t => t.reader);
        typeToId = list.ToFrozenDictionary(t => t.type, t => t.id);
    }

    public static IPacket ReadPacket(ushort id, ref RefBinaryReader reader)
        => idToReader[id](ref reader);

    public static void WritePacket(IPacket packet, ref RefBinaryWriter writer)
    {
        var id = typeToId[packet.GetType()];
        writer.Write(id);
        packet.Serialize(ref writer);
    }

    public static void WritePacket<T>(T packet, ref RefBinaryWriter writer) where T : IPacket<T>
    {
        var id = typeToId[packet.GetType()];
        writer.Write(id);
        packet.Serialize(ref writer);
    }
}