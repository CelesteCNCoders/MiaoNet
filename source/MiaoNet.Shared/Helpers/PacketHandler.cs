namespace MiaoNet.Shared;

#if MIAO_CLIENT
public delegate void PacketHandler<TPacket>(TPacket packet) 
    where TPacket : IPacket;
#elif MIAO_SERVER
public delegate Task PacketHandler<TPacket>(Server.MiaoClientConnection connection, TPacket packet) 
    where TPacket : IPacket;
#endif