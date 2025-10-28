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
    private readonly Dictionary<int, PlayerGraphicsInfo> graphicsInfos;
    private readonly Dictionary<int, MiaoNetGhost> ghosts;

    public MiaoNetMainComponent(MiaoNetContext context) : base(context)
    {
        graphicsInfos = new();
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

        if (Engine.Scene is not Level level) return;
        var player = level.Tracker.GetEntity<Player>();
        if (player is null) return;
        context.QueuePacket(
            new PacketPlayerMapChanged(
                level.Session.Area.SID,
                level.Session.Level,
                new(player.X, player.Y, (byte)player.Dashes)
            )
        );
    }

    public override void OnDisconnected()
    {
        graphicsInfos.Clear();
        foreach (var pair in ghosts)
            pair.Value.RemoveSelf();
        ghosts.Clear();
    }

    private void Context_ClientInitialized(ClientState clientState)
    {
        foreach ((_, var player) in clientState.Players.Where(p => p.Key != clientState.Self.ID))
            HandleNewPlayer(player);
    }

    private void Context_PlayerJoined(OnlinePlayer player)
        => HandleNewPlayer(player);

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (player.LocationInfo.MapSid != context.ClientState!.Self.LocationInfo.MapSid)
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

        HandleLocationChanging(player.Channel.ID, player.Info, player.LocationInfo, packet.GraphicsInfo, packet.InitialState);
    }

    private void HandleNewPlayer(OnlinePlayer player)
    {
        Logger.Info(nameof(MiaoNet), $"New player joined: {player.Info}, locationInfo: {player.LocationInfo}.");
        HandleLocationChanging(player.Channel.ID, player.Info, player.LocationInfo, player.GraphicsInfo, player.State);
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
        int channelID, PlayerInfo info, PlayerLocationInfo locationInfo,
        PlayerGraphicsInfo? graphicsInfo, PlayerState? initialState
    )
    {
        var state = context.ClientState!;
        Logger.Info(
            nameof(MiaoNet), $"Location changing... " +
            $"Channel: {channelID}, Player: {info}, LocationInfo: {locationInfo}."
        );
        bool needGhost = !string.IsNullOrEmpty(locationInfo.MapSid) &&
            channelID == state.SelfChannel.ID &&
            locationInfo.MapSid == state.Self.LocationInfo.MapSid;
        Logger.Info(nameof(MiaoNet), $"Need create ghost? {needGhost}");
        if (ghosts.TryGetValue(info.ID, out MiaoNetGhost? ghost))
        {
            if (needGhost)
            {
                //ghost.GraphicsInfo = graphicsInfo;
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
                ghosts[info.ID] = new(info.ID, info.Name, graphicsInfo, initialState!);
            }
        }
    }

    public override void Update()
    {
        base.Update();
        if (Engine.Scene is not Level level)
            return;
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

        FFlags flags = 0;
        if (player.Facing is Facings.Left)
            flags |= FFlags.FacingLeft;
        if (player.StateMachine.State is Player.StDash)
            flags |= FFlags.Dashing;

        var packetFrame = new PacketPlayerFrame(
                player.Position.X,
                player.Position.Y,
                (ushort)player.Sprite.CurrentAnimationFrame,
                (ushort)animID,
                player.Sprite.Scale.X, player.Sprite.Scale.Y,
                flags
            );
        context.QueuePacket(packetFrame);
    }

    public override void Render()
    {
        if (context.ClientState is null)
            return;
        var channels = context.ClientState.Channels;
        int i = 0;
        int m = Draw.DefaultFont.LineSpacing;
        foreach ((_, var channel) in channels)
        {
            Draw.Text(Draw.DefaultFont, channel.ToString(), new Vector2(10, m * i), Color.Red);
            i += 1;
            foreach ((_, var player) in channel.Players)
            {
                Draw.Text(Draw.DefaultFont, player.ToString(), new Vector2(20, m * i), Color.Green);
                i += 1;
            }
        }
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