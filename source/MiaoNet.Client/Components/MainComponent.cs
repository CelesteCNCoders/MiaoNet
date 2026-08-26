using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;
using MonoMod.Utils;
using FFlags = MiaoNet.Shared.PlayerStateDelta.FrameFlags;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Main component, handle player sync
/// </summary>
public sealed partial class MainComponent : MiaoNetComponent
{
    private const int MaxFollowersCount = 12;
    private const float SendFireworksCooldown = 0.5f;
    private float sendFireworksTimer;

    private bool pendingMapChanged;
    private readonly Dictionary<int, MiaoNetGhost> ghosts;

    private GhostNameTag? selfNameTag;

    public (Session? session, SaveData? saveData, int slot) LastLocationBeforeTeleport;

    private OnlinePlayer? playerWatching;

    public bool Watching => playerWatching is not null;

    internal bool WatchedPlayerPaused => playerWatching?.IsPaused == true;

    public MainComponent(MiaoNetContext context) : base(context)
    {
        ghosts = new();

        context.PlayerLeft += Context_PlayerLeft;
        context.PlayerFrameNotification += Context_PlayerFrameNotification;
        context.PlayerLocationChanged += Context_PlayerLocationChanged;
        context.PlayerLocationChangeResponded += Context_PlayerLocationChangeResponded;
        context.PlayerLiveStateNotification += Context_PlayerLiveStateNotification;
        context.PlayerGlobalFlagsChanged += Context_PlayerGlobalFlagsChanged;
        context.PlayerCreatedFireworks += Context_PlayerCreatedFireworks;
        context.PlayerAudioPlayed += Context_PlayerAudioPlayed;
        context.PlayerGrabPlayer += Context_PlayerGrabPlayer;
        context.PlayerGrabJumpOut += Context_PlayerGrabJumpOut;
        context.SelfChannelMoved += Context_SelfChannelMoved;
        context.PlayerChannelMoved += Context_PlayerChannelMoved;
        context.WatchSnapshotRequested += Context_WatchSnapshotRequested;
        context.WatchSceneDeltaReceived += Context_WatchSceneDeltaReceived;
        context.WatchProducerStopped += Context_WatchProducerStopped;
        context.WatchEnded += Context_WatchEnded;

        MiaoNetModule.PlayerLocationChanged += MiaoNetModule_OnPlayerLocationChanged;
        MiaoNetModule.PlayerSoundPlayed += MiaoNetModule_PlayerSoundPlayed;
        MiaoNetModule.PlayerDied += MiaoNetModule_PlayerDied;
        MiaoNetModule.PlayerDeathWipeStarted += MiaoNetModule_PlayerDeathWipeStarted;
        MiaoNetModule.PreviewPlayerRespawn += MiaoNetModule_PreviewPlayerRespawn;
        MiaoNetModule.PlayerRoomTransition += MiaoNetModule_PlayerRoomTransition;
        WatchEntitySyncRegistry.EventProduced += WatchEntitySyncRegistry_EventProduced;
    }

    public override void OnConnected()
    {
        if (Engine.Scene is Level level)
            MiaoNetModule_OnPlayerLocationChanged(PlayerLocation.FetchFrom(level.Session), true);
        else if (Engine.Scene is Editor.MapEditor debugMap)
            MiaoNetModule_OnPlayerLocationChanged(new PlayerLocation(debugMap.mapData.Area, string.Empty), true);
    }

    public override void OnDisconnected()
    {
        foreach (var pair in ghosts)
            pair.Value.RemoveSelf();
        ghosts.Clear();
        selfNameTag?.RemoveSelf();
        selfNameTag = null;
        CleanUpWatching();
        CleanUpInteractions(Engine.Scene as Level);
        MiaoNetModule.Settings.GroupPhotoMode = false;
#if DEBUG
        if (!Engine.Scene.Tracker.IsEntityTracked<GroupPhotoPlatform>())
            return;
#endif
        var pf = Engine.Scene.Tracker.GetEntity<GroupPhotoPlatform>();
        pf?.RemoveSelf();
    }

    public override void Update()
    {
        base.Update();
        var settings = MiaoNetModule.Settings;
        Level? level = Engine.Scene as Level;
        Player? player = level?.Tracker.GetEntity<Player>();

        // TODO this can be optimized
        OnlinePlayer self = ClientState.Self;
        {
            // online status update
            var previousGlobalFlags = self.GlobalFlags;
            var globalFlags = previousGlobalFlags;
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.Paused, Engine.Scene.Paused);
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.Typing, context.ChatComponent.Active);
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.LiveMode, settings.LiveMode);
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.Interactions, settings.PlayerInteractions);
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.GroupPhotoMode, settings.GroupPhotoMode);
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.Watching, playerWatching is not null);
            globalFlags = WithFlag(globalFlags, PlayerGlobalFlags.TakingGolden, level?.Session.GrabbedGolden == true);
            if (previousGlobalFlags != globalFlags)
            {
                self.GlobalFlags = globalFlags;
                context.QueuePacket(new PacketUpdateGlobalFlag(globalFlags));
            }

            static PlayerGlobalFlags WithFlag(PlayerGlobalFlags current, PlayerGlobalFlags flag, bool value)
                => value ? (current | flag) : (current & ~flag);
        }

        if (level is null)
            return;

        if (UpdateWatchSceneRestore(level))
            return;

        UpdateWatchSceneProducer(level);

        // location update
        if (pendingMapChanged)
        {
            SafeGuard.Assert(TryGetAndSendState(level, PlayerLocation.FetchFrom(level.Session)));
            pendingMapChanged = false;
        }

        if (player is null || player.Dead)
            return;

        // show or remove own name
        if (settings.ShowOwnName && !Watching)
        {
            if (selfNameTag is null)
            {
                selfNameTag = new(player, ClientState.Self, context.ShowAvatar);
                player.Scene.Add(selfNameTag);
            }
            else if (selfNameTag.Scene != player.Scene)
            {
                selfNameTag.RemoveSelf();
                player.Scene.Add(selfNameTag);
            }
            selfNameTag.Entity = player;
        }
        else if (selfNameTag is not null)
        {
            player.Scene.CompletelyRemove(selfNameTag);
            selfNameTag = null;
        }

        // watching
        UpdateWatching(level, player);

        // player interactions
        UpdateInteractions(level, player);

        // do not send frames when paused or in freeze frames
        if (level.Paused || Engine.FreezeTimer > 0f)
            return;

        PlayerState? selfState = self.State;
        if (selfState is null)
            return;

        // player frame
        if (watchProducerSessions.Count > 0
            || !settings.GroupPhotoMode
            || level.OnInterval(1f / 2f))
            SendPlayerFrame(level, player, selfState, settings.FollowersSyncMode.HasSend);

        // fireworks
        if (settings.Fireworks)
        {
            if (sendFireworksTimer <= 0f)
            {
                var button = settings.CreateFireworksButton;
                if (button.Pressed && !level.Paused)
                {
                    MInputHack.ConsumeAllInputs();
                    const float Radius = 74f;
                    const float VMin = 248f - Radius, VSMin = VMin * VMin;
                    const float VMax = 248f + Radius, VSMax = VMax * VMax;
                    float initialSpeed = MathF.Sqrt(VSMin + (VSMax - VSMin) * Random.Shared.NextSingle());
                    Color color = player.Hair.Color;
                    level.Add(new SelfFireworks(player.Position, color, initialSpeed));
                    context.QueuePacket(new PacketCreateFireworks(color, initialSpeed));
                    sendFireworksTimer = SendFireworksCooldown;
                }
            }
            else
            {
                sendFireworksTimer -= Engine.RawDeltaTime;
            }
        }

        // group photo platform
        {
            var pf = level.Tracker.GetEntity<GroupPhotoPlatform>();
            if (MiaoNetModule.Settings.GroupPhotoMode)
            {
                if (pf is null)
                    level.Add(new GroupPhotoPlatform());
            }
            else
            {
                pf?.RemoveSelf();
            }
        }
    }

    private void SendPlayerFrame(
        Level level,
        Player player,
        PlayerState selfState,
        bool sendFollowers
    )
    {
        bool currentDashing = player.StateMachine.State is Player.StDash;
        int currentDashes = player.Dashes;

        PlayerStateFlags stateFlags = PlayerStateFlags.None;

        if (player.Facing is Facings.Left)
            stateFlags |= PlayerStateFlags.FacingLeft;

        if (currentDashing)
            stateFlags |= PlayerStateFlags.Dashing;

        if (player.StateMachine.State == Player.StRedDash)
            stateFlags |= PlayerStateFlags.RedBoosted;

        if (player.StateMachine.State == Player.StStarFly)
            stateFlags |= PlayerStateFlags.StarFlying;

        if (MiaoNetModule.Settings.PlayerInteractions && player.InControl)
            stateFlags |= PlayerStateFlags.Interactions;

        if (player.Ducking)
            stateFlags |= PlayerStateFlags.Ducking;

        if (player.IsTired)
            stateFlags |= PlayerStateFlags.Tired;

        FFlags flags = FFlags.None;

        if (currentDashes != selfState.Dashes)
            flags |= FFlags.DashesChange;

        if (selfState.WindDirection != player.windDirection)
            flags |= FFlags.HasWindDirection;

        // Level.TransitionTo owns both sides' camera while its coroutine runs.
        // Resume authoritative samples only after the Player transition ends so
        // a late intermediate sample cannot pull the Watcher camera backwards.
        if (watchProducerSessions.Count > 0 && level.transition is null)
            flags |= FFlags.HasCameraPosition;

        HoldableInfo? holdableInfo = null;
        FollowerInfo[]? followerInitials;
        FollowerInfoDelta[]? followerDeltas = null;

        List<Follower> currentFollowers = sendFollowers ? player.Leader.Followers : [];
        followerInitials = FetchFollowerInitialsIfNeeded(selfState.FollowerInfos, player.Leader.Entity, currentFollowers, MaxFollowersCount);
        if (followerInitials is not null)
        {
            flags |= FFlags.HasFollowerInitials;
        }
        else if (currentFollowers.Count > 0)
        {
            flags |= FFlags.HasFollowerDeltas;
            followerDeltas = FetchFollowerDeltas(player.Leader.Entity.Position, player.Leader.Followers, MaxFollowersCount);
        }
        SafeGuard.Assert(!(flags.HasFlag(FFlags.HasFollowerInitials) && flags.HasFlag(FFlags.HasFollowerDeltas)));

        if (player.Holding is not null)
        {
            flags |= FFlags.HasHoldable;
            holdableInfo = FetchHoldableInfo(player.Holding, selfState.HoldableInfo);
        }

        selfState.StateFlags = stateFlags;
        selfState.Dashes = (byte)currentDashes;
        selfState.WindDirection = player.windDirection;
        if (followerInitials is not null)
            selfState.ApplyFollowersInitials(followerInitials);
        else if (followerDeltas is not null)
            selfState.ApplyFollowersDeltas(followerDeltas);
        if (holdableInfo is not null)
            selfState.ApplyHoldableInfo((HoldableInfo)holdableInfo);
        else
            selfState.HoldableInfo = new HoldableInfo();

        var stateDelta = new PlayerStateDelta(
            player.Position,
            player.Sprite.CurrentAnimationID,
            (ushort)player.Sprite.CurrentAnimationFrame,
            player.Sprite.Scale,
            flags, stateFlags
        );

        if (stateDelta.DashesChange)
            stateDelta.Dashes = (byte)currentDashes;
        if (stateDelta.HasHoldable)
            stateDelta.HoldableInfo = (HoldableInfo)holdableInfo!;
        if (stateDelta.StateFlags.HasFlag(PlayerStateFlags.Dashing))
            stateDelta.DashDirection = (byte)(player.DashDir.Angle() / MathF.Tau * byte.MaxValue);
        if (stateDelta.HasFollowerInitials)
            stateDelta.FollowerInitials = followerInitials;
        else if (stateDelta.HasFollowerDeltas)
            stateDelta.FollowerDeltas = followerDeltas;
        if (stateDelta.HasWindDirection)
            stateDelta.WindDirection = player.windDirection;
        if (stateDelta.HasCameraPosition)
        {
            stateDelta.CameraPosition = level.Camera.Position;
            selfState.CameraPosition = stateDelta.CameraPosition;
        }

        context.QueuePacket(new PacketPlayerFrame(stateDelta));
    }

    #region holdable & follower info fetching
    private static HoldableInfo FetchHoldableInfo(Holdable holdable, in HoldableInfo previous)
    {
        Entity entity = holdable.Entity;
        Entity holder = holdable.Holder;
        Vector2 offset = entity.Position - holder.Position;
        Vector2? offsetN = (previous.Type != HoldableType.None && previous.Offset == offset) ? null : offset;

        if (entity is Glider jelly)
        {
            Sprite spr = jelly.Get<Sprite>();
            return new(
                HoldableType.Jelly, offsetN,
                spr.CurrentAnimationID, (ushort)spr.CurrentAnimationFrame,
                spr.Scale, spr.Rotation
            );
        }
        else if (entity is TheoCrystal)
        {
            return new HoldableInfo(HoldableType.Theo, offsetN);
        }
        else if (entity is MiaoNetGhost)
        {
            return new HoldableInfo(HoldableType.Player, offsetN);
        }
        else
        {
            return new HoldableInfo(HoldableType.None, null);
        }
    }

    private static FollowerInfo[]? FetchFollowerInitialsIfNeeded(FollowerInfo[] previous, Entity leader, List<Follower> followers, int take)
    {
        int count = Math.Min(followers.Count, take);
        if (previous.Length != count || !AllSameFollowerIdentity(previous, followers, take))
            return FetchFollowerInitials(leader, followers, take);
        return null;

        static bool AllSameFollowerIdentity(FollowerInfo[] previous, List<Follower> followers, int take)
        {
            int count = Math.Min(followers.Count, take);
            SafeGuard.Assert(previous.Length == count);
            for (int i = 0; i < count; i++)
            {
                Entity entity = followers[i].Entity;
                Sprite sprite = entity.Get<Sprite>();
                string spriteID = sprite is null
                    ? string.Empty
                    : SpriteIDTracker.LookupID(sprite) ?? string.Empty;
                if (previous[i].Type != GetFollowerType(entity)
                    || !StringComparer.Ordinal.Equals(previous[i].SpriteID.Value, spriteID))
                    return false;
            }
            return true;
        }
    }

    private static FollowerInfo[] FetchFollowerInitials(Entity leader, List<Follower> followers, int take)
    {
        int count = Math.Min(followers.Count, take);
        var array = new FollowerInfo[count];

        for (int i = 0; i < array.Length; i++)
            array[i] = FetchFollowerInitial(leader.Position, followers[i]);
        return array;

        static FollowerInfo FetchFollowerInitial(Vector2 leaderEntityPosition, Follower follower)
        {
            Entity entity = follower.Entity;
            FollowerType type = GetFollowerType(entity);
            Sprite spr = entity.Get<Sprite>();

            // TODO Strawberry Jam's RefillShard's sprite only contains Path
            Vector2S offset = (Vector2S)(entity.Position - leaderEntityPosition);
            return spr is not null
                ? new FollowerInfo(
                    type, SpriteIDTracker.LookupID(spr) ?? string.Empty,
                    spr.CurrentAnimationID, (ushort)spr.CurrentAnimationFrame,
                    offset: (Vector2S)(entity.Position - leaderEntityPosition)
                )
                : new FollowerInfo(
                    type, string.Empty,
                    string.Empty, 0,
                    offset
                );
        }
    }

    private static FollowerType GetFollowerType(Entity entity) => entity switch
    {
        Strawberry => FollowerType.Strawberry,
        StrawberrySeed => FollowerType.StrawberrySeed,
        Key => FollowerType.Key,
        _ => FollowerType.Custom
    };

    // TODO pool?
    private static FollowerInfoDelta[] FetchFollowerDeltas(Vector2 leaderEntityPosition, List<Follower> allFollowers, int take)
    {
        int count = Math.Min(allFollowers.Count, take);
        var array = new FollowerInfoDelta[count];

        for (int i = 0; i < array.Length; i++)
            array[i] = FetchFollowerDelta(leaderEntityPosition, allFollowers[i]);
        return array;

        static FollowerInfoDelta FetchFollowerDelta(Vector2 leaderEntityPosition, Follower follower)
        {
            Entity entity = follower.Entity;
            Sprite spr = entity.Get<Sprite>();
            Vector2S offset = (Vector2S)(entity.Position - leaderEntityPosition);
            return spr is not null
                ? new FollowerInfoDelta(spr.CurrentAnimationID, (ushort)spr.CurrentAnimationFrame, offset)
                : new FollowerInfoDelta(string.Empty, 0, offset);
        }
    }
    #endregion

    private bool TryGetAndSendState(Level level, PlayerLocation location)
    {
        Player player = level.Tracker.GetEntity<Player>();
        PlayerDeadBody? body = null;
        if (player is null)
        {
            body = (PlayerDeadBody?)level.Entities.FirstOrDefault(e => e is PlayerDeadBody);
            if (body is not null)
                player = body.player;
            else
                return false;
        }
        PlayerStateFlags stateFlags = PlayerStateFlags.None;

        if (player.StateMachine.State is Player.StDash)
            stateFlags |= PlayerStateFlags.Dashing;

        if (player.StateMachine.State == Player.StRedDash)
            stateFlags |= PlayerStateFlags.RedBoosted;

        if (body is not null)
            stateFlags |= PlayerStateFlags.Dead;

        if (player.Facing == Facings.Left)
            stateFlags |= PlayerStateFlags.FacingLeft;

        if (MiaoNetModule.Settings.PlayerInteractions)
            stateFlags |= PlayerStateFlags.Interactions;

        if (player.Ducking)
            stateFlags |= PlayerStateFlags.Ducking;

        PlayerState initialState = new PlayerState()
        {
            Position = player.Position,
            Animation = player.Sprite.CurrentAnimationID,
            AnimationFrame = (ushort)player.Sprite.CurrentAnimationFrame,
            Scale = player.Sprite.Scale,
            Dashes = (byte)player.Dashes,
            DeltaTime = Engine.DeltaTime,
            PlayerSpriteMode = player.Sprite.Mode,
            FollowerInfos = FetchFollowerInitials(player.Leader.Entity, player.Leader.Followers, MaxFollowersCount),
            WindDirection = player.windDirection,
            CameraPosition = level.Camera.Position,
            HoldableInfo = player.Holding is not null ? FetchHoldableInfo(player.Holding, new()) : new(),
            StateFlags = stateFlags
        };

        ClientState.SelfState = initialState;
        PacketPlayerLocationChanged p = new(location, initialState);
        context.QueuePacket(p);
        return true;
    }

    #region event handlers
    private void MiaoNetModule_OnPlayerLocationChanged(PlayerLocation location, bool forceFullChange)
    {
        if (!HasState)
            return;
        if ((watchProducerEntityResyncPending && location == watchProducerLocation)
            || ClientState.SelfState?.StateFlags.HasFlag(PlayerStateFlags.Dead) == true)
            MarkWatchProducerEntityResync(location);
        else
            MarkWatchProducerRoomReload(location);
        var changeResult = ClientState.OnPlayerLocationChanged(location);
        bool fullSync = forceFullChange || changeResult is PlayerLocation.ChangeResult.FullSync;
        if (changeResult is PlayerLocation.ChangeResult.None && !fullSync)
            return;

        TryDisableGroupPhotoModeAndTip();

        if (fullSync)
        {
            Level? level = Engine.Scene as Level;
            CleanUpGhosts(level);
            CleanUpInteractions(level);

            if (location.IsInMap)
            {
                if (level is not null)
                {
                    // we assume player will at least exists in 2 frames...
                    if (!TryGetAndSendState(level, location))
                    {
                        level.OnEndOfFrame += () =>
                        {
                            bool sentState = TryGetAndSendState(level, location);
                            SafeGuard.Assert(sentState);
                        };
                    }
                }
                else if (Engine.Scene is LevelLoader)
                {
                    if (pendingMapChanged)
                        Logger.Warn(LT.MiaoNet, "pendingMapChanged is still true, is this a bug?");
                    pendingMapChanged = true;
                }
                else
                {
                    ClientState.SelfState = null;
                }
            }
            else
            {
                context.QueuePacket(new PacketPlayerLocationChanged(location, null));
            }

            foreach (var pair in ClientState.Players)
                pair.Value.State = null;
        }
        else
        {
            context.QueuePacket(new PacketPlayerLocationChanged(location, null));
        }

        void TryDisableGroupPhotoModeAndTip()
        {
            if (MiaoNetModule.Settings.GroupPhotoMode)
            {
                MiaoNetModule.Settings.GroupPhotoMode = false;
                context.ChatComponent.AddLocalChat(MiaoNetChatText.CreateCommandTip(Dialog.Get("miaonet_group_photo_mode_off_on_map_change")));
            }
        }
    }

    private void CleanUpGhosts(Level? level)
    {
        if (level is not null)
        {
            foreach (var g in ghosts)
                level.CompletelyRemove(g.Value);
        }
        ghosts.Clear();
    }

    private void Context_PlayerLocationChanged(OnlinePlayer player, PacketPlayerLocationChangedNotification packet)
    {
        Logger.Debug(LT.MiaoNet, $"LocationChanging: {player.Info.Name} to {packet.Location}");
        if (Engine.Scene is not Level level)
            return;

        bool roomOnly = packet.InitialState is null
            && packet.Location.IsInMap
            && ClientState.Self.Location.Map == packet.Location.Map;

        if (roomOnly)
            return;
        HandleLocationChanging(level, player);
    }

    private void Context_PlayerLocationChangeResponded(PacketPlayerLocationChangedResponse packet)
    {
        if (Engine.Scene is not Level level)
            return;
        Logger.Debug(LT.MiaoNet, $"LocationChangeResponding: Players count = {packet.Players.Count}");
        CleanUpGhosts(level);
        foreach (var item in packet.Players)
        {
            OnlinePlayer player = ClientState.GetPlayer(item.PlayerID);
            HandleLocationChanging(level, player);
        }
    }

    private void Context_PlayerFrameNotification(OnlinePlayer player, PacketPlayerFrame packet)
    {
        if (Engine.Scene is not Level level)
            return;

        var delta = packet.StateDelta;

        BufferWatchCameraSample(player, delta);
        if (playerWatching?.ID == player.ID)
        {
            WatchBadelineOldsiteAdapter.RecordRemotePlayerFrame(delta);
            WatchAngryOshiroAdapter.RecordRemotePlayerFrame(delta);
        }

        if (ghosts.TryGetValue(player.ID, out var ghost))
        {
            if (!ghost.BeingHeldLocally)
                ghost.Position = delta.Position;

            // hmmm can we avoid these tons of updates?

            ghost.UpdateInteractions(delta.StateFlags.HasFlag(PlayerStateFlags.Interactions));
            ghost.UpdateSprite(delta.Animation, delta.AnimationFrame, delta.StateFlags.HasFlag(PlayerStateFlags.FacingLeft), delta.Scale);
            if (delta.HasHoldable)
            {
                var hi = delta.HoldableInfo;
                if (hi.Type == HoldableType.Jelly)
                    ghost.UpdateHoldable(
                        hi.Type,
                        hi.Offset,
                        hi.Animation,
                        hi.AnimationFrame,
                        hi.Scale,
                        hi.Rotation
                    );
                else
                    ghost.UpdateSimpleHoldable(hi.Type, hi.Offset);

                if (player.ID == heldByPlayerGhost?.OnlinePlayer.ID)
                    OnHeldByPlayerFrame(level, ghost);
            }
            else
            {
                ghost.UpdateNoHoldable();
            }
            if (delta.HasFollowerInitials)
                ghost.OnFollowerInitials(delta.FollowerInitials);
            else if (delta.HasFollowerDeltas)
                ghost.OnFollowerDeltas(delta.FollowerDeltas);

            if (delta.HasWindDirection)
                ghost.UpdateWind(delta.WindDirection);

            ghost.UpdateDashing(
                delta.StateFlags.HasFlag(PlayerStateFlags.Dashing), delta.DashDirection / (float)byte.MaxValue * MathF.Tau,
                delta.DashesChange, delta.Dashes
            );
            ghost.UpdateStarFlying(delta.StateFlags.HasFlag(PlayerStateFlags.StarFlying));
            ghost.UpdateRedBoosted(delta.StateFlags.HasFlag(PlayerStateFlags.RedBoosted));
            ghost.UpdateDucking(delta.StateFlags.HasFlag(PlayerStateFlags.Ducking));
            ghost.UpdateTired(delta.StateFlags.HasFlag(PlayerStateFlags.Tired));
        }
        else
        {
            // server can be late to know we aren't in the previous location
            Logger.Warn(LT.MiaoNetSync, $"Notified but ghost does not exists for {player.Info}");
        }
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (Engine.Scene is not Level level)
            return;
        if (!ClientState.Self.ShouldSyncFrom(player))
            return;
        if (!ghosts.Remove(player.ID, out MiaoNetGhost? ghost))
        {
            Logger.Warn(LT.MiaoNet, $"Try removing the ghost of player({player.Info}) but it doesn't exist.");
            return;
        }
        level.CompletelyRemove(ghost);
    }

    private void MiaoNetModule_PreviewPlayerRespawn(Player player, Level level, bool fromSL)
    {
        if (!HasState)
            return;
        var state = ClientState.SelfState;
        if (state is null)
        {
            SafeGuard.Assert(TryGetAndSendState(level, PlayerLocation.FetchFrom(level.Session)));
            state = ClientState.SelfState;
            SafeGuard.Assert(state is not null);
        }
        if (state.StateFlags.HasFlag(PlayerStateFlags.Dead))
        {
            MarkWatchProducerEntityResync(PlayerLocation.FetchFrom(level.Session));
            state.StateFlags &= ~PlayerStateFlags.Dead;
            var type = fromSL ? LiveStateType.RespawnFromSL : LiveStateType.Respawn;
            PacketPlayerLiveState packet = new(type, player.Position);
            context.QueuePacket(packet);
        }
    }

    private void MiaoNetModule_PlayerDied(Player player, Vector2 direction)
    {
        if (!HasState)
            return;
        CleanUpInteractions(player.SceneAs<Level>());
        var state = ClientState.SelfState!;
        if (!state.StateFlags.HasFlag(PlayerStateFlags.Dead))
        {
            watchProducerDeathRespawnLocation = null;
            state.StateFlags |= PlayerStateFlags.Dead;
            PacketPlayerLiveState packet = new(LiveStateType.Die, direction);
            context.QueuePacket(packet);
        }
    }

    private void MiaoNetModule_PlayerDeathWipeStarted()
    {
        if (!HasState
            || ClientState.SelfState is not { } state
            || !state.StateFlags.HasFlag(PlayerStateFlags.Dead))
            return;

        context.QueuePacket(new PacketPlayerLiveState(LiveStateType.DeathWipe, Vector2.Zero));
        Logger.Debug(LT.MiaoNetWatch, "Emitted Player death-wipe start notification.");
    }

    private void Context_PlayerLiveStateNotification(OnlinePlayer player, LiveStateType flag, Vector2 vector2)
    {
        if (ghosts.TryGetValue(player.ID, out var ghost))
        {
            if (flag == LiveStateType.Die)
            {
                ghost.OnDied(vector2);
                if (playerWatching?.ID == player.ID && Engine.Scene is Level level)
                    BeginWatchDeathTransition(level);
            }
            else if (flag == LiveStateType.DeathWipe)
            {
                if (playerWatching?.ID == player.ID && Engine.Scene is Level level)
                    SignalWatchDeathWipe(level);
            }
            else if (playerWatching?.ID == player.ID
                && watchDeathTransitionPhase != WatchDeathTransitionPhase.None)
            {
                BufferWatchRespawnNotification(
                    vector2,
                    flag == LiveStateType.RespawnFromSL
                );
            }
            else
                ghost.OnRespawning(vector2, flag == LiveStateType.RespawnFromSL);
        }
        else
        {
            Logger.Warn(LT.MiaoNetSync, $"Live state notified but ghost does not exists for {player.Info}");
        }
    }

    private void Context_PlayerGlobalFlagsChanged(OnlinePlayer player, PlayerGlobalFlags previousFlag)
    {
        if (!ghosts.TryGetValue(player.ID, out var ghost))
            return;
        ghost.OnUpdatePaused(player.GlobalFlags.HasFlag(PlayerGlobalFlags.Paused));
        ghost.OnUpdateWatching(player.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching));
    }

    private void Context_PlayerCreatedFireworks(OnlinePlayer player, Color color, float initialSpeed)
    {
        // TODO prevent this server-side
        if (!MiaoNetModule.Settings.Fireworks)
            return;
        if (!ghosts.TryGetValue(player.ID, out var ghost))
            return;
        ghost.OnCreatedFireworks(color, initialSpeed);
    }

    private void MiaoNetModule_PlayerSoundPlayed(string sound, string? param, float value)
    {
        if (!HasState)
            return;
        if (!MiaoNetModule.Settings.PlayerAudioSyncMode.HasSend)
            return;
        if (sound is SFX.char_mad_revive)
            sound = MiaoNetSFX.PlayerRevive;
        context.QueuePacket(new PacketPlayerPlayedAudio(new(sound, param, value)));
    }

    private void Context_PlayerAudioPlayed(OnlinePlayer player, PlayerPlayedAudio audio)
    {
        // TODO check this packet is sent "legally" server-side
        // TODO and also, we need to introduce player global settings
        if (!ghosts.TryGetValue(player.ID, out var ghost))
        {
            Logger.Warn(LT.MiaoNetSync, $"Received player {player.Info} played audio {audio.Event} but no ghost found.");
            return;
        }

        if (audio.HasParam)
            ghost.OnPlayAudio(audio.Event, audio.Param, audio.ParamValue);
        else
            ghost.OnPlayAudio(audio.Event);
    }

    private void Context_PlayerChannelMoved(OnlinePlayer player, PacketPlayerChannelMovedNotification notification)
    {
        if (Engine.Scene is not Level level)
            return;
        HandleLocationChanging(level, player);
    }

    private void Context_SelfChannelMoved(PacketPlayerChannelMovedResponse response)
    {
        if (Engine.Scene is not Level level)
            return;
        CleanUpGhosts(level);
        if (response.Players is not null)
        {
            foreach (var p in response.Players)
                HandleLocationChanging(level, ClientState.GetPlayer(p.PlayerID));
        }
    }

    #endregion

    private void HandleLocationChanging(Level level, OnlinePlayer other)
    {
        if (ghosts.TryGetValue(other.ID, out MiaoNetGhost? ghost))
        {
            level.CompletelyRemove(ghost);
            ghosts.Remove(other.ID);
        }
        if (other.State is not null && other.Location.IsInMap)
        {
            ghosts[other.ID] = ghost = new(other, context.ShowAvatar);
            if (playerWatching?.ID == other.ID)
                ghost.SetWatchFocus(true);
            level.Add(ghost);
            Logger.Debug(LT.MiaoNet, $"Created ghost for {other.Info}");
        }
        else
        {
            Logger.Debug(LT.MiaoNet, $"Removed ghost for {other.Info}");
        }
    }

    public bool TryGetGhostTarget(int playerID, [NotNullWhen(true)] out Entity? entity)
    {
        if (ghosts.TryGetValue(playerID, out MiaoNetGhost? ghost))
        {
            entity = ghost;
            return true;
        }
        else
        {
            entity = null;
            return false;
        }
    }
}
