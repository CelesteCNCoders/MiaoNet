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
    private uint playerSequence;
    private const uint PlayerEpoch = 1;

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

    private async Task FrameLoop()
    {
        while (true)
        {
            position = new(position.X + Random.Shared.Next(0, 30) / 60f, position.Y);
            PlayerStateDelta d = new(position, "idle", 0, Vector2.One, PlayerStateDelta.FrameFlags.None, PlayerStateFlags.FacingLeft);
            playerSequence++;
            connection.QueuePacket(new PacketPlayerFrame(
                PlayerEpoch,
                playerSequence,
                d,
                CreateState()
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
            new PacketPlayerLocationChanged(
                PlayerEpoch,
                0,
                new PlayerLocation("Celeste/LostLevels", AreaMode.Normal, "intro-00-past"),
                CreateState()
            )
        );
        _ = FrameLoop();

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

    private PlayerState CreateState()
        => new()
        {
            Position = position,
            Animation = "idle",
            AnimationFrame = 0,
            Scale = Vector2.One,
            StateFlags = PlayerStateFlags.FacingLeft,
            Dashes = 1,
            DeltaTime = 0f,
            PlayerSpriteMode = PlayerSpriteMode.Madeline,
            HoldableInfo = new HoldableInfo(),
            FollowerInfos = Array.Empty<FollowerInfo>(),
            WindDirection = Vector2.Zero
        };

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
