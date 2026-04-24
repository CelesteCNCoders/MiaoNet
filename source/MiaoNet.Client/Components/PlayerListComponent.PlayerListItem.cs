using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class PlayerListComponent
{
    public class PlayerListItem : IPlayerListEntry
    {
        private static readonly Color DefaultColor = Color.LightGray;

        public OnlinePlayer Player;
        public string DisplayName;
        public string? MapName;
        public bool IsLocallyKnownMap;
        public Color MapNameColor = DefaultColor;
        public Color MapSideColor = DefaultColor;
        public MTexture? AreaIconTexture;
        public string? AreaSideText;
        public string? PingText;

        PlayerLocation IPlayerListEntry.Location => Player.Location;

        bool IPlayerListEntry.IsLocallyKnownMap => IsLocallyKnownMap;

        PlayerInfo IPlayerListEntry.PlayerInfo => Player.Info;

        public PlayerListItem(OnlinePlayer player, bool showAvatar)
        {
            Player = player;
            DisplayName = player.GetDisplayName(true, showAvatar);
            Update();
        }

        public void Update()
        {
            PlayerLocation loc = Player.Location;
            if (loc.IsEmpty)
            {
                IsLocallyKnownMap = true;
                MapName = null;
                AreaIconTexture = null;
                AreaSideText = null;
                MapNameColor = MapSideColor = DefaultColor;
            }
            else
            {
                AreaSideText = loc.SideCharacter.ToString();

                var areaData = AreaData.Get(loc.MapSid);
                if (areaData is not null)
                {
                    IsLocallyKnownMap = true;
                    MapName = Dialog.Get(areaData.Name);

                    string iconPath = areaData.Icon;
                    string? lobbySid;
                    AreaData? lobbyAreaData;
                    if (
                        (lobbySid = CollabUtils2Interop.GetLobbyForMap?.Invoke(loc.MapSid)) is not null &&
                        (lobbyAreaData = AreaData.Get(lobbySid)) is not null
                    )
                    {
                        AreaIconTexture = GFX.Gui.GetOrDefault(lobbyAreaData.Icon, null);
                    }
                    else
                    {
                        AreaIconTexture = GFX.Gui.GetOrDefault(iconPath, null);
                    }
                    MapNameColor = Color.Lerp(areaData.TitleBaseColor, DefaultColor, 0.5f);
                    MapSideColor = Color.Lerp(areaData.TitleAccentColor, DefaultColor, 0.8f);
                }
                else
                {
                    IsLocallyKnownMap = false;
                    MapName = loc.MapSid;
                    AreaIconTexture = null;
                    MapNameColor = MapSideColor = DefaultColor;
                }
            }
            UpdatePing();
        }

        public void UpdatePing()
            => PingText = Player.LastPing == -1 ? null : $"{Player.LastPing}ms";
    }
}
