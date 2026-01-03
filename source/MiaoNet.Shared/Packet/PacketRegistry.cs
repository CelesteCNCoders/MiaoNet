using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;

namespace MiaoNet.Shared;

public static class PacketRegistry
{
    private delegate T RefBinaryReadHandler<out T>(ref RefBinaryReader reader, IPacketSerializationContext context);
    private static readonly FrozenDictionary<ushort, RefBinaryReadHandler<IContextualPacket>> idToReader;
    private static readonly FrozenDictionary<Type, ushort> typeToId;

    static PacketRegistry()
    {
        var asm = typeof(PacketRegistry).Assembly;

        var infoAttr = asm.GetCustomAttribute<PacketRegistryAttribute>()!;

        List<(ushort id, Type type, RefBinaryReadHandler<IContextualPacket> reader)> list =
            infoAttr.Types.Select((type, id) =>
            {
                var interfaceType = typeof(IContextualPacket<>).MakeGenericType(type);
                if (!type.IsAssignableTo(interfaceType))
                    throw new InvalidOperationException(SR.TypeMustAtLeaseImplContextualPacket);

                var map = type.GetInterfaceMap(
                    typeof(IContextualRefBinarySerializable<,>)
                        .MakeGenericType(type, typeof(IPacketSerializationContext))
                );
                var readerIndex = Array.FindIndex(
                    map.InterfaceMethods,
                    m => m.Name is nameof(IContextualPacket<>.Deserialize)
                );
                SafeGuard.Assert(readerIndex is 0 or 1);

                var reader = (RefBinaryReadHandler<IContextualPacket>)map.TargetMethods[readerIndex]
                    .CreateDelegate(typeof(RefBinaryReadHandler<>).MakeGenericType(type));
                return ((ushort)(id + 1), type, reader); // 0 reserved
            }).ToList();

        idToReader = list.ToFrozenDictionary(t => t.id, t => t.reader);
        typeToId = list.ToFrozenDictionary(t => t.type, t => t.id);
    }

    public static IContextualPacket ReadPacket(ushort id, ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        if (!idToReader.TryGetValue(id, out var handler))
            throw new KeyNotFoundException(string.Format(SR.PacketNotFoundByID, id));
        return handler(ref reader, context);
    }

    public static void WritePacket(IContextualPacket packet, ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        if (!typeToId.TryGetValue(packet.GetType(), out ushort id))
            throw new KeyNotFoundException(string.Format(SR.TypeIsNotRegisteredAsAPacket, packet.GetType().FullName));
        writer.Write(id);
        packet.Serialize(ref writer, context);
    }
}