using MiaoNet.Shared;
using System.Buffers.Binary;

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
        foreach (DreamMirror mirror in level.Entities.OfType<DreamMirror>())
        {
            if (!WatchChapterEntityID.TryAssignFromMap(level, mirror, "dreammirror", out int id))
                continue;
            byte[] p = new byte[PayloadSize];
            if (mirror.Visible) p[0] |= 1;
            if (mirror.smashed) p[0] |= 2;
            if (mirror.smashEnded) p[0] |= 4;
            if (mirror.updateShine) p[0] |= 8;
            if (mirror.autoUpdateReflection) p[0] |= 16;
            if (mirror.breakingGlass?.Visible == true) p[0] |= 32;
            if (mirror.reflection?.Visible == true) p[0] |= 64;
            p[1] = WatchChapterAnimation.Encode(mirror.breakingGlass?.CurrentAnimationID);
            p[2] = (byte)Math.Max(0, mirror.breakingGlass?.CurrentAnimationFrame ?? 0);
            WatchEntityPayloadCodec.WriteSingle(p, 4, mirror.shineAlpha);
            WatchEntityPayloadCodec.WriteSingle(p, 8, mirror.reflectionAlpha);
            WatchEntityPayloadCodec.WriteSingle(p, 12, mirror.Position.X);
            WatchEntityPayloadCodec.WriteSingle(p, 16, mirror.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 20, mirror.breakingGlass?.Rate ?? 0f);
            yield return new(new(Kind, id), p);
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
            DreamMirror? mirror = level.Entities.OfType<DreamMirror>().FirstOrDefault(candidate =>
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
            mirror.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 12), WatchEntityPayloadCodec.ReadSingle(p, 16));
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Rate = WatchEntityPayloadCodec.ReadSingle(p, 20);
            WatchChapterAnimation.Apply(mirror.breakingGlass, p[1], p[2]);
            mirror.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }

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
        foreach (ResortMirror mirror in level.Entities.OfType<ResortMirror>())
        {
            if (!WatchEntityIDTable<ResortMirror>.TryGet(mirror, level.Session.Level, out int id)) continue;
            byte[] p = new byte[PayloadSize];
            if (mirror.Visible) p[0] |= 1;
            if (mirror.smashed) p[0] |= 2;
            if (mirror.shardReflection) p[0] |= 4;
            if (mirror.breakingGlass?.Visible == true) p[0] |= 8;
            if (mirror.evil?.Visible == true) p[0] |= 16;
            p[1] = WatchChapterAnimation.Encode(mirror.breakingGlass?.CurrentAnimationID);
            p[2] = (byte)Math.Max(0, mirror.breakingGlass?.CurrentAnimationFrame ?? 0);
            WatchEntityPayloadCodec.WriteSingle(p, 4, mirror.shineAlpha);
            WatchEntityPayloadCodec.WriteSingle(p, 8, mirror.mirrorAlpha);
            WatchEntityPayloadCodec.WriteSingle(p, 12, mirror.Position.X);
            WatchEntityPayloadCodec.WriteSingle(p, 16, mirror.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 20, mirror.breakingGlass?.Rate ?? 0f);
            yield return new(new(Kind, id), p);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            ResortMirror? mirror = level.Entities.OfType<ResortMirror>().FirstOrDefault(candidate => WatchEntityIDTable<ResortMirror>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
            if (mirror is null) continue;
            mirror.Visible = (p[0] & 1) != 0;
            mirror.smashed = (p[0] & 2) != 0;
            mirror.shardReflection = (p[0] & 4) != 0;
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Visible = (p[0] & 8) != 0;
            if (mirror.evil is not null) mirror.evil.Visible = (p[0] & 16) != 0;
            mirror.shineAlpha = WatchEntityPayloadCodec.ReadSingle(p, 4);
            mirror.mirrorAlpha = WatchEntityPayloadCodec.ReadSingle(p, 8);
            mirror.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 12), WatchEntityPayloadCodec.ReadSingle(p, 16));
            if (mirror.breakingGlass is not null)
                mirror.breakingGlass.Rate = WatchEntityPayloadCodec.ReadSingle(p, 20);
            WatchChapterAnimation.Apply(mirror.breakingGlass, p[1], p[2]);
            mirror.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
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
        foreach (TempleMirrorPortal portal in level.Entities.OfType<TempleMirrorPortal>())
        {
            if (!WatchEntityIDTable<TempleMirrorPortal>.TryGet(portal, level.Session.Level, out int id)) continue;
            byte[] p = new byte[PayloadSize];
            if (portal.Visible) p[0] |= 1;
            if (portal.canTrigger) p[0] |= 2;
            if (portal.curtain?.Visible == true) p[0] |= 4;
            if (portal.leftTorch?.Visible == true) p[0] |= 8;
            if (portal.rightTorch?.Visible == true) p[0] |= 16;
            if (portal.buffer is not null || portal.bufferAlpha > 0f) p[3] |= 1;
            if (portal.curtain?.Sprite.CurrentAnimationID == "fall") p[3] |= 2;
            if (portal.leftTorch?.light is not null) p[3] |= 4;
            if (portal.rightTorch?.light is not null) p[3] |= 8;
            if (portal.curtain is not null) {
                p[1] = WatchChapterAnimation.Encode(portal.curtain.Sprite.CurrentAnimationID);
                p[2] = (byte)Math.Max(0, portal.curtain.Sprite.CurrentAnimationFrame);
            }
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), portal.switchCounter);
            WatchEntityPayloadCodec.WriteSingle(p, 8, portal.DistortionFade);
            WatchEntityPayloadCodec.WriteSingle(p, 12, portal.bufferAlpha);
            WatchEntityPayloadCodec.WriteSingle(p, 16, portal.bufferTimer);
            WatchEntityPayloadCodec.WriteSingle(p, 20, portal.Position.X);
            WatchEntityPayloadCodec.WriteSingle(p, 24, portal.Position.Y);
            yield return new(new(Kind, id), p);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            TempleMirrorPortal? portal = level.Entities.OfType<TempleMirrorPortal>().FirstOrDefault(candidate => WatchEntityIDTable<TempleMirrorPortal>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID)
                ?? Recreate(level, state.Key.EntityID);
            if (portal is null) continue;
            bool presentationActive = (p[3] & 1) != 0;
            if (presentationActive)
                EnsurePresentation(portal);
            portal.Visible = (p[0] & 1) != 0;
            portal.canTrigger = false;
            portal.switchCounter = BinaryPrimitives.ReadInt32LittleEndian(p[4..]);
            portal.DistortionFade = WatchEntityPayloadCodec.ReadSingle(p, 8);
            portal.bufferAlpha = WatchEntityPayloadCodec.ReadSingle(p, 12);
            portal.bufferTimer = WatchEntityPayloadCodec.ReadSingle(p, 16);
            portal.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 20), WatchEntityPayloadCodec.ReadSingle(p, 24));
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
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
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
        foreach (Gondola gondola in level.Entities.OfType<Gondola>())
        {
            if (!WatchEntityIDTable<Gondola>.TryGet(gondola, level.Session.Level, out int id)) continue;
            byte[] p = new byte[PayloadSize];
            if (gondola.Visible) p[0] |= 1;
            if (gondola.Collidable) p[0] |= 2;
            if (gondola.brokenLever) p[0] |= 4;
            if (gondola.inCliffside) p[0] |= 8;
            if (gondola.front.Visible) p[0] |= 16;
            if (gondola.Lever.Visible) p[0] |= 32;
            p[1] = WatchChapterAnimation.Encode(gondola.front.CurrentAnimationID);
            p[2] = (byte)Math.Max(0, gondola.front.CurrentAnimationFrame);
            p[3] = WatchChapterAnimation.Encode(gondola.Lever.CurrentAnimationID);
            p[4] = (byte)Math.Max(0, gondola.Lever.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteSingle(p, 8, gondola.Position.X);
            WatchEntityPayloadCodec.WriteSingle(p, 12, gondola.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 16, gondola.Rotation);
            WatchEntityPayloadCodec.WriteSingle(p, 20, gondola.Speed.X);
            WatchEntityPayloadCodec.WriteSingle(p, 24, gondola.Speed.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 28, gondola.RotationSpeed);
            WatchEntityPayloadCodec.WriteSingle(p, 32, gondola.front.Rate);
            WatchEntityPayloadCodec.WriteSingle(p, 36, gondola.Lever.Rate);
            yield return new(new(Kind, id), p);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            Gondola? gondola = level.Entities.OfType<Gondola>().FirstOrDefault(candidate => WatchEntityIDTable<Gondola>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
            if (gondola is null) continue;
            gondola.Visible = (p[0] & 1) != 0;
            gondola.Collidable = false;
            gondola.brokenLever = (p[0] & 4) != 0;
            gondola.inCliffside = (p[0] & 8) != 0;
            gondola.front.Visible = (p[0] & 16) != 0;
            gondola.Lever.Visible = (p[0] & 32) != 0;
            gondola.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 8), WatchEntityPayloadCodec.ReadSingle(p, 12));
            gondola.Rotation = WatchEntityPayloadCodec.ReadSingle(p, 16);
            gondola.Speed = new(WatchEntityPayloadCodec.ReadSingle(p, 20), WatchEntityPayloadCodec.ReadSingle(p, 24));
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
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
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
        foreach (WaveDashTutorialMachine machine in level.Entities.OfType<WaveDashTutorialMachine>())
        {
            if (!WatchEntityIDTable<WaveDashTutorialMachine>.TryGet(machine, level.Session.Level, out int id)) continue;
            byte[] p = new byte[PayloadSize];
            if (machine.Visible) p[0] |= 1;
            if (machine.playerInside) p[0] |= 2;
            if (machine.inCutscene) p[0] |= 4;
            if (machine.frontEntity?.Visible == true) p[0] |= 8;
            if (machine.presentation?.Viewing == true) p[0] |= 16;
            p[1] = WatchChapterAnimation.Encode(machine.noise.CurrentAnimationID);
            p[2] = (byte)Math.Max(0, machine.noise.CurrentAnimationFrame);
            p[3] = WatchChapterAnimation.Encode(machine.neon.CurrentAnimationID);
            p[4] = (byte)Math.Max(0, machine.neon.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteSingle(p, 8, machine.insideEase);
            WatchEntityPayloadCodec.WriteSingle(p, 12, machine.cameraEase);
            WatchEntityPayloadCodec.WriteSingle(p, 16, machine.Position.X);
            WatchEntityPayloadCodec.WriteSingle(p, 20, machine.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 24, machine.presentation?.ease ?? 0f);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(28), machine.presentation?.pageIndex ?? 0);
            WatchEntityPayloadCodec.WriteSingle(p, 32, machine.presentation?.pageEase ?? 0f);
            yield return new(new(Kind, id), p);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            WaveDashTutorialMachine? machine = level.Entities.OfType<WaveDashTutorialMachine>().FirstOrDefault(candidate => WatchEntityIDTable<WaveDashTutorialMachine>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
            if (machine is null) continue;
            machine.Visible = (p[0] & 1) != 0;
            machine.playerInside = (p[0] & 2) != 0;
            machine.inCutscene = false;
            machine.insideEase = WatchEntityPayloadCodec.ReadSingle(p, 8);
            machine.cameraEase = 0f;
            machine.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 16), WatchEntityPayloadCodec.ReadSingle(p, 20));
            if (machine.frontEntity is not null) machine.frontEntity.Visible = (p[0] & 8) != 0;
            if (machine.presentation is not null)
            {
                machine.presentation.Viewing = (p[0] & 16) != 0;
                machine.presentation.ease = WatchEntityPayloadCodec.ReadSingle(p, 24);
                int pageIndex = BinaryPrimitives.ReadInt32LittleEndian(p[28..]);
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
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
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
        foreach (PowerSourceNumber number in level.Entities.OfType<PowerSourceNumber>())
        {
            if (!WatchChapterEntityID.TryAssignFromMap(level, number, "powerSourceNumber", out int id)) continue;
            byte[] p = new byte[PayloadSize];
            if (number.Visible) p[0] |= 1;
            if (number.gotKey) p[0] |= 2;
            if (number.image.Visible) p[0] |= 4;
            if (number.glow.Visible) p[0] |= 8;
            WatchEntityPayloadCodec.WriteSingle(p, 4, number.ease);
            WatchEntityPayloadCodec.WriteSingle(p, 8, number.timer);
            WatchEntityPayloadCodec.WriteSingle(p, 12, number.Position.X);
            WatchEntityPayloadCodec.WriteSingle(p, 16, number.Position.Y);
            yield return new(new(Kind, id), p);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            PowerSourceNumber? number = level.Entities.OfType<PowerSourceNumber>().FirstOrDefault(candidate => WatchChapterEntityID.TryAssignFromMap(level, candidate, "powerSourceNumber", out int id) && id == state.Key.EntityID);
            if (number is null) continue;
            number.Visible = (p[0] & 1) != 0;
            number.gotKey = (p[0] & 2) != 0;
            number.image.Visible = (p[0] & 4) != 0;
            number.glow.Visible = (p[0] & 8) != 0;
            number.ease = WatchEntityPayloadCodec.ReadSingle(p, 4);
            number.timer = WatchEntityPayloadCodec.ReadSingle(p, 8);
            number.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 12), WatchEntityPayloadCodec.ReadSingle(p, 16));
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
    private static void Update(On.Celeste.PowerSourceNumber.orig_Update orig, PowerSourceNumber self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        if (!MiaoNetModule.IsWatchedPlayerPaused) self.Components.Update();
    }
}
