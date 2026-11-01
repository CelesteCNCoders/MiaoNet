using System.Collections.Frozen;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public delegate Task PacketHandler<TPacket>(MiaoClientConnection connection, PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;

public sealed class PacketHandlerRegister
{
    private readonly Dictionary<Type, PacketHandler<IContextualPacket>> handlers = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket
    {
        Task HandlePacketAsync(MiaoClientConnection c, PacketEnvelope envelope, IContextualPacket p)
            => handler(c, envelope, (TPacket)p);

        handlers.Add(typeof(TPacket), HandlePacketAsync);
    }

    internal FrozenDictionary<Type, PacketHandler<IContextualPacket>> Freeze()
        => handlers.ToFrozenDictionary();
}
