using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class PlayerListComponent
{
    public class PlayerListEntry : IPlayerListEntry
    {
        private static readonly Color DefaultColor = Color.LightGray;

        public readonly OnlinePlayer Player;
        public string DisplayName;
        public string? MapName;
        public string? MapRoom;
        public bool IsLocallyKnownMap;
        public Color MapNameColor = DefaultColor;
        public Color MapSideColor = DefaultColor;
        public MTexture? AreaIconTexture;
        public string? AreaModeText;
        public string? PingText;

        PlayerLocation IPlayerListEntry.Location => Player.Location;

        bool IPlayerListEntry.IsLocallyKnownMap => IsLocallyKnownMap;

        PlayerInfo IPlayerListEntry.PlayerInfo => Player.Info;

        public PlayerListEntry(OnlinePlayer player, bool showAvatar, ClipType clipType)
        {
            Player = player;
            DisplayName = player.GetDisplayName(true, showAvatar);
            Update(clipType);
        }

        public void Update(ClipType clipType)
        {
            PlayerLocation loc = Player.Location;
            if (loc.IsEmpty)
            {
                IsLocallyKnownMap = true;
                MapName = null;
                MapRoom = null;
                AreaIconTexture = null;
                AreaModeText = null;
                MapNameColor = MapSideColor = DefaultColor;
            }
            else
            {
                AreaModeText = loc.Map.AreaModeCharacter.ToString();

                var areaData = AreaData.Get(loc.Map.Sid);
                if (areaData is not null)
                {
                    IsLocallyKnownMap = true;
                    MapName = Dialog.Get(areaData.Name);
                    MapRoom = Clip(loc.Room, clipType);

                    string iconPath = areaData.Icon;
                    string? lobbySid;
                    AreaData? lobbyAreaData;
                    if (
                        (lobbySid = CollabUtils2Interop.GetLobbyForMap?.Invoke(loc.Map.Sid)) is not null &&
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
                    MapName = Clip(loc.Map.Sid, clipType);
                    MapRoom = Clip(loc.Room, clipType);
                    AreaIconTexture = null;
                    MapNameColor = MapSideColor = DefaultColor;
                }
            }
            UpdatePing();
        }

        public void UpdatePing()
            => PingText = Player.LastPing == -1 ? null : $"{Player.LastPing}ms";

        private static string Clip(string str, ClipType clipType)
        {
            const int ClipLength = 24;
            if (str.Length > ClipLength)
            {
                return clipType switch
                {
                    ClipType.None => str,
                    ClipType.KeepPrefix => $"{str[..ClipLength]}...",
                    ClipType.KeepSuffix => $"...{str.Substring(str.Length - ClipLength)}",
                };
            }
            else
            {
                return str;
            }
        }
    }
}
