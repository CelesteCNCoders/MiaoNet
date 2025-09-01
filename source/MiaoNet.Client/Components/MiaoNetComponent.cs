using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public abstract class MiaoNetComponent
{
    protected readonly MiaoNetContext context;

    public MiaoNetComponent(MiaoNetContext context)
    {
        this.context = context;
    }

    /// <summary>This method is NOT called on main thread.</summary>
    public virtual void OnConnected() { }

    public virtual void OnDisconnected() { }

    public virtual void Update() { }

    public virtual void Render() { }
}