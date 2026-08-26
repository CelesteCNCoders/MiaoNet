using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchConditionalBlockAdapter : IWatchEntityAdapter
{
    private const ushort FakeWallSubID = 1;
    private const ushort ExitBlockSubID = 2;
    private const int PayloadSize = 16;

    private sealed class RemoteVisual
    {
        public float TileAlpha;
        public float CutoutAlpha;
    }

    private static readonly WatchConditionalBlockAdapter instance = new();
    private static readonly ConditionalWeakTable<FakeWall, RemoteVisual> fakeRemote = new();
    private static readonly ConditionalWeakTable<ExitBlock, RemoteVisual> exitRemote = new();

    public WatchEntityKind Kind => WatchEntityKind.ConditionalBlock;

    public static void Load()
    {
        On.Celeste.FakeWall.ctor_EntityID_EntityData_Vector2_Modes += FakeWall_ctor;
        On.Celeste.FakeWall.Update += FakeWall_Update;
        On.Celeste.ExitBlock.ctor_EntityData_Vector2 += ExitBlock_ctor;
        On.Celeste.ExitBlock.Update += ExitBlock_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ExitBlock.Update -= ExitBlock_Update;
        On.Celeste.ExitBlock.ctor_EntityData_Vector2 -= ExitBlock_ctor;
        On.Celeste.FakeWall.Update -= FakeWall_Update;
        On.Celeste.FakeWall.ctor_EntityID_EntityData_Vector2_Modes -= FakeWall_ctor;
        WatchEntityIDTable<FakeWall>.Clear();
        WatchEntityIDTable<ExitBlock>.Clear();
        fakeRemote.Clear();
        exitRemote.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (FakeWall wall in level.Entities.OfType<FakeWall>())
        {
            if (!WatchEntityIDTable<FakeWall>.TryGet(wall, room, out int id))
                continue;
            byte[] payload = new byte[PayloadSize];
            if (wall.Visible) payload[0] |= 1;
            if (wall.transitionFade) payload[0] |= 4;
            payload[1] = 0;
            payload[2] = (byte)wall.mode;
            WatchEntityPayloadCodec.WriteSingle(payload, 4, wall.tiles?.Alpha ?? 0f);
            WatchEntityPayloadCodec.WriteSingle(payload, 8, wall.cutout?.Alpha ?? 0f);
            WatchEntityPayloadCodec.WriteSingle(payload, 12, wall.transitionStartAlpha);
            yield return new(new(Kind, id, FakeWallSubID), payload);
        }

        foreach (ExitBlock block in level.Entities.OfType<ExitBlock>())
        {
            if (!WatchEntityIDTable<ExitBlock>.TryGet(block, room, out int id))
                continue;
            byte[] payload = new byte[PayloadSize];
            if (block.Visible) payload[0] |= 1;
            if (block.Collidable) payload[0] |= 2;
            payload[1] = 1;
            WatchEntityPayloadCodec.WriteSingle(payload, 4, block.tiles?.Alpha ?? 0f);
            WatchEntityPayloadCodec.WriteSingle(payload, 8, block.cutout?.Alpha ?? 0f);
            WatchEntityPayloadCodec.WriteSingle(payload, 12, block.startAlpha);
            yield return new(new(Kind, id, ExitBlockSubID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<(int ID, ushort SubID), WatchEntityState> desired = states
            .ToDictionary(state => (state.Key.EntityID, state.Key.SubID));
        bool changed = false;

        foreach (FakeWall wall in level.Entities.OfType<FakeWall>().ToArray())
        {
            if (!WatchEntityIDTable<FakeWall>.TryGet(wall, level.Session.Level, out int id))
                continue;
            if (!desired.Remove((id, FakeWallSubID), out WatchEntityState state))
            {
                if (isCompleteState) wall.RemoveSelf();
                continue;
            }
            Apply(wall, state.Payload.Span, isCompleteState);
            changed = true;
        }

        foreach (ExitBlock block in level.Entities.OfType<ExitBlock>().ToArray())
        {
            if (!WatchEntityIDTable<ExitBlock>.TryGet(block, level.Session.Level, out int id))
                continue;
            if (!desired.Remove((id, ExitBlockSubID), out WatchEntityState state))
            {
                if (isCompleteState) block.RemoveSelf();
                continue;
            }
            Apply(block, state.Payload.Span, isCompleteState);
            changed = true;
        }

        foreach (((int id, ushort subID), WatchEntityState state) in desired)
        {
            if (subID == FakeWallSubID && RecreateFakeWall(level, id, state.Payload.Span) is { } wall)
                Apply(wall, state.Payload.Span, true);
            else if (subID == ExitBlockSubID && RecreateExitBlock(level, id) is { } block)
                Apply(block, state.Payload.Span, true);
            else
                continue;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }

    private static void Apply(FakeWall wall, ReadOnlySpan<byte> payload, bool snap)
    {
        wall.Visible = (payload[0] & 1) != 0;
        wall.transitionFade = (payload[0] & 4) != 0;
        wall.transitionStartAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        RemoteVisual remote = fakeRemote.GetValue(wall, static _ => new());
        remote.TileAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        remote.CutoutAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        if (snap)
        {
            if (wall.tiles is not null) wall.tiles.Alpha = remote.TileAlpha;
            if (wall.cutout is not null) wall.cutout.Alpha = remote.CutoutAlpha;
        }
    }

    private static void Apply(ExitBlock block, ReadOnlySpan<byte> payload, bool snap)
    {
        block.Visible = (payload[0] & 1) != 0;
        block.Collidable = false;
        block.startAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        RemoteVisual remote = exitRemote.GetValue(block, static _ => new());
        remote.TileAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        remote.CutoutAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        if (snap)
        {
            if (block.tiles is not null) block.tiles.Alpha = remote.TileAlpha;
            if (block.cutout is not null) block.cutout.Alpha = remote.CutoutAlpha;
        }
    }

    private static void FakeWall_Update(On.Celeste.FakeWall.orig_Update orig, FakeWall self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        Smooth(self.tiles, self.cutout, fakeRemote.GetValue(self, static _ => new()));
        self.Components.Update();
    }

    private static void ExitBlock_Update(On.Celeste.ExitBlock.orig_Update orig, ExitBlock self)
    {
        if (!MiaoNetModule.IsWatching) { orig(self); return; }
        Smooth(self.tiles, self.cutout, exitRemote.GetValue(self, static _ => new()));
        self.Components.Update();
        self.Collidable = false;
    }

    private static void Smooth(TileGrid? tiles, EffectCutout? cutout, RemoteVisual remote)
    {
        float amount = 3f * Engine.RawDeltaTime;
        if (tiles is not null) tiles.Alpha = Calc.Approach(tiles.Alpha, remote.TileAlpha, amount);
        if (cutout is not null) cutout.Alpha = Calc.Approach(cutout.Alpha, remote.CutoutAlpha, amount);
    }

    private static FakeWall? RecreateFakeWall(Level level, int id, ReadOnlySpan<byte> payload)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name is "fakeWall" or "fakeBlock");
        if (data is null) return null;
        FakeWall.Modes mode = (FakeWall.Modes)payload[2];
        FakeWall wall = new(new(level.Session.Level, id), data,
            new(level.Bounds.Left, level.Bounds.Top), mode);
        WatchEntityIDTable<FakeWall>.Set(wall, level.Session.Level, id);
        level.Add(wall);
        return wall;
    }

    private static ExitBlock? RecreateExitBlock(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "exitBlock");
        if (data is null) return null;
        ExitBlock block = new(data, new(level.Bounds.Left, level.Bounds.Top));
        WatchEntityIDTable<ExitBlock>.Set(block, level.Session.Level, id);
        level.Add(block);
        return block;
    }

    private static void FakeWall_ctor(
        On.Celeste.FakeWall.orig_ctor_EntityID_EntityData_Vector2_Modes orig,
        FakeWall self, EntityID eid, EntityData data, Vector2 offset, FakeWall.Modes mode)
    {
        orig(self, eid, data, offset, mode);
        WatchEntityIDTable<FakeWall>.Set(self, data.Level.Name, data.ID);
    }

    private static void ExitBlock_ctor(
        On.Celeste.ExitBlock.orig_ctor_EntityData_Vector2 orig,
        ExitBlock self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        WatchEntityIDTable<ExitBlock>.Set(self, data.Level.Name, data.ID);
    }
}
