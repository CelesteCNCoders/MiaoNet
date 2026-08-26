using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal static class WatchChapterEntityID
{
    public static bool TryAssignFromMap<T>(Level level, T entity, string mapName, out int id)
        where T : Entity
    {
        if (WatchEntityIDTable<T>.TryGet(entity, level.Session.Level, out id))
            return true;
        Vector2 roomOffset = new(level.Session.LevelData.Bounds.Left, level.Session.LevelData.Bounds.Top);
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(candidate =>
            candidate.Name == mapName
            && Vector2.DistanceSquared(candidate.Position + roomOffset, entity.Position) < 1f
        );
        if (data is null)
            return false;
        id = data.ID;
        WatchEntityIDTable<T>.Set(entity, level.Session.Level, id);
        return true;
    }
}

internal static class WatchChapterAnimation
{
    private static readonly string[] IDs =
    [
        "idle", "break", "broken", "appear", "open", "close", "on", "off",
        "noise", "loop", "lever", "pull", "activate", "activated", "fall"
    ];

    public static byte Encode(string? id)
    {
        for (int i = 0; i < IDs.Length; i++)
            if (IDs[i] == id)
                return (byte)i;
        return byte.MaxValue;
    }

    public static void Apply(Sprite? sprite, byte animation, byte frame)
    {
        if (sprite is null || animation >= IDs.Length || !sprite.Has(IDs[animation]))
            return;
        string id = IDs[animation];
        if (sprite.CurrentAnimationID != id)
            sprite.Play(id, restart: true);
        if (sprite.CurrentAnimationTotalFrames > 0)
            sprite.SetAnimationFrame(Math.Min(frame, sprite.CurrentAnimationTotalFrames - 1));
    }
}

internal sealed class WatchDreamMirrorAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private static readonly WatchDreamMirrorAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.DreamMirror;

    public static void Load()
    {
        On.Celeste.DreamMirror.Update += Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.DreamMirror.Update -= Update;
        WatchEntityIDTable<DreamMirror>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (DreamMirror mirror in WatchRoomEntityIndex.Enumerate<DreamMirror>(level))
        {
            if (!WatchChapterEntityID.TryAssignFromMap(level, mirror, "dreammirror", out int id))
                continue;
            byte flags = 0;
            if (mirror.Visible) flags |= 1;
            if (mirror.smashed) flags |= 2;
            if (mirror.smashEnded) flags |= 4;
            if (mirror.updateShine) flags |= 8;
            if (mirror.autoUpdateReflection) flags |= 16;
            if (mirror.breakingGlass?.Visible == true) flags |= 32;
            if (mirror.reflection?.Visible == true) flags |= 64;
            var current = (
                Flags: flags,
                Animation: WatchChapterAnimation.Encode(mirror.breakingGlass?.CurrentAnimationID),
                AnimationFrame: (byte)Math.Max(0, mirror.breakingGlass?.CurrentAnimationFrame ?? 0),
                ShineAlpha: mirror.shineAlpha,
                ReflectionAlpha: mirror.reflectionAlpha,
                mirror.Position,
                Rate: mirror.breakingGlass?.Rate ?? 0f
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.Animation;
                    payload[2] = state.AnimationFrame;
                    WatchEntityPayloadCodec.WriteSingle(payload, 4, state.ShineAlpha);
                    WatchEntityPayloadCodec.WriteSingle(payload, 8, state.ReflectionAlpha);
                    WatchEntityPayloadCodec.WriteVector2(payload, 12, state.Position);
                    WatchEntityPayloadCodec.WriteSingle(payload, 20, state.Rate);
                }
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize)
                continue;
            DreamMirror? mirror = WatchRoomEntityIndex.Enumerate<DreamMirror>(level).FirstOrDefault(candidate =>
                WatchChapterEntityID.TryAssignFromMap(level, candidate, "dreammirror", out int id)
                && id == state.Key.EntityID);
            if (mirror is null)
                continue;
            mirror.Visible = (p[0] & 1) != 0;
            mirror.smashed = (p[0] & 2) != 0;
            mirror.smashEnded = (p[0] & 4) != 0;
            mirror.updateShine = (p[0] & 8) != 0;
            mirror.autoUpdateReflection = false;
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Visible = (p[0] & 32) != 0;
            if (mirror.reflection is not null)
                mirror.reflection.Visible = (p[0] & 64) != 0;
            mirror.shineAlpha = WatchEntityPayloadCodec.ReadSingle(p, 4);
            mirror.reflectionAlpha = WatchEntityPayloadCodec.ReadSingle(p, 8);
            mirror.Position = WatchEntityPayloadCodec.ReadVector2(p, 12);
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Rate = WatchEntityPayloadCodec.ReadSingle(p, 20);
            WatchChapterAnimation.Apply(mirror.breakingGlass, p[1], p[2]);
            mirror.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void Update(On.Celeste.DreamMirror.orig_Update orig, DreamMirror self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        foreach (Coroutine routine in self.Components.GetAll<Coroutine>())
            routine.Active = false;
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            self.Components.Update();
        self.Collidable = false;
    }
}

internal sealed class WatchResortMirrorAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private static readonly WatchResortMirrorAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.ResortMirror;

    public static void Load()
    {
        On.Celeste.ResortMirror.ctor += Ctor;
        WatchEntitySyncRegistry.Register(instance);
    }
    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ResortMirror.ctor -= Ctor;
        WatchEntityIDTable<ResortMirror>.Clear();
    }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (ResortMirror mirror in WatchRoomEntityIndex.Enumerate<ResortMirror>(level))
        {
            if (!WatchEntityIDTable<ResortMirror>.TryGet(mirror, level.Session.Level, out int id)) continue;
            byte flags = 0;
            if (mirror.Visible) flags |= 1;
            if (mirror.smashed) flags |= 2;
            if (mirror.shardReflection) flags |= 4;
            if (mirror.breakingGlass?.Visible == true) flags |= 8;
            if (mirror.evil?.Visible == true) flags |= 16;
            var current = (
                Flags: flags,
                Animation: WatchChapterAnimation.Encode(mirror.breakingGlass?.CurrentAnimationID),
                AnimationFrame: (byte)Math.Max(0, mirror.breakingGlass?.CurrentAnimationFrame ?? 0),
                ShineAlpha: mirror.shineAlpha,
                MirrorAlpha: mirror.mirrorAlpha,
                mirror.Position,
                Rate: mirror.breakingGlass?.Rate ?? 0f
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.Animation;
                    payload[2] = state.AnimationFrame;
                    WatchEntityPayloadCodec.WriteSingle(payload, 4, state.ShineAlpha);
                    WatchEntityPayloadCodec.WriteSingle(payload, 8, state.MirrorAlpha);
                    WatchEntityPayloadCodec.WriteVector2(payload, 12, state.Position);
                    WatchEntityPayloadCodec.WriteSingle(payload, 20, state.Rate);
                }
            );
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            ResortMirror? mirror = WatchEntityIDTable<ResortMirror>.Find(level, state.Key.EntityID);
            if (mirror is null) continue;
            mirror.Visible = (p[0] & 1) != 0;
            mirror.smashed = (p[0] & 2) != 0;
            mirror.shardReflection = (p[0] & 4) != 0;
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Visible = (p[0] & 8) != 0;
            if (mirror.evil is not null) mirror.evil.Visible = (p[0] & 16) != 0;
            mirror.shineAlpha = WatchEntityPayloadCodec.ReadSingle(p, 4);
            mirror.mirrorAlpha = WatchEntityPayloadCodec.ReadSingle(p, 8);
            mirror.Position = WatchEntityPayloadCodec.ReadVector2(p, 12);
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Rate = WatchEntityPayloadCodec.ReadSingle(p, 20);
            WatchChapterAnimation.Apply(mirror.breakingGlass, p[1], p[2]);
            mirror.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    private static void Ctor(On.Celeste.ResortMirror.orig_ctor orig, ResortMirror self, EntityData data, Vector2 offset)
    { orig(self, data, offset); WatchEntityIDTable<ResortMirror>.Set(self, data.Level.Name, data.ID); }
}

internal sealed class WatchTempleMirrorPortalAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 28;
    private static readonly WatchTempleMirrorPortalAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.TempleMirrorPortal;
    public static void Load()
    {
        On.Celeste.TempleMirrorPortal.ctor_EntityData_Vector2 += Ctor;
        On.Celeste.TempleMirrorPortal.OnPlayer += OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }
    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.TempleMirrorPortal.OnPlayer -= OnPlayer;
        On.Celeste.TempleMirrorPortal.ctor_EntityData_Vector2 -= Ctor;
        WatchEntityIDTable<TempleMirrorPortal>.Clear();
    }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (TempleMirrorPortal portal in WatchRoomEntityIndex.Enumerate<TempleMirrorPortal>(level))
        {
            if (!WatchEntityIDTable<TempleMirrorPortal>.TryGet(portal, level.Session.Level, out int id)) continue;
            byte flags = 0;
            if (portal.Visible) flags |= 1;
            if (portal.canTrigger) flags |= 2;
            if (portal.curtain?.Visible == true) flags |= 4;
            if (portal.leftTorch?.Visible == true) flags |= 8;
            if (portal.rightTorch?.Visible == true) flags |= 16;
            byte extraFlags = 0;
            if (portal.buffer is not null || portal.bufferAlpha > 0f) extraFlags |= 1;
            if (portal.curtain?.Sprite.CurrentAnimationID == "fall") extraFlags |= 2;
            if (portal.leftTorch?.light is not null) extraFlags |= 4;
            if (portal.rightTorch?.light is not null) extraFlags |= 8;
            var current = (
                Flags: flags,
                Animation: portal.curtain is null
                    ? (byte)0
                    : WatchChapterAnimation.Encode(portal.curtain.Sprite.CurrentAnimationID),
                AnimationFrame: (byte)Math.Max(0, portal.curtain?.Sprite.CurrentAnimationFrame ?? 0),
                ExtraFlags: extraFlags,
                SwitchCounter: portal.switchCounter,
                portal.DistortionFade,
                portal.bufferAlpha,
                portal.bufferTimer,
                portal.Position
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.Animation;
                    payload[2] = state.AnimationFrame;
                    payload[3] = state.ExtraFlags;
                    WatchEntityPayloadCodec.WriteInt32(payload, 4, state.SwitchCounter);
                    WatchEntityPayloadCodec.WriteSingle(payload, 8, state.DistortionFade);
                    WatchEntityPayloadCodec.WriteSingle(payload, 12, state.bufferAlpha);
                    WatchEntityPayloadCodec.WriteSingle(payload, 16, state.bufferTimer);
                    WatchEntityPayloadCodec.WriteVector2(payload, 20, state.Position);
                }
            );
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            TempleMirrorPortal? portal = WatchEntityIDTable<TempleMirrorPortal>.Find(level, state.Key.EntityID)
                ?? Recreate(level, state.Key.EntityID);
            if (portal is null) continue;
            bool presentationActive = (p[3] & 1) != 0;
            if (presentationActive)
                EnsurePresentation(portal);
            portal.Visible = (p[0] & 1) != 0;
            portal.canTrigger = false;
            portal.switchCounter = WatchEntityPayloadCodec.ReadInt32(p, 4);
            portal.DistortionFade = WatchEntityPayloadCodec.ReadSingle(p, 8);
            portal.bufferAlpha = WatchEntityPayloadCodec.ReadSingle(p, 12);
            portal.bufferTimer = WatchEntityPayloadCodec.ReadSingle(p, 16);
            portal.Position = WatchEntityPayloadCodec.ReadVector2(p, 20);
            if (portal.curtain is not null)
            {
                portal.curtain.Visible = (p[0] & 4) != 0;
                portal.curtain.Collidable = false;
                if ((p[3] & 2) != 0)
                    portal.curtain.Depth = -8999;
                WatchChapterAnimation.Apply(portal.curtain.Sprite, p[1], p[2]);
            }
            if (portal.leftTorch is not null)
            {
                portal.leftTorch.Visible = (p[0] & 8) != 0;
                if ((p[3] & 4) != 0 && portal.leftTorch.light is null)
                    portal.leftTorch.Light(0);
            }
            if (portal.rightTorch is not null)
            {
                portal.rightTorch.Visible = (p[0] & 16) != 0;
                if ((p[3] & 8) != 0 && portal.rightTorch.light is null)
                    portal.rightTorch.Light(1);
            }
            portal.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    private static void Ctor(On.Celeste.TempleMirrorPortal.orig_ctor_EntityData_Vector2 orig, TempleMirrorPortal self, EntityData data, Vector2 offset)
    { orig(self, data, offset); WatchEntityIDTable<TempleMirrorPortal>.Set(self, data.Level.Name, data.ID); }
    private static TempleMirrorPortal? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "templeMirrorPortal");
        if (data is null) return null;
        TempleMirrorPortal portal = new(data, level.LevelOffset);
        WatchEntityIDTable<TempleMirrorPortal>.Set(portal, level.Session.Level, id);
        level.Add(portal);
        return portal;
    }
    private static void EnsurePresentation(TempleMirrorPortal portal)
    {
        if (portal.Get<BeforeRenderHook>() is null)
            portal.Add(new BeforeRenderHook(portal.BeforeRender));
        if (portal.Get<DisplacementRenderHook>() is null)
            portal.Add(new DisplacementRenderHook(portal.RenderDisplacement));
    }

    private static void OnPlayer(On.Celeste.TempleMirrorPortal.orig_OnPlayer orig, TempleMirrorPortal self, Player player)
    { if (!MiaoNetModule.IsWatching) orig(self, player); }
}

internal sealed class WatchGondolaAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 40;
    private static readonly WatchGondolaAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.Gondola;
    public static void Load()
    {
        On.Celeste.Gondola.ctor += Ctor;
        On.Celeste.Gondola.Update += Update;
        WatchEntitySyncRegistry.Register(instance);
    }
    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Gondola.Update -= Update;
        On.Celeste.Gondola.ctor -= Ctor;
        WatchEntityIDTable<Gondola>.Clear();
    }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (Gondola gondola in WatchRoomEntityIndex.Enumerate<Gondola>(level))
        {
            if (!WatchEntityIDTable<Gondola>.TryGet(gondola, level.Session.Level, out int id)) continue;
            byte flags = 0;
            if (gondola.Visible) flags |= 1;
            if (gondola.Collidable) flags |= 2;
            if (gondola.brokenLever) flags |= 4;
            if (gondola.inCliffside) flags |= 8;
            if (gondola.front.Visible) flags |= 16;
            if (gondola.Lever.Visible) flags |= 32;
            var current = (
                Flags: flags,
                FrontAnimation: WatchChapterAnimation.Encode(gondola.front.CurrentAnimationID),
                FrontFrame: (byte)Math.Max(0, gondola.front.CurrentAnimationFrame),
                LeverAnimation: WatchChapterAnimation.Encode(gondola.Lever.CurrentAnimationID),
                LeverFrame: (byte)Math.Max(0, gondola.Lever.CurrentAnimationFrame),
                gondola.Position,
                gondola.Rotation,
                gondola.Speed,
                gondola.RotationSpeed,
                FrontRate: gondola.front.Rate,
                LeverRate: gondola.Lever.Rate
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.FrontAnimation;
                    payload[2] = state.FrontFrame;
                    payload[3] = state.LeverAnimation;
                    payload[4] = state.LeverFrame;
                    WatchEntityPayloadCodec.WriteVector2(payload, 8, state.Position);
                    WatchEntityPayloadCodec.WriteSingle(payload, 16, state.Rotation);
                    WatchEntityPayloadCodec.WriteVector2(payload, 20, state.Speed);
                    WatchEntityPayloadCodec.WriteSingle(payload, 28, state.RotationSpeed);
                    WatchEntityPayloadCodec.WriteSingle(payload, 32, state.FrontRate);
                    WatchEntityPayloadCodec.WriteSingle(payload, 36, state.LeverRate);
                }
            );
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            Gondola? gondola = WatchEntityIDTable<Gondola>.Find(level, state.Key.EntityID);
            if (gondola is null) continue;
            gondola.Visible = (p[0] & 1) != 0;
            gondola.Collidable = false;
            gondola.brokenLever = (p[0] & 4) != 0;
            gondola.inCliffside = (p[0] & 8) != 0;
            gondola.front.Visible = (p[0] & 16) != 0;
            gondola.Lever.Visible = (p[0] & 32) != 0;
            gondola.Position = WatchEntityPayloadCodec.ReadVector2(p, 8);
            gondola.Rotation = WatchEntityPayloadCodec.ReadSingle(p, 16);
            gondola.Speed = WatchEntityPayloadCodec.ReadVector2(p, 20);
            gondola.RotationSpeed = WatchEntityPayloadCodec.ReadSingle(p, 28);
            gondola.front.Rate = WatchEntityPayloadCodec.ReadSingle(p, 32);
            gondola.Lever.Rate = WatchEntityPayloadCodec.ReadSingle(p, 36);
            WatchChapterAnimation.Apply(gondola.front, p[1], p[2]);
            WatchChapterAnimation.Apply(gondola.Lever, p[3], p[4]);
            gondola.UpdatePositions();
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    private static void Ctor(On.Celeste.Gondola.orig_ctor orig, Gondola self, EntityData data, Vector2 offset)
    { orig(self, data, offset); WatchEntityIDTable<Gondola>.Set(self, data.Level.Name, data.ID); }
    private static void Update(On.Celeste.Gondola.orig_Update orig, Gondola self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        foreach (Coroutine routine in self.Components.GetAll<Coroutine>()) routine.Active = false;
        if (!MiaoNetModule.IsWatchedPlayerPaused) self.Components.Update();
        self.Collidable = false;
        self.UpdatePositions();
    }
}

internal sealed class WatchWaveDashTutorialAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 36;
    private static readonly WatchWaveDashTutorialAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.WaveDashTutorial;
    public static void Load()
    {
        On.Celeste.WaveDashTutorialMachine.ctor_EntityData_Vector2 += Ctor;
        On.Celeste.WaveDashTutorialMachine.Update += Update;
        On.Celeste.WaveDashPresentation.Update += PresentationUpdate;
        WatchEntitySyncRegistry.Register(instance);
    }
    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.WaveDashPresentation.Update -= PresentationUpdate;
        On.Celeste.WaveDashTutorialMachine.Update -= Update;
        On.Celeste.WaveDashTutorialMachine.ctor_EntityData_Vector2 -= Ctor;
        WatchEntityIDTable<WaveDashTutorialMachine>.Clear();
    }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (WaveDashTutorialMachine machine in WatchRoomEntityIndex.Enumerate<WaveDashTutorialMachine>(level))
        {
            if (!WatchEntityIDTable<WaveDashTutorialMachine>.TryGet(machine, level.Session.Level, out int id)) continue;
            byte flags = 0;
            if (machine.Visible) flags |= 1;
            if (machine.playerInside) flags |= 2;
            if (machine.inCutscene) flags |= 4;
            if (machine.frontEntity?.Visible == true) flags |= 8;
            if (machine.presentation?.Viewing == true) flags |= 16;
            var current = (
                Flags: flags,
                NoiseAnimation: WatchChapterAnimation.Encode(machine.noise.CurrentAnimationID),
                NoiseFrame: (byte)Math.Max(0, machine.noise.CurrentAnimationFrame),
                NeonAnimation: WatchChapterAnimation.Encode(machine.neon.CurrentAnimationID),
                NeonFrame: (byte)Math.Max(0, machine.neon.CurrentAnimationFrame),
                machine.insideEase,
                machine.cameraEase,
                machine.Position,
                PresentationEase: machine.presentation?.ease ?? 0f,
                PageIndex: machine.presentation?.pageIndex ?? 0,
                PageEase: machine.presentation?.pageEase ?? 0f
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.NoiseAnimation;
                    payload[2] = state.NoiseFrame;
                    payload[3] = state.NeonAnimation;
                    payload[4] = state.NeonFrame;
                    WatchEntityPayloadCodec.WriteSingle(payload, 8, state.insideEase);
                    WatchEntityPayloadCodec.WriteSingle(payload, 12, state.cameraEase);
                    WatchEntityPayloadCodec.WriteVector2(payload, 16, state.Position);
                    WatchEntityPayloadCodec.WriteSingle(payload, 24, state.PresentationEase);
                    WatchEntityPayloadCodec.WriteInt32(payload, 28, state.PageIndex);
                    WatchEntityPayloadCodec.WriteSingle(payload, 32, state.PageEase);
                }
            );
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            WaveDashTutorialMachine? machine = WatchEntityIDTable<WaveDashTutorialMachine>.Find(level, state.Key.EntityID);
            if (machine is null) continue;
            machine.Visible = (p[0] & 1) != 0;
            machine.playerInside = (p[0] & 2) != 0;
            machine.inCutscene = false;
            machine.insideEase = WatchEntityPayloadCodec.ReadSingle(p, 8);
            machine.cameraEase = 0f;
            machine.Position = WatchEntityPayloadCodec.ReadVector2(p, 16);
            if (machine.frontEntity is not null) machine.frontEntity.Visible = (p[0] & 8) != 0;
            if (machine.presentation is not null)
            {
                machine.presentation.Viewing = (p[0] & 16) != 0;
                machine.presentation.ease = WatchEntityPayloadCodec.ReadSingle(p, 24);
                int pageIndex = WatchEntityPayloadCodec.ReadInt32(p, 28);
                machine.presentation.pageIndex = machine.presentation.pages.Count == 0
                    ? 0 : Math.Clamp(pageIndex, 0, machine.presentation.pages.Count - 1);
                machine.presentation.pageEase = WatchEntityPayloadCodec.ReadSingle(p, 32);
            }
            WatchChapterAnimation.Apply(machine.noise, p[1], p[2]);
            WatchChapterAnimation.Apply(machine.neon, p[3], p[4]);
            machine.Collidable = false;
            if (machine.talk is not null) machine.talk.Enabled = false;
            if (machine.frontWall is not null) machine.frontWall.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    private static void Ctor(On.Celeste.WaveDashTutorialMachine.orig_ctor_EntityData_Vector2 orig, WaveDashTutorialMachine self, EntityData data, Vector2 offset)
    { orig(self, data, offset); WatchEntityIDTable<WaveDashTutorialMachine>.Set(self, data.Level.Name, data.ID); }
    private static void Update(On.Celeste.WaveDashTutorialMachine.orig_Update orig, WaveDashTutorialMachine self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        foreach (Coroutine routine in self.Components.GetAll<Coroutine>()) routine.Active = false;
        if (!MiaoNetModule.IsWatchedPlayerPaused) self.Components.Update();
        self.Collidable = false;
        if (self.talk is not null) self.talk.Enabled = false;
        if (self.frontWall is not null) self.frontWall.Collidable = false;
    }
    private static void PresentationUpdate(On.Celeste.WaveDashPresentation.orig_Update orig, WaveDashPresentation self)
    {
        if (!MiaoNetModule.IsWatching) orig(self);
        else if (!MiaoNetModule.IsWatchedPlayerPaused) self.Components.Update();
    }
}

internal sealed class WatchPowerSourceNumberAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 20;
    private static readonly WatchPowerSourceNumberAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.PowerSourceNumber;
    public static void Load()
    {
        On.Celeste.PowerSourceNumber.Update += Update;
        WatchEntitySyncRegistry.Register(instance);
    }
    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.PowerSourceNumber.Update -= Update;
        WatchEntityIDTable<PowerSourceNumber>.Clear();
    }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (PowerSourceNumber number in WatchRoomEntityIndex.Enumerate<PowerSourceNumber>(level))
        {
            if (!WatchChapterEntityID.TryAssignFromMap(level, number, "powerSourceNumber", out int id)) continue;
            byte flags = 0;
            if (number.Visible) flags |= 1;
            if (number.gotKey) flags |= 2;
            if (number.image.Visible) flags |= 4;
            if (number.glow.Visible) flags |= 8;
            var current = (Flags: flags, number.ease, number.timer, number.Position);
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    WatchEntityPayloadCodec.WriteSingle(payload, 4, state.ease);
                    WatchEntityPayloadCodec.WriteSingle(payload, 8, state.timer);
                    WatchEntityPayloadCodec.WriteVector2(payload, 12, state.Position);
                }
            );
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            PowerSourceNumber? number = WatchRoomEntityIndex.Enumerate<PowerSourceNumber>(level).FirstOrDefault(candidate => WatchChapterEntityID.TryAssignFromMap(level, candidate, "powerSourceNumber", out int id) && id == state.Key.EntityID);
            if (number is null) continue;
            number.Visible = (p[0] & 1) != 0;
            number.gotKey = (p[0] & 2) != 0;
            number.image.Visible = (p[0] & 4) != 0;
            number.glow.Visible = (p[0] & 8) != 0;
            number.ease = WatchEntityPayloadCodec.ReadSingle(p, 4);
            number.timer = WatchEntityPayloadCodec.ReadSingle(p, 8);
            number.Position = WatchEntityPayloadCodec.ReadVector2(p, 12);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    private static void Update(On.Celeste.PowerSourceNumber.orig_Update orig, PowerSourceNumber self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        if (!MiaoNetModule.IsWatchedPlayerPaused) self.Components.Update();
    }
}
