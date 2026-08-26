using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRefillAdapter : IWatchEntityAdapter
{
    private static readonly WatchRefillAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.Refill;

    public static void Load()
    {
        On.Celeste.Refill.ctor_EntityData_Vector2 += Refill_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Refill.ctor_EntityData_Vector2 -= Refill_ctor;
        WatchEntityIDTable<Refill>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        Dictionary<int, Refill> entities = level.Entities.OfType<Refill>()
            .Where(entity => WatchEntityIDTable<Refill>.TryGet(entity, room, out _))
            .GroupBy(entity =>
            {
                WatchEntityIDTable<Refill>.TryGet(entity, room, out int id);
                return id;
            })
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (int id in level.Session.LevelData.Entities
            .Where(data => data.Name == "refill")
            .Select(data => data.ID))
        {
            WatchEntityPhase phase = WatchEntityPhase.Gone;
            if (entities.TryGetValue(id, out Refill? refill))
            {
                phase = refill.Collidable
                    ? WatchEntityPhase.Ready
                    : refill.oneUse ? WatchEntityPhase.Gone : WatchEntityPhase.Cooldown;
            }
            yield return new WatchEntityState(new WatchEntityKey(Kind, id), [(byte)phase]);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityPhase>? phases = WatchPickupAdapterHelpers.ParsePhases(Kind, states);
        if (phases is null)
            return WatchEntityApplyResult.None;

        bool changed = false;
        bool requiresReload = false;
        string room = level.Session.Level;
        Dictionary<int, Refill> entities = level.Entities.OfType<Refill>()
            .Where(entity => WatchEntityIDTable<Refill>.TryGet(entity, room, out _))
            .GroupBy(entity =>
            {
                WatchEntityIDTable<Refill>.TryGet(entity, room, out int id);
                return id;
            })
            .ToDictionary(group => group.Key, group => group.Last());

        foreach ((int id, WatchEntityPhase phase) in phases)
        {
            if (!entities.TryGetValue(id, out Refill? refill))
            {
                requiresReload |= phase != WatchEntityPhase.Gone;
                continue;
            }

            switch (phase)
            {
                case WatchEntityPhase.Ready:
                    if (!refill.Collidable)
                    {
                        refill.Respawn();
                        changed = true;
                    }
                    break;

                case WatchEntityPhase.Cooldown:
                    if (refill.Collidable || refill.sprite.Visible || refill.respawnTimer <= 0f)
                    {
                        HideRefill(refill, false);
                        changed = true;
                    }
                    break;

                case WatchEntityPhase.Gone:
                    if (refill.Collidable || refill.sprite.Visible || refill.outline.Visible)
                    {
                        HideRefill(refill, true);
                        changed = true;
                    }
                    break;
            }
        }

        return WatchPickupAdapterHelpers.Result(changed, requiresReload);
    }


    private static void HideRefill(Refill refill, bool gone)
    {
        if (refill.Collidable)
        {
            Audio.Play(
                refill.twoDashes
                    ? "event:/new_content/game/10_farewell/pinkdiamond_touch"
                    : "event:/game/general/diamond_touch",
                refill.Position
            );
            refill.wiggler.Start();
        }
        refill.Collidable = false;
        refill.sprite.Visible = false;
        refill.flash.Visible = false;
        refill.outline.Visible = !gone;
        refill.respawnTimer = gone ? float.MaxValue : 2.5f;
    }

    private static void Refill_ctor(
        On.Celeste.Refill.orig_ctor_EntityData_Vector2 orig,
        Refill self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Refill>.Set(self, data.Level.Name, data.ID);
    }
}

internal sealed class WatchFlyFeatherAdapter : IWatchEntityAdapter
{
    private const byte CollectEvent = 1;
    private const byte ShieldBounceEvent = 2;
    private const byte RespawnEvent = 3;

    private static readonly WatchFlyFeatherAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.FlyFeather;

    public static void Load()
    {
        On.Celeste.FlyFeather.ctor_EntityData_Vector2 += FlyFeather_ctor;
        On.Celeste.FlyFeather.OnPlayer += FlyFeather_OnPlayer;
        On.Celeste.FlyFeather.Respawn += FlyFeather_Respawn;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FlyFeather.Respawn -= FlyFeather_Respawn;
        On.Celeste.FlyFeather.OnPlayer -= FlyFeather_OnPlayer;
        On.Celeste.FlyFeather.ctor_EntityData_Vector2 -= FlyFeather_ctor;
        WatchEntityIDTable<FlyFeather>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (FlyFeather feather in level.Entities.OfType<FlyFeather>())
        {
            if (!WatchEntityIDTable<FlyFeather>.TryGet(feather, room, out int id))
                continue;

            WatchEntityPhase phase = feather.Collidable
                ? WatchEntityPhase.Ready
                : feather.singleUse ? WatchEntityPhase.Gone : WatchEntityPhase.Cooldown;
            yield return new WatchEntityState(new WatchEntityKey(Kind, id), [(byte)phase]);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityPhase>? phases = WatchPickupAdapterHelpers.ParsePhases(Kind, states);
        if (phases is null)
            return WatchEntityApplyResult.None;

        bool changed = false;
        bool requiresReload = false;
        string room = level.Session.Level;
        Dictionary<int, FlyFeather> entities = level.Entities.OfType<FlyFeather>()
            .Where(entity => WatchEntityIDTable<FlyFeather>.TryGet(entity, room, out _))
            .GroupBy(entity =>
            {
                WatchEntityIDTable<FlyFeather>.TryGet(entity, room, out int id);
                return id;
            })
            .ToDictionary(group => group.Key, group => group.Last());

        foreach ((int id, WatchEntityPhase phase) in phases)
        {
            if (!entities.TryGetValue(id, out FlyFeather? feather))
            {
                requiresReload |= phase != WatchEntityPhase.Gone;
                continue;
            }

            switch (phase)
            {
                case WatchEntityPhase.Ready:
                    if (!feather.Collidable)
                    {
                        feather.outline.Visible = false;
                        feather.Collidable = true;
                        feather.sprite.Visible = true;
                        feather.respawnTimer = 0f;
                        changed = true;
                    }
                    break;

                case WatchEntityPhase.Cooldown:
                    if (feather.Collidable || feather.sprite.Visible || feather.respawnTimer <= 0f)
                    {
                        HideFeather(feather, false);
                        changed = true;
                    }
                    break;

                case WatchEntityPhase.Gone:
                    if (feather.Collidable || feather.sprite.Visible || feather.outline.Visible)
                    {
                        HideFeather(feather, true);
                        changed = true;
                    }
                    break;
            }
        }

        return WatchPickupAdapterHelpers.Result(changed, requiresReload);
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        FlyFeather? feather = FindFeather(level, entityEvent.Key.EntityID);
        if (feather is null)
            return;

        ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
        switch (entityEvent.EventID)
        {
            case CollectEvent when payload.Length == 9:
                Vector2 speed = WatchEntityPayloadCodec.ReadVector2(payload, 0);
                if (!float.IsFinite(speed.X) || !float.IsFinite(speed.Y) || payload[8] > 1)
                    return;

                bool renewal = payload[8] != 0;
                Audio.Play(
                    feather.shielded
                        ? renewal
                            ? "event:/game/06_reflection/feather_bubble_renew"
                            : "event:/game/06_reflection/feather_bubble_get"
                        : renewal
                            ? "event:/game/06_reflection/feather_renew"
                            : "event:/game/06_reflection/feather_get",
                    feather.Position
                );
                level.Shake(0.3f);
                feather.Collidable = false;
                feather.sprite.Visible = false;
                level.ParticlesFG.Emit(
                    FlyFeather.P_Collect,
                    10,
                    feather.Position,
                    Vector2.One * 6f
                );
                SlashFx.Burst(
                    feather.Position,
                    speed == Vector2.Zero ? 0f : Calc.Angle(speed)
                );
                break;

            case ShieldBounceEvent when payload.Length == 8:
                Vector2 direction = WatchEntityPayloadCodec.ReadVector2(payload, 0);
                if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
                    return;

                feather.moveWiggleDir = direction;
                feather.moveWiggle.Start();
                feather.shieldRadiusWiggle.Start();
                Audio.Play("event:/game/06_reflection/feather_bubble_bounce", feather.Position);
                break;

            case RespawnEvent when payload.Length == 0:
                feather.outline.Visible = false;
                feather.Collidable = true;
                feather.sprite.Visible = true;
                feather.respawnTimer = 0f;
                feather.wiggler.Start();
                Audio.Play("event:/game/06_reflection/feather_reappear", feather.Position);
                level.ParticlesFG.Emit(
                    FlyFeather.P_Respawn,
                    16,
                    feather.Position,
                    Vector2.One * 2f
                );
                break;
        }
    }

    private static void HideFeather(FlyFeather feather, bool gone)
    {
        feather.Collidable = false;
        feather.sprite.Visible = false;
        feather.outline.Visible = !gone;
        feather.respawnTimer = gone ? float.MaxValue : FlyFeather.RespawnTime;
    }

    private static FlyFeather? FindFeather(Level level, int id)
    {
        string room = level.Session.Level;
        return WatchEntityIDTable<FlyFeather>.Find(level, room, id);
    }

    private static void FlyFeather_OnPlayer(
        On.Celeste.FlyFeather.orig_OnPlayer orig,
        FlyFeather self,
        Player player
    )
    {
        bool wasCollidable = self.Collidable;
        bool shieldBounce = self.shielded && !player.DashAttacking;
        bool renewal = player.StateMachine.State == Player.StStarFly;
        Vector2 speed = player.Speed;
        Vector2 direction = Calc.SafeNormalize(self.Center - player.Center, Vector2.UnitY);
        orig(self, player);

        if (!wasCollidable || WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;

        if (shieldBounce)
        {
            byte[] payload = new byte[8];
            WatchEntityPayloadCodec.WriteVector2(payload, 0, direction);
            PublishFeatherEvent(self, ShieldBounceEvent, payload);
        }
        else if (!self.Collidable)
        {
            byte[] payload = new byte[9];
            WatchEntityPayloadCodec.WriteVector2(payload, 0, speed);
            payload[8] = renewal ? (byte)1 : (byte)0;
            PublishFeatherEvent(self, CollectEvent, payload);
        }
    }

    private static void FlyFeather_Respawn(
        On.Celeste.FlyFeather.orig_Respawn orig,
        FlyFeather self
    )
    {
        bool wasCollidable = self.Collidable;
        orig(self);
        if (!wasCollidable && self.Collidable && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            PublishFeatherEvent(self, RespawnEvent, []);
    }

    private static void PublishFeatherEvent(FlyFeather self, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (self.Scene is not Level level
            || !WatchEntityIDTable<FlyFeather>.TryGet(self, level.Session.Level, out int id))
            return;

        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.FlyFeather, id), eventID, payload)
        );
    }

    private static void FlyFeather_ctor(
        On.Celeste.FlyFeather.orig_ctor_EntityData_Vector2 orig,
        FlyFeather self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FlyFeather>.Set(self, data.Level.Name, data.ID);
    }
}

internal sealed class WatchBoosterAdapter : IWatchEntityAdapter
{
    private sealed class BoosterInfo
    {
        public string Level { get; }
        public int ID { get; }
        public WatchEntityPhase Phase { get; set; }

        public BoosterInfo(string level, int id)
        {
            Level = level;
            ID = id;
            Phase = WatchEntityPhase.Ready;
        }
    }

    private static readonly WatchBoosterAdapter instance = new();
    private static readonly ConditionalWeakTable<Booster, BoosterInfo> infos = new();

    public WatchEntityKind Kind => WatchEntityKind.Booster;

    public static void Load()
    {
        On.Celeste.Booster.ctor_EntityData_Vector2 += Booster_ctor;
        On.Celeste.Booster.OnPlayer += Booster_OnPlayer;
        On.Celeste.Booster.PlayerBoosted += Booster_PlayerBoosted;
        On.Celeste.Booster.PlayerReleased += Booster_PlayerReleased;
        On.Celeste.Booster.PlayerDied += Booster_PlayerDied;
        On.Celeste.Booster.Respawn += Booster_Respawn;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Booster.Respawn -= Booster_Respawn;
        On.Celeste.Booster.PlayerDied -= Booster_PlayerDied;
        On.Celeste.Booster.PlayerReleased -= Booster_PlayerReleased;
        On.Celeste.Booster.PlayerBoosted -= Booster_PlayerBoosted;
        On.Celeste.Booster.OnPlayer -= Booster_OnPlayer;
        On.Celeste.Booster.ctor_EntityData_Vector2 -= Booster_ctor;
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Booster booster in level.Entities.OfType<Booster>())
        {
            if (!infos.TryGetValue(booster, out BoosterInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, room))
                continue;

            byte[] payload = new byte[16];
            payload[0] = (byte)info.Phase;
            WatchEntityPayloadCodec.WriteVector2(payload, 1, booster.sprite.RenderPosition);
            payload[9] = booster.sprite.Visible ? (byte)1 : (byte)0;
            payload[10] = booster.outline.Visible ? (byte)1 : (byte)0;
            payload[11] = booster.sprite.FlipX ? (byte)1 : (byte)0;
            WatchEntityPayloadCodec.WriteSingle(payload, 12, booster.respawnTimer);
            yield return new WatchEntityState(new WatchEntityKey(Kind, info.ID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityState> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || payload.Length != 16
                || payload[0] > (byte)WatchEntityPhase.Returning
                || payload[9] > 1
                || payload[10] > 1
                || payload[11] > 1
                || !float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 1))
                || !float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 5))
                || !float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 12))
                || !desiredByID.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (Booster booster in level.Entities.OfType<Booster>())
        {
            if (!infos.TryGetValue(booster, out BoosterInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, room)
                || !desiredByID.TryGetValue(info.ID, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase phase = (WatchEntityPhase)payload[0];
            Vector2 renderPosition = WatchEntityPayloadCodec.ReadVector2(payload, 1);
            bool spriteVisible = payload[9] != 0;
            bool outlineVisible = payload[10] != 0;
            bool flipX = payload[11] != 0;
            float respawnTimer = WatchEntityPayloadCodec.ReadSingle(payload, 12);
            bool phaseChanged = info.Phase != phase;
            bool differs = phaseChanged
                || booster.sprite.RenderPosition != renderPosition
                || booster.sprite.Visible != spriteVisible
                || booster.outline.Visible != outlineVisible
                || booster.sprite.FlipX != flipX
                || booster.respawnTimer != respawnTimer;
            if (!differs)
                continue;

            if (phaseChanged)
            {
                switch (phase)
                {
                    case WatchEntityPhase.Ready:
                        if (info.Phase == WatchEntityPhase.Cooldown)
                        {
                            booster.Respawn();
                            booster.AppearParticles();
                        }
                        else
                        {
                            booster.BoostingPlayer = false;
                            booster.sprite.Play("loop");
                            booster.wiggler.Start();
                        }
                        break;

                    case WatchEntityPhase.Active:
                        booster.BoostingPlayer = false;
                        booster.respawnTimer = 0f;
                        booster.sprite.Play("inside");
                        booster.wiggler.Start();
                        Audio.Play(
                            booster.red
                                ? "event:/game/05_mirror_temple/redbooster_enter"
                                : "event:/game/04_cliffside/greenbooster_enter",
                            booster.Position
                        );
                        break;

                    case WatchEntityPhase.Returning:
                        booster.BoostingPlayer = true;
                        booster.respawnTimer = 0f;
                        booster.sprite.Play("spin");
                        booster.wiggler.Start();
                        Audio.Play(
                            booster.red
                                ? "event:/game/05_mirror_temple/redbooster_dash"
                                : "event:/game/04_cliffside/greenbooster_dash",
                            booster.Position
                        );
                        if (booster.Scene is Level boosterLevel)
                        {
                            boosterLevel.Particles.Emit(
                                booster.red ? Booster.P_BurstRed : Booster.P_Burst,
                                12,
                                booster.Center,
                                Vector2.One * 3f
                            );
                        }
                        if (booster.red)
                        {
                            booster.loopingSfx.Play("event:/game/05_mirror_temple/redbooster_move");
                            booster.loopingSfx.DisposeOnTransition = false;
                        }
                        break;

                    case WatchEntityPhase.Cooldown:
                        booster.PlayerReleased();
                        break;
                }
            }

            info.Phase = phase;
            booster.BoostingPlayer = phase == WatchEntityPhase.Returning;
            booster.sprite.RenderPosition = renderPosition;
            booster.sprite.Visible = spriteVisible;
            booster.outline.Visible = outlineVisible;
            booster.sprite.FlipX = flipX;
            booster.respawnTimer = respawnTimer;
            booster.loopingSfx.Position = booster.sprite.Position;
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void Booster_ctor(
        On.Celeste.Booster.orig_ctor_EntityData_Vector2 orig,
        Booster self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new BoosterInfo(data.Level.Name, data.ID));
    }

    private static void Booster_OnPlayer(
        On.Celeste.Booster.orig_OnPlayer orig,
        Booster self,
        Player player
    )
    {
        orig(self, player);
        if (infos.TryGetValue(self, out BoosterInfo? info)
            && self.sprite.CurrentAnimationID == "inside")
            info.Phase = WatchEntityPhase.Active;
    }

    private static void Booster_PlayerBoosted(
        On.Celeste.Booster.orig_PlayerBoosted orig,
        Booster self,
        Player player,
        Vector2 direction
    )
    {
        orig(self, player, direction);
        if (infos.TryGetValue(self, out BoosterInfo? info))
            info.Phase = WatchEntityPhase.Returning;
    }

    private static void Booster_PlayerReleased(
        On.Celeste.Booster.orig_PlayerReleased orig,
        Booster self
    )
    {
        orig(self);
        if (infos.TryGetValue(self, out BoosterInfo? info))
            info.Phase = WatchEntityPhase.Cooldown;
    }

    private static void Booster_PlayerDied(
        On.Celeste.Booster.orig_PlayerDied orig,
        Booster self
    )
    {
        orig(self);
        if (infos.TryGetValue(self, out BoosterInfo? info))
            info.Phase = WatchEntityPhase.Cooldown;
    }

    private static void Booster_Respawn(
        On.Celeste.Booster.orig_Respawn orig,
        Booster self
    )
    {
        orig(self);
        if (infos.TryGetValue(self, out BoosterInfo? info))
            info.Phase = WatchEntityPhase.Ready;
    }
}

internal static class WatchPickupAdapterHelpers
{
    public static Dictionary<int, WatchEntityPhase>? ParsePhases(
        WatchEntityKind kind,
        IReadOnlyCollection<WatchEntityState> states
    )
    {
        Dictionary<int, WatchEntityPhase> phases = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] > (byte)WatchEntityPhase.Gone
                || !phases.TryAdd(state.Key.EntityID, (WatchEntityPhase)state.Payload.Span[0]))
                return null;
        }
        return phases;
    }

    public static WatchEntityApplyResult Result(bool changed, bool requiresReload)
    {
        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.SceneChanged | WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }
}
