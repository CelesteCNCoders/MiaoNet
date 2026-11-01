using System.Collections.Generic;
using Celeste.Mod.CelesteNet.Client;
using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.Client.Entities;
using Monocle;
using MonoMod.RuntimeDetour;

namespace MiaoNet.CelesteNetBridge;

/// <summary>
/// Silences the real CelesteNet client's own client-side behaviour. The client stays connected and
/// keeps its data pipeline alive; it just must not build UI, spawn ghosts, or drive the local player.
/// <para>
/// Three separate leak surfaces were measured (docs/local/research/ui-suppression.md, section E):
/// HUD components drawn through the render helper, entities the components add into the Level
/// (GhostNameTag / GhostEmote / Ghost, rendered by the scene itself), and the components' own
/// Update/Tick, which keep running even when Initialize() is skipped.
/// </para>
/// </summary>
internal sealed class CelesteNetSuppression {

    /// <summary>Components whose Init() is skipped entirely: never Enabled, never register handlers.</summary>
    private static readonly Type[] SkipInitTypes = [
        typeof(CelesteNetChatComponent),
        typeof(CelesteNetPlayerListComponent),
        typeof(CelesteNetEmoteComponent),
        typeof(CelesteNetStatusComponent),
        typeof(CelesteNetDebugMapComponent),
    ];

    private readonly List<Hook> hooks = [];

    /// <summary>Number of detours the last <see cref="Install"/> call managed to install.</summary>
    public int InstalledCount { get; private set; }

    /// <summary>Number of detours the bridge expects to install.</summary>
    public const int ExpectedCount = 6;

    public void Install() {
        InstalledCount = 0;
        Add("CelesteNetGameComponent.Init", typeof(CelesteNetGameComponent).GetMethod("Init")!, new Action<
            Action<CelesteNetGameComponent>, CelesteNetGameComponent>(OnComponentInit));

        Add("CelesteNetGameComponent.Draw", typeof(CelesteNetGameComponent).GetMethod("Draw")!, new Action<
            Action<CelesteNetGameComponent, Microsoft.Xna.Framework.GameTime?>, CelesteNetGameComponent, Microsoft.Xna.Framework.GameTime?>(
            (_, _, _) => { }));

        Add("CelesteNetRenderHelperComponent.Initialize",
            typeof(CelesteNetRenderHelperComponent).GetMethod("Initialize")!,
            new Action<Action<CelesteNetRenderHelperComponent>, CelesteNetRenderHelperComponent>((_, _) => { }));

        // No 12 game hooks, no transition ILHook, therefore no ghosts.
        Add("CelesteNetMainComponent.Initialize",
            typeof(CelesteNetMainComponent).GetMethod("Initialize")!,
            new Action<Action<CelesteNetMainComponent>, CelesteNetMainComponent>((_, _) => { }));

        // Update() adds the local player's own name tag and keeps sending playerFrame 60x/s;
        // Tick() sends state and frames. Neither belongs to a bridge that only relays third-party data.
        Add("CelesteNetMainComponent.Update", typeof(CelesteNetMainComponent).GetMethod("Update")!, new Action<
            Action<CelesteNetMainComponent, Microsoft.Xna.Framework.GameTime>, CelesteNetMainComponent, Microsoft.Xna.Framework.GameTime>(
            (_, _, _) => { }));

        Add("CelesteNetMainComponent.Tick", typeof(CelesteNetMainComponent).GetMethod("Tick")!, new Action<
            Action<CelesteNetMainComponent>, CelesteNetMainComponent>((_, _) => { }));
    }

    public void Uninstall() {
        foreach (Hook hook in hooks)
            hook.Dispose();
        hooks.Clear();
        InstalledCount = 0;
    }

    private void Add(string name, System.Reflection.MethodInfo from, Delegate to) {
        try {
            hooks.Add(new Hook(from, to));
            InstalledCount++;
        } catch (Exception e) {
            Logger.Log(LogLevel.Warn, "cnet-bridge", "hook failed: " + name + " -> " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static void OnComponentInit(Action<CelesteNetGameComponent> orig, CelesteNetGameComponent self) {
        if (Array.IndexOf(SkipInitTypes, self.GetType()) >= 0)
            return;

        orig(self);
    }
}
