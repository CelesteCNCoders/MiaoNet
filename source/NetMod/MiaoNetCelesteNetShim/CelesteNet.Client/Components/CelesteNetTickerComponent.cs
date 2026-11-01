using Celeste.Mod.MiaoNetCelesteNetShim;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Components;

/// <summary>
/// Drives <see cref="ITickReceiver.Tick"/> for every component.
/// <para>
/// The original tick rate comes from the server (<c>DataTickRate</c>); MiaoNet has no
/// equivalent, so the shim uses the original server default of 60 TpS. Mods build their
/// own cadence on the counter (CoopHelper sends a heartbeat every 30 ticks), so this has
/// to keep running while connected.
/// </para>
/// </summary>
public class CelesteNetTickerComponent : CelesteNetGameComponent
{
    public const float MinTickDelay = 0.01f;

    /// <summary>
    /// Original servers default to <c>MaxTickRate = 60</c>.
    /// </summary>
    public volatile float TickRate = 60f;

    private float tickDelay;

    private bool loggedFirstTick;

    public CelesteNetTickerComponent(CelesteNetClientContext context, Game game) : base(context, game) { }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (TickRate <= 0 || Context is null || !(Client?.IsReady ?? false))
            return;

        tickDelay -= Engine.RawDeltaTime;
        if (TickRate < 60f && tickDelay > 0f)
            return;

        if (!loggedFirstTick)
        {
            loggedFirstTick = true;
            CelesteNetCompatBackend.Diag($"Ticker running, ticking {Context.Components.Count} component(s).");
        }

        tickDelay += 1f / TickRate;
        if (tickDelay < MinTickDelay)
            tickDelay = MinTickDelay;

        foreach (CelesteNetGameComponent component in Context.Components.Values)
            component.Tick();
    }
}
