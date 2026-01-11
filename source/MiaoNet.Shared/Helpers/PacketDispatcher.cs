using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MiaoNet.Shared;

#if MIAO_CLIENT || MIAO_MOCKCLIENT
public delegate void PacketHandler<TPacket>(TPacket packet)
    where TPacket : IContextualPacket;
#elif MIAO_SERVER
public delegate Task PacketHandler<TPacket>(Server.MiaoClientConnection connection, TPacket packet) 
    where TPacket : IContextualPacket;
#endif

#if MIAO_CLIENT || MIAO_MOCKCLIENT
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<Type, PacketHandler<IContextualPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.ToFrozenDictionary();
    }

    public bool DispatchPacket(IContextualPacket packet)
    {
        if (dictionary.TryGetValue(packet.GetType(), out PacketHandler<IContextualPacket>? d))
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
    private readonly FrozenDictionary<Type, PacketHandler<IContextualPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.Select(pair => new KeyValuePair<Type, PacketHandler<IContextualPacket>>(pair.Key, pair.Value)).ToFrozenDictionary();
    }

    public async ValueTask<bool> DispatchPacketAsync(Server.MiaoClientConnection connection, IContextualPacket packet)
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