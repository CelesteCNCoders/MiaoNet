using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoServerConnection : IDisposable
{
    private Socket? socket;
    private readonly NetworkStream networkStream;
    private readonly MemoryStream memoryStream;

    public IPEndPoint IPEndPoint { get; }

    public MiaoServerConnection(IPEndPoint ipEndPoint, HandshakeData handshakeData)
    {
        IPEndPoint = ipEndPoint;
        socket = new(SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        socket.Connect(IPEndPoint);
        networkStream = new(socket);
        memoryStream = new(512);
        memoryStream.Seek(2, SeekOrigin.Begin);

        networkStream.Write(Connection.HandshakeHead);
        SendHandshake(handshakeData);
    }

    public void Dispose()
    {
        if (socket is null)
            return;
        socket.Shutdown(SocketShutdown.Both);
        socket.Close();
        socket = null;
    }

    public Packet ReceivePacket()
    {
        const int HeadSize = 2 * sizeof(ushort);
        Span<byte> headSpan = stackalloc byte[HeadSize];
        networkStream.ReadAtLeast(headSpan, HeadSize);
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headSpan);
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(headSpan.Slice(sizeof(ushort)));

        // TODO stackalloc
        Span<byte> payloadSpan = stackalloc byte[size];
        networkStream.ReadAtLeast(payloadSpan, size);

        RefBinaryReader reader = new(payloadSpan);
        Packet packet = PacketRegistry.ReadPacket(type, ref reader);
        return packet;
    }

    public void SendPacket(Packet packet)
    {
        RefBinaryWriter writer = new(memoryStream);

        PacketRegistry.WritePacket(packet, ref writer);
        ushort length = (ushort)(memoryStream.Position - 2 * sizeof(ushort));
        memoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write(length);

        networkStream.Write(memoryStream.GetBuffer(), 0, length + 2 * sizeof(ushort));
    }

    private void SendHandshake(HandshakeData handshakeData)
    {
        RefBinaryWriter writer = new(memoryStream);

        handshakeData.WriteTo(ref writer);
        ushort length = (ushort)(memoryStream.Position - sizeof(ushort));
        memoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write(length);

        networkStream.Write(memoryStream.GetBuffer(), 0, length + sizeof(ushort));
    }
}