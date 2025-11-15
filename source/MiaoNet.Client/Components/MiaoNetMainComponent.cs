using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;
using FFlags = MiaoNet.Shared.PacketPlayerFrame.FrameFlags;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Main component, handle player sync
/// </summary>
public sealed class MiaoNetMainComponent : MiaoNetComponent
{
    private int errCount;
    private readonly Dictionary<int, MiaoNetGhost> ghosts;

    public MiaoNetMainComponent(MiaoNetContext context) : base(context)
    {
        ghosts = new();

        context.ClientInitialized += Context_ClientInitialized;
        context.PlayerJoined += Context_PlayerJoined;
        context.PlayerLeft += Context_PlayerLeft;
        context.PlayerFrameNotification += Context_PlayerFrameNotification;
        context.PlayerMapChanged += Context_PlayerMapChanged;
        context.PlayerMapChangeResponse += Context_PlayerMapChangeResponse;
    }

    public override void OnConnected()
    {
        errCount = 0;
    }

    public override void OnDisconnected()
    {
        foreach (var pair in ghosts)
            pair.Value.RemoveSelf();
        ghosts.Clear();
    }

    private void Context_ClientInitialized(ClientState clientState)
    {
        foreach ((_, var player) in clientState.Players.Where(p => p.Key != clientState.Self.ID))
            HandleNewPlayer(player);
        if (Engine.Scene is Level level)
            context.OnPlayerLocationChanged(level, PlayerLocation.FetchFrom(level.Session));
    }

    private void Context_PlayerJoined(OnlinePlayer player)
        => HandleNewPlayer(player);

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (player.Location.MapSid != context.ClientState!.Self.Location.MapSid)
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
        if (ghosts.TryGetValue(player.Info.ID, out var ghost))
        {
            ghost.Position = new(packet.X, packet.Y);
            string? sid = null;
            if (packet.AnimationID != ushort.MaxValue)
                KnownPlayerAnimations.IDToString.TryGetValue(packet.AnimationID, out sid);

            ghost.UpdateSprite(packet.AnimationFrame, sid, packet.FacingLeft, packet.ScaleX, packet.ScaleY);
            if (packet.Flags.HasFlag(FFlags.StartDash))
                ghost.OnStartDash();
            if (packet.Flags.HasFlag(FFlags.EndDash))
                ghost.OnEndDash();
            if (packet.Flags.HasFlag(FFlags.DashesChange))
                ghost.OnDashesChange(packet.Dashes);
        }
        else
        {
            Logger.Warn(nameof(MiaoNet), $"Notified but ghost not exists: {player.Info}");
            OnWarn();
        }
    }

    private void Context_PlayerMapChanged(OnlinePlayer player, PacketPlayerMapChangedNotification packet)
    {
        Logger.Info(nameof(MiaoNet), $"Player map changed: {player}.");
        HandleLocationChanging(player.Channel.ID, player.Info, player.Location, packet.GraphicsInfo, packet.InitialState);
    }

    private void HandleNewPlayer(OnlinePlayer player)
    {
        Logger.Info(nameof(MiaoNet), $"New player joined: {player.Info}, locationInfo: {player.Location}.");
        HandleLocationChanging(player.Channel.ID, player.Info, player.Location, player.GraphicsInfo, player.State);
    }

    private void Context_PlayerMapChangeResponse(PacketPlayerMapChangedResponse packet)
    {
        foreach (var item in packet.PlayersInMap)
        {
            OnlinePlayer player = context.ClientState!.Players[item.PlayerID];
            player.State = item.State;
            player.GraphicsInfo = item.GraphicsInfo;
            ghosts[player.ID] = new(player.ID, player.Info.Name, player.GraphicsInfo, player.State);
        }
    }

    private void HandleLocationChanging(
        int channelID, PlayerInfo info, PlayerLocation location,
        PlayerGraphicsInfo? graphicsInfo, PlayerState? initialState
    )
    {
        var state = context.ClientState!;
        Logger.Info(
            nameof(MiaoNet), $"Location changing... " +
            $"Channel: {channelID}, Player: {info}, LocationInfo: {location}."
        );
        bool needGhost = !string.IsNullOrEmpty(location.MapSid) &&
            channelID == state.SelfChannel.ID &&
            location.MapSid == state.Self.Location.MapSid;
        Logger.Info(nameof(MiaoNet), $"Need create ghost? {needGhost}");

        if (ghosts.TryGetValue(info.ID, out MiaoNetGhost? ghost))
        {
            if (needGhost)
            {
                ghost.GraphicsInfo = graphicsInfo;
            }
            else
            {
                ghost.RemoveSelf();
                ghosts.Remove(ghost.PlayerID);
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
                    // TODO need we make local state changes to wait for server to confirm?
                    return;
                }
                ghosts[info.ID] = new(info.ID, info.Name, graphicsInfo, initialState!);
            }
        }
    }

    public override void Update()
    {
        base.Update();
        SafeGuard.Assert(context.HasState);
        if (Engine.Scene is not Level level)
            return;
        if (level.OnRawInterval(1f))
            errCount = Math.Max(0, errCount - 1);
        foreach (var pair in ghosts)
        {
            if (pair.Value.Scene != level)
            {
                pair.Value.RemoveSelf();
                level.Add(pair.Value);
            }
        }
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

        PlayerState selfState = context.ClientState.Self.State!;
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
            player.Position.X,
            player.Position.Y,
            (ushort)player.Sprite.CurrentAnimationFrame,
            (ushort)animID,
            player.Sprite.Scale.X, player.Sprite.Scale.Y,
            flags
        );
        if (packetFrame.DashesChange)
            packetFrame.Dashes = (byte)currentDashes;
        context.QueuePacket(packetFrame);
    }

    private void OnWarn()
    {
        errCount++;
        if (errCount > 120)
        {
            Logger.Error(nameof(MiaoNet), "Warning too many times, disconnect.");
            context.Disconnect();
        }
    }
}