using System.Collections.Immutable;
using System.Diagnostics;

namespace MiaoNet.Server.GameScope;

public abstract class Scope
{
    private Scope? parent;
    private ImmutableHashSet<ServerPlayer> connections;
    private ImmutableHashSet<ServerPlayer> allConnections;
    private volatile bool allConnectionsDirty;

    public Scope? Parent => parent;

    public ImmutableHashSet<ServerPlayer> Connections => connections;

    protected abstract IEnumerable<Scope> ChildScopes { get; }

    public ImmutableHashSet<ServerPlayer> AllConnections
    {
        get
        {
            if (allConnectionsDirty)
            {
                var builder = connections.ToBuilder();
                foreach (var child in ChildScopes)
                    builder.UnionWith(child.AllConnections);
                allConnections = builder.ToImmutable();
                allConnectionsDirty = false;
            }
            return allConnections;
        }
    }

    public bool IsEmpty => connections.IsEmpty && !ChildScopes.Any();

    public abstract bool Permanent { get; }

    protected Scope(Scope? parent)
    {
        this.parent = parent;
        connections = ImmutableHashSet<ServerPlayer>.Empty;
        allConnections = ImmutableHashSet<ServerPlayer>.Empty;
    }

    internal void AddConnection(ServerPlayer connection)
    {
        bool result = ImmutableInterlocked.Update(ref connections, (d, c) => d.Add(c), connection);
        Debug.Assert(result);
        InvalidateAllConnections();
    }

    internal void RemoveConnection(ServerPlayer connection)
    {
        bool result = ImmutableInterlocked.Update(ref connections, (d, c) => d.Remove(c), connection);
        Debug.Assert(result);
        InvalidateAllConnections();
    }

    internal void RemoveChild(Scope child)
    {
        RemoveChildCore(child);
        InvalidateAllConnections();
    }

    protected abstract void RemoveChildCore(Scope child);

    private void InvalidateAllConnections()
    {
        allConnectionsDirty = true;
        parent?.InvalidateAllConnections();
    }
}

public abstract class Scope<TSelfKey, TChildKey> : Scope
    where TSelfKey : notnull
    where TChildKey : notnull
{
    private ImmutableDictionary<TChildKey, Scope> children;

    public TSelfKey Key { get; }

    public ImmutableDictionary<TChildKey, Scope> Children => children;

    protected override IEnumerable<Scope> ChildScopes => children.Values;

    protected Scope(TSelfKey key, Scope? parent) : base(parent)
    {
        Key = key;
        children = ImmutableDictionary<TChildKey, Scope>.Empty;
    }

    internal void AddChild(TChildKey key, Scope child)
    {
        bool result = ImmutableInterlocked.Update(ref children, (d, c) => d.Add(c.key, c.child), (key, child));
        Debug.Assert(result);
    }

    internal void RemoveChild(TChildKey key)
    {
        if (children.ContainsKey(key))
        {
            ImmutableInterlocked.Update(ref children, (d, k) => d.Remove(k), key);
        }
    }

    protected override void RemoveChildCore(Scope child)
    {
        foreach (var kvp in children)
        {
            if (ReferenceEquals(kvp.Value, child))
            {
                ImmutableInterlocked.Update(ref children, (d, k) => d.Remove(k), kvp.Key);
                return;
            }
        }
    }

    public Scope? GetChild(TChildKey key)
        => children.GetValueOrDefault(key);
}

public readonly struct NoKey
{
    public static readonly NoKey Instance = default;
}
