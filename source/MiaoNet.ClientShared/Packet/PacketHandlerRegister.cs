using System.Collections.Frozen;

namespace MiaoNet.Shared;

public delegate void PacketHandler<TPacket>(PacketEnvelope envelope, TPacket packet)
    where TPacket : IContextualPacket;

public sealed class PacketHandlerRegister
{
    private readonly Dictionary<Type, PacketHandler<IContextualPacket>> handlers = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket
    {
        Register(typeof(TPacket), HandlePacket);

        void HandlePacket(PacketEnvelope envelope, IContextualPacket p)
            => handler(envelope, (TPacket)p);
    }

    private void Register(Type packetType, PacketHandler<IContextualPacket> handler)
        => handlers.Add(packetType, handler);

    internal FrozenDictionary<Type, PacketHandler<IContextualPacket>> Freeze()
        => handlers.ToFrozenDictionary();
}
