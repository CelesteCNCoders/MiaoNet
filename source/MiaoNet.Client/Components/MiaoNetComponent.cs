using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public abstract class MiaoNetComponent
{
    protected readonly MiaoNetContext context;

    public MiaoNetComponent(MiaoNetContext context)
    {
        this.context = context;
    }

    public virtual void OnConnected() { }

    public virtual void OnDisconnected() { }

    public virtual void Update() { }

    public virtual void Render() { }
}