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
    public static bool IsCelesteNetPointedAtTheBridge() => ResolveTarget(out string host, out int port)
        && host is "localhost" or "127.0.0.1"
        && port == LocalPort
        && LocalPort != 0;

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

        string target = raw.Trim().ToLowerInvariant();
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
        if (IsCelesteNetPointedAtTheBridge())
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