namespace MiaoNet.Shared;

internal interface INetModTransport
{
    void Send(byte netModID, byte localID, PacketRoute route, IContextualPacket packet);
}
