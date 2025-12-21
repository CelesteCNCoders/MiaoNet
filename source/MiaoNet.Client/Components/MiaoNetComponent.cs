using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public abstract class MiaoNetComponent
{
    protected readonly MiaoNetContext context;

    /// <summary>
    /// Get this will assert client state is not null.
    /// Use <see cref="HasState"/> to check if it's null.
    /// </summary>
    protected ClientState ClientState
    {
        get
        {
            SafeGuard.Assert(HasState);
            return context.ClientState!;
        }
    }

    protected PooledStringManager PooledStringManager
        => context.PooledStringManager!;

    protected bool HasState => context.ClientState is not null;

    public MiaoNetComponent(MiaoNetContext context)
    {
        this.context = context;
    }

    public virtual void OnConnected() { }

    public virtual void OnDisconnected() { }

    public virtual void Update() { }

    public virtual void Render() { }
}