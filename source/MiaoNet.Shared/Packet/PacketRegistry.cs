using System.Collections.Frozen;
using System.Reflection;

namespace MiaoNet.Shared;

public delegate IContextualPacket RefBinaryPacketReadHandler(ref RefBinaryReader reader, IPacketSerializationContext context);

public static class PacketRegistry
{
    private static readonly FrozenDictionary<ushort, RefBinaryPacketReadHandler> idToReader;
    private static readonly FrozenDictionary<Type, ushort> typeToId;

    static PacketRegistry()
    {
        var asm = typeof(PacketRegistry).Assembly;

        var infoAttr = asm.GetCustomAttribute<PacketRegistryAttribute>()!;

        List<(ushort id, Type type, RefBinaryPacketReadHandler reader)> list =
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

                var reader = map.TargetMethods[readerIndex].CreateDelegate<RefBinaryPacketReadHandler>();
                return ((ushort)(id + 1), type, reader); // 0 reserved
            }).ToList();

        idToReader = list.ToFrozenDictionary(t => t.id, t => t.reader);
        typeToId = list.ToFrozenDictionary(t => t.type, t => t.id);
    }

    public static RefBinaryPacketReadHandler GetPacketReader(ushort id)
    {
        if (!idToReader.TryGetValue(id, out var handler))
            throw new KeyNotFoundException(string.Format(SR.PacketNotFoundByID, id));
        return handler;
    }

    public static ushort GetPacketID(IContextualPacket packet)
    {
        if (!typeToId.TryGetValue(packet.GetType(), out ushort id))
            throw new KeyNotFoundException(string.Format(SR.TypeIsNotRegisteredAsAPacket, packet.GetType().FullName));
        return id;
    }
}