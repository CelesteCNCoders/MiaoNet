using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchBumperAdapter : IWatchEntityAdapter
{
    private const byte HitEvent = 1;
    private static readonly WatchBumperAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.Bumper;

    public static void Load()
    {
        On.Celeste.Bumper.ctor_EntityData_Vector2 += Bumper_ctor;
        On.Celeste.Bumper.OnPlayer += Bumper_OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Bumper.OnPlayer -= Bumper_OnPlayer;
        On.Celeste.Bumper.ctor_EntityData_Vector2 -= Bumper_ctor;
        WatchEntityIDTable<Bumper>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Bumper bumper in level.Entities.OfType<Bumper>())
        {
            if (!WatchEntityIDTable<Bumper>.TryGet(bumper, room, out int id))
                continue;

            yield return new WatchEntityState(
                new WatchEntityKey(Kind, id),
                [bumper.respawnTimer <= 0f ? (byte)1 : (byte)0, bumper.fireMode ? (byte)1 : (byte)0]
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, (bool Ready, bool Fire)> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 2
                || state.Payload.Span[0] > 1
                || state.Payload.Span[1] > 1
                || !desiredByID.TryAdd(
                    state.Key.EntityID,
                    (state.Payload.Span[0] != 0, state.Payload.Span[1] != 0)
                ))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (Bumper bumper in level.Entities.OfType<Bumper>())
        {
            if (!WatchEntityIDTable<Bumper>.TryGet(bumper, room, out int id)
                || !desiredByID.TryGetValue(id, out var desired))
                continue;

            if (bumper.fireMode != desired.Fire)
            {
                bumper.OnChangeMode(desired.Fire ? Session.CoreModes.Hot : Session.CoreModes.Cold);
                changed = true;
            }

            bool ready = bumper.respawnTimer <= 0f;
            if (ready == desired.Ready)
                continue;

            if (desired.Ready)
            {
                bumper.respawnTimer = 0f;
                bumper.sprite.Play("on");
                bumper.spriteEvil.Play("on");
                bumper.light.Visible = true;
                bumper.bloom.Visible = true;
            }
            else
            {
                bumper.respawnTimer = Bumper.RespawnTime;
                bumper.light.Visible = false;
                bumper.bloom.Visible = false;
            }
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != HitEvent || entityEvent.Payload.Length != 16)
            return;

        Bumper? bumper = level.Entities.OfType<Bumper>().FirstOrDefault(candidate =>
            WatchEntityIDTable<Bumper>.TryGet(candidate, level.Session.Level, out int id)
            && id == entityEvent.Key.EntityID
        );
        if (bumper is null)
            return;

        ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
        bumper.hitDir = new Vector2(
            WatchEntityPayloadCodec.ReadSingle(payload, 0),
            WatchEntityPayloadCodec.ReadSingle(payload, 4)
        );
        bumper.Position = new Vector2(
            WatchEntityPayloadCodec.ReadSingle(payload, 8),
            WatchEntityPayloadCodec.ReadSingle(payload, 12)
        );
        bumper.respawnTimer = Bumper.RespawnTime;
        bumper.hitWiggler.Start();
        bumper.sprite.Play("hit", true);
        bumper.spriteEvil.Play("hit", true);
        bumper.light.Visible = false;
        bumper.bloom.Visible = false;
        Audio.Play(
            bumper.fireMode
                ? "event:/game/09_core/hotpinball_activate"
                : "event:/game/06_reflection/pinballbumper_hit",
            bumper.Position
        );
    }

    private static void Bumper_ctor(
        On.Celeste.Bumper.orig_ctor_EntityData_Vector2 orig,
        Bumper self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Bumper>.Set(self, data.Level.Name, data.ID);
    }

    private static void Bumper_OnPlayer(
        On.Celeste.Bumper.orig_OnPlayer orig,
        Bumper self,
        Player player
    )
    {
        bool wasReady = self.respawnTimer <= 0f;
        orig(self, player);
        if (!wasReady
            || self.respawnTimer <= 0f
            || WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level
            || !WatchEntityIDTable<Bumper>.TryGet(self, level.Session.Level, out int id))
            return;

        byte[] payload = new byte[16];
        WatchEntityPayloadCodec.WriteSingle(payload, 0, self.hitDir.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 4, self.hitDir.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, self.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, self.Position.Y);
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Bumper, id), HitEvent, payload)
        );
    }
}

internal sealed class WatchCloudAdapter : IWatchEntityAdapter
{
    private static readonly WatchCloudAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.Cloud;

    public static void Load()
    {
        On.Celeste.Cloud.ctor_EntityData_Vector2 += Cloud_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Cloud.ctor_EntityData_Vector2 -= Cloud_ctor;
        WatchEntityIDTable<Cloud>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Cloud cloud in level.Entities.OfType<Cloud>())
        {
            if (!WatchEntityIDTable<Cloud>.TryGet(cloud, room, out int id))
                continue;

            byte[] payload = new byte[10];
            payload[0] = (byte)GetPhase(cloud);
            WatchEntityPayloadCodec.WriteSingle(payload, 1, cloud.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(payload, 5, cloud.speed);
            payload[9] = cloud.Visible ? (byte)1 : (byte)0;
            yield return new WatchEntityState(new WatchEntityKey(Kind, id), payload);
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
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 10
                || state.Payload.Span[0] > (byte)WatchEntityPhase.Returning
                || state.Payload.Span[9] > 1
                || !desiredByID.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (Cloud cloud in level.Entities.OfType<Cloud>())
        {
            if (!WatchEntityIDTable<Cloud>.TryGet(cloud, room, out int id)
                || !desiredByID.TryGetValue(id, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase previous = GetPhase(cloud);
            WatchEntityPhase desired = (WatchEntityPhase)payload[0];
            float y = WatchEntityPayloadCodec.ReadSingle(payload, 1);
            float speed = WatchEntityPayloadCodec.ReadSingle(payload, 5);
            bool visible = payload[9] != 0;
            bool differs = previous != desired
                || cloud.Position.Y != y
                || cloud.speed != speed
                || cloud.Visible != visible;
            if (!differs)
                continue;

            cloud.Position = new Vector2(cloud.Position.X, y);
            cloud.speed = speed;
            cloud.Visible = visible;
            cloud.waiting = desired == WatchEntityPhase.Ready;
            cloud.returning = desired == WatchEntityPhase.Returning;
            cloud.respawnTimer = desired == WatchEntityPhase.Gone ? Math.Max(cloud.respawnTimer, 0.1f) : 0f;
            cloud.Collidable = desired is WatchEntityPhase.Ready or WatchEntityPhase.Active;

            if (previous == WatchEntityPhase.Ready && desired == WatchEntityPhase.Active)
            {
                Audio.Play(
                    cloud.fragile
                        ? "event:/game/04_cliffside/cloud_pink_boost"
                        : "event:/game/04_cliffside/cloud_blue_boost",
                    cloud.Position
                );
                cloud.wiggler.Start();
            }
            if (desired == WatchEntityPhase.Gone)
                cloud.sprite.Play("fade");
            else if (desired == WatchEntityPhase.Returning
                && previous != WatchEntityPhase.Returning
                && cloud.sprite.Has("spawn"))
                cloud.sprite.Play("spawn");

            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }

    private static WatchEntityPhase GetPhase(Cloud cloud)
    {
        if (!cloud.Visible || cloud.respawnTimer > 0f)
            return WatchEntityPhase.Gone;
        if (cloud.returning)
            return WatchEntityPhase.Returning;
        return cloud.waiting ? WatchEntityPhase.Ready : WatchEntityPhase.Active;
    }

    private static void Cloud_ctor(
        On.Celeste.Cloud.orig_ctor_EntityData_Vector2 orig,
        Cloud self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Cloud>.Set(self, data.Level.Name, data.ID);
    }
}
