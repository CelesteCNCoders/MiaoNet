using System.Diagnostics;

namespace MiaoNet.Shared;

public interface IPacketHandlerRegister
{
    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IPacket;
}

#if MIAO_CLIENT
public sealed class PacketHandlerRegister : IPacketHandlerRegister
{
    public Dictionary<Type, PacketHandler<IPacket>> Dictionary { get; set; } = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IPacket
    {
        // QUESTION can this be more optimized?
        void HandlePacket(IPacket p) => handler((TPacket)p);

        Dictionary.Add(typeof(TPacket), HandlePacket);
    }
}
#elif MIAO_SERVER
public sealed class PacketHandlerRegister : IPacketHandlerRegister
{
    public Dictionary<Type, PacketHandler<IPacket>> Dictionary { get; set; } = new();

    public void Register<TPacket>(PacketHandler<TPacket> handler) where TPacket : IPacket
    {
        // QUESTION can this be more optimized?
        Task HandlePacketAsync(Server.MiaoClientConnection c, IPacket p) 
            => handler(c, (TPacket)p);

        Dictionary.Add(typeof(TPacket), HandlePacketAsync);
    }
}
#endif