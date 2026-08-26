using System.Runtime.CompilerServices;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchCheckpointAdapter : IWatchEntityAdapter
{
    private sealed class IDHolder
    {
        public string Level { get; }

        public int ID { get; }

        public IDHolder(string level, int id)
        {
            Level = level;
            ID = id;
        }
    }

    private static readonly WatchCheckpointAdapter checkpointInstance = new(WatchEntityKind.Checkpoint);
    private static readonly WatchCheckpointAdapter summitInstance = new(WatchEntityKind.SummitCheckpoint);
    private static readonly ConditionalWeakTable<Checkpoint, IDHolder> checkpointIDs = new();
    private static readonly ConditionalWeakTable<SummitCheckpoint, IDHolder> summitCheckpointIDs = new();

    public WatchEntityKind Kind { get; }

    private WatchCheckpointAdapter(WatchEntityKind kind)
    {
        Kind = kind;
    }

    public static void Load()
    {
        On.Celeste.Checkpoint.ctor_EntityData_Vector2 += Checkpoint_ctor;
        On.Celeste.SummitCheckpoint.ctor += SummitCheckpoint_ctor;
        WatchEntitySyncRegistry.Register(checkpointInstance);
        WatchEntitySyncRegistry.Register(summitInstance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(checkpointInstance);
        WatchEntitySyncRegistry.Unregister(summitInstance);
        On.Celeste.Checkpoint.ctor_EntityData_Vector2 -= Checkpoint_ctor;
        On.Celeste.SummitCheckpoint.ctor -= SummitCheckpoint_ctor;
        checkpointIDs.Clear();
        summitCheckpointIDs.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        if (Kind == WatchEntityKind.Checkpoint)
        {
            foreach (Checkpoint checkpoint in level.Entities.OfType<Checkpoint>())
            {
                if (checkpointIDs.TryGetValue(checkpoint, out IDHolder? holder)
                    && StringComparer.Ordinal.Equals(holder.Level, level.Session.Level))
                {
                    yield return new WatchEntityState(
                        new WatchEntityKey(Kind, holder.ID),
                        [checkpoint.triggered ? (byte)1 : (byte)0]
                    );
                }
            }
        }
        else
        {
            foreach (SummitCheckpoint checkpoint in level.Entities.OfType<SummitCheckpoint>())
            {
                if (summitCheckpointIDs.TryGetValue(checkpoint, out IDHolder? holder)
                    && StringComparer.Ordinal.Equals(holder.Level, level.Session.Level))
                {
                    yield return new WatchEntityState(
                        new WatchEntityKey(Kind, holder.ID),
                        [checkpoint.Activated ? (byte)1 : (byte)0]
                    );
                }
            }
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, bool> activatedByID = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] > 1
                || !activatedByID.TryAdd(state.Key.EntityID, state.Payload.Span[0] != 0))
            {
                Logger.Warn(LT.MiaoNetWatch, $"Ignored invalid {Kind} watch state.");
                return WatchEntityApplyResult.None;
            }
        }

        bool changed = false;
        if (Kind == WatchEntityKind.Checkpoint)
        {
            foreach (Checkpoint checkpoint in level.Entities.OfType<Checkpoint>())
            {
                if (!checkpointIDs.TryGetValue(checkpoint, out IDHolder? holder)
                    || !StringComparer.Ordinal.Equals(holder.Level, level.Session.Level)
                    || !activatedByID.TryGetValue(holder.ID, out bool activated))
                    continue;

                changed |= checkpoint.Active || checkpoint.triggered != activated;
                checkpoint.Active = false;
                if (activated && !checkpoint.triggered)
                    checkpoint.TurnOn(false);
                else if (!activated && checkpoint.triggered)
                    ResetCheckpoint(checkpoint);
            }
        }
        else
        {
            foreach (SummitCheckpoint checkpoint in level.Entities.OfType<SummitCheckpoint>())
            {
                if (!summitCheckpointIDs.TryGetValue(checkpoint, out IDHolder? holder)
                    || !StringComparer.Ordinal.Equals(holder.Level, level.Session.Level)
                    || !activatedByID.TryGetValue(holder.ID, out bool activated))
                    continue;

                changed |= checkpoint.Active || checkpoint.Activated != activated;
                checkpoint.Active = false;
                checkpoint.Activated = activated;
            }
        }

        return changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }

    private static void ResetCheckpoint(Checkpoint checkpoint)
    {
        checkpoint.triggered = false;
        checkpoint.sine = 0f;
        checkpoint.fade = 0f;
        checkpoint.sprite.Play("off");
        checkpoint.sprite.Color = Color.White;
        checkpoint.flash.Visible = false;
        if (checkpoint.light is not null)
            checkpoint.Remove(checkpoint.light);
        if (checkpoint.bloom is not null)
            checkpoint.Remove(checkpoint.bloom);
    }

    private static void Checkpoint_ctor(
        On.Celeste.Checkpoint.orig_ctor_EntityData_Vector2 orig,
        Checkpoint self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        checkpointIDs.AddOrUpdate(self, new IDHolder(data.Level.Name, data.ID));
    }

    private static void SummitCheckpoint_ctor(
        On.Celeste.SummitCheckpoint.orig_ctor orig,
        SummitCheckpoint self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        summitCheckpointIDs.AddOrUpdate(self, new IDHolder(data.Level.Name, data.ID));
    }
}
