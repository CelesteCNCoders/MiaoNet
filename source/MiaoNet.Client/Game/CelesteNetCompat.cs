using System.Reflection;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Keeps MiaoNet and the real CelesteNet client from being connected at the same time. Each client
/// draws its own interface, and two live interfaces freeze the game, so the two take turns: whichever
/// one the player connects last wins, and the older one is disconnected.
/// <para>
/// Everything here goes through reflection: MiaoNet.dll must never reference CelesteNet types, or the
/// mod would stop loading for players who do not have CelesteNet.
/// </para>
/// </summary>
internal static class CelesteNetCompat {

    private static bool resolved;
    private static Type? moduleType;
    private static PropertyInfo? settingsProperty;
    private static PropertyInfo? connectedProperty;
    private static PropertyInfo? wantsToBeConnectedProperty;
    private static PropertyInfo? serverOverrideProperty;
    private static MethodInfo? stopMethod;

    /// <summary>Set while MiaoNet is disconnecting because CelesteNet connected first.</summary>
    private static bool steppingAside;

    /// <summary>Whether the CelesteNet client mod is installed and enabled.</summary>
    public static bool IsInstalled => Resolve();

    /// <summary>Whether the real CelesteNet client currently has a connection.</summary>
    public static bool IsConnected
        => Resolve() && TryGetSetting(connectedProperty, out bool connected) && connected;

    /// <summary>
    /// Stops CelesteNet whether it is connected or still connecting. Called right before MiaoNet
    /// connects, so the connection the player just asked for is the one that survives.
    /// </summary>
    public static void Quiesce() {
        if (!Resolve())
            return;

        try {
            if (IsConnected) {
                connectedProperty!.SetValue(Settings, false);
                Logger.Info(LT.MiaoNet, "CelesteNet was connected, disconnecting it");
                return;
            }

            // Connected stays false while the client is still connecting, so the setter above would
            // not have stopped anything.
            if (TryGetSetting(wantsToBeConnectedProperty, out bool wants) && wants) {
                wantsToBeConnectedProperty!.SetValue(Settings, false);
                stopMethod!.Invoke(FindModule(), null);
                Logger.Info(LT.MiaoNet, "CelesteNet was connecting, stopping it");
            }
        } catch (Exception e) {
            Logger.Warn(LT.MiaoNet, "Failed to stop CelesteNet: " + e.Message);
        }
    }

    /// <summary>
    /// Called every frame. With the bridge off, a CelesteNet connection that comes up while MiaoNet is
    /// connected makes MiaoNet step aside. With the bridge on, CelesteNet is supposed to talk to the
    /// bridge, and a connection to a real server is what we have to stop.
    /// </summary>
    public static void OnFrame() {
        if (!Resolve())
            return;

        if (MiaoNetModule.IsCelesteNetBridgeActive) {
            if (IsConnected && !IsOnOurBridge())
                Quiesce();
            return;
        }

        if (!IsConnected) {
            steppingAside = false;
            return;
        }

        MiaoNetContext? context = MiaoNetModule.Instance?.MiaoNetContextOptional;
        if (context is null || !context.HasConnection || steppingAside)
            return;

        steppingAside = true;
        Logger.Info(LT.MiaoNet, "CelesteNet connected while MiaoNet was connected, disconnecting MiaoNet");
        context.Disconnect();
        context.StatusComponent.ShowStatusMessage(Dialog.Get("miaonet_cnet_took_over_message"));
    }

    /// <summary>
    /// With the bridge on, CelesteNet should only ever connect to the loopback server the bridge runs.
    /// An override pointing anywhere else means the player used CelesteNet's own menu.
    /// </summary>
    private static bool IsOnOurBridge() {
        int bridgePort = MiaoNetModule.CelesteNetBridgePort;
        if (bridgePort == 0)
            return false;

        if (serverOverrideProperty?.GetValue(Settings) is not string target || target.Length == 0)
            return false;

        // CelesteNet's own "connect locally" button writes just "localhost" (port defaults to
        // 17230); auto connect writes "127.0.0.1:<port>".
        target = target.Trim().ToLowerInvariant();
        int separator = target.LastIndexOf(':');
        string host = separator > 0 ? target[..separator] : target;
        int port = 17230;
        if (separator > 0 && int.TryParse(target[(separator + 1)..], out int parsed))
            port = parsed;

        if (host is not ("localhost" or "127.0.0.1"))
            return false;

        return port == bridgePort;
    }

    private static EverestModule? FindModule()
        => Everest.Modules.FirstOrDefault(module => module.Metadata.Name == "CelesteNet.Client");

    private static object? Settings => settingsProperty?.GetValue(null);

    private static bool TryGetSetting(PropertyInfo? property, out bool value) {
        value = false;
        if (property?.GetValue(Settings) is not bool current)
            return false;

        value = current;
        return true;
    }

    private static bool Resolve() {
        if (resolved)
            return moduleType is not null;

        resolved = true;
        EverestModule? module = FindModule();
        if (module is null)
            return false;

        moduleType = module.GetType();
        settingsProperty = moduleType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
        Type? settingsType = settingsProperty?.PropertyType;
        connectedProperty = settingsType?.GetProperty("Connected");
        wantsToBeConnectedProperty = settingsType?.GetProperty("WantsToBeConnected");
        serverOverrideProperty = settingsType?.GetProperty("ServerOverride");
        stopMethod = moduleType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);

        if (settingsProperty is null || connectedProperty is null || stopMethod is null) {
            Logger.Warn(LT.MiaoNet, "CelesteNet has an unknown shape; the two clients cannot be kept apart");
            moduleType = null;
        }

        return moduleType is not null;
    }
}
