using System.Diagnostics;

namespace MiaoNet.Shared;

public interface IPacketHandlerRegister
{
    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket;
}

#if MIAO_CLIENT || MIAO_MOCKCLIENT
public sealed class PacketHandlerRegister : IPacketHandlerRegister
{
    public Dictionary<Type, PacketHandler<IContextualPacket>> Dictionary { get; set; } = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket
    {
        // QUESTION can this be more optimized?
        void HandlePacket(IContextualPacket p) => handler((TPacket)p);

        Dictionary.Add(typeof(TPacket), HandlePacket);
    }
}
#elif MIAO_SERVER
public sealed class PacketHandlerRegister : IPacketHandlerRegister
{
    public Dictionary<Type, PacketHandler<IContextualPacket>> Dictionary { get; set; } = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IContextualPacket
    {
        // QUESTION can this be more optimized?
        Task HandlePacketAsync(Server.MiaoClientConnection c, IContextualPacket p) 
            => handler(c, (TPacket)p);

        Dictionary.Add(typeof(TPacket), HandlePacketAsync);
    }
}
#endif