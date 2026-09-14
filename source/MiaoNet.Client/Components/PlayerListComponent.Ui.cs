using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.PlayerList;
using Celeste.Mod.MiaoNet.UI.Rendering;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

// adapts the player list component into the XNA-free UI view model.
// lives next to the component so it can read the private channel list and icon textures
// without widening their accessibility.
public sealed partial class PlayerListComponent
{
    private PlayerListIcons? uiIcons;

    // bumped whenever something the list shows changes (roster, location, ping, a relevant
    // setting). the UI host only rebuilds its node tree when this changes.
    internal int UIVersion { get; private set; }

    internal PlayerListIcons UIIcons => uiIcons ??= new PlayerListIcons
    {
        Paused = new MiaoNetTexture(texPlayerPaused),
        Interactions = new MiaoNetTexture(texPlayerInteractions),
        LiveMode = new MiaoNetTexture(texLiveMode),
        TakingGolden = new MiaoNetTexture(texTakingGolden),
        GroupPhotoMode = new MiaoNetTexture(texGroupPhotoMode),
        DebugMap = new MiaoNetTexture(texPlayerDebugMap),
    };

    private void BumpUIVersion() => UIVersion++;

    internal List<PlayerListChannel> BuildUIChannels()
    {
        bool liveMode = MiaoNetModule.Settings.LiveMode;
        var channels = new List<PlayerListChannel>(channelPlayerList.Count);

        foreach (PlayerListChannelEntry channel in channelPlayerList)
        {
            var rows = new List<PlayerRow>(channel.Players.Count);
            foreach (PlayerListEntry entry in channel.Players)
            {
                rows.Add(BuildUIRow(entry, liveMode));
            }

            channels.Add(new PlayerListChannel
            {
                Header = channel.Header,
                Rows = rows,
            });
        }

        return channels;
    }

    private static PlayerRow BuildUIRow(PlayerListEntry entry, bool liveMode)
    {
        OnlinePlayer player = entry.Player;
        PlayerLocation location = player.Location;
        bool hasLocation = !location.IsEmpty;

        string? roomText = null;
        bool usesDebugRoomIcon = false;
        string? mapName = null;

        if (hasLocation)
        {
            if (location.IsInDebugMap)
            {
                usesDebugRoomIcon = true;
            }
            else
            {
                // live mode masks the room with "*"
                roomText = liveMode ? "*" : entry.MapRoom;
            }

            // unknown maps get masked to "*" in live mode too
            mapName = entry.IsLocallyKnownMap ? entry.MapName : liveMode ? "*" : entry.MapName;
        }

        return new PlayerRow
        {
            DisplayName = entry.DisplayName,
            NameColor = ToUIColor(player.Info.Color),
            Status = ToStatus(player.GlobalFlags),
            PingText = entry.PingText,
            HasLocation = hasLocation,
            RoomText = roomText,
            UsesDebugRoomIcon = usesDebugRoomIcon,
            MapName = mapName,
            MapNameColor = ToUIColor(entry.MapNameColor),
            AreaModeText = entry.AreaModeText,
            MapSideColor = ToUIColor(entry.MapSideColor),
            AreaIcon = entry.AreaIconTexture is { } icon ? new MiaoNetTexture(icon) : null,
        };
    }

    private static UIColor ToUIColor(Color color)
        => UIColor.FromBytes(color.R, color.G, color.B, color.A);

    private static PlayerStatus ToStatus(PlayerGlobalFlags flags)
    {
        PlayerStatus status = PlayerStatus.None;
        if (flags.HasFlag(PlayerGlobalFlags.Paused))
        {
            status |= PlayerStatus.Paused;
        }
        if (flags.HasFlag(PlayerGlobalFlags.Interactions))
        {
            status |= PlayerStatus.Interactions;
        }
        if (flags.HasFlag(PlayerGlobalFlags.LiveMode))
        {
            status |= PlayerStatus.LiveMode;
        }
        if (flags.HasFlag(PlayerGlobalFlags.TakingGolden))
        {
            status |= PlayerStatus.TakingGolden;
        }
        if (flags.HasFlag(PlayerGlobalFlags.GroupPhotoMode))
        {
            status |= PlayerStatus.GroupPhotoMode;
        }
        if (flags.HasFlag(PlayerGlobalFlags.Watching))
        {
            status |= PlayerStatus.Watching;
        }
        return status;
    }
}
