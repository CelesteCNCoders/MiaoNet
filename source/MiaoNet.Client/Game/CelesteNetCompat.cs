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
    /// Whether CelesteNet has been asked to connect but has not got a client object yet. Its settings
    /// toggle starts dialing inside the same call that sets this flag, so a frame-time check that only
    /// looks at <see cref="IsConnected"/> arrives after the socket is already open.
    /// </summary>
    private static bool IsPending
        => Resolve() && TryGetSetting(wantsToBeConnectedProperty, out bool wants) && wants;

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
            if (!MiaoNetModule.IsCelesteNetPointedAtBridge && (IsConnected || IsPending))
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
