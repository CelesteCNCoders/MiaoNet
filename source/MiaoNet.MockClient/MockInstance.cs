using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MiaoNet.Shared;

namespace MiaoNet.MockClient;

public sealed class MockInstance : IPacketSerializationContext, IDisposable
{
    private Vector2 position;

    private ConcurrentQueue<IContextualPacket> packetQueue;
    private Stream stream = null!;
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
        await ConnectAsync("127.0.0.1", 21473);
        var serverVersion = await DoVersionCheckAsync(Connection.Version);
        if (serverVersion is not null)
        {
            Log($"Version mismatch. Server requires {serverVersion.ToString(3)}");
            return;
        }

        PlayerInfo playerInfo = new(-1, name, string.Empty, string.Empty, Color.White);
        MemoryStream ms = new(32);
        RefBinaryWriter writer = new(ms);
        writer.Write(playerInfo);
        byte[] authData = ms.GetBuffer().AsSpan(0, checked((int)ms.Position)).ToArray();
        HandshakeData handshakeData = new(0, false, authData, []);

        await SendHandshakeAsync(handshakeData);
        var ack = await ReceiveHandshakeAckAsync();
        if (ack.DeniedReason is not null)
        {
            Log($"Handshake denied: {ack.DeniedReason}");
            return;
        }
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
        await netStream.WriteAsync(Connection.HandshakeHead);
        var sslStream = new SslStream(netStream, false, (_, _, _, _) => true);
        //stream = new TeeStream(sslStream, new FileStream($"{name}.bin", FileMode.Create, FileAccess.Write));
        stream = sslStream;
        SslClientAuthenticationOptions options = new()
        {
            TargetHost = host,
            EnabledSslProtocols = Connection.AllowedSslProtocols,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };
        await sslStream.AuthenticateAsClientAsync(options);
    }

    private async Task<Version?> DoVersionCheckAsync(Version clientVersion)
    {
        ushort major = (ushort)clientVersion.Major;
        ushort minor = (ushort)clientVersion.Minor;
        ushort build = (ushort)clientVersion.Build;

        const int VersionLength = 3 * sizeof(ushort);
        byte[] buffer = new byte[VersionLength];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[0..2], major);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..4], minor);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], build);
        await stream.WriteAsync(buffer);

        byte[] passedBuffer = new byte[1];
        await stream.ReadExactlyAsync(passedBuffer);
        bool passed = passedBuffer[0] != 0;
        if (passed)
            return null;

        byte[] serverVersionBuffer = new byte[VersionLength];
        await stream.ReadExactlyAsync(serverVersionBuffer);
        var serverSpan = serverVersionBuffer.AsSpan();
        ushort majorServer = BinaryPrimitives.ReadUInt16LittleEndian(serverSpan[0..2]);
        ushort minorServer = BinaryPrimitives.ReadUInt16LittleEndian(serverSpan[2..4]);
        ushort buildServer = BinaryPrimitives.ReadUInt16LittleEndian(serverSpan[4..6]);
        return new Version(majorServer, minorServer, buildServer);
    }

    private async Task SendHandshakeAsync(HandshakeData data)
    {
        MemoryStream ms = new(128);
        ms.Seek(2, SeekOrigin.Begin);
        RefBinaryWriter writer = new(ms);
        writer.Write(data);
        ushort size = (ushort)(ms.Position - 2);
        ms.Seek(0, SeekOrigin.Begin);
        writer.Write(size);
        await stream.WriteAsync(ms.GetBuffer().AsMemory().Slice(0, size + 2));
    }

    private async Task<HandshakeAckData> ReceiveHandshakeAckAsync()
    {
        byte[] head = new byte[2];
        await stream.ReadExactlyAsync(head);
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(head);
        byte[] payload = new byte[size];
        await stream.ReadExactlyAsync(payload);
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
                await stream.WriteAsync(ms.GetBuffer().AsMemory().Slice(0, size + 4), token);
                await stream.FlushAsync(token);
            }
            await Task.Delay(100, token);
        }
    }

    private async Task HandleReceivingAsync(CancellationToken token)
    {
        byte[] headBuffer = new byte[4];
        while (true)
        {
            await stream.ReadAtLeastAsync(headBuffer, 4, true, token);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headBuffer.AsSpan()[0..2]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(headBuffer.AsSpan()[2..4]);
            byte[] payloadBuffer = new byte[size];
            await stream.ReadAtLeastAsync(payloadBuffer, size, true, token);
            RefBinaryReader reader = new(payloadBuffer);
            var readHandler = PacketRegistry.GetPacketReader(type);
            IContextualPacket packet;
            try
            {
                packet = readHandler(ref reader, this);
            }
            catch (Exception)
            {
                Log($"Read failed, raw payload: {Convert.ToBase64String(payloadBuffer)}");
                throw;
            }
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

        if (packet is PacketBeTeleportedRequest teleportRequest)
        {
            Log($"Received teleport request from player {teleportRequest.SourcePlayerID}");
            var session = new PlayerSessionData(
                position: position,
                respawnPoint: position,
                inventory: new PlayerSessionData.PlayerInventory(1, false, true, false),
                stringFlags: Array.Empty<string>(),
                levelStringFlags: Array.Empty<string>(),
                strawberries: Array.Empty<PlayerSessionData.StringIntPair>(),
                doNotLoad: Array.Empty<PlayerSessionData.StringIntPair>(),
                keys: Array.Empty<PlayerSessionData.StringIntPair>(),
                counters: Array.Empty<PlayerSessionData.StringIntPair>(),
                startCheckpoint: null,
                colorGrade: null,
                summitGems: 0,
                flags: PlayerSessionData.SessionFlags.FirstLevel,
                lightingAlphaAdd: 0f,
                bloomBaseAdd: 0f,
                darkRoomAlpha: 0f,
                time: 0,
                coreMode: CoreModes.None
            );
            var response = new PacketBeTeleportedResponse(session) { RequestID = teleportRequest.RequestID };
            packetQueue.Enqueue(response);
            return;
        }
    }

    private void Log(string msg)
    {
        Console.WriteLine($"[{DateTime.Now:t}] [{name}] {msg}");
    }

    public void Dispose()
    {
        ((IDisposable)stream).Dispose();
    }
}
