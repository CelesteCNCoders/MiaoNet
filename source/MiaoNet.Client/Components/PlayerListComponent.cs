using System.Text;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class PlayerListComponent : MiaoNetComponent
{
    public bool Active { get; set; }
    private readonly PlayerListEntryComparer pComparer;
    private readonly List<(OnlineChannel, List<OnlinePlayer>)> channelPlayerList;

    public PlayerListComponent(MiaoNetContext context)
        : base(context)
    {
        pComparer = new();
        channelPlayerList = new();
        context.ClientInitialized += _ => BuildPlayerList();
        context.PlayerJoined += _ => BuildPlayerList();
        context.PlayerLeft += _ => BuildPlayerList();
        context.PlayerMapChanged += (_, _) => SortPlayerList();
        context.PlayerMapRoomChanged += (_, _) => SortPlayerList();
    }

    private void BuildPlayerList()
    {
        #region sth i used to test the rendering
#if false
        OnlineChannel cMain = new(0, "main");
        List<OnlinePlayer> mainChannelPlayerList = [
            new OnlinePlayer(cMain, new PlayerInfo(0, "sapcc"), new PlayerLocation("Celeste/1a", "a-01")),
            new OnlinePlayer(cMain, new PlayerInfo(1, "Ccc"), new PlayerLocation("Celeste/2a", "a-01")),
            new OnlinePlayer(cMain, new PlayerInfo(2, "AAlice"), new PlayerLocation("Celeste/Farewell", "j-17")),
            new OnlinePlayer(cMain, new PlayerInfo(3, "sapcc"), new PlayerLocation("Celeste/Farewell", "j-16")),
            new OnlinePlayer(cMain, new PlayerInfo(4, "Admin"), new PlayerLocation("Celeste/Farewell", "end-golden")),
            new OnlinePlayer(cMain, new PlayerInfo(5, "EmptyPos"), PlayerLocation.Empty),
            new OnlinePlayer(cMain, new PlayerInfo(6, "David"), new PlayerLocation("Celeste/1a", "b-0c")),
            new OnlinePlayer(cMain, new PlayerInfo(7, "voidsd"), new PlayerLocation("SpringCollab2020/Expert/ZZ-HeartSide", "idk-a")),
            new OnlinePlayer(cMain, new PlayerInfo(8, "mo_fish"), PlayerLocation.Empty),
        ];
        foreach (var player in mainChannelPlayerList)
            cMain.Players.Add(player.ID, player);
        OnlineChannel cOther = new(1, "xinzhan");
        List<OnlinePlayer> otherChannelPlayerList = [
            new OnlinePlayer(cOther, new PlayerInfo(9, "O5DZ"), new PlayerLocation("StrawberryJam2021/Advanced/Lobby","a-00")),
            new OnlinePlayer(cOther, new PlayerInfo(10, "idk_others"), new PlayerLocation("StrawberryJam2021/Advanced/Lobby","a-01")),
            new OnlinePlayer(cOther, new PlayerInfo(11, "idk_others_too"), new PlayerLocation("Celeste/Core","f-0j")),
        ];
        foreach (var player in otherChannelPlayerList)
            cOther.Players.Add(player.ID, player);
        OnlineChannel cOther2 = new(2, "xinzhan2");
        List<OnlinePlayer> otherChannel2PlayerList = [
            new OnlinePlayer(cOther, new PlayerInfo(9, "O5DZ222"), new PlayerLocation("StrawberryJam2021/Advanced/Lobby","a-00")),
            new OnlinePlayer(cOther, new PlayerInfo(10, "idk_others222"), new PlayerLocation("StrawberryJam2021/Advanced/Lobby","a-01")),
            new OnlinePlayer(cOther, new PlayerInfo(11, "idk_others_too222"), new PlayerLocation("Celeste/Core","f-0j")),
        ];
        foreach (var player in otherChannel2PlayerList)
            cOther2.Players.Add(player.ID, player);
        channelPlayerList = [
            (cMain, mainChannelPlayerList),
            (cOther, otherChannelPlayerList),
            (cOther2, otherChannel2PlayerList)
        ];
        var comparer = new PlayerListEntryComparer();
        foreach (var pair in channelPlayerList)
            pair.Item2.Sort(comparer);
        return;
#endif
        #endregion

        channelPlayerList.Clear();
        var state = ClientState;

        foreach (var (_, channel) in state.Channels)
        {
            var playerList = new List<OnlinePlayer>();
            if (channel == state.SelfChannel)
                playerList.Add(state.Self);
            foreach (var (_, player) in channel.Players)
                playerList.Add(player);
            channelPlayerList.Add((channel, playerList));
        }
    }

    private void SortPlayerList()
    {
        foreach (var (_, list) in channelPlayerList)
            list.Sort(pComparer);
    }

    public override void OnDisconnected()
    {
        Active = false;
    }

    public override void Update()
    {
        var settings = MiaoNetModule.Settings;
        if (settings.PlayerListButtonMode == MiaoNetModuleSettings.ButtonMode.Press)
        {
            if (settings.PlayerListButton.Pressed)
            {
                settings.PlayerListButton.ConsumePress();
                Active = !Active;
            }
        }
        else
        {
            Active = settings.PlayerListButton.Check;
        }
    }

    public override void Render()
    {
        if (!Active)
            return;

        // TODO implement this
        /* ↓ this is expected, not currently implemented...
         * 
         * #<ChannelName> <PlayerCount>/<Max?> Players                                         
         *                                                                                     
         * // ------>           |      |                                              <------- 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         *                                                                                     
         * #<Channel2Name> <PlayerCount>/<Max?> Players                                        
         *                                                                                     
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <MapSide?> [MapIcon?] <Ping> 
         *                                                                                     
         * #!<PrivateChannelName>                                                              
         *                                                                                     
         * [Avatar] <PlayerName>                                                        <Ping> 
         * [Avatar] <PlayerName>                                                        <Ping> 
         *                                                                                     
         *                                                                                     
         * <------------------------------- maxLineWidth ------------------------------------> 
         */

        float scale = MiaoNetModule.Settings.UIScaleValue;

        const float RectXOffset = 16f;
        const float RectYOffset = 16f;
        const float RectXPadding = 16f;
        const float RectYPadding = 16f;
        const float MiddlePadding = 8f;
        float midTextWidth = MiaoNetFont.Measure(" @ ").X * scale;
        float lineHeight = MiaoNetFont.ENZhsLineHeight * scale;

        float maxLineWidth = 0f;
        float totalHeight = 0f;

        Span<float> channelYOffsets = stackalloc float[channelPlayerList.Count];
        Span<float> channelHeights = stackalloc float[channelPlayerList.Count];

        totalHeight += RectYOffset;
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            channelYOffsets[i] = totalHeight;
            totalHeight += RectYPadding;
            totalHeight += lineHeight; // channel header

            foreach (var player in channelPlayerList[i].Item2)
            {
                float width = MiaoNetFont.Measure(player.Info.Name).X * scale;
                width += MiaoNetFont.Measure(player.Location.ToString()).X * scale;
                if (player.OnlineStatus != PlayerOnlineStatus.Normal)
                {
                    width += midTextWidth;
                    width += MiaoNetFont.Measure(player.OnlineStatus.ToString()).X * scale;
                }
                if (player.LastPing != -1)
                {
                    width += midTextWidth;
                    width += MiaoNetFont.Measure($"{player.LastPing}ms").X * scale;
                }
                width += MiddlePadding;
                width += midTextWidth;
                maxLineWidth = Math.Max(maxLineWidth, width);
                totalHeight += lineHeight;
            }

            totalHeight += RectYPadding;
            channelHeights[i] = totalHeight - channelYOffsets[i];
            totalHeight += lineHeight; // empty line padding
        }

        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            float yOffset = channelYOffsets[i];
            Draw.Rect(
                RectXOffset + 4f, yOffset + 4f,
                maxLineWidth + 2 * RectXPadding, channelHeights[i],
                Color.Gray with { A = 0x77 }
            );
            Draw.Rect(
                RectXOffset, yOffset,
                maxLineWidth + 2 * RectXPadding, channelHeights[i],
                Color.Black with { A = 0x77 }
            );
        }

        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            (OnlineChannel channel, List<OnlinePlayer> playerList) = channelPlayerList[i];
            float curX = RectXOffset + RectXPadding;
            float curY = channelYOffsets[i] + RectYPadding;
            MiaoNetFont.Draw(
                $"#{channel.Name} {playerList.Count} Players",
                position: new(curX, curY),
                justify: Vector2.Zero,
                scale: Vector2.One * scale,
                Color.Yellow
            );
            curY += lineHeight;
            foreach (var player in playerList)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(player.Info.Name);
                sb.Append(" @ ");
                if (player.OnlineStatus != PlayerOnlineStatus.Normal)
                {
                    sb.Append(player.OnlineStatus);
                    sb.Append(" @ ");
                }
                sb.Append(player.Location);
                if(player.LastPing != -1)
                {
                    sb.Append(" @ ");
                    sb.Append(player.LastPing);
                    sb.Append("ms");
                }
                MiaoNetFont.Draw(
                    sb.ToString(),
                    position: new(curX, curY),
                    justify: Vector2.Zero,
                    scale: Vector2.One * scale,
                    Color.White
                );
                curY += lineHeight;
            }
        }
    }
}
