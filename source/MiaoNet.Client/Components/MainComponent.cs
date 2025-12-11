using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;
using FFlags = MiaoNet.Shared.PacketPlayerFrame.FrameFlags;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Main component, handle player sync
/// </summary>
public sealed class MainComponent : MiaoNetComponent
{
    private bool pendingMapChanged;
    private readonly Dictionary<int, MiaoNetGhost> ghosts;

    public MainComponent(MiaoNetContext context) : base(context)
    {
        ghosts = new();

        context.PlayerLeft += Context_PlayerLeft;
        context.PlayerFrameNotification += Context_PlayerFrameNotification;
        context.PlayerMapChanged += Context_PlayerMapChanged;
        context.PlayerMapRoomChanged += Context_PlayerMapRoomChanged;
        context.PlayerMapChangeResponded += Context_PlayerMapChangeResponded;
        context.PlayerStateFlagsNotification += Context_PlayerStateFlagsNotification;

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

        if (!KnownPlayerAnimations.StringToID.TryGetValue(player.Sprite.CurrentAnimationID, out var animID))
        {
            // TODO extendable
            animID = ushort.MaxValue;
        }

        bool currentDashing = player.StateMachine.State is Player.StDash;
        int currentDashes = player.Dashes;

        PlayerState? selfState = ClientState.SelfState!;

        FFlags flags = 0;
        if (player.Facing is Facings.Left)
            flags |= FFlags.FacingLeft;
        if (currentDashing && !selfState.Dashing)
            flags |= FFlags.StartDash;
        if (!currentDashing && selfState.Dashing)
            flags |= FFlags.EndDash;
        if (currentDashes != selfState.Dashes)
            flags |= FFlags.DashesChange;

        selfState.Dashing = currentDashing;
        selfState.Dashes = (byte)currentDashes;

        var packetFrame = new PacketPlayerFrame(
            player.Position,
            (ushort)player.Sprite.CurrentAnimationFrame,
            (ushort)animID,
            player.Sprite.Scale,
            flags
        );
        if (packetFrame.DashesChange)
            packetFrame.Dashes = (byte)currentDashes;
        context.QueuePacket(packetFrame);
    }

    private bool TryGetAndSendState(Level level, PlayerLocation location)
    {
        Player player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return false;
        PlayerState initialState = new PlayerState(player.Position, (byte)player.Dashes, Engine.DeltaTime);
        initialState.PlayerSpriteMode = player.Sprite.Mode;
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
            string? sid = null;
            if (packet.AnimationID != ushort.MaxValue)
                KnownPlayerAnimations.IDToString.TryGetValue(packet.AnimationID, out sid);

            ghost.UpdateSprite(packet.AnimationFrame, sid, packet.FacingLeft, packet.Scale);
            if (packet.Flags.HasFlag(FFlags.StartDash))
                ghost.OnStartDash();
            if (packet.Flags.HasFlag(FFlags.EndDash))
                ghost.OnEndDash();
            if (packet.Flags.HasFlag(FFlags.DashesChange))
                ghost.OnDashesChange(packet.Dashes);
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
        Logger.Info(nameof(MiaoNet), $"Player map changed: {player}, state: {packet.InitialState}.");
        HandleLocationChanging(player, packet.GraphicsInfo, packet.InitialState);
    }

    private void Context_PlayerMapRoomChanged(OnlinePlayer player, string room)
    {
        Logger.Debug(nameof(MiaoNet), $"Player map room changed: {player}.");
        HandleLocationChanging(player, null, null);
    }

    private void Context_PlayerMapChangeResponded(PacketPlayerMapChangedResponse packet)
    {
        Logger.Info(nameof(MiaoNet), $"Map changed responed, players count: {packet.PlayersInMap.Length}");
        foreach (var item in packet.PlayersInMap)
        {
            OnlinePlayer player = ClientState.Players[item.PlayerID];
            HandleLocationChanging(player, player.GraphicsInfo, player.State);
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