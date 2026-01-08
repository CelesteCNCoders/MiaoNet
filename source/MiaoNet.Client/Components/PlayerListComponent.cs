using System.Text;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed partial class PlayerListComponent : MiaoNetComponent
{
    public bool Active { get; set; }
    private readonly PlayerListEntryComparer pComparer;
    private readonly List<(OnlineChannel, List<PlayerListItem>)> channelPlayerList;

    public PlayerListComponent(MiaoNetContext context)
        : base(context)
    {
        pComparer = new();
        channelPlayerList = new();
        context.ClientInitialized += Context_ClientInitialized;
        context.PlayerJoined += _ => BuildPlayerList();
        context.PlayerLeft += _ => BuildPlayerList();
        context.PlayerMapChanged += (p, _) => UpdatePlayer(p);
        context.PlayerMapRoomChanged += (p, _) => UpdatePlayer(p);
        context.PingDataReceived += Context_PingDataReceived;
    }

    private void BuildPlayerList()
    {
        #region sth i used to test the rendering
#if false
        int id = 0;
        channelPlayerList.Clear();
        OnlineChannel cMain = new(0, "main");
        List<PlayerListItem> mainChannelPlayerList = [
            CreateTestPlayer(cMain, "sapcc", "Celeste/1-ForsakenCity", "a-01"),
            CreateTestPlayer(cMain, "Ccc", "Celeste/2-OldSite", "a-01"),
            CreateTestPlayer(cMain, "AAlice", "Celeste/LostLevels", "j-17"),
            CreateTestPlayer(cMain, "sapcc", "Celeste/LostLevels", "j-16"),
            CreateTestPlayer(cMain, "Admin", "Celeste/LostLevels", "end-golden"),
            CreateTestPlayer(cMain, "EmptyPos", "", ""),
            CreateTestPlayer(cMain, "David", "Celeste/1-ForsakenCity", "b-0c"),
            CreateTestPlayer(cMain, "voidsd", "SpringCollab2020/Expert/ZZ-HeartSide", "idk-a"),
            CreateTestPlayer(cMain, "mo_fish", "", ""),
        ];
        foreach (var item in mainChannelPlayerList)
            cMain.Players.Add(item.Player.ID, item.Player);
        OnlineChannel cOther = new(1, "xinzhan");
        List<PlayerListItem> otherChannelPlayerList = [
            CreateTestPlayer(cOther, "O5DZ", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "idk_others", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "idk_others_too", "Celeste/9-Core", "f-0j"),
        ];
        foreach (var item in otherChannelPlayerList)
            cOther.Players.Add(item.Player.ID, item.Player);
        OnlineChannel cOther2 = new(2, "xinzhan2");
        List<PlayerListItem> otherChannel2PlayerList = [
            CreateTestPlayer(cOther, "O5DZ222", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "idk_others222", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "idk_others_too222", "Celeste/9-Core", "f-0j"),
        ];
        foreach (var item in otherChannel2PlayerList)
            cOther2.Players.Add(item.Player.ID, item.Player);
        channelPlayerList.AddRange([
            (cMain, mainChannelPlayerList),
            (cOther, otherChannelPlayerList),
            (cOther2, otherChannel2PlayerList)
        ]);
        SortPlayerList();
        return;

        PlayerListItem CreateTestPlayer(OnlineChannel channel, string name, string sid, string room)
        {
            return new PlayerListItem(new OnlinePlayer(channel, new(id++, name), PlayerOnlineStatus.Normal)
            {
                Location = new PlayerLocation(sid, AreaMode.Normal, room),
                LastPing = Random.Shared.Next(20, Random.Shared.Next(20, Random.Shared.Next(20, 2000)))
            });
        }
#endif
        #endregion

        channelPlayerList.Clear();
        var state = ClientState;

        foreach (var (_, channel) in state.Channels)
        {
            var playerList = new List<PlayerListItem>();
            if (channel == state.SelfChannel)
                playerList.Add(new PlayerListItem(state.Self));
            foreach (var (_, player) in channel.Players)
                playerList.Add(new PlayerListItem(player));
            channelPlayerList.Add((channel, playerList));
        }
        SortPlayerList();
    }

    private void Context_PingDataReceived()
    {
        foreach (var pair in channelPlayerList)
            foreach (var item in pair.Item2)
                item.UpdatePing();
    }

    private void UpdatePlayer(OnlinePlayer player)
    {
        var channel = player.Channel;
        var pair = channelPlayerList.Find(p => p.Item1 == channel);
        var item = pair.Item2.Find(i => i.Player == player);
        item!.Update();
        return;
    }

    private void Context_ClientInitialized(ClientState state)
    {
        BuildPlayerList();
        state.SelfLocationChanged += State_SelfLocationChanged;
    }

    private void State_SelfLocationChanged()
    {
        UpdatePlayer(ClientState.Self);
    }

    private void SortPlayerList()
    {
        foreach (var (_, list) in channelPlayerList)
            list.Sort((x, y) => pComparer.Compare(x.Player, y.Player));
    }

    public override void OnDisconnected()
    {
        Active = false;
        channelPlayerList.Clear();
    }

    public override void Update()
    {
        var settings = MiaoNetModule.Settings;
        bool wantsTo;
        if (settings.PlayerListButtonMode == ButtonMode.Press)
        {
            if (settings.PlayerListButton.Pressed)
            {
                settings.PlayerListButton.ConsumePress();
                wantsTo = !Active;
            }
            else
            {
                wantsTo = Active;
            }
        }
        else
        {
            wantsTo = settings.PlayerListButton.Check;
        }
        if (Active != wantsTo)
        {
            if (wantsTo)
            {
                if (MiaoNetContext.IsSuitableToOpenUI)
                    Active = true;
            }
            else
            {
                Active = false;
            }
        }
    }

    // TODO this method can be optimized
    public override void Render()
    {
        if (!Active)
            return;

        /*
         * 
         * #<ChannelName> <PlayerCount>/<Max?> Players                                         
         *                                                                                     
         * // ------>      |<MiddlePadding>|                                         <------- 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         *                                                                                     
         * #<Channel2Name> <PlayerCount>/<Max?> Players                                        
         *                                                                                     
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         * [Avatar] <PlayerName>       <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?] <Ping> 
         *                                                                                     
         * #!<PrivateChannelName>                                                              
         *                                                                                     
         * [Avatar] <PlayerName>                                                     <Ping> 
         * [Avatar] <PlayerName>                                                     <Ping> 
         *                                                                                     
         *                                                                                     
         * <------------------------------- maxLineWidth ------------------------------------> 
         */

        float scale = MiaoNetModule.Settings.PlayerListUIScaleValue;

        const float RectXMargin = 16f;
        const float RectYMargin = 16f;
        const float RectXPadding = 16f;
        const float RectYPadding = 16f;
        const float MiddlePadding = 48f;

        float lineHeight = MiaoNetFont.ENZhsLineHeight * scale;

        float maxPingWidth = 0f;
        float maxLineWidth = 0f;

        float spaceWidth = MiaoNetFont.Measure(" ").X * scale;
        float colonWidth = MiaoNetFont.Measure(":").X * scale;

        Span<float> channelYOffsets = stackalloc float[channelPlayerList.Count];
        Span<float> channelHeights = stackalloc float[channelPlayerList.Count];

        // calculate channel rect max width and heights
        {
            float curY = 0f;
            for (int i = 0; i < channelPlayerList.Count; i++)
            {
                var channel = channelPlayerList[i].Item1;
                float headerWidth = MiaoNetFont.Measure($"#{channel.Name}   {channel.Players.Count} Players").X * scale;
                maxLineWidth = Math.Max(maxLineWidth, headerWidth);

                curY += RectYMargin;
                channelYOffsets[i] = curY;
                curY += RectYPadding;
                curY += lineHeight; // channel header

                foreach (var item in channelPlayerList[i].Item2)
                {
                    float width = 0f;

                    width += MiaoNetFont.Measure(item.Player.Info.Name).X * scale;
                    width += MiddlePadding;

                    if (!item.Player.Location.IsEmpty)
                    {
                        width += colonWidth;
                        width += MiaoNetFont.Measure(item.Player.Location.MapRoom).X * scale;

                        if (item.AreaIconTexture is not null)
                        {
                            width += spaceWidth;
                            width += lineHeight / item.AreaIconTexture.Height;
                        }

                        width += spaceWidth;
                        width += MiaoNetFont.Measure(item.MapName ?? item.Player.Location.MapSid).X * scale;

                        width += spaceWidth;
                        width += MiaoNetFont.Measure(item.AreaSideText!).X * scale;
                    }

                    if (item.PingText is not null)
                    {
                        float pingWidth = MiaoNetFont.Measure(item.PingText).X * scale + spaceWidth;
                        width += spaceWidth;
                        width += pingWidth;
                        maxPingWidth = Math.Max(maxPingWidth, pingWidth);
                    }

                    maxLineWidth = Math.Max(maxLineWidth, width);

                    curY += lineHeight;
                }

                curY += RectYPadding;
                channelHeights[i] = curY - channelYOffsets[i];
                curY += RectYMargin;
            }
        }

        float totalMaxLineWidth = maxLineWidth + maxPingWidth;

        // draw background
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            float yOffset = channelYOffsets[i];
            Draw.Rect(
                RectXMargin + 4f, yOffset + 4f,
                totalMaxLineWidth + 2 * RectXPadding, channelHeights[i],
                Color.Gray with { A = 0x77 }
            );
            Draw.Rect(
                RectXMargin, yOffset,
                totalMaxLineWidth + 2 * RectXPadding, channelHeights[i],
                Color.Black with { A = 0x77 }
            );
        }

        // draw channels
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            (OnlineChannel channel, List<PlayerListItem> itemList) = channelPlayerList[i];
            float xOffset = RectXMargin + RectXPadding;
            float curY = channelYOffsets[i] + RectYPadding;
            // draw header
            MiaoNetFont.Draw(
                $"#{channel.Name}   {itemList.Count} Players",
                position: new(xOffset, curY),
                justify: Vector2.Zero,
                scale: Vector2.One * scale,
                Color.Yellow
            );
            curY += lineHeight;
            // draw players
            foreach (var item in itemList)
            {
                // draw player name
                MiaoNetFont.Draw(
                    item.Player.Info.Name,
                    position: new(xOffset, curY),
                    justify: Vector2.Zero,
                    scale: Vector2.One * scale,
                    Color.White
                );

                // start right to left drawing
                float x = xOffset + totalMaxLineWidth;

                // draw ping
                if (item.PingText is not null)
                {
                    MiaoNetFont.Draw(
                        item.PingText,
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        Color.LightGray
                    );
                }
                x -= maxPingWidth; // align

                // draw player location
                if (!item.Player.Location.IsEmpty)
                {
                    var loc = item.Player.Location;

                    var iconTex = item.AreaIconTexture;
                    if (iconTex is not null)
                    {
                        float iconScale = lineHeight / iconTex.Height;
                        iconTex.DrawJustified(
                            new(x, curY),
                            Vector2.UnitX,
                            Color.White,
                            Vector2.One * iconScale
                        );
                        x -= iconTex.Width * iconScale;
                        x -= spaceWidth;
                    }


                    // draw side
                    string sideName = loc.SideCharacter.ToString();
                    MiaoNetFont.Draw(
                        sideName,
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        item.MapSideColor
                    );
                    x -= MiaoNetFont.Measure(sideName).X * scale;
                    x -= spaceWidth;

                    // draw name or sid
                    string mapName = item.MapName ?? loc.MapSid;
                    MiaoNetFont.Draw(
                        mapName,
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        item.MapNameColor
                    );
                    x -= MiaoNetFont.Measure(mapName).X * scale;
                    x -= spaceWidth;

                    // draw a colon
                    MiaoNetFont.Draw(
                        ":",
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        Color.LightGray
                    );
                    x -= colonWidth;

                    // draw room name
                    MiaoNetFont.Draw(
                        loc.MapRoom,
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        Color.LightGray
                    );
                    x -= MiaoNetFont.Measure(loc.MapRoom).X * scale;
                }

                curY += lineHeight;
            }
        }
    }
}
