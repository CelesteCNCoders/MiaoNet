using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchFakeHeartAdapter : IWatchEntityAdapter
{
    private const byte CollectEvent = 1;
    private const byte BounceEvent = 2;
    private const byte RespawnEvent = 3;

    private static readonly WatchFakeHeartAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.FakeHeart;

    public static void Load()
    {
        On.Celeste.FakeHeart.ctor_EntityData_Vector2 += FakeHeart_ctor;
        On.Celeste.FakeHeart.OnPlayer += FakeHeart_OnPlayer;
        On.Celeste.FakeHeart.Collect += FakeHeart_Collect;
        On.Celeste.FakeHeart.Update += FakeHeart_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FakeHeart.Update -= FakeHeart_Update;
        On.Celeste.FakeHeart.Collect -= FakeHeart_Collect;
        On.Celeste.FakeHeart.OnPlayer -= FakeHeart_OnPlayer;
        On.Celeste.FakeHeart.ctor_EntityData_Vector2 -= FakeHeart_ctor;
        WatchEntityIDTable<FakeHeart>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (FakeHeart heart in WatchRoomEntityIndex.Enumerate<FakeHeart>(level))
        {
            if (!WatchEntityIDTable<FakeHeart>.TryGet(heart, room, out int id))
                continue;

            WatchEntityPhase phase = heart.Visible && heart.Collidable
                ? WatchEntityPhase.Ready
                : WatchEntityPhase.Cooldown;
            yield return WatchEntityState.FromTyped(
                new(Kind, id),
                (byte)phase,
                static value => [value]
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityPhase> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] > (byte)WatchEntityPhase.Cooldown
                || !desiredByID.TryAdd(
                    state.Key.EntityID,
                    (WatchEntityPhase)state.Payload.Span[0]
                ))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (FakeHeart heart in WatchRoomEntityIndex.Enumerate<FakeHeart>(level))
        {
            if (!WatchEntityIDTable<FakeHeart>.TryGet(heart, room, out int id)
                || !desiredByID.TryGetValue(id, out WatchEntityPhase phase))
                continue;

            bool ready = phase == WatchEntityPhase.Ready;
            if (heart.Visible == ready && heart.Collidable == ready)
                continue;

            heart.Visible = ready;
            heart.Collidable = ready;
            heart.respawnTimer = ready ? 0f : FakeHeart.RespawnTime;
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        FakeHeart? heart = FindHeart(level, entityEvent.Key.EntityID);
        if (heart is null)
            return;

        ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
        switch (entityEvent.EventID)
        {
            case CollectEvent when payload.Length == 4:
                float angle = WatchEntityPayloadCodec.ReadSingle(payload, 0);
                if (!float.IsFinite(angle))
                    return;

                heart.Visible = false;
                heart.Collidable = false;
                heart.respawnTimer = FakeHeart.RespawnTime;
                Celeste.Freeze(0.05f);
                level.Shake(0.3f);
                SlashFx.Burst(heart.Position, angle);
                break;

            case BounceEvent when payload.Length == 8:
                Vector2 direction = WatchEntityPayloadCodec.ReadVector2(payload, 0);
                if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
                    return;

                heart.moveWiggleDir = direction;
                heart.moveWiggler.Start();
                heart.ScaleWiggler.Start();
                Audio.Play("event:/game/general/crystalheart_bounce", heart.Position);
                break;

            case RespawnEvent when payload.Length == 0:
                heart.Visible = true;
                heart.Collidable = true;
                heart.respawnTimer = 0f;
                heart.ScaleWiggler.Start();
                break;
        }
    }

    private static FakeHeart? FindHeart(Level level, int id)
    {
        string room = level.Session.Level;
        return WatchEntityIDTable<FakeHeart>.Find(level, room, id);
    }

    private static void FakeHeart_ctor(
        On.Celeste.FakeHeart.orig_ctor_EntityData_Vector2 orig,
        FakeHeart self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FakeHeart>.Set(self, data.Level.Name, data.ID);
    }

    private static void FakeHeart_OnPlayer(
        On.Celeste.FakeHeart.orig_OnPlayer orig,
        FakeHeart self,
        Player player
    )
    {
        bool bounced = self.Visible
            && self.Scene is Level { Frozen: false }
            && !player.DashAttacking;
        Vector2 direction = Calc.SafeNormalize(self.Center - player.Center, Vector2.UnitY);
        orig(self, player);

        if (!bounced || WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;

        byte[] payload = new byte[8];
        WatchEntityPayloadCodec.WriteVector2(payload, 0, direction);
        PublishEvent(self, BounceEvent, payload);
    }

    private static void FakeHeart_Collect(
        On.Celeste.FakeHeart.orig_Collect orig,
        FakeHeart self,
        Player player,
        float angle
    )
    {
        bool wasVisible = self.Visible;
        orig(self, player, angle);
        if (!wasVisible || self.Visible || WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;

        byte[] payload = new byte[4];
        WatchEntityPayloadCodec.WriteSingle(payload, 0, angle);
        PublishEvent(self, CollectEvent, payload);
    }

    private static void FakeHeart_Update(
        On.Celeste.FakeHeart.orig_Update orig,
        FakeHeart self
    )
    {
        bool wasVisible = self.Visible;
        orig(self);
        if (!wasVisible && self.Visible && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            PublishEvent(self, RespawnEvent, []);
    }

    private static void PublishEvent(FakeHeart self, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (self.Scene is not Level level
            || !WatchEntityIDTable<FakeHeart>.TryGet(self, level.Session.Level, out int id))
            return;

        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.FakeHeart, id), eventID, payload)
        );
    }
}
