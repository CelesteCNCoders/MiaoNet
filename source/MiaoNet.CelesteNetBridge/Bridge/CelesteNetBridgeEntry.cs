using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Celeste.Mod.MiaoNet;
using MiaoNet.CelesteNetBridge.Protocol;
using MiaoNet.CelesteNetBridge.Server;
using MiaoNet.Shared;

namespace MiaoNet.CelesteNetBridge;

/// <summary>
/// Keeps the real CelesteNet client, replaces the server it talks to, and carries the frames that
/// client exchange over MiaoNet's NetMod pipeline.
/// <para>
/// The bridge is a NetMod consumer, nothing more: MiaoNet never learns that CelesteNet exists, and
/// the bridge never learns what any DataType means.
/// </para>
/// <para>
/// It ships inside the MiaoNet mod as a second assembly and is started by <c>MiaoNetModule</c> through
/// reflection, so MiaoNet.dll itself never references CelesteNet and still loads when it is absent.
/// </para>
/// </summary>
public static class CelesteNetBridgeEntry {

    /// <summary>NetMod name declared in the MiaoNet handshake.</summary>
    private const string NetModName = "MiaoNetCelesteNetBridge";

    /// <summary>Bumped whenever the packet layout below changes.</summary>
    private const ushort ProtocolVersion = 1;

    /// <summary>
    /// Frames that belong to one connection and must never be relayed: the handshake, the startup
    /// sequence, heartbeats, and the DataContext registration tables. Everything else is treated as
    /// third-party data and relayed verbatim.
    /// </summary>
    private static readonly HashSet<string> connectionLocalTypes = [
        "keepAlive", "tickRate", "ready", "channelList", "channelMove", "playerInfo",
        "disconnectReason", "filterList", "stringMap", "slimMap", "session", "sessionRequest",
    ];

    private static readonly List<CelesteNetFakeSession> sessions = [];

    /// <summary>Type ids already relayed per session, for a log that names each kind once.</summary>
    private static readonly Dictionary<CelesteNetFakeSession, HashSet<string>> relayedTypes = [];

    /// <summary>Remote players already announced to each session.</summary>
    private static readonly Dictionary<CelesteNetFakeSession, HashSet<uint>> introducedPlayers = [];

    private static NetMod? netMod;
    private static CelesteNetFakeServer? server;
    private static CelesteNetSuppression? suppress;

    public static bool Initialized { get; private set; }

    /// <summary>Starts the bridge: declares the NetMod, suppresses the CelesteNet UI, opens the port.</summary>
    public static void Initialize(int port) {
        if (Initialized)
            return;

        suppress = new();
        suppress.Install();

        // The declaration has to exist before the MiaoNet handshake goes out.
        netMod = MiaoNetNetMods.Register(new NetMod(
            NetModName,
            ProtocolVersion,
            NetModPacketEntry.Create<PacketBridgeFrame>(PacketScope.Map, OnRemoteFrame)
        ));

        server = new(new CelesteNetFakeServerOptions {
            Port = port,
            Logger = message => Logger.Log(LogLevel.Verbose, "cnet-bridge", message),
            IdentityProvider = LocalIdentity,
        });
        server.SessionStarted += OnSessionStarted;
        server.Start();

        Initialized = true;
        Logger.Log(LogLevel.Info, "cnet-bridge", $"listening on 127.0.0.1:{port}");
    }

    public static void Shutdown() {
        if (!Initialized)
            return;

        Initialized = false;
        if (server != null) {
            server.SessionStarted -= OnSessionStarted;
            server.Dispose();
            server = null;
        }

        lock (sessions)
            sessions.Clear();

        suppress?.Uninstall();
        suppress = null;

        netMod?.Dispose();
        netMod = null;
    }

    private static void OnSessionStarted(CelesteNetFakeSession session) {
        lock (sessions) {
            sessions.Add(session);
            relayedTypes[session] = [];
            introducedPlayers[session] = [];
        }

        session.FrameReceived += OnLocalFrame;
        session.Closed += OnSessionClosed;
    }

    private static void OnSessionClosed(CelesteNetFakeSession session) {
        lock (sessions) {
            sessions.Remove(session);
            relayedTypes.Remove(session);
            introducedPlayers.Remove(session);
        }
    }

    /// <summary>The local real client sent something: hand third-party payloads to MiaoNet.</summary>
    private static void OnLocalFrame(CelesteNetFakeSession session, CelesteNetFrame frame) {
        if (frame.TypeId is null || connectionLocalTypes.Contains(frame.TypeId))
            return;

        if (netMod is null || !MiaoNetNetMods.IsConnected)
            return;

        try {
            netMod.Send(new PacketBridgeFrame(frame.Payload));

            bool first;
            lock (sessions)
                first = relayedTypes.TryGetValue(session, out HashSet<string>? seen) && seen.Add(frame.TypeId);
            if (first) {
                Logger.Log(
                    LogLevel.Verbose, "cnet-bridge",
                    $"relay {frame.TypeId} ({frame.Payload.Length} B)"
                );
            }
        } catch (Exception e) {
            Logger.Log(LogLevel.Warn, "cnet-bridge", "send failed: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>Another player's payload arrived: push it into the local real client.</summary>
    private static void OnRemoteFrame(PacketEnvelope envelope, PacketBridgeFrame packet) {
        uint sender = (uint)envelope.SenderPlayerID;

        CelesteNetFakeSession[] targets;
        lock (sessions)
            targets = [.. sessions];

        foreach (CelesteNetFakeSession session in targets) {
            // Our own frames already went out; echoing them back would duplicate everything.
            if (session.PlayerId == sender)
                continue;

            bool first;
            lock (sessions)
                first = introducedPlayers[session].Add(sender);

            if (first) {
                // The real client can only resolve refs to a player it has seen a DataPlayerInfo for.
                _ = SendSafeAsync(session, CelesteNetDataTypes.PlayerInfo(sender, RemoteName(sender)));
            }

            _ = SendSafeAsync(session, CelesteNetFrameRewriter.RewriteSource(packet.Payload, sender));
        }
    }

    /// <summary>
    /// The CelesteNet identity this client should use. Reusing the MiaoNet player id keeps both
    /// sides' references consistent, and gives every player a distinct id.
    /// </summary>
    private static (uint Id, string Name) LocalIdentity() {
        ClientState? state = MiaoNetModule.Instance?.MiaoNetContext?.ClientState;
        OnlinePlayer? self = state?.Self;
        if (self is not null)
            return ((uint)self.ID, self.Info.Name);

        return (1, "Player");
    }

    /// <summary>
    /// The remote player's real MiaoNet name. CoopHelper keys players by (machine, name), so a made-up
    /// name would make one player look like two different people.
    /// </summary>
    private static string RemoteName(uint id) {
        ClientState? state = MiaoNetModule.Instance?.MiaoNetContext?.ClientState;
        if (state is not null && state.Players.TryGetValue((int)id, out OnlinePlayer? player))
            return player.Info.Name;

        return "Player" + id;
    }

    private static async Task SendSafeAsync(CelesteNetFakeSession session, byte[] payload) {
        try {
            await session.SendAsync(payload).ConfigureAwait(false);
        } catch (Exception) {
            // The session is going away; its own pump reports it.
        }
    }
}