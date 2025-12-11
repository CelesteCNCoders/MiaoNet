using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoServerConnection : IDisposable
{
    private Socket? tcpSocket;
    private NetworkStream networkStream = null!;

    private readonly MemoryStream sendMemoryStream;
    private readonly MemoryStream receiveMemoryStream;

    private readonly ConcurrentQueue<IPacket> sendQueue;
    private readonly SemaphoreSlim sendSemaphore;

    public EndPoint EndPoint { get; }

    private MiaoServerConnection(EndPoint endPoint)
    {
        EndPoint = endPoint;
        tcpSocket = new(SocketType.Stream, ProtocolType.Tcp);

        sendMemoryStream = new(512);
        sendMemoryStream.Seek(2, SeekOrigin.Begin);
        receiveMemoryStream = new(512);

        sendQueue = new();
        sendSemaphore = new(0);
    }

    public static async Task<(MiaoServerConnection?, HandshakeAckData?)> CreateAsync(
        EndPoint endPoint,
        HandshakeData handshakeData,
        CancellationToken token
    )
    {
        MiaoServerConnection con = new(endPoint);
        con.tcpSocket!.Connect(con.EndPoint);
        con.networkStream = new(con.tcpSocket);

        await con.networkStream.WriteAsync(Connection.HandshakeHead, token);
        await con.SendHandshakeAsync(handshakeData, token);
        var ack = await con.ReceiveHandshakeAsync(token);
        if (ack is null)
        {
            con.Dispose();
            return (null, null);
        }

        return (con, ack);
    }

    public void Dispose()
    {
        sendSemaphore.Dispose();
        if (tcpSocket is null)
            return;
        if (tcpSocket.Connected)
            tcpSocket.Shutdown(SocketShutdown.Both);
        tcpSocket.Close();
        tcpSocket = null;
    }

    public async Task SendPacketsLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            while (sendQueue.TryDequeue(out IPacket? packet))
                await SendPacketAsync(packet, token);
            await sendSemaphore.WaitAsync(token);
        }
    }

    public int QueuePacket(IPacket packet)
    {
        sendQueue.Enqueue(packet);
        int count = sendQueue.Count;
        sendSemaphore.Release();
        return count;
    }

    public async Task SendPacketAsync(IPacket packet, CancellationToken token)
    {
        RefBinaryWriter writer = new(sendMemoryStream);
        PacketRegistry.WritePacket(packet, ref writer);
        ushort length = (ushort)(sendMemoryStream.Position - 2 * sizeof(ushort));
        sendMemoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write(length);
        await networkStream.WriteAsync(sendMemoryStream.GetBuffer().AsMemory(0, length + 2 * sizeof(ushort)), token);
    }

    private async Task SendHandshakeAsync(HandshakeData data, CancellationToken token)
    {
        RefBinaryWriter writer = new(sendMemoryStream);
        writer.Write(data);
        ushort length = (ushort)(sendMemoryStream.Position - sizeof(ushort));
        sendMemoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write(length);
        await networkStream.WriteAsync(sendMemoryStream.GetBuffer().AsMemory(0, length + sizeof(ushort)), token);
    }

    private async Task<HandshakeAckData?> ReceiveHandshakeAsync(CancellationToken token)
    {
        const int HeadSize = sizeof(ushort);

        var buffer = receiveMemoryStream.GetBuffer();

        Memory<byte> headMemory = buffer.AsMemory().Slice(0, HeadSize);
        int count = await networkStream.ReadAtLeastAsync(headMemory, HeadSize, false, token);
        if (count < HeadSize)
            return null;
        receiveMemoryStream.Seek(HeadSize, SeekOrigin.Begin);

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span);

        if (receiveMemoryStream.Capacity < size)
            receiveMemoryStream.Capacity = size;

        Memory<byte> payloadMemory = buffer.AsMemory().Slice(0, size);
        count = await networkStream.ReadAtLeastAsync(payloadMemory, size, false, token);
        if (count < size)
            return null;

        RefBinaryReader reader = new(payloadMemory.Span);
        HandshakeAckData packet = reader.Read<HandshakeAckData>();
        return packet;
    }

    public async Task<IPacket?> ReceivePacketAsync(CancellationToken token)
    {
        const int HeadSize = 2 * sizeof(ushort);

        var buffer = receiveMemoryStream.GetBuffer();

        Memory<byte> headMemory = buffer.AsMemory().Slice(0, HeadSize);
        int count = await networkStream.ReadAtLeastAsync(headMemory, HeadSize, false, token);
        if (count < HeadSize)
            return null;
        receiveMemoryStream.Seek(HeadSize, SeekOrigin.Begin);

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span);
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span.Slice(sizeof(ushort)));

        if (receiveMemoryStream.Capacity < size)
            receiveMemoryStream.Capacity = size;

        Memory<byte> payloadMemory = buffer.AsMemory().Slice(0, size);
        count = await networkStream.ReadAtLeastAsync(payloadMemory, size, false, token);
        if (count < size)
            return null;

        RefBinaryReader reader = new(payloadMemory.Span);
        IPacket packet = PacketRegistry.ReadPacket(type, ref reader);
        return packet;
    }
}