using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoServerConnection : IDisposable
{
    private Socket? tcpSocket;
    private readonly NetworkStream networkStream;
    private readonly MemoryStream memoryStream;

    private volatile TaskCompletionSource tcs;
    private readonly ConcurrentQueue<IPacket> packetSendQueue;

    public EndPoint EndPoint { get; }

    public MiaoServerConnection(EndPoint endPoint, HandshakeData handshakeData)
    {
        EndPoint = endPoint;
        tcpSocket = new(SocketType.Stream, ProtocolType.Tcp);
        tcpSocket.NoDelay = true;
        tcpSocket.Connect(EndPoint);

        packetSendQueue = new();

        networkStream = new(tcpSocket);
        memoryStream = new(512);
        memoryStream.Seek(2, SeekOrigin.Begin);

        networkStream.Write(Connection.HandshakeHead);
        WriteHandshake(handshakeData);
        tcs = new();
    }

    public void Dispose()
    {
        if (tcpSocket is null)
            return;
        if (tcpSocket.Connected)
        {
            tcpSocket.Shutdown(SocketShutdown.Both);
            tcpSocket.Close();
        }
        tcpSocket = null;
    }

    public async ValueTask<IPacket> ReceivePacketAsync()
    {
        const int HeadSize = 2 * sizeof(ushort);
        ushort size, type;
        using (IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(HeadSize))
        {
            Memory<byte> headMemory = owner.Memory.Slice(0, HeadSize);
            await networkStream.ReadAtLeastAsync(headMemory, HeadSize);

            size = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span);
            type = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Slice(sizeof(ushort)).Span);
        }

        using (IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(size))
        {
            Memory<byte> payloadMemory = owner.Memory.Slice(0, size);
            await networkStream.ReadAtLeastAsync(payloadMemory, size);

            RefBinaryReader reader = new(payloadMemory.Span);
            IPacket packet = PacketRegistry.ReadPacket(type, ref reader);
            return packet;
        }
    }

    public async Task SendPacketsLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Yield();

            while (packetSendQueue.TryDequeue(out IPacket? packet))
            {
                RefBinaryWriter writer = new(memoryStream);
                PacketRegistry.WritePacket(packet, ref writer);
                ushort length = (ushort)(memoryStream.Position - 2 * sizeof(ushort));
                memoryStream.Seek(0, SeekOrigin.Begin);
                writer.Write(length);

                await networkStream.WriteAsync(memoryStream.GetBuffer().AsMemory(0, length + 2 * sizeof(ushort)), token);
            }

            await tcs.Task;
            tcs = new();
        }
    }

    public int QueuePacket(IPacket packet)
    {
        packetSendQueue.Enqueue(packet);
        tcs.TrySetResult();
        return packetSendQueue.Count;
    }

    private void WriteHandshake(HandshakeData handshakeData)
    {
        RefBinaryWriter writer = new(memoryStream);

        writer.Write(handshakeData);
        ushort length = (ushort)(memoryStream.Position - sizeof(ushort));
        memoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write(length);

        networkStream.Write(memoryStream.GetBuffer(), 0, length + sizeof(ushort));
    }
}