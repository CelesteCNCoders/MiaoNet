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
            MiaoNetModule_OnPlayerLocationChanged(PlayerLocation.FetchFrom(level.Session));
        if (Engine.Scene is Editor.MapEditor editor)
            MiaoNetModule_OnPlayerLocationChanged(new PlayerLocation(editor.mapData.Area, string.Empty));
    }

    public override void OnDisconnected()
    {
        foreach (var pair in ghosts)
            pair.Value.RemoveSelf();
        ghosts.Clear();
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
                selfNameTag.Tag |= Tags.Global;
                player.Scene.Add(selfNameTag);
            }
            selfNameTag.Entity = player;
        }
        else
        {
            selfNameTag?.RemoveSelf();
            selfNameTag = null;
        }

        bool currentDashing = player.StateMachine.State is Player.StDash;
        int currentDashes = player.Dashes;

        PlayerState? selfState = self.State;
        if (selfState is null)
            return;

        FFlags flags = 0;
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
            flags |= FFlags.HasFollowerInitials;
        else if (player.Leader.Followers.Count > 0)
            flags |= FFlags.HasFollowerDeltas;
        DynamicData.For(player.Leader).Set(MiaoNetModule.LeaderFollowersDirtyField, false);

        SafeGuard.Assert(!(flags.HasFlag(FFlags.HasFollowerInitials) && flags.HasFlag(FFlags.HasFollowerDeltas)));

        selfState.Dashing = currentDashing;
        selfState.Dashes = (byte)currentDashes;

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
            packetFrame.FollowerInitials = FetchFollowerInitials(player.Leader);
        else if (packetFrame.HasFollowerDeltas)
            packetFrame.FollowerDeltas = FetchFollowerDeltas(player.Leader);
        context.QueuePacket(packetFrame);

        {
            if (level.Paused)
                PauseUpdatedBurst.Update(level.Displacement);
        }
    }

    private HoldableInfo FetchHoldableInfo(Holdable holdable)
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

    private FollowerInfo[] FetchFollowerInitials(Leader leader)
    {
        var array = new FollowerInfo[leader.Followers.Count];
        for (int i = 0; i < array.Length; i++)
            array[i] = FetchFollowerInitial(leader.Entity.Position, leader.Followers[i]);
        return array;

        FollowerInfo FetchFollowerInitial(Vector2 leaderEntityPosition, Follower follower)
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

            string sprID = SpriteIDTracker.LookupID(spr) ?? throw new NullReferenceException();
            return new FollowerInfo(
                type, sprID,
                spr.CurrentAnimationID, (ushort)spr.CurrentAnimationFrame,
                offset: entity.Position - leaderEntityPosition
            );
        }
    }

    private FollowerInfoDelta[] FetchFollowerDeltas(Leader leader)
    {
        var array = new FollowerInfoDelta[leader.Followers.Count];
        for (int i = 0; i < array.Length; i++)
            array[i] = FetchFollowerDelta(leader.Entity.Position, leader.Followers[i]);
        return array;

        FollowerInfoDelta FetchFollowerDelta(Vector2 leaderEntityPosition, Follower follower)
        {
            Entity entity = follower.Entity;
            Sprite spr = entity.Get<Sprite>();
            var mgr = PooledStringManager;
            Vector2 offset = entity.Position - leaderEntityPosition;
            return new(
                spr.CurrentAnimationID,
                (ushort)spr.CurrentAnimationFrame,
                (short)offset.X, (short)offset.Y
            );
        }
    }

    private bool TryGetAndSendState(Level level, PlayerLocation location)
    {
        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return false;
        PlayerState initialState = new PlayerState(player.Position, (byte)player.Dashes, Engine.DeltaTime)
        {
            PlayerSpriteMode = player.Sprite.Mode,
            FollowerInfos = FetchFollowerInitials(player.Leader)
        };
        ClientState.SelfState = initialState;
        PacketPlayerMapChanged p = new(location, initialState);
        context.QueuePacket(p);
        return true;
    }

    #region event handlers
    private void MiaoNetModule_OnPlayerLocationChanged(PlayerLocation location)
    {
        if (!HasState)
            return;
        switch (ClientState.OnPlayerLocationChanged(location))
        {
        case PlayerLocation.ChangeResult.RoomOnly:
        {
            PacketPlayerMapRoomChanged p = new(location.MapRoom);
            context.QueuePacket(p);
            break;
        }
        case PlayerLocation.ChangeResult.FromDebugMap:
        case PlayerLocation.ChangeResult.All:
        {
            if (!location.IsInMap)
            {
                PacketPlayerMapChanged p = new(location, null);
                context.QueuePacket(p);
                break;
            }
            Scene scene = Engine.Scene;
            if (scene is Level level)
            {
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
                    Logger.Warn(nameof(MiaoNet), "pendingMapChanged is still true, is this a bug?");
                pendingMapChanged = true;
            }
            else
            {
                ClientState.SelfState = null;
            }
            break;
        }
        }
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (!ClientState.Self.ShouldSyncFrom(player))
            return;
        if (!ghosts.Remove(player.Info.ID, out MiaoNetGhost? ghost))
        {
            Logger.Warn(nameof(MiaoNet), $"Try removing a player({player.Info}) which is not exists.");
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
            // TODO hmm... pass a PooledStringManager is weird
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
            Logger.Warn(nameof(MiaoNet), $"Notified but ghost does not exists for {player.Info}");
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
            Logger.Warn(nameof(MiaoNet), $"Flgas notified but ghost does not exists for {player.Info}");
        }
    }

    private void Context_PlayerMapChanged(OnlinePlayer player, PacketPlayerMapChangedNotification packet)
    {
        Logger.Debug(nameof(MiaoNet), $"Player map changed: {player}, state: {packet.InitialState}.");
        HandleLocationChanging(player, packet.GraphicsInfo, packet.InitialState);
    }

    private void Context_PlayerMapRoomChanged(OnlinePlayer player, string room)
    {
        Logger.Debug(nameof(MiaoNet), $"Player map room changed: {player}.");
        HandleLocationChanging(player, null, null);
    }

    private void Context_PlayerMapChangeResponded(PacketPlayerMapChangedResponse packet)
    {
        Logger.Debug(nameof(MiaoNet), $"Map changed responed, players count: {packet.PlayersInMap.Length}");
        foreach (var item in packet.PlayersInMap)
        {
            OnlinePlayer player = ClientState.Players[item.PlayerID];
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
        if (Engine.Scene is Editor.MapEditor)
            return;

        bool needGhost = ClientState.Self.ShouldSyncFrom(other);
        Logger.Debug(nameof(MiaoNet), $"needGhost of {other.Info} = {needGhost}");

        Level? level = Engine.Scene as Level;

        if (needGhost)
            SafeGuard.Assert(level is not null);

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
                Logger.Debug(nameof(MiaoNet), $"added ghost for {other.Info}!");
            }
        }
    }
    #endregion

    // should we expose the ghost entity...?
    public bool TryGetGhost(int playerID, [NotNullWhen(true)] out MiaoNetGhost? ghost)
        => ghosts.TryGetValue(playerID, out ghost);
}