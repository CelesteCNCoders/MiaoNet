//#define MOCK_DATA
using System.Diagnostics;
using System.Text;
using Celeste.Mod.MiaoNet.UI.PlayerList;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed partial class PlayerListComponent : MiaoNetComponent
{
    public bool Active { get; set; }

    private readonly PlayerListEntryComparer pComparer;
    private readonly List<PlayerListChannelEntry> channelPlayerList;

    private readonly MTexture texPlayerPaused;
    private readonly MTexture texPlayerDebugMap;
    private readonly MTexture texPlayerInteractions;
    private readonly MTexture texLiveMode;
    private readonly MTexture texTakingGolden;
    private readonly MTexture texGroupPhotoMode;

    private static ClipType ClipType => MiaoNetModule.Settings.PlayerListMapNameClipType;

    public PlayerListComponent(MiaoNetContext context)
        : base(context)
    {
        pComparer = new();
        channelPlayerList = new();
        context.ClientInitialized += Context_ClientInitialized;
        // TODO surely full-rebuild is not necessary
        // channel created/removed events are removed
        // we may reintroduce those if needed
        context.PlayerJoined += _ => BuildPlayerList();
        context.PlayerLeft += _ => BuildPlayerList();
        context.PlayerLocationChanged += (p, _) => UpdatePlayer(p);
        context.PingDataReceived += Context_PingDataReceived;
        context.SelfChannelMoved += _ => BuildPlayerList();
        context.PlayerChannelMoved += (_, _) => BuildPlayerList();

        texPlayerDebugMap = GFX.Gui["miaonet/debug_map"];
        texPlayerPaused = GFX.Gui["miaonet/paused"];
        texPlayerInteractions = GFX.Gui["miaonet/interactions"];
        texLiveMode = GFX.Gui["miaonet/live_mode"];
        texTakingGolden = GFX.Gui["miaonet/taking_golden"];
        texGroupPhotoMode = GFX.Gui["miaonet/group_photo_mode"];

        MiaoNetModule.Settings.SettingsChanged += Settings_SettingsChanged;
    }

    private void Settings_SettingsChanged(MiaoNetModuleSettings settings, SettingsCategory category)
    {
        if (category is not SettingsCategory.PlayerList)
            return;
        if (HasState)
            BuildPlayerList();
    }

    private void BuildPlayerList()
    {
#if MOCK_DATA
        int id = 0;
        channelPlayerList.Clear();

        OnlineChannel cMain = new(0, new ChannelInfo("main"));
        List<PlayerListEntry> mainChannelPlayerList = [
            CreateTestPlayer(cMain, "sapcc", "Celeste/1-ForsakenCity", "a-01"),
            CreateTestPlayer(cMain, "Ccc", "Celeste/2-OldSite", "a-01"),
            CreateTestPlayer(cMain, "AAlice", "Celeste/LostLevels", "j-17"),
            CreateTestPlayer(cMain, "sapcc", "Celeste/LostLevels", "j-16"),
            CreateTestPlayer(cMain, "Admin", "Celeste/LostLevels", "end-golden"),
            CreateTestPlayer(cMain, "eeee", "eeee/eeee", ""),
            CreateTestPlayer(cMain, "David", "Celeste/1-ForsakenCity", "b-0c"),
            CreateTestPlayer(cMain, "Voidsd", "SpringCollab2020/Expert/ZZ-HeartSide", "idk-a"),
            CreateTestPlayer(cMain, "Mo_fish", "", ""),
            CreateTestPlayer(cMain, "Dilant", "", "")
        ];
        foreach (var item in mainChannelPlayerList)
            cMain.Players.Add(item.Player);

        OnlineChannel cOther = new(1, new ChannelInfo("xinzhan"));
        List<PlayerListEntry> otherChannelPlayerList = [
            CreateTestPlayer(cOther, "O5DZ", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "Feng_Luo", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "someone1", "Celeste/9-Core", "f-0j"),
        ];
        foreach (var item in otherChannelPlayerList)
            cOther.Players.Add(item.Player);

        OnlineChannel cOther2 = new(2, new ChannelInfo("xinzhan2"));
        List<PlayerListEntry> otherChannel2PlayerList = [
            CreateTestPlayer(cOther2, "someone2", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther2, "someone3", "Celeste/9-Core", "f-0j"),
        ];
        for (int i = 0; i < 3; i++)
            otherChannel2PlayerList.Add(CreateTestPlayer(cOther2, $"P {i}", "Celeste/9-Core", "f-0j"));
        foreach (var item in otherChannel2PlayerList)
            cOther2.Players.Add(item.Player);

        OnlineChannel pv = new(ChannelInfo.PrivateChannelVirtualID, new ChannelInfo("!<private>"));
        List<PlayerListEntry> pvChannelPlayerList = [
            CreateTestPlayer(pv, "someone4", string.Empty, string.Empty),
            CreateTestPlayer(pv, "someone5", string.Empty, string.Empty),
        ];
        foreach (var item in pvChannelPlayerList)
            pv.Players.Add(item.Player);

        channelPlayerList.AddRange([
            new(cMain, mainChannelPlayerList),
            new(cOther, otherChannelPlayerList),
            new(cOther2, otherChannel2PlayerList),
            new(pv, pvChannelPlayerList)
        ]);
        SortPlayerList();
        return;

        PlayerListEntry CreateTestPlayer(OnlineChannel channel, string name, string sid, string room)
        {
            id++;
            return new PlayerListEntry(new OnlinePlayer(
                channel, id, new PlayerInfo(id, name, string.Empty, string.Empty, Color.AntiqueWhite),
                PlayerGlobalFlags.None
            )
            {
                Location = new PlayerLocation(sid, sid.Length != 0 ? (AreaMode)Random.Shared.Next(0, 3) : AreaMode.Normal, room),
                LastPing = Random.Shared.Next(20, Random.Shared.Next(20, Random.Shared.Next(20, 2000)))
            }, false, ClipType);
        }
#else
        channelPlayerList.Clear();
        var state = ClientState;

        foreach (var (_, channel) in state.Channels)
        {
            // hide the local virtual private channel when it has no players
            if (channel.ID == ChannelInfo.PrivateChannelVirtualID && channel.Players.Count == 0)
                continue;

            var playerListEntries = new List<PlayerListEntry>();

            // add self
            if (channel == state.SelfChannel)
                playerListEntries.Add(new PlayerListEntry(state.Self, context.ShowAvatar, ClipType));

            // add other players
            foreach (var player in channel.Players)
                playerListEntries.Add(new PlayerListEntry(player, context.ShowAvatar, ClipType));

            channelPlayerList.Add(new PlayerListChannelEntry(channel, playerListEntries));
        }
        var selfChannelEntryIndex = channelPlayerList.FindIndex(e => e.Channel == state.SelfChannel);
        var selfChannelEntry = channelPlayerList[selfChannelEntryIndex];
        channelPlayerList.RemoveAt(selfChannelEntryIndex);
        channelPlayerList.Insert(0, selfChannelEntry);
        SortPlayerList();

        // the local virtual private channel is always pinned to the bottom of the list
        int privateChannelEntryIndex = channelPlayerList.FindIndex(e => e.Channel.ID == ChannelInfo.PrivateChannelVirtualID);
        if (privateChannelEntryIndex >= 0)
        {
            var privateChannelEntry = channelPlayerList[privateChannelEntryIndex];
            channelPlayerList.RemoveAt(privateChannelEntryIndex);
            channelPlayerList.Add(privateChannelEntry);
        }
#endif
        BumpUIVersion();
    }

    private void Context_PingDataReceived()
    {
        foreach (var channel in channelPlayerList)
            foreach (var item in channel.Players)
                item.UpdatePing();
        BumpUIVersion();
    }

    private void UpdatePlayer(OnlinePlayer player)
    {
#if MOCK_DATA
        return;
#endif
        var channel = channelPlayerList.Find(c => c.Channel == player.Channel);
        var item = channel!.Players.Find(i => i.Player == player);
        item!.Update(ClipType);
        SortPlayerList();
        BumpUIVersion();
        return;
    }

    private void Context_ClientInitialized(ClientState state)
    {
        BuildPlayerList();
        state.SelfLocationChanged += new(State_SelfLocationChanged);

        void State_SelfLocationChanged()
            => UpdatePlayer(ClientState.Self);
    }

    private void SortPlayerList()
    {
        foreach (var c in channelPlayerList)
            c.Players.Sort(pComparer.Compare);
    }

    public override void OnDisconnected()
    {
        Active = false;
        channelPlayerList.Clear();
        BumpUIVersion();
    }
}
