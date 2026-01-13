using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using MiaoNet.Shared;

namespace MiaoNet.MockClient;

public sealed class MockInstance : IPacketSerializationContext, IDisposable
{
    private Vector2 position;

    private ConcurrentQueue<IContextualPacket> packetQueue;
    private TeeStream teeStream = null!;
    private readonly string name;

    public PooledStringManager PooledStringManager { get; }

    public MockInstance(string name)
    {
        PooledStringManager = new(KnownPooledStrings.All);
        packetQueue = new();
        _ = ProcessAsync(name);
        this.name = name;

    }

    private async Task FrameLoop()
    {
        while (true)
        {
            position = new(position.X + Random.Shared.Next(0, 30) / 60f, position.Y);
            packetQueue.Enqueue(
                new PacketPlayerFrame(
                    position,
                    "idle",
                    (ushort)Random.Shared.Next(0, 3),
                    new Vector2(1f, 1f),
                    PacketPlayerFrame.FrameFlags.FacingLeft
                )
            );
            await Task.Delay((int)(1f / 60f * 1000f));
        }
    }

    private async Task ProcessAsync(string name)
    {
        await ConnectAsync("s.saplonily.top", 21478);
        await teeStream.WriteAsync(Connection.HandshakeHead);
        await SendHandshakeAsync(new HandshakeData(new Version(0, 2, 0), 0, name, []));
        var ack = await ReceivedHandshakeAckAsync();
        Log($"Received ack.");

        packetQueue.Enqueue(
            new PacketPlayerMapChanged(
                new PlayerLocation("Celeste/LostLevels", AreaMode.Normal, "intro-00-past"),
                new PlayerState(position, 2, 1f / 60f)
            )
        );
        _ = FrameLoop();

        CancellationTokenSource cts = new();
        Task sendingTask = HandleSendingAsync(cts.Token);
        Task receivingTask = HandleReceivingAsync(cts.Token);

        Task completedTask = await Task.WhenAny(sendingTask, receivingTask);
        cts.Cancel();

        try
        {
            if (completedTask.IsFaulted)
                await completedTask;
        }
        catch (Exception e)
        {
            Log($"Closed due to {e}");
        }

        return;
    }

    private async Task ConnectAsync(string host, int port)
    {
        EndPoint ep = IPAddress.TryParse(host, out var ipa)
            ? new IPEndPoint(ipa, port)
            : new DnsEndPoint(host, port);
        Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        await socket.ConnectAsync(ep);
        NetworkStream netStream = new(socket);
        var sslStream = new SslStream(netStream, false, (_, _, _, _) => true);
        teeStream = new(sslStream, new FileStream($"{name}.bin", FileMode.Create, FileAccess.Write));
        await sslStream.AuthenticateAsClientAsync(host);
    }

    private async Task SendHandshakeAsync(HandshakeData data)
    {
        MemoryStream ms = new(128);
        ms.Seek(2, SeekOrigin.Begin);
        RefBinaryWriter writer = new(ms);
        data.Serialize(ref writer);
        ushort size = (ushort)(ms.Position - 2);
        ms.Seek(0, SeekOrigin.Begin);
        writer.Write(size);
        await teeStream.WriteAsync(ms.GetBuffer().AsMemory().Slice(0, size + 2));
    }

    private async Task<HandshakeAckData> ReceivedHandshakeAckAsync()
    {
        byte[] head = new byte[2];
        await teeStream.ReadAtLeastAsync(head, 2);
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(head);
        byte[] payload = new byte[size];
        await teeStream.ReadAtLeastAsync(payload, size);
        RefBinaryReader reader = new(payload);
        HandshakeAckData data = reader.Read<HandshakeAckData>();
        return data;
    }

    private async Task HandleSendingAsync(CancellationToken token)
    {
        MemoryStream ms = new(512);
        while (true)
        {
            while (packetQueue.TryDequeue(out var packet))
            {
                ms.Seek(4, SeekOrigin.Begin);
                RefBinaryWriter writer = new(ms);
                packet.Serialize(ref writer, this);
                ushort size = (ushort)(ms.Position - 4);
                ushort type = PacketRegistry.GetPacketID(packet);
                ms.Seek(0, SeekOrigin.Begin);
                writer.Write(size);
                writer.Write(type);
                await teeStream.WriteAsync(ms.GetBuffer().AsMemory().Slice(0, size + 4), token);
                await teeStream.FlushAsync(token);
            }
            await Task.Delay(100, token);
        }
    }

    private async Task HandleReceivingAsync(CancellationToken token)
    {
        byte[] headBuffer = new byte[4];
        while (true)
        {
            await teeStream.ReadAtLeastAsync(headBuffer, 4, true, token);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headBuffer.AsSpan()[0..2]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(headBuffer.AsSpan()[2..4]);
            byte[] payloadBuffer = new byte[size];
            await teeStream.ReadAtLeastAsync(payloadBuffer, size, true, token);
            RefBinaryReader reader = new(payloadBuffer);
            var readHandler = PacketRegistry.GetPacketReader(type);
            var packet = readHandler(ref reader, this);
            HandlePacket(packet);
        }
    }

    private void HandlePacket(IContextualPacket packet)
    {
        if (packet is PacketPing packetPing)
        {
            packetQueue.Enqueue(new PacketPong() { RequestID = packetPing.RequestID });
            return;
        }
    }

    private void Log(string msg)
    {
        Console.WriteLine($"[{DateTime.Now:t}] [{name}] {msg}");
    }

    public void Dispose()
    {
        ((IDisposable)teeStream).Dispose();
    }
}
