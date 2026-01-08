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
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    private Socket? tcpSocket;
    private NetworkStream networkStream = null!;

    // TODO use memory stream is not so good
    private readonly MemoryStream sendMemoryStream;

    private readonly ConcurrentQueue<IContextualPacket> sendQueue;
    private readonly SemaphoreSlim sendSemaphore;

    public EndPoint EndPoint { get; }

    private MiaoServerConnection(EndPoint endPoint)
    {
        EndPoint = endPoint;
        tcpSocket = new(SocketType.Stream, ProtocolType.Tcp);
        tcpSocket.NoDelay = true;

        sendMemoryStream = new(512);
        sendMemoryStream.Seek(2, SeekOrigin.Begin);

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
        await con.tcpSocket!.ConnectAsync(con.EndPoint, token);
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

    public async Task SendPacketsLoopAsync(IPacketSerializationContext context, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            while (sendQueue.TryDequeue(out IContextualPacket? packet))
                await SendPacketAsync(packet, context, token);
            if (token.IsCancellationRequested)
                return;
            await sendSemaphore.WaitAsync(token);
        }
    }

    public int QueuePacket(IContextualPacket packet)
    {
        sendQueue.Enqueue(packet);
        int count = sendQueue.Count;
        sendSemaphore.Release();
        return count;
    }

    // TODO these are awful, we need a refactor
    public async Task SendPacketAsync(IContextualPacket packet, IPacketSerializationContext context, CancellationToken token)
    {
        RefBinaryWriter writer = new(sendMemoryStream);
        ushort type = PacketRegistry.GetPacketID(packet);
        writer.Write(type);
        packet.Serialize(ref writer, context);
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

        var headBuffer = pool.Rent(HeadSize);
        ushort size;
        try
        {
            Memory<byte> headMemory = headBuffer.AsMemory().Slice(0, HeadSize);
            int count = await networkStream.ReadAtLeastAsync(headMemory, HeadSize, false, token);
            if (count < HeadSize)
                return null;

            size = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span);
        }
        finally
        {
            pool.Return(headBuffer);
        }

        var payloadBuffer = pool.Rent(size);
        try
        {
            Memory<byte> payloadMemory = payloadBuffer.AsMemory().Slice(0, size);
            int count = await networkStream.ReadAtLeastAsync(payloadMemory, size, false, token);
            if (count < size)
                return null;

            RefBinaryReader reader = new(payloadMemory.Span);
            HandshakeAckData packet = reader.Read<HandshakeAckData>();
            return packet;
        }
        finally
        {
            pool.Return(payloadBuffer);
        }
    }

    public async Task<IContextualPacket?> ReceivePacketAsync(IPacketSerializationContext context, CancellationToken token)
    {
        const int HeadSize = 2 * sizeof(ushort);

        var headBuffer = pool.Rent(HeadSize);
        ushort size, type;
        try
        {
            Memory<byte> headMemory = headBuffer.AsMemory().Slice(0, HeadSize);
            int count = await networkStream.ReadAtLeastAsync(headMemory, HeadSize, false, token);
            if (count < HeadSize)
                return null;

            size = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span);
            type = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span.Slice(sizeof(ushort)));
        }
        finally
        {
            pool.Return(headBuffer);
        }

        var payloadBuffer = pool.Rent(size);
        try
        {
            Memory<byte> payloadMemory = payloadBuffer.AsMemory().Slice(0, size);
            int count = await networkStream.ReadAtLeastAsync(payloadMemory, size, false, token);
            if (count < size)
                return null;

            try
            {
                RefBinaryReader reader = new(payloadMemory.Span);
                var readHandler = PacketRegistry.GetPacketReader(type);
                IContextualPacket packet = readHandler(ref reader, context);
                return packet;
            }
            catch (Exception)
            {
                Logger.Error(
                    $"{nameof(MiaoNet)}/ReadPacket",
                    $"Read packet failed, size: {size}, type: {type}. Raw payload:\n" +
                        Convert.ToBase64String(payloadMemory.ToArray())
                );
                throw;
            }
        }
        finally
        {
            pool.Return(payloadBuffer);
        }
    }
}