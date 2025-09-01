using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MiaoNet.Shared;

#if MIAO_CLIENT
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<Type, PacketHandler<IPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.ToFrozenDictionary();
    }

    public bool DispatchPacket(IPacket packet)
    {
        if (dictionary.TryGetValue(packet.GetType(), out PacketHandler<IPacket>? d))
        {
            d(packet);
            return true;
        }
        else
        {
            return false;
        }
    }
}
#elif MIAO_SERVER
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<Type, PacketHandler<IPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.Select(pair => new KeyValuePair<Type, PacketHandler<IPacket>>(pair.Key, pair.Value)).ToFrozenDictionary();
    }

    public async ValueTask<bool> DispatchPacketAsync(Server.MiaoClientConnection connection, IPacket packet)
    {
        if (dictionary.TryGetValue(packet.GetType(), out var handler))
        {
            await handler(connection, packet);
            return true;
        }
        else
        {
            return false;
        }
    }
}
#endif