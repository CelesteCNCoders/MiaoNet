using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;
using MonoMod.Utils;
using FFlags = MiaoNet.Shared.PacketPlayerFrame.FrameFlags;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Main component, handle player sync
/// </summary>
public sealed class MainComponent : MiaoNetComponent
{
    private bool pendingMapChanged;
    private readonly Dictionary<int, MiaoNetGhost> ghosts;

    private GhostNameTag? selfNameTag;

    public MainComponent(MiaoNetContext context) : base(context)
    {
        ghosts = new();

        context.PlayerLeft += Context_PlayerLeft;
        context.PlayerFrameNotification += Context_PlayerFrameNotification;
        context.PlayerMapChanged += Context_PlayerMapChanged;
        context.PlayerMapRoomChanged += Context_PlayerMapRoomChanged;
        context.PlayerMapChangeResponded += Context_PlayerMapChangeResponded;
        context.PlayerStateFlagsNotification += Context_PlayerStateFlagsNotification;
        context.PlayerOnlineStatusChanged += Context_PlayerOnlineStatusChanged;

        MiaoNetModule.PlayerLocationChanged += MiaoNetModule_OnPlayerLocationChanged;
        Everest.Events.Player.OnDie += Player_OnDie;
        Everest.Events.Player.OnSpawn += Player_OnSpawn;
    }

    public override void OnConnected()
    {
        if (Engine.Scene is Level level)
            MiaoNetModule_OnPlayerLocationChanged(PlayerLocation.FetchFrom(level.Session), true);
    }

    public override void OnDisconnected()
    {
        foreach (var pair in ghosts)
            pair.Value.RemoveSelf();
        ghosts.Clear();

        // FIXME unremoved ghost
        if (Engine.Scene is not null)
        {
            foreach (var ghost in Engine.Scene.Tracker.GetEntities<MiaoNetGhost>())
                ghost.RemoveSelf();
        }

        selfNameTag?.RemoveSelf();
        selfNameTag = null;
    }

    private void Player_OnDie(Player player)
    {
        if (!HasState)
            return;
        var state = ClientState.SelfState!;
        if (!state.Dead)
        {
            state.Dead = true;
            PacketPlayerStateFlags packet = new(PacketPlayerStateFlags.StateFlags.PlayerDied);
            context.QueuePacket(packet);
        }
    }

    private void Player_OnSpawn(Player player)
    {
        if (!HasState)
            return;
        var state = ClientState.SelfState;
        if (state is null)
        {
            Level level = player.SceneAs<Level>();
            SafeGuard.Assert(TryGetAndSendState(level, PlayerLocation.FetchFrom(level.Session)));
            state = ClientState.SelfState;
            SafeGuard.Assert(state is not null);
        }
        if (state.Dead)
        {
            state.Dead = false;
            PacketPlayerStateFlags packet = new(PacketPlayerStateFlags.StateFlags.PlayerRespawning);
            context.QueuePacket(packet);
        }
    }

    public override void Update()
    {
        base.Update();

        if (Engine.Scene is not Level level)
            return;

        var self = ClientState.Self;
        var p = self.OnlineStatus;
        self.OnlineStatus = level.Paused ? PlayerOnlineStatus.Paused : PlayerOnlineStatus.Normal;
        if (p != self.OnlineStatus)
            context.QueuePacket(new PacketUpdateOnlineStatus(self.OnlineStatus));

        if (pendingMapChanged)
        {
            SafeGuard.Assert(TryGetAndSendState(level, PlayerLocation.FetchFrom(level.Session)));
            pendingMapChanged = false;
        }

        //if (level.OnRawInterval(1f))
        //    errCount = Math.Max(0, errCount - 1);

        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return;

        if (MiaoNetModule.Settings.ShowOwnName)
        {
            if (selfNameTag is null)
            {
                selfNameTag = new(player, ClientState.Self.Info.Name);
                selfNameTag.Tag |= Tags.Persistent |
                    Tags.TransitionUpdate |
                    Tags.FrozenUpdate |
                    Tags.PauseUpdate |
                    Tags.Global;
                player.Scene.Add(selfNameTag);
            }
            else if (selfNameTag.Scene != player.Scene)
            {
                selfNameTag.RemoveSelf();
                player.Scene.Add(selfNameTag);
            }
            selfNameTag.Entity = player;
        }
        else
        {
            selfNameTag?.RemoveSelf();
            selfNameTag = null;
        }

        // do not send frame when paused
        if (level.Paused)
            return;

        bool currentDashing = player.StateMachine.State is Player.StDash;
        int currentDashes = player.Dashes;

        PlayerState? selfState = self.State;
        if (selfState is null)
            return;

        FollowerInfo[]? followerInitials = null;
        FollowerInfoDelta[]? followerDeltas = null;

        FFlags flags = FFlags.None;
        if (player.Facing is Facings.Left)
            flags |= FFlags.FacingLeft;
        if (currentDashing)
            flags |= FFlags.Dashing;
        if (currentDashes != selfState.Dashes)
            flags |= FFlags.DashesChange;
        if (player.Holding is not null)
            flags |= FFlags.HasHoldable;
        if (player.StateMachine.State == Player.StStarFly)
            flags |= FFlags.StarFlying;

        if (DynamicData.For(player.Leader).Get(MiaoNetModule.LeaderFollowersDirtyField) as bool? is not false)
        {
            flags |= FFlags.HasFollowerInitials;
            followerInitials = FetchFollowerInitials(player.Leader);
        }
        else if (player.Leader.Followers.Count > 0)
        {
            flags |= FFlags.HasFollowerDeltas;
            followerDeltas = FetchFollowerDeltas(player.Leader);
        }

        DynamicData.For(player.Leader).Set(MiaoNetModule.LeaderFollowersDirtyField, false);
        SafeGuard.Assert(!(flags.HasFlag(FFlags.HasFollowerInitials) && flags.HasFlag(FFlags.HasFollowerDeltas)));

        selfState.Dashing = currentDashing;
        selfState.Dashes = (byte)currentDashes;
        if (followerInitials is not null)
            selfState.ApplyFollowersInitials(followerInitials);
        else if (followerDeltas is not null)
            selfState.ApplyFollowersDeltas(followerDeltas);

        var packetFrame = new PacketPlayerFrame(
            player.Position,
            player.Sprite.CurrentAnimationID,
            (ushort)player.Sprite.CurrentAnimationFrame,
            player.Sprite.Scale,
            flags
        );

        if (packetFrame.DashesChange)
            packetFrame.Dashes = (byte)currentDashes;
        if (packetFrame.HasHoldable)
            packetFrame.HoldableInfo = FetchHoldableInfo(player.Holding!);
        if (packetFrame.Dashing)
            packetFrame.DashDirection = (byte)(player.DashDir.Angle() / MathF.Tau * byte.MaxValue);
        if (packetFrame.HasFollowerInitials)
            packetFrame.FollowerInitials = followerInitials;
        else if (packetFrame.HasFollowerDeltas)
            packetFrame.FollowerDeltas = followerDeltas;
        context.QueuePacket(packetFrame);
    }

    private static HoldableInfo FetchHoldableInfo(Holdable holdable)
    {
        Entity entity = holdable.Entity;
        if (entity is Glider jelly)
        {
            Sprite spr = jelly.Get<Sprite>();
            return new(
                HoldableType.Jelly,
                spr.CurrentAnimationID, (ushort)spr.CurrentAnimationFrame,
                spr.Scale, spr.Rotation
            );
        }
        else if (entity is TheoCrystal)
        {
            return new(HoldableType.Theo);
        }
        else
        {
            return new(HoldableType.None);
        }
    }

    private static FollowerInfo[] FetchFollowerInitials(Leader leader)
    {
        var array = new FollowerInfo[leader.Followers.Count];
        for (int i = 0; i < array.Length; i++)
            array[i] = FetchFollowerInitial(leader.Entity.Position, leader.Followers[i]);
        return array;

        static FollowerInfo FetchFollowerInitial(Vector2 leaderEntityPosition, Follower follower)
        {
            Entity entity = follower.Entity;
            FollowerType type = entity switch
            {
                Strawberry => FollowerType.Strawberry,
                StrawberrySeed => FollowerType.StrawberrySeed,
                Key => FollowerType.Key,
                _ => FollowerType.Custom
            };
            Sprite spr = entity.Get<Sprite>();

            // TODO Strawberry Jam's RefillShard's sprite only contains Path
            string sprID = SpriteIDTracker.LookupID(spr) ?? string.Empty;
            return new FollowerInfo(
                type, sprID,
                spr.CurrentAnimationID, (ushort)spr.CurrentAnimationFrame,
                offset: (Vector2S)(entity.Position - leaderEntityPosition)
            );
        }
    }

    private static FollowerInfoDelta[] FetchFollowerDeltas(Leader leader)
    {
        var array = new FollowerInfoDelta[leader.Followers.Count];
        for (int i = 0; i < array.Length; i++)
            array[i] = FetchFollowerDelta(leader.Entity.Position, leader.Followers[i]);
        return array;

        static FollowerInfoDelta FetchFollowerDelta(Vector2 leaderEntityPosition, Follower follower)
        {
            Entity entity = follower.Entity;
            Sprite spr = entity.Get<Sprite>();
            Vector2 offset = entity.Position - leaderEntityPosition;
            return new(
                spr.CurrentAnimationID,
                (ushort)spr.CurrentAnimationFrame,
                (Vector2S)offset
            );
        }
    }

    private bool TryGetAndSendState(Level level, PlayerLocation location)
    {
        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
        {
            PlayerDeadBody? body = (PlayerDeadBody?)level.Entities.FirstOrDefault(e => e is PlayerDeadBody);
            if (body is not null)
                player = body.player;
            else
                return false;
        }
        PlayerState initialState = new PlayerState(player.Position, (byte)player.Dashes, Engine.DeltaTime)
        {
            PlayerSpriteMode = player.Sprite.Mode,
            FollowerInfos = FetchFollowerInitials(player.Leader)
        };
        // FIXME server maybe late to ack this
        ClientState.SelfState = initialState;
        PacketPlayerMapChanged p = new(location, initialState);
        context.QueuePacket(p);
        return true;
    }

    #region event handlers
    private void MiaoNetModule_OnPlayerLocationChanged(PlayerLocation location, bool forceFullChange)
    {
        if (!HasState)
            return;
        if (location.IsEmpty && ClientState.Self.OnlineStatus != PlayerOnlineStatus.Normal)
        {
            ClientState.Self.OnlineStatus = PlayerOnlineStatus.Normal;
            context.QueuePacket(new PacketUpdateOnlineStatus(PlayerOnlineStatus.Normal));
        }
        var changeResult = ClientState.OnPlayerLocationChanged(location);
        if (changeResult is PlayerLocation.ChangeResult.All || forceFullChange)
        {
            if (location.IsInMap)
            {
                Scene scene = Engine.Scene;
                if (scene is Level level)
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
                else if (scene is LevelLoader levelLoader)
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
                PacketPlayerMapChanged p = new(location, null);
                context.QueuePacket(p);
            }
        }
        else if (changeResult is PlayerLocation.ChangeResult.RoomOnly)
        {
            PacketPlayerMapRoomChanged p = new(location.MapRoom);
            context.QueuePacket(p);
        }
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (!ClientState.Self.ShouldSyncFrom(player))
            return;
        if (!ghosts.Remove(player.Info.ID, out MiaoNetGhost? ghost))
        {
            Logger.Warn(LT.MiaoNet, $"Try removing a player({player.Info}) which is not exists.");
            return;
        }
        ghost.RemoveSelf();
    }

    private void Context_PlayerFrameNotification(OnlinePlayer player, PacketPlayerFrame packet)
    {
        if (Engine.Scene is Editor.MapEditor)
            return;

        if (ghosts.TryGetValue(player.Info.ID, out var ghost))
        {
            ghost.Position = packet.Position;

            ghost.UpdateSprite(packet.Animation, packet.AnimationFrame, packet.FacingLeft, packet.Scale);
            if (packet.HasHoldable)
            {
                var hi = packet.HoldableInfo;
                if (hi.Type == HoldableType.Jelly)
                    ghost.UpdateHoldable(
                        hi.Type,
                        hi.Animation,
                        hi.AnimationFrame,
                        hi.Scale,
                        hi.Rotation
                    );
                else
                    ghost.UpdateSimpleHoldable(hi.Type);
            }
            else
            {
                ghost.UpdateNoHoldable();
            }
            if (packet.HasFollowerInitials)
                ghost.OnFollowerInitials(packet.FollowerInitials);
            else if (packet.HasFollowerDeltas)
                ghost.OnFollowerDeltas(packet.FollowerDeltas);

            ghost.UpdateDashing(
                packet.Dashing, packet.DashDirection / (float)byte.MaxValue * MathF.Tau,
                packet.DashesChange, packet.Dashes
            );
            ghost.NotifyStarFlying(packet.StarFlying);
        }
        else
        {
            Logger.Warn(LT.MiaoNet, $"Notified but ghost does not exists for {player.Info}");
            // TODO something that records the warning times
            // if there are so many warnings then we may have to
            // disconnect from the server (a server or client bug?)
            //OnWarn();
        }
    }

    private void Context_PlayerStateFlagsNotification(OnlinePlayer player, PacketPlayerStateFlags.StateFlags flags)
    {
        if (ghosts.TryGetValue(player.Info.ID, out var ghost))
        {
            if (flags.HasFlag(PacketPlayerStateFlags.StateFlags.PlayerDied))
                ghost.OnDied();
            if (flags.HasFlag(PacketPlayerStateFlags.StateFlags.PlayerRespawning))
                ghost.OnRespawning();
        }
        else
        {
            Logger.Warn(LT.MiaoNet, $"Flgas notified but ghost does not exists for {player.Info}");
        }
    }

    private void Context_PlayerMapChanged(OnlinePlayer player, PacketPlayerMapChangedNotification packet)
    {
        Logger.Debug(LT.MiaoNet, $"Player map changed: {player}, state: {packet.InitialState}.");
        HandleLocationChanging(player, packet.GraphicsInfo, packet.InitialState);
    }

    private void Context_PlayerMapRoomChanged(OnlinePlayer player, string room)
    {
        Logger.Debug(LT.MiaoNet, $"Player map room changed: {player}.");
        HandleLocationChanging(player, null, null);
    }

    private void Context_PlayerMapChangeResponded(PacketPlayerMapChangedResponse packet)
    {
        Logger.Debug(LT.MiaoNet, $"Map changed responded, players count: {packet.PlayersInMap.Length}");
        foreach (var item in packet.PlayersInMap)
        {
            OnlinePlayer player = ClientState.GetPlayer(item.PlayerID);
            HandleLocationChanging(player, player.GraphicsInfo, player.State);
        }
    }

    private void Context_PlayerOnlineStatusChanged(OnlinePlayer player, PlayerOnlineStatus previousStatus)
    {
        if (ghosts.TryGetValue(player.ID, out var ghost))
        {
            ghost.OnUpdateOnlineStatus(player.OnlineStatus);
        }
    }

    private void HandleLocationChanging(OnlinePlayer other, PlayerGraphicsInfo? graphicsInfo, PlayerState? initialState)
    {
        // TODO check if there're unused ghosts?
        // TODO do not handle ghost adding/removing here
        if (Engine.Scene is Editor.MapEditor)
            return;

        bool needGhost = ClientState.Self.ShouldSyncFrom(other);
        Logger.Debug(LT.MiaoNet, $"needGhost of {other.Info} = {needGhost}");

        Level? level = Engine.Scene as Level;

        if (needGhost)
        {
            SafeGuard.Assert(level is not null);
        }

        if (ghosts.TryGetValue(other.ID, out MiaoNetGhost? ghost))
        {
            if (needGhost)
            {
                ghost.GraphicsInfo = graphicsInfo;
                if (initialState is not null)
                    ghost.ApplyState(initialState);
                level!.Add(ghost);
            }
            else
            {
                ghost.RemoveSelf();
                ghosts.Remove(other.ID);
            }
        }
        else
        {
            if (needGhost)
            {
                if (initialState is null)
                {
                    // the server maybe late to know that we're already in a same map
                    // but...
                    // TODO make local state changes wait for server to confirm
                    return;
                }
                ghosts[other.ID] = ghost = new(other, other.Info.Name, graphicsInfo, initialState!);
                level!.Add(ghost);
                Logger.Debug(LT.MiaoNet, $"added ghost for {other.Info}!");
            }
        }
    }
    #endregion

    // should we expose the ghost entity...?
    public bool TryGetGhost(int playerID, [NotNullWhen(true)] out MiaoNetGhost? ghost)
        => ghosts.TryGetValue(playerID, out ghost);
}