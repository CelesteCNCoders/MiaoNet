using System;
using Celeste.Mod.CelesteNet.Client;
using MonoMod.RuntimeDetour;

namespace MiaoNet.CelesteNetBridge;

/// <summary>
/// Points every CelesteNet connection at the bridge's own loopback server while the bridge runs.
/// <para>
/// The frame-time check in MiaoNet only notices a connection once the client object exists, which is
/// after the connect thread has already opened the socket: CelesteNet's own settings menu writes
/// <c>WantsToBeConnected</c> and starts connecting inside the same call. Rewriting the target here,
/// before <c>Start</c> reads it, means the player's click connects to the bridge instead of leaving the
/// machine, and the settings toggle stays on because the connection really does happen.
/// </para>
/// </summary>
internal sealed class CelesteNetConnectionGuard {

    private Hook? hook;

    /// <summary>Whether the detour on <c>CelesteNetClientModule.Start</c> is in place.</summary>
    public bool Installed { get; private set; }

    public void Install() {
        if (Installed)
            return;

        try {
            hook = new Hook(
                typeof(CelesteNetClientModule).GetMethod("Start")!,
                new Action<Action<CelesteNetClientModule>, CelesteNetClientModule>(OnStart)
            );
            Installed = true;
        } catch (Exception e) {
            Logger.Log(LogLevel.Warn, "cnet-bridge",
                "hook failed: CelesteNetClientModule.Start -> " + e.GetType().Name + ": " + e.Message);
        }
    }

    public void Uninstall() {
        hook?.Dispose();
        hook = null;
        Installed = false;
    }

    private static void OnStart(Action<CelesteNetClientModule> orig, CelesteNetClientModule self) {
        if (!CelesteNetBridgeEntry.Initialized || CelesteNetBridgeEntry.IsCelesteNetPointedAtTheBridge()) {
            orig(self);
            return;
        }

        int port = CelesteNetBridgeEntry.LocalPort;
        if (port == 0) {
            orig(self);
            return;
        }

        string requested = CelesteNetBridgeEntry.ConnectionTarget;
        CelesteNetClientModule.Settings.ServerOverride = $"127.0.0.1:{port}";
        Logger.Log(LogLevel.Info, "cnet-bridge",
            $"redirected a CelesteNet connection from '{requested}' to 127.0.0.1:{port}");

        orig(self);
    }
}
