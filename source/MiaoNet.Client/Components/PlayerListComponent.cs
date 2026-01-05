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
        int id = 0;
        channelPlayerList.Clear();
        OnlineChannel cMain = new(0, "main");
        List<OnlinePlayer> mainChannelPlayerList = [
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
        foreach (var player in mainChannelPlayerList)
            cMain.Players.Add(player.ID, player);
        OnlineChannel cOther = new(1, "xinzhan");
        List<OnlinePlayer> otherChannelPlayerList = [
            CreateTestPlayer(cOther, "O5DZ", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "idk_others", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "idk_others_too", "Celeste/9-Core", "f-0j"),
        ];
        foreach (var player in otherChannelPlayerList)
            cOther.Players.Add(player.ID, player);
        OnlineChannel cOther2 = new(2, "xinzhan2");
        List<OnlinePlayer> otherChannel2PlayerList = [
            CreateTestPlayer(cOther, "O5DZ222", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "idk_others222", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "idk_others_too222", "Celeste/9-Core", "f-0j"),
        ];
        foreach (var player in otherChannel2PlayerList)
            cOther2.Players.Add(player.ID, player);
        channelPlayerList.AddRange([
            (cMain, mainChannelPlayerList),
            (cOther, otherChannelPlayerList),
            (cOther2, otherChannel2PlayerList)
        ]);
        var comparer = new PlayerListEntryComparer();
        foreach (var pair in channelPlayerList)
            pair.Item2.Sort(comparer);
        return;

        OnlinePlayer CreateTestPlayer(OnlineChannel channel, string name, string sid, string room)
        {
            return new OnlinePlayer(channel, new(id++, name), PlayerOnlineStatus.Normal)
            {
                Location = new PlayerLocation(sid, AreaMode.Normal, room),
                LastPing = Random.Shared.Next(20, Random.Shared.Next(20, Random.Shared.Next(20, 2000)))
            };
        }
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
         * // ------>      |<MiddlePadding>|                                              <------- 
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
         * [Avatar] <PlayerName>                                                        <Ping> 
         * [Avatar] <PlayerName>                                                        <Ping> 
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

                foreach (var player in channelPlayerList[i].Item2)
                {
                    float width = 0f;

                    width += MiaoNetFont.Measure(player.Info.Name).X * scale;
                    width += MiddlePadding;

                    width += colonWidth;
                    width += MiaoNetFont.Measure(player.Location.MapRoom).X * scale;

                    // TODO cache these
                    var areaData = AreaData.Get(player.Location.MapSid);
                    if (areaData is not null)
                    {
                        string iconPath = areaData.Icon;
                        MTexture? iconTex = GFX.Gui.GetOrDefault(iconPath, null);
                        if (iconTex is not null)
                        {
                            width += spaceWidth;
                            width += lineHeight / iconTex.Height;
                        }
                    }

                    string mapName = areaData is null || !Dialog.Has(areaData.Name) 
                        ? player.Location.MapSid 
                        : Dialog.Get(areaData.Name);

                    width += spaceWidth;
                    width += MiaoNetFont.Measure(mapName).X * scale;

                    width += spaceWidth;
                    width += MiaoNetFont.Measure(player.Location.SideCharacter.ToString()).X * scale;

                    if (player.LastPing != -1)
                    {
                        float pingWidth = MiaoNetFont.Measure($"{player.LastPing}ms").X * scale + spaceWidth;
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

        // draw background
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            float yOffset = channelYOffsets[i];
            Draw.Rect(
                RectXMargin + 4f, yOffset + 4f,
                maxLineWidth + 2 * RectXPadding, channelHeights[i],
                Color.Gray with { A = 0x77 }
            );
            Draw.Rect(
                RectXMargin, yOffset,
                maxLineWidth + 2 * RectXPadding, channelHeights[i],
                Color.Black with { A = 0x77 }
            );
        }

        // draw channels
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            (OnlineChannel channel, List<OnlinePlayer> playerList) = channelPlayerList[i];
            float xOffset = RectXMargin + RectXPadding;
            float curY = channelYOffsets[i] + RectYPadding;
            // draw header
            MiaoNetFont.Draw(
                $"#{channel.Name}   {playerList.Count} Players",
                position: new(xOffset, curY),
                justify: Vector2.Zero,
                scale: Vector2.One * scale,
                Color.Yellow
            );
            curY += lineHeight;
            // draw players
            foreach (var player in playerList)
            {
                // draw player name
                MiaoNetFont.Draw(
                    player.Info.Name,
                    position: new(xOffset, curY),
                    justify: Vector2.Zero,
                    scale: Vector2.One * scale,
                    Color.White
                );

                // start right to left drawing
                float x = xOffset + maxLineWidth;

                // draw ping
                if (player.LastPing != -1)
                {
                    MiaoNetFont.Draw(
                        $"{player.LastPing}ms",
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        Color.LightGray
                    );
                }
                x -= maxPingWidth; // align

                // draw player location
                if (!player.Location.IsEmpty)
                {
                    var loc = player.Location;
                    Color nameColor = Color.LightGray;
                    Color sideColor = Color.LightGray;
                    // draw map icon
                    var areaData = AreaData.Get(player.Location.MapSid);
                    if (areaData is not null)
                    {
                        string iconPath = areaData.Icon;
                        nameColor = Color.Lerp(areaData.TitleBaseColor, nameColor, 0.5f);
                        sideColor = Color.Lerp(areaData.TitleAccentColor, sideColor, 0.8f);
                        MTexture? iconTex = GFX.Gui.GetOrDefault(iconPath, null);
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
                    }

                    // draw side
                    string sideName = loc.SideCharacter.ToString();
                    MiaoNetFont.Draw(
                        sideName,
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        sideColor
                    );
                    x -= MiaoNetFont.Measure(sideName).X * scale;
                    x -= spaceWidth;

                    // draw name or sid
                    string mapName = areaData is null || !Dialog.Has(areaData.Name) 
                        ? loc.MapSid 
                        : Dialog.Get(areaData.Name);
                    MiaoNetFont.Draw(
                        mapName,
                        position: new(x, curY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        nameColor
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
