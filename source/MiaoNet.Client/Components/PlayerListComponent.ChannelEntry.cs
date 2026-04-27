using System.Diagnostics.CodeAnalysis;

namespace Celeste.Mod.MiaoNet;

public sealed partial class PlayerListComponent
{
    private sealed class PlayerListChannelEntry
    {
        public readonly OnlineChannel Channel;
        public readonly List<PlayerListEntry> Players;
        public string Header;

        public PlayerListChannelEntry(OnlineChannel channel, List<PlayerListEntry> players)
        {
            Channel = channel;
            Players = players;
            Update();
        }

        [MemberNotNull(nameof(Header))]
        public void Update()
        {
            Header = PFormat.Format(
                Dialog.Get("miaonet_player_list_channel_header"),
                Channel.Name,
                Players.Count
            );
        }
    }
}