using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.Client;
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

    private static readonly List<CelesteNetFakeSession> sessions = [];

    /// <summary>Type ids already relayed per session, for a log that names each kind once.</summary>
    private static readonly Dictionary<CelesteNetFakeSession, HashSet<string>> relayedTypes = [];

    /// <summary>Remote players already announced to each session.</summary>
    private static readonly Dictionary<CelesteNetFakeSession, HashSet<uint>> introducedPlayers = [];

    /// <summary>Names of the announced remote players, so a tombstone can carry the real one.</summary>
    private static readonly Dictionary<uint, string> announcedNames = [];

    /// <summary>The MiaoNet context whose player-leave event is being watched, if any.</summary>
    private static MiaoNetContext? watchedContext;

    /// <summary>Whether MiaoNet had a connection at the previous heartbeat.</summary>
    private static bool miaoNetWasConnected;

    /// <summary>The newest CelesteNet release this bridge was verified against.</summary>
    private static readonly Version TestedCelesteNetVersion = new(2, 4, 2);

    private static NetMod? netMod;
    private static CelesteNetFakeServer? server;
    private static CelesteNetSuppression? suppress;
    private static CelesteNetConnectionGuard? guard;

    public static bool Initialized { get; private set; }

    /// <summary>The port the local server listens on, or 0 when it is not running.</summary>
    public static int LocalPort => server?.Port ?? 0;

    /// <summary>Where the real client would connect if it were started right now.</summary>
    public static string ConnectionTarget {
        get {
            string? target = CelesteNetClientModule.Settings.ServerOverride;
            return string.IsNullOrWhiteSpace(target) ? "celeste.0x0a.de" : target.Trim();
        }
    }

    /// <summary>
    /// Whether CelesteNet would connect to the bridge's own loopback server. While the bridge runs it is
    /// the only address that is allowed: anything else is the player using CelesteNet's own menu, and
    /// that connection would leave the game with two clients pointing at two different servers.
    /// </summary>
    /// <summary>
    /// Whether CelesteNet is currently pointed at this bridge. Read by <c>MiaoNetModule</c> through
    /// reflection, so it stays a property: that is the shape the client looks up.
    /// </summary>
    public static bool IsCelesteNetPointedAtTheBridge => ResolveTarget(out string host, out int port)
        && IsLoopbackHost(host)
        && port == LocalPort
        && LocalPort != 0;

    /// <summary>Whether a host name in <c>ServerOverride</c> means this machine.</summary>
    private static bool IsLoopbackHost(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Splits CelesteNet's <c>ServerOverride</c>. CelesteNet's own "connect locally" button writes just
    /// <c>localhost</c> and lets the port default, while auto connect writes <c>127.0.0.1:&lt;port&gt;</c>.
    /// </summary>
    private static bool ResolveTarget(out string host, out int port) {
        host = "";
        port = 17230;

        string? raw = CelesteNetClientModule.Settings.ServerOverride;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string target = raw.Trim();
        int separator = target.LastIndexOf(':');
        if (separator <= 0) {
            host = target;
            return true;
        }

        host = target[..separator];
        if (!int.TryParse(target[(separator + 1)..], out port))
            port = 17230;

        return true;
    }

    /// <summary>Turns the bridge on or off at runtime, the way the settings menu does.</summary>
    public static void SetEnabled(bool enabled) {
        if (enabled)
            Initialize();
        else
            Shutdown();
    }

    /// <summary>Starts the bridge: declares the NetMod, suppresses the CelesteNet UI, opens the port.</summary>
    public static void Initialize() {
        if (Initialized)
            return;

        WarnAboutUntestedVersion();

        // Whatever the real client was doing before the switch flipped, it is over now.
        Quiesce();

        suppress = new();
        suppress.Install();
        if (suppress.InstalledCount < CelesteNetSuppression.ExpectedCount) {
            // CelesteNet's interface leaking back on screen is the one failure players cannot miss.
            ReportFailure(
                $"only {suppress.InstalledCount}/{CelesteNetSuppression.ExpectedCount} CelesteNet UI hooks installed; " +
                "CelesteNet's own interface may show up"
            );
        }

        guard = new();
        guard.Install();
        if (!guard.Installed) {
            ReportFailure("could not intercept CelesteNet's own connect button; it may still dial out");
        }

        server = new(new CelesteNetFakeServerOptions {
            Logger = message => Logger.Log(LogLevel.Verbose, "cnet-bridge", message),
            IdentityProvider = LocalIdentity,
        });
        server.SessionStarted += OnSessionStarted;

        try {
            server.Start();
        } catch (Exception e) {
            server = null;
            suppress.Uninstall();
            suppress = null;
            guard.Uninstall();
            guard = null;
            ReportFailure("could not start the local CelesteNet server: " + e.Message);
            return;
        }

        // The declaration has to exist before the MiaoNet handshake goes out.
        netMod = MiaoNetNetMods.Register(new NetMod(
            NetModName,
            ProtocolVersion,
            NetModPacketEntry.Create<PacketBridgeFrame>(PacketScope.Map, OnRemoteFrame)
        ));

        Initialized = true;
        Logger.Log(LogLevel.Info, "cnet-bridge",
            $"relaying CelesteNet through 127.0.0.1:{server.Port}"
            + (server.UsedFallbackPort ? " (the default port 17230 was busy)" : "")
        );
    }

    /// <summary>
    /// The suppression detours are the only part of the bridge bound to CelesteNet's internals, so a
    /// newer client is worth a warning even when everything installs today.
    /// </summary>
    private static void WarnAboutUntestedVersion() {
        Version? installed = Everest.Modules
            .FirstOrDefault(module => module.Metadata.Name == "CelesteNet.Client")?.Metadata.Version;
        if (installed is null || installed <= TestedCelesteNetVersion)
            return;

        Logger.Log(
            LogLevel.Warn, "cnet-bridge",
            $"CelesteNet {installed} is newer than the tested {TestedCelesteNetVersion}; " +
            "the UI suppression detours may need an update"
        );
    }

    /// <summary>
    /// A failure the player has to know about: the bridge not working is invisible otherwise, because
    /// its own log lines are verbose. The status message only shows up once MiaoNet has a context.
    /// </summary>
    private static void ReportFailure(string message) {
        Logger.Log(LogLevel.Error, "cnet-bridge", message);

        try {
            MiaoNetModule.Instance?.MiaoNetContextOptional?.StatusComponent.ShowStatusMessage(message);
        } catch (Exception e) {
            Logger.Log(LogLevel.Warn, "cnet-bridge", "could not show the failure on screen: " + e.Message);
        }
    }

    public static void Shutdown() {
        if (!Initialized)
            return;

        Initialized = false;

        if (watchedContext is not null) {
            watchedContext.PlayerLeft -= OnPlayerLeft;
            watchedContext = null;
        }

        ResetRedirect();
        Quiesce();
        if (server != null) {
            server.SessionStarted -= OnSessionStarted;
            server.Dispose();
            server = null;
        }

        lock (sessions)
            sessions.Clear();

        guard?.Uninstall();
        guard = null;

        suppress?.Uninstall();
        suppress = null;

        netMod?.Dispose();
        netMod = null;
    }

    /// <summary>
    /// Drops the redirect once the bridge is gone, so a later "connect" goes wherever the player points
    /// it instead of dialing a loopback port nobody listens on any more.
    /// </summary>
    private static void ResetRedirect() {
        if (IsCelesteNetPointedAtTheBridge)
            CelesteNetClientModule.Settings.ServerOverride = "";
    }

    /// <summary>
    /// Stops the real client's connection, in whatever state it is: while the bridge runs it owns
    /// CelesteNet, and two live clients at once freeze the game.
    /// </summary>
    public static void Quiesce() {
        CelesteNetClientModule? module = CelesteNetClientModule.Instance;
        if (module is null)
            return;

        CelesteNetClientSettings settings = CelesteNetClientModule.Settings;
        if (settings.Connected) {
            settings.Connected = false;
            Logger.Log(LogLevel.Info, "cnet-bridge", "disconnected CelesteNet because the bridge owns the connection");
            return;
        }

        // Connected stays false while the client is still connecting, so stop it by hand.
        if (settings.WantsToBeConnected) {
            settings.WantsToBeConnected = false;
            module.Stop();
            Logger.Log(LogLevel.Info, "cnet-bridge", "stopped a pending CelesteNet connection");
        }
    }

    private static void OnSessionStarted(CelesteNetFakeSession session) {
        lock (sessions) {
            sessions.Add(session);
            relayedTypes[session] = [];
            introducedPlayers[session] = [];
        }

        session.FrameReceived += OnLocalFrame;
        session.Heartbeat += OnSessionHeartbeat;
        session.Closed += OnSessionClosed;

        WatchPlayerLeaves();
    }

    private static void OnSessionClosed(CelesteNetFakeSession session) {
        session.FrameReceived -= OnLocalFrame;
        session.Heartbeat -= OnSessionHeartbeat;
        session.Closed -= OnSessionClosed;

        lock (sessions) {
            sessions.Remove(session);
            relayedTypes.Remove(session);
            introducedPlayers.Remove(session);
        }
    }

    /// <summary>
    /// The bridge has no update loop of its own, and the keepAlive the fake server already sends every
    /// 250 ms is the only periodic callback it gets. Two things need one: attaching to MiaoNet's player
    /// list (the context may appear after the first CelesteNet connection) and noticing that MiaoNet
    /// itself went away.
    /// </summary>
    private static void OnSessionHeartbeat(CelesteNetFakeSession session) {
        WatchPlayerLeaves();

        bool connected = MiaoNetNetMods.IsConnected;
        if (miaoNetWasConnected && !connected)
            AnnounceEveryoneLeft(session);

        miaoNetWasConnected = connected;
    }

    /// <summary>
    /// Subscribes to MiaoNet's leave event. Idempotent, because the context is created lazily the first
    /// time the player uses MiaoNet, which can be later than the first CelesteNet connection.
    /// </summary>
    private static void WatchPlayerLeaves() {
        if (watchedContext is not null)
            return;

        MiaoNetContext? context = MiaoNetModule.Instance?.MiaoNetContextOptional;
        if (context is null)
            return;

        context.PlayerLeft += OnPlayerLeft;
        watchedContext = context;
    }

    /// <summary>
    /// A player leaving MiaoNet has to leave CelesteNet as well: the client only drops a player when it
    /// receives a dead <c>playerInfo</c>, and every mod that keys data by player id (CoopHelper, the
    /// appearance mods) would otherwise keep a player that is never coming back.
    /// </summary>
    private static void OnPlayerLeft(OnlinePlayer player) {
        uint id = (uint)player.ID;

        CelesteNetFakeSession[] targets;
        lock (sessions)
            targets = [.. sessions];

        foreach (CelesteNetFakeSession session in targets) {
            if (!ForgetIntroduced(session, id))
                continue;

            _ = SendSafeAsync(session, CelesteNetDataTypes.PlayerInfo(id, PlayerNameOf(id), alive: false));
            Logger.Log(LogLevel.Verbose, "cnet-bridge", $"announced that #{id} left CelesteNet");
        }
    }

    /// <summary>
    /// Without a MiaoNet connection this side cannot hear from anybody any more, so every player the
    /// local client knows about is gone as far as it is concerned.
    /// </summary>
    private static void AnnounceEveryoneLeft(CelesteNetFakeSession session) {
        uint[] ids;
        lock (sessions) {
            if (!introducedPlayers.TryGetValue(session, out HashSet<uint>? introduced))
                return;

            ids = [.. introduced];
            introduced.Clear();
        }

        foreach (uint id in ids) {
            _ = SendSafeAsync(session, CelesteNetDataTypes.PlayerInfo(id, PlayerNameOf(id), alive: false));
        }

        if (ids.Length > 0)
            Logger.Log(LogLevel.Info, "cnet-bridge", $"MiaoNet went away, announced {ids.Length} players as left");
    }

    private static bool ForgetIntroduced(CelesteNetFakeSession session, uint id) {
        lock (sessions)
            return introducedPlayers.TryGetValue(session, out HashSet<uint>? introduced) && introduced.Remove(id);
    }

    private static string PlayerNameOf(uint id) {
        lock (announcedNames)
            return announcedNames.TryGetValue(id, out string? name) ? name : "";
    }

    /// <summary>The local real client sent something: hand third-party payloads to MiaoNet.</summary>
    private static void OnLocalFrame(CelesteNetFakeSession session, CelesteNetFrame frame) {
        if (frame.TypeId is not { } typeId || !CelesteNetRelayFilter.IsRelayable(typeId, DeclaringSourceOf(typeId)))
            return;

        if (netMod is null || !MiaoNetNetMods.IsConnected)
            return;

        try {
            netMod.Send(new PacketBridgeFrame(frame.Payload));

            bool first;
            lock (sessions)
                first = relayedTypes.TryGetValue(session, out HashSet<string>? seen) && seen.Add(typeId);
            if (first) {
                Logger.Log(
                    LogLevel.Verbose, "cnet-bridge",
                    $"relay {typeId} ({frame.Payload.Length} B)"
                );
            }
        } catch (Exception e) {
            Logger.Log(LogLevel.Warn, "cnet-bridge", "send failed: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Which assembly declares the type the frame belongs to, as the local client knows it. Reading it from
    /// the client's own DataContext is what makes the bridge survive a CelesteNet update that adds a type:
    /// anything new that ships inside CelesteNet is treated as connection state without being listed here.
    /// </summary>
    private static string? DeclaringSourceOf(string typeId) {
        Celeste.Mod.CelesteNet.DataContext? data = CelesteNetClientModule.Instance?.Context?.Client?.Data;
        if (data is null || !data.IDToDataType.TryGetValue(typeId, out Type? type))
            return null;

        return data.DataTypeToSource.TryGetValue(type, out string? source) ? source : null;
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
                string name = RemoteName(sender);
                lock (announcedNames)
                    announcedNames[sender] = name;

                _ = SendSafeAsync(session, CelesteNetDataTypes.PlayerInfo(sender, name));
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