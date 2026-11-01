using Celeste.Mod.MiaoNetCelesteNetShim;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client;

public sealed class CelesteNetClientModule : EverestModule
{
    // Fields on purpose: the original type exposes these as fields and mods bind by kind.
    public static CelesteNetClientModule? Instance;
    public CelesteNetClientContext? Context;
    public CelesteNetClient? Client => Context?.Client;
    public static CelesteNetClientSettings Settings { get; } = new();

    public CelesteNetClientModule()
    {
        Instance = this;
    }

    public override void Load()
    {
        // The declarations have to exist before the player can connect: the handshake carries
        // them, and they are what a peer resolves an incoming net-mod frame with.
        CelesteNetCompatBackend.Load();
    }

    public override void LoadContent(bool firstLoad)
    {
        CelesteNetCompatBackend.Diag($"CelesteNetClientModule.LoadContent(firstLoad: {firstLoad})");
        EnsureContext();
    }

    /// <summary>
    /// Creates the client context once the game exists. Called from this module's <c>LoadContent</c>:
    /// the shim declares <c>CelesteNet.Client</c> as a mod of its own, so Everest registers this
    /// module like any other and runs its lifecycle like any other's.
    /// </summary>
    public void EnsureContext()
    {
        if (Context is not null)
            return;

        if (Celeste.Instance is null)
        {
            // TEMPORARY diagnostic: without Celeste.Instance the context is never created.
            CelesteNetCompatBackend.Diag("CelesteNetClientModule.EnsureContext skipped: Celeste.Instance is null.");
            return;
        }

        Context = new CelesteNetClientContext(Celeste.Instance);
        Context.Init(Settings);
        CelesteNetCompatBackend.Diag($"CelesteNetClientModule context created, {Context.Components.Count} component(s).");
    }

    public override void Unload()
    {
        CelesteNetCompatBackend.Unload();
        Context?.Dispose();
        Context = null;
        Instance = null;
    }
}
