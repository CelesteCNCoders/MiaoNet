using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MiaoNet.ClientShared;
using MiaoNet.Shared;

namespace MiaoNet.MockClient;

public sealed class MockInstance : IPacketSerializationContext, IDisposable
{
    private const string HostName = "127.0.0.1";
    private const int Port = 21473;

    private Vector2 position;

    public readonly string Name;

    private MiaoServerConnection connection = null!;

    public PooledStringManager PooledStringManager { get; }

    public MockInstance(string name)
    {
        PooledStringManager = new(KnownPooledStrings.All);
        _ = ProcessAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log($"{t.Exception}");
            }
        });
        Name = name;
    }

    private async Task ChatLoop()
    {
        ChatChannel[] channels = [ChatChannel.Global, ChatChannel.Channel, ChatChannel.Map];
        int i = 0;
        while (true)
        {
            await Task.Delay(3000);
            var ch = channels[i % channels.Length];
            connection.QueuePacket(new PacketSendChatMessage(ch, $"[{ch}] hello from {Name}"));
            i++;
        }
    }

    private async Task FrameLoop()
    {
        while (true)
        {
            position = new(position.X + Random.Shared.Next(0, 30) / 60f, position.Y);
            connection.QueuePacket(new PacketPlayerFrame(
                position,
                "idle",
                (ushort)Random.Shared.Next(0, 3),
                new Vector2(1f, 1f),
                PacketPlayerFrame.FrameFlags.FacingLeft
            ));

            await Task.Delay((int)(1f / 60f * 1000f));
        }
    }

    private async Task ProcessAsync()
    {
        EndPoint ep = IPAddress.TryParse(HostName, out var ipa)
            ? new IPEndPoint(ipa, Port)
            : new DnsEndPoint(HostName, Port);

        connection = await MiaoServerConnection.CreateAsync(ep, HostName, true, default);
        Version? serverVersion = await connection.MakeVersionCheck(Connection.Version, default);
        if (serverVersion is not null)
        {
            Log($"Version mismatch. Server requires {serverVersion.ToString(3)}");
            return;
        }

        PlayerInfo playerInfo = new(-1, Name, string.Empty, string.Empty, Color.White);
        MemoryStream ms = new(32);
        RefBinaryWriter writer = new(ms);
        writer.Write(playerInfo);
        byte[] authData = ms.GetBuffer().AsSpan(0, checked((int)ms.Position)).ToArray();
        HandshakeData handshakeData = new(0, false, authData, []);

        var ack = await connection.MakeHandshakeAsync(handshakeData, default);
        if (ack.DeniedReason is not null)
        {
            Log($"Handshake denied: {ack.DeniedReason}");
            return;
        }
        Log($"Received ack.");

        connection.QueuePacket(
            new PacketPlayerMapChanged(
                new PlayerLocation("Celeste/LostLevels", AreaMode.Normal, "intro-00-past"),
                new PlayerState(position, 2, 1f / 60f)
            )
        );
        _ = FrameLoop();
        // _ = ChatLoop();

        CancellationTokenSource cts = new();
        Task sendingTask = connection.SendPacketsLoopAsync(this, cts.Token);
        Task receivingTask = HandlePacketsAsync(connection.ReceivePacketsLoopAsync(this, cts.Token), cts.Token);

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

    private async Task HandlePacketsAsync(IAsyncEnumerable<IContextualPacket> packets, CancellationToken token)
    {
        await foreach (var packet in packets)
        {
            if (packet is PacketPing packetPing)
            {
                connection.QueuePacket(new PacketPong() { RequestID = packetPing.RequestID });
            }
            else if (packet is PacketBeTeleportedRequest teleportRequest)
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
                connection.QueuePacket(response);
            }
        }
    }

    private void Log(string msg)
    {
        Console.WriteLine($"[{DateTime.Now:t}] [{Name}] {msg}");
    }

    public void Close(bool shutdown)
    {
        connection.Close(shutdown);
    }

    public void Dispose()
    {
        connection.Dispose();
    }
}
