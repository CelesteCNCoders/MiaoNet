using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.MiaoNetCelesteNetShim;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client;

public sealed class CelesteNetClientContext : DrawableGameComponent
{
    public static event Action<CelesteNetClientContext>? OnCreate;
    public static event Action<CelesteNetClientContext>? OnInit;
    public static event Action<CelesteNetClientContext>? OnStart;
    public static event Action<CelesteNetClientContext>? OnDispose;

    // Fields on purpose: the original type exposes them as fields.
    public CelesteNetClient? Client;
    public Dictionary<Type, CelesteNetGameComponent> Components = new();
    public bool IsDisposed { get; private set; }

    public CelesteNetClientContext(Game game, CelesteNetClientContext? oldCtx = null) : base(game)
    {
        UpdateOrder = -10000;
        DrawOrder = int.MaxValue;

        Game.Components.Add(this);

        AddComponent(new CelesteNetTickerComponent(this, game));
        AddComponent(new CelesteNetPlayerListComponent(this, game));
        AddComponent(new CelesteNetCompatStateComponent(this, game));
        DiscoverModComponents(game);

        OnCreate?.Invoke(this);
    }

    /// <summary>
    /// The original context instantiates every <see cref="CelesteNetGameComponent"/> found
    /// in the mod assembly, which is how a mod's own component ends up being ticked. The
    /// shim has no way to tell which assembly it stands in for, so it scans the loaded
    /// assemblies instead.
    /// </summary>
    private void DiscoverModComponents(Game game)
    {
        foreach (Type type in DataContext.GetLoadedTypes())
        {
            if (type.IsAbstract || !typeof(CelesteNetGameComponent).IsAssignableFrom(type))
                continue;
            if (Components.ContainsKey(type))
                continue;
            if (type.GetConstructor([typeof(CelesteNetClientContext), typeof(Game)]) is null)
                continue;

            try
            {
                if (Activator.CreateInstance(type, this, game) is CelesteNetGameComponent component)
                    AddComponent(component);
            }
            catch (Exception e)
            {
                Logger.Warn(nameof(CelesteNetClientContext), $"Failed to create component {type.FullName}: {e}");
            }
        }
    }

    private void AddComponent(CelesteNetGameComponent component)
    {
        Components[component.GetType()] = component;
        if (!Game.Components.Contains(component))
            Game.Components.Add(component);

        CelesteNetCompatBackend.Diag($"Component registered: {component.GetType().FullName}");
    }

    public void Init(CelesteNetClientSettings settings)
    {
        Client = new CelesteNetClient(settings, new());
        foreach (CelesteNetGameComponent component in Components.Values)
            component.Init();
        CelesteNetCompatBackend.Diag($"Context initialized with {Components.Count} component(s).");
        OnInit?.Invoke(this);
    }

    public void Start(CancellationToken token = default)
    {
        if (Client is null)
            return;

        Client.Start(token);
        foreach (CelesteNetGameComponent component in Components.Values)
            component.Start();
        CelesteNetCompatBackend.Diag($"CelesteNetClientContext started (isConnected: {Client.Con?.IsConnected}).");
        OnStart?.Invoke(this);
    }

    public T? Get<T>() where T : CelesteNetGameComponent
        => Components.TryGetValue(typeof(T), out CelesteNetGameComponent? component) ? (T)component : null;

    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;
        IsDisposed = true;

        foreach (CelesteNetGameComponent component in Components.Values)
        {
            if (!component.IsDisposed)
                component.Dispose();
        }
        Components.Clear();

        Client?.Dispose();
        Client = null;
        Game.Components.Remove(this);
        OnDispose?.Invoke(this);
        base.Dispose(disposing);
    }
}
