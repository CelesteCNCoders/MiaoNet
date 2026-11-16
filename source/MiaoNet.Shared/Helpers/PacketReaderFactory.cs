using System.Globalization;
using System.Reflection;

namespace MiaoNet.Shared;

public static class PacketReaderFactory
{
    public static RefBinaryPacketReadHandler CreateFor(Type type)
    {
        Type interfaceType = typeof(IContextualPacket<>).MakeGenericType(type);
        if (!type.IsAssignableTo(interfaceType))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.TypeMustAtLeaseImplContextualPacket
            ));
        }

        InterfaceMapping map = type.GetInterfaceMap(
            typeof(IContextualRefBinarySerializable<,>)
                .MakeGenericType(type, typeof(IPacketSerializationContext))
        );
        int readerIndex = Array.FindIndex(
            map.InterfaceMethods,
            m => m.Name is nameof(IContextualPacket<>.Deserialize)
        );
        SafeGuard.Assert(readerIndex is 0 or 1);

        return map.TargetMethods[readerIndex].CreateDelegate<RefBinaryPacketReadHandler>();
    }
}
