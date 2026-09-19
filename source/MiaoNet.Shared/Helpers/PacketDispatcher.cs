using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MiaoNet.Shared;

#if MIAO_CLIENT || MIAO_MOCKCLIENT || INSPECTOR
public delegate void PacketHandler<TPacket>(PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;
#elif MIAO_SERVER
public delegate Task PacketHandler<TPacket>(Server.MiaoClientConnection connection, PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;
#endif

#if MIAO_CLIENT || MIAO_MOCKCLIENT || INSPECTOR
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<Type, PacketHandler<IContextualPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
    {
        dictionary = register.Dictionary.ToFrozenDictionary();
    }

    public bool DispatchPacket(PacketEnvelope envelope, IContextualPacket packet)
    {
        if (dictionary.TryGetValue(packet.GetType(), out PacketHandler<IContextualPacket>? d))
        {
            d(envelope, packet);
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

    public async ValueTask<bool> DispatchPacketAsync(Server.MiaoClientConnection connection, PacketEnvelope envelope, IContextualPacket packet)
    {
        if (dictionary.TryGetValue(packet.GetType(), out var handler))
        {
            await handler(connection, envelope, packet);
            return true;
        }
        else
        {
            return false;
        }
    }
}
#endif
