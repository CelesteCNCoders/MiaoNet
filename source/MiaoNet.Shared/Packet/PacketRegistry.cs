using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;

namespace MiaoNet.Shared;

public delegate IContextualPacket RefBinaryPacketReadHandler(ref RefBinaryReader reader, IPacketSerializationContext context);

public static class PacketRegistry
{
    public const byte MaxPacketCount = byte.MaxValue;

    private static readonly FrozenDictionary<byte, RefBinaryPacketReadHandler> idToReader;
    private static readonly FrozenDictionary<Type, byte> typeToID;

    static PacketRegistry()
    {
        var asm = typeof(PacketRegistry).Assembly;

        var infoAttr = asm.GetCustomAttribute<PacketRegistryAttribute>()!;

        if (infoAttr.Types.Length > MaxPacketCount)
            throw new InvalidOperationException(SR.TooManyPacketsRegistered);

        List<(byte id, Type type, RefBinaryPacketReadHandler reader)> list =
            infoAttr.Types.Select((type, id) =>
            {
                RefBinaryPacketReadHandler reader = PacketReaderFactory.CreateFor(type);
                return ((byte)(id + 1), type, reader); // 0 reserved
            }).ToList();

        idToReader = list.ToFrozenDictionary(t => t.id, t => t.reader);
        typeToID = list.ToFrozenDictionary(t => t.type, t => t.id);
    }

    public static RefBinaryPacketReadHandler GetPacketReader(byte id)
    {
        if (!idToReader.TryGetValue(id, out var handler))
            throw new KeyNotFoundException(string.Format(CultureInfo.InvariantCulture, SR.PacketNotFoundByID, id));
        return handler;
    }

    public static byte GetPacketID(IContextualPacket packet)
    {
        if (typeToID.TryGetValue(packet.GetType(), out byte id))
            return id;

        throw new KeyNotFoundException(string.Format(
            CultureInfo.InvariantCulture, SR.TypeIsNotRegisteredAsAPacket, packet.GetType().FullName
        ));
    }
}
