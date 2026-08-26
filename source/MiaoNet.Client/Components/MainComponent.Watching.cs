using System.Globalization;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MainComponent
{
    private int? watchSessionID;
    private HashSet<string>? watchBaselineFlags;
    private PlayerMapLocation watchMap;
    private int lastWatchSequence;
    private PlayerLocation watchTouchSwitchLocation;
    private HashSet<int>? watchActiveTouchSwitchIDs;
    private bool watchTouchSwitchStatePending;
    private bool watchTouchSwitchStateApplied;
    private bool watchRoomReloadPending;
    private PlayerLocation watchRoomReloadLocation;
    private bool watchSceneRestorePending;
    private PlayerLocation watchSceneRestoreLocation;

    public bool WatchRequestPending { get; private set; }

    private void CleanUpWatching()
    {
        WatchRequestPending = false;
        StopWatching(false);
        ClearWatchSceneProducer();
    }

    private void UpdateWatching(Level level, Player player)
    {
        if (playerWatching is not null)
        {
            if (playerWatching.State is null)
            {
                StopWatching();
                return;
            }

            if (playerWatching.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching))
            {
                context.ChatComponent.AddLocalChat(MiaoNetChatText.CreateCommandTip(
                    PFormat.Format(
                        CultureInfo.CurrentCulture,
                        Dialog.Get("miaonet_commands_watch_others_watching"),
                        playerWatching.Info.Name
                    )
                ));
                StopWatching();
                return;
            }

            var selfLoc = ClientState.Self.Location;
            var otherLoc = playerWatching.Location;
            if (selfLoc.Room != otherLoc.Room && !otherLoc.IsInDebugMap && level.transition is null)
            {
                var otherPos = playerWatching.State.Position;
                var session = level.Session;
                var data = session.MapData.Get(otherLoc.Room);
                Vector2 newRoomSpawnPoint = data.Spawns.ClosestTo(otherPos);
                session.RespawnPoint = newRoomSpawnPoint;
                var p = player.Position;
                player.Position = newRoomSpawnPoint;

                level.TransitionTo(data, (player.Position - p).SafeNormalize());
            }

            if (UpdateWatchRoomReload(level))
                return;

            ApplyWatchTouchSwitchState(level);
            player.Visible = false;
            player.StateMachine.State = Player.StFrozen;

            if (level.InCutscene && !level.SkippingCutscene)
                level.SkipCutscene();

            const int W = Celeste.GameWidth;
            const int H = Celeste.GameHeight;

            var cam = level.Camera;

            Vector2 target = playerWatching.State.Position;
            Vector2 camTarget = target - new Vector2(W, H) / 2f;
            camTarget.X = MathHelper.Clamp(camTarget.X, level.Bounds.Left, level.Bounds.Right - W);
            camTarget.Y = MathHelper.Clamp(camTarget.Y, level.Bounds.Top, level.Bounds.Bottom - H);
            cam.Position = Calc.Approach(cam.Position, camTarget, ((cam.Position - camTarget).Length() * 4f) * Engine.RawDeltaTime);
        }
    }

    public bool TryBeginWatchRequest()
    {
        if (WatchRequestPending || Watching || watchSceneRestorePending)
            return false;

        WatchRequestPending = true;
        return true;
    }

    public bool CompleteWatchRequest()
    {
        bool wasPending = WatchRequestPending;
        WatchRequestPending = false;
        return wasPending;
    }

    public bool CancelWatchRequest()
    {
        bool wasPending = WatchRequestPending;
        WatchRequestPending = false;
        return wasPending;
    }

    public bool StartWatching(OnlinePlayer player, int sessionID, WatchSceneSnapshot snapshot)
    {
        if (Engine.Scene is not Level level
            || snapshot.Location.Map != ClientState.Self.Location.Map
            || snapshot.Location.Map != player.Location.Map)
            return false;

        watchBaselineFlags = new(level.Session.Flags, StringComparer.Ordinal);
        ReplaceFlags(level.Session.Flags, snapshot.Flags);
        watchMap = snapshot.Location.Map;
        lastWatchSequence = snapshot.Sequence;
        watchSessionID = sessionID;
        playerWatching = player;
        watchTouchSwitchLocation = snapshot.Location;
        watchActiveTouchSwitchIDs = snapshot.ActiveTouchSwitchIDs.ToHashSet();
        watchTouchSwitchStatePending = true;
        watchTouchSwitchStateApplied = false;
        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;

        Logger.Info(
            LT.MiaoNetWatch,
            $"Watch session {sessionID} started for player {player.ID}; " +
            $"snapshot flags={snapshot.Flags.Count}, " +
            $"touchSwitches={snapshot.ActiveTouchSwitchIDs.Count}, sequence={snapshot.Sequence}."
        );
        return true;
    }

    public OnlinePlayer? StopWatching(bool notifyServer = true)
    {
        OnlinePlayer? player = playerWatching;
        playerWatching = null;

        if (watchSessionID is int sessionID)
        {
            if (notifyServer && context.HasConnection)
                context.QueuePacket(new PacketWatchStop(sessionID));
            Logger.Info(LT.MiaoNetWatch, $"Watch session {sessionID} stopped; notifyServer={notifyServer}.");
        }

        bool restoreScene = watchTouchSwitchStateApplied;
        PlayerLocation restoreLocation = watchTouchSwitchLocation;

        watchSessionID = null;
        lastWatchSequence = 0;
        watchMap = default;
        watchTouchSwitchLocation = default;
        watchActiveTouchSwitchIDs = null;
        watchTouchSwitchStatePending = false;
        watchTouchSwitchStateApplied = false;
        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;

        var level = Engine.Scene as Level ?? (Engine.Scene as AssetReloadHelper)?.OrigScene as Level;
        if (level is not null && watchBaselineFlags is not null)
            ReplaceFlags(level.Session.Flags, watchBaselineFlags);
        watchBaselineFlags = null;
        if (restoreScene && level is not null)
        {
            watchSceneRestorePending = true;
            watchSceneRestoreLocation = restoreLocation;
        }

        if (player is not null)
        {
            var playerEntity = level?.Tracker.GetEntity<Player>();
            if (playerEntity is not null)
            {
                playerEntity.Visible = true;
                playerEntity.StateMachine.State = Player.StNormal;
                playerEntity.ForceCameraUpdate = false;
            }
        }

        return player;
    }

    private void Context_WatchSceneDeltaReceived(PacketWatchSceneDeltaNotification packet)
    {
        if (watchSessionID != packet.SessionID
            || playerWatching?.ID != packet.TargetPlayerID
            || packet.Delta.Sequence != lastWatchSequence + 1
            || Engine.Scene is not Level level
            || PlayerLocation.FetchFrom(level.Session).Map != watchMap
            || packet.Delta.Location.Map != watchMap)
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Rejected watch delta for session {packet.SessionID}, target {packet.TargetPlayerID}, " +
                $"sequence {packet.Delta.Sequence}."
            );
            return;
        }

        packet.Delta.ApplyTo(level.Session.Flags);
        if (watchRoomReloadPending && watchRoomReloadLocation != packet.Delta.Location)
        {
            watchRoomReloadPending = false;
            watchRoomReloadLocation = default;
        }
        if (packet.Delta.HasTouchSwitchState)
        {
            if (watchTouchSwitchLocation != packet.Delta.Location)
                watchTouchSwitchStateApplied = false;
            watchTouchSwitchLocation = packet.Delta.Location;
            watchActiveTouchSwitchIDs = packet.Delta.ActiveTouchSwitchIDs.ToHashSet();
            watchTouchSwitchStatePending = true;
        }
        if (packet.Delta.RequiresRoomReload)
        {
            watchRoomReloadPending = true;
            watchRoomReloadLocation = packet.Delta.Location;
        }
        lastWatchSequence = packet.Delta.Sequence;
        Logger.Debug(
            LT.MiaoNetWatch,
            $"Applied watch delta for session {packet.SessionID}, sequence {packet.Delta.Sequence}, " +
            $"room={packet.Delta.Location.Room}, added={packet.Delta.AddedFlags.Count}, " +
            $"removed={packet.Delta.RemovedFlags.Count}, " +
            $"roomReload={packet.Delta.RequiresRoomReload}, " +
            $"touchSwitchState={packet.Delta.HasTouchSwitchState}, " +
            $"activeTouchSwitches={packet.Delta.ActiveTouchSwitchIDs.Count}."
        );
    }

    private void Context_WatchEnded(PacketWatchEnded packet)
    {
        if (watchSessionID != packet.SessionID)
            return;

        Logger.Info(LT.MiaoNetWatch, $"Watch session {packet.SessionID} ended by server: {packet.Reason}.");
        OnlinePlayer? player = StopWatching(false);
        string playerName = player?.Info.Name ?? packet.SessionID.ToString(CultureInfo.InvariantCulture);
        string reason = Dialog.Get($"miaonet_commands_watch_ended_{packet.Reason}");
        context.ChatComponent.AddLocalChat(MiaoNetChatText.CreateCommandTip(
            PFormat.Format(
                CultureInfo.CurrentCulture,
                Dialog.Get("miaonet_commands_watch_ended"),
                playerName,
                reason
            )
        ));
    }

    private static void ReplaceFlags(ISet<string> destination, IEnumerable<string> source)
    {
        destination.Clear();
        destination.UnionWith(source);
    }

    private void ApplyWatchTouchSwitchState(Level level)
    {
        if (!watchTouchSwitchStatePending
            || watchActiveTouchSwitchIDs is null
            || level.transition is not null
            || PlayerLocation.FetchFrom(level.Session) != watchTouchSwitchLocation)
            return;

        HashSet<int> missingIDs = new(watchActiveTouchSwitchIDs);
        int activatedCount = 0;
        foreach (TouchSwitch touchSwitch in level.Tracker.GetEntities<TouchSwitch>().Cast<TouchSwitch>())
        {
            if (!TouchSwitchIDTracker.TryGetID(touchSwitch, out int id)
                || !watchActiveTouchSwitchIDs.Contains(id))
                continue;

            missingIDs.Remove(id);
            if (!touchSwitch.Switch.Activated)
            {
                touchSwitch.TurnOn();
                activatedCount++;
            }
        }

        watchTouchSwitchStatePending = false;
        watchTouchSwitchStateApplied |= activatedCount > 0;
        if (missingIDs.Count > 0)
        {
            Logger.Warn(
                LT.MiaoNetWatch,
                $"Could not find {missingIDs.Count} TouchSwitch instance(s) while applying " +
                $"watch state for room {watchTouchSwitchLocation.Room}."
            );
        }
        Logger.Debug(
            LT.MiaoNetWatch,
            $"Applied TouchSwitch watch state for room {watchTouchSwitchLocation.Room}; " +
            $"requested={watchActiveTouchSwitchIDs.Count}, activated={activatedCount}."
        );
    }

    private bool UpdateWatchSceneRestore(Level level)
    {
        if (!watchSceneRestorePending || level.transition is not null)
            return false;

        watchSceneRestorePending = false;
        if (PlayerLocation.FetchFrom(level.Session) != watchSceneRestoreLocation)
        {
            watchSceneRestoreLocation = default;
            return false;
        }

        watchSceneRestoreLocation = default;
        level.Reload();
        Logger.Info(LT.MiaoNetWatch, "Reloaded the current room after restoring local watch state.");
        return true;
    }

    private bool UpdateWatchRoomReload(Level level)
    {
        if (!watchRoomReloadPending
            || level.transition is not null
            || PlayerLocation.FetchFrom(level.Session) != watchRoomReloadLocation)
            return false;

        watchRoomReloadPending = false;
        watchRoomReloadLocation = default;
        watchTouchSwitchStateApplied = true;
        level.Reload();
        Logger.Info(LT.MiaoNetWatch, "Reloaded the current room to follow the watched scene lifecycle.");
        return true;
    }

    private static void GotoLevel(Level level, Player player, Vector2 at)
    {
        var session = level.Session;
        var data = session.MapData.GetAt(at);
        session.Level = data.Name;
        session.RespawnPoint = data.Spawns.ClosestTo(at);
        player.Position = session.RespawnPoint.Value;
        level.LoadLevel(Player.IntroTypes.Transition);
    }
}
