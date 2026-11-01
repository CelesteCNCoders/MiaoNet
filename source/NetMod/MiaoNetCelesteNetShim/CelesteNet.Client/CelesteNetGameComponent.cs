using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Client;

/// <summary>
/// The namespace is part of the compatibility surface: the original type lives in
/// <c>Celeste.Mod.CelesteNet.Client</c>, and only <c>Components.ITickReceiver</c>
/// lives in the <c>Components</c> child namespace.
/// </summary>
public abstract class CelesteNetGameComponent : DrawableGameComponent, Components.ITickReceiver
{
    public const int UI_WIDTH = 1920;
    public const int UI_HEIGHT = 1080;

    // Fields on purpose: the original type exposes them as fields.
    public CelesteNetClientContext? Context;
    public CelesteNetClient? Client => Context?.Client;
    public CelesteNetClientSettings ClientSettings => Context?.Client?.Settings ?? CelesteNetClientModule.Settings;
    public CelesteNetClientSettings Settings => CelesteNetClientModule.Settings;
    public bool Persistent;
    public bool AutoDispose = true;

    public bool IsDisposed { get; private set; }

    protected CelesteNetGameComponent(CelesteNetClientContext context, Game game) : base(game)
    {
        Context = context;
        UpdateOrder = 10000;
        DrawOrder = 10000;
        Enabled = false;
        context.Components[GetType()] = this;
    }

    public virtual void Init()
    {
        Enabled = true;
        Client?.Data.RegisterHandlersIn(this);
    }

    public virtual void Start() { }
    public virtual void Tick() { }
    public virtual void Disconnect(bool forceDispose = false)
    {
        Context = null;
        if ((AutoDispose || forceDispose) && !IsDisposed)
            Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        Client?.Data.UnregisterHandlersIn(this);
        Game.Components.Remove(this);
        base.Dispose(disposing);
    }
}
