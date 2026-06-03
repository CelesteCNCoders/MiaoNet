//#define MOCK_DATA

using System.Diagnostics;
using System.Text;
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

    // -v ~ +v
    private const float PausedTexOffsetRange = 4f;
    private float pausedTexFloatTimer;
    private float pausedTexOffset;

    private float scroll;
    private float scrollTarget;

    private static ClipType ClipType => MiaoNetModule.Settings.PlayerListMapNameClipType;

    public PlayerListComponent(MiaoNetContext context)
        : base(context)
    {
        pComparer = new();
        channelPlayerList = new();
        context.ClientInitialized += Context_ClientInitialized;
        // TODO surely full-rebuild is not necessary
        context.PlayerJoined += _ => BuildPlayerList();
        context.PlayerLeft += _ => BuildPlayerList();
        context.PlayerMapChanged += (p, _) => UpdatePlayer(p);
        context.PlayerMapRoomChanged += (p, _) => UpdatePlayer(p);
        context.PingDataReceived += Context_PingDataReceived;
        context.SelfChannelMoved += _ => BuildPlayerList();
        context.PlayerChannelMoved += (_, _) => BuildPlayerList();
        context.ChannelCreated += _ => BuildPlayerList();
        context.ChannelRemoved += _ => BuildPlayerList();

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
            CreateTestPlayer(cMain, "voidsd", "SpringCollab2020/Expert/ZZ-HeartSide", "idk-a"),
            CreateTestPlayer(cMain, "mo_fish", "", ""),
        ];
        foreach (var item in mainChannelPlayerList)
            cMain.Players.Add(item.Player);
        OnlineChannel cOther = new(1, new ChannelInfo("xinzhan"));
        List<PlayerListEntry> otherChannelPlayerList = [
            CreateTestPlayer(cOther, "O5DZ", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "idk_others", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "idk_others_too", "Celeste/9-Core", "f-0j"),
        ];
        foreach (var item in otherChannelPlayerList)
            cOther.Players.Add(item.Player);
        OnlineChannel cOther2 = new(2, new ChannelInfo("xinzhan2"));
        List<PlayerListEntry> otherChannel2PlayerList = [
            CreateTestPlayer(cOther, "O5DZ222", "StrawberryJam2021/Advanced/Lobby", "a-00"),
            CreateTestPlayer(cOther, "idk_others222", "StrawberryJam2021/Advanced/Lobby", "a-01"),
            CreateTestPlayer(cOther, "idk_others_too222", "Celeste/9-Core", "f-0j"),
        ];
        for (int i = 0; i < 16; i++)
            otherChannel2PlayerList.Add(CreateTestPlayer(cOther, $"P {i}", "Celeste/9-Core", "f-0j"));
        foreach (var item in otherChannel2PlayerList)
            cOther2.Players.Add(item.Player);
        channelPlayerList.AddRange([
            new(cMain, mainChannelPlayerList),
            new(cOther, otherChannelPlayerList),
            new(cOther2, otherChannel2PlayerList)
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
#endif
    }

    private void Context_PingDataReceived()
    {
        foreach (var channel in channelPlayerList)
            foreach (var item in channel.Players)
                item.UpdatePing();
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
        scroll = 0f;
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
                if (context.IsSuitableToOpenUI)
                {
                    Active = true;
                    context.HasComponentFocus = true;
                }
            }
            else
            {
                Active = false;
                context.HasComponentFocus = false;
                scrollTarget = 0f;
                scroll = 0f;
            }
        }

        if (Active)
        {
            pausedTexFloatTimer += Engine.RawDeltaTime * 2f;
            pausedTexFloatTimer = Calc.WrapAngle(pausedTexFloatTimer);
            pausedTexOffset = MathF.Sin(pausedTexFloatTimer) * PausedTexOffsetRange;
            const float KeyboardScrollSpeed = 1024f;
            if (settings.PlayerListScrollUp.Check)
                scrollTarget -= KeyboardScrollSpeed * Engine.RawDeltaTime;
            else if (settings.PlayerListScrollDown.Check)
                scrollTarget += KeyboardScrollSpeed * Engine.RawDeltaTime;
            scrollTarget = Math.Max(scrollTarget, 0);

            float maxMove = Math.Max(Math.Abs(scrollTarget - scroll), 8f) * 8f * Engine.RawDeltaTime;
            scroll = Calc.Approach(scroll, scrollTarget, maxMove);
        }
    }

    // TODO this can still be optimized
    public override void Render()
    {
        if (!Active)
            return;

        /*
         * 
         * #<ChannelName> <PlayerCount>/<Max?> Players                                         
         *                                                                                     
         * // ------>                        |<MiddlePadding>|                                            <------- 
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         *                                                                                                   
         * #<Channel2Name> <PlayerCount>/<Max?> Players                                                      
         *                                                                                                   
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         * [Avatar] <PlayerName> <OnlineStatus>             <MapRoom>: <MapSid.Dialog> <Side?> [MapIcon?]    <Ping> 
         *                                                                                                   
         * #!<PrivateChannelName>                                                                            
         *                                                                                                   
         * [Avatar] <PlayerName>                                                                             <Ping> 
         * [Avatar] <PlayerName>                                                                             <Ping> 
         *                                                                                     
         *                                                                                                  |      |
         * <------------------------------------------ maxLineWidth ----------------------------------->   maxPingWidth
         */

        float scale = MiaoNetModule.Settings.PlayerListUIScaleValue;

        const float RectXMargin = 16f;
        const float RectYMargin = 16f;
        const float RectXPadding = 16f;
        const float RectYPadding = 16f;
        const float MiddlePadding = 32f;
        const float PlayerEntryXPadding = 4f;
        const float PlayerEntryYPadding = 2f;

        float lineHeight = MiaoNetFont.ENZhsLineHeight * scale;
        float playerEntryHeight = lineHeight + 2 * PlayerEntryYPadding;

        float maxPingWidth = 0f;
        float maxLineWidth = 0f;

        float spaceWidth = MiaoNetFont.Measure(" ").X * scale;
        float colonWidth = MiaoNetFont.Measure(":").X * scale;

        Span<float> channelYOffsets = stackalloc float[channelPlayerList.Count];
        Span<float> channelHeights = stackalloc float[channelPlayerList.Count];

        // calculate channel rect max width and heights
        {
            float curY = -scroll;
            for (int i = 0; i < channelPlayerList.Count; i++)
            {
                var channel = channelPlayerList[i];
                float headerWidth = MiaoNetFont.Measure(channel.Header).X * scale;
                maxLineWidth = Math.Max(maxLineWidth, headerWidth);

                curY += RectYMargin;
                channelYOffsets[i] = curY;
                curY += RectYPadding;
                curY += lineHeight; // channel header

                foreach (var item in channelPlayerList[i].Players)
                {
                    var player = item.Player;

                    float itemWidth = 0f;

                    itemWidth += MiaoNetFont.Measure(item.DisplayName).X * scale;
                    itemWidth += MiddlePadding;

                    if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.Paused))
                    {
                        float texScale = lineHeight / texPlayerPaused.Height;
                        itemWidth += texScale * texPlayerPaused.Width + 2 * PausedTexOffsetRange;
                    }

                    if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.Interactions))
                    {
                        float texScale = lineHeight / texPlayerInteractions.Height;
                        itemWidth += texScale * texPlayerInteractions.Width;
                    }

                    if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.LiveMode))
                    {
                        float texScale = lineHeight / texLiveMode.Height;
                        itemWidth += texScale * texLiveMode.Width;
                    }

                    if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.TakingGolden))
                    {
                        float texScale = lineHeight / texTakingGolden.Height;
                        itemWidth += texScale * texTakingGolden.Width;
                    }

                    if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.GroupPhotoMode))
                    {
                        float texScale = lineHeight / texGroupPhotoMode.Height;
                        itemWidth += texScale * texGroupPhotoMode.Width;
                    }

                    if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching))
                    {
                        float texScale = lineHeight / texPlayerDebugMap.Height;
                        itemWidth += texScale * texPlayerDebugMap.Width;
                    }

                    if (!player.Location.IsEmpty)
                    {
                        bool liveMode = MiaoNetModule.Settings.LiveMode;

                        itemWidth += colonWidth;
                        if (!player.Location.IsInDebugMap)
                        {
                            itemWidth += MiaoNetFont.Measure(liveMode ? "*" : item.MapRoom!).X * scale;
                        }
                        else
                        {
                            float texScale = lineHeight / texPlayerDebugMap.Height;
                            itemWidth += texScale * texPlayerDebugMap.Width;
                        }

                        if (item.AreaIconTexture is not null)
                        {
                            itemWidth += spaceWidth;
                            float texScale = lineHeight / item.AreaIconTexture.Height;
                            itemWidth += texScale * item.AreaIconTexture.Width;
                        }

                        itemWidth += spaceWidth;
                        string mapName = item.IsLocallyKnownMap ? item.MapName! : liveMode ? "*" : item.MapName!;
                        itemWidth += MiaoNetFont.Measure(mapName).X * scale;

                        if (item.AreaModeText is not null)
                        {
                            itemWidth += spaceWidth;
                            itemWidth += MiaoNetFont.Measure(item.AreaModeText).X * scale;
                        }
                    }

                    if (item.PingText is not null)
                    {
                        float pingWidth = MiaoNetFont.Measure(item.PingText).X * scale + spaceWidth;
                        maxPingWidth = Math.Max(maxPingWidth, pingWidth);
                    }

                    maxLineWidth = Math.Max(maxLineWidth, itemWidth);

                    curY += playerEntryHeight;
                }

                curY += RectYPadding;
                channelHeights[i] = curY - channelYOffsets[i];
                curY += RectYMargin;
            }
        }

        float totalMaxLineWidth = maxLineWidth + maxPingWidth + 2 * PlayerEntryXPadding;

        // draw background
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            float dstX = RectXMargin, dstY = channelYOffsets[i];
            float dstWidth = totalMaxLineWidth + 2 * RectXPadding, dstHeight = channelHeights[i];

            Draw.Rect(dstX, dstY, dstWidth, dstHeight, Color.Black * (0xcc / 255f));
            Draw.Rect(dstX, dstY, dstWidth, 3f, Color.CornflowerBlue);
            Draw.Rect(dstX, dstY, 3f, dstHeight, Color.Cyan);

            var players = channelPlayerList[i].Players;
            float curY = dstY + RectYPadding + lineHeight;
            for (int j = 0; j < players.Count; j++)
            {
                Color c = (j % 2) switch
                {
                    0 => new Color(0x00, 0x00, 0x00, 0x22),
                    1 => new Color(0x22, 0x22, 0x22, 0x88),
                };
                Draw.Rect(dstX + RectXPadding, curY, dstWidth - 2 * RectXPadding, playerEntryHeight, c);
                curY += playerEntryHeight;
            }
        }

        // draw channels
        for (int i = 0; i < channelPlayerList.Count; i++)
        {
            PlayerListChannelEntry channel = channelPlayerList[i];
            List<PlayerListEntry> playerEntries = channel.Players;
            float xOffset = RectXMargin + RectXPadding;
            float curY = channelYOffsets[i] + RectYPadding;
            // draw header
            MiaoNetFont.Draw(
                channel.Header,
                position: new(xOffset, curY),
                justify: Vector2.Zero,
                scale: Vector2.One * scale,
                Color.Yellow
            );
            curY += lineHeight;
            // draw players
            foreach (var item in playerEntries)
            {
                float drawY = curY + PlayerEntryYPadding;
                var player = item.Player;

                // -- left to right drawing --
                float x = xOffset + PlayerEntryXPadding;
                // draw player name
                string playerName = item.DisplayName;
                MiaoNetFont.Draw(
                    playerName,
                    position: new(x, drawY),
                    justify: Vector2.Zero,
                    scale: Vector2.One * scale,
                    player.Info.Color
                );
                x += MiaoNetFont.Measure(playerName).X * scale;

                if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.Paused))
                {
                    x += PausedTexOffsetRange;

                    float texScale = lineHeight / texPlayerPaused.Height;
                    texPlayerPaused.Draw(new(x + pausedTexOffset, drawY), Vector2.Zero, Color.White, Vector2.One * texScale);

                    x += texScale * texPlayerPaused.Width + PausedTexOffsetRange;
                }

                if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.Interactions))
                {
                    float texScale = lineHeight / texPlayerInteractions.Height;
                    texPlayerInteractions.Draw(new(x, drawY), Vector2.Zero, Color.White, Vector2.One * texScale);

                    x += texScale * texPlayerInteractions.Width;
                }

                if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.LiveMode))
                {
                    float texScale = lineHeight / texLiveMode.Height;
                    texLiveMode.Draw(new(x, drawY), Vector2.Zero, Color.White, Vector2.One * texScale);

                    x += texScale * texLiveMode.Width;
                }

                if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.TakingGolden))
                {
                    float texScale = lineHeight / texTakingGolden.Height;
                    texTakingGolden.Draw(new(x, drawY), Vector2.Zero, Color.White, Vector2.One * texScale);

                    x += texScale * texTakingGolden.Width;
                }

                if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.GroupPhotoMode))
                {
                    float texScale = lineHeight / texGroupPhotoMode.Height;
                    texGroupPhotoMode.Draw(new(x, drawY), Vector2.Zero, Color.White, Vector2.One * texScale);

                    x += texScale * texGroupPhotoMode.Width;
                }

                if (player.GlobalFlags.HasFlag(PlayerGlobalFlags.Watching))
                {
                    float texScale = lineHeight / texPlayerDebugMap.Height;
                    texPlayerDebugMap.Draw(new(x, drawY), Vector2.Zero, Color.White, Vector2.One * texScale);

                    x += texScale * texPlayerDebugMap.Width;
                }

                // -- right to left drawing --
                x = xOffset + totalMaxLineWidth - PlayerEntryXPadding;

                // draw ping
                if (item.PingText is not null)
                {
                    MiaoNetFont.Draw(
                        item.PingText,
                        position: new(x, drawY),
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

                    var iconTex = item.AreaIconTexture;
                    if (iconTex is not null)
                    {
                        float iconScale = lineHeight / iconTex.Height;
                        iconTex.DrawJustified(
                            new(x, drawY),
                            Vector2.UnitX,
                            Color.White,
                            Vector2.One * iconScale
                        );
                        x -= iconTex.Width * iconScale;
                        x -= spaceWidth;
                    }


                    // draw side
                    if (item.AreaModeText is not null)
                    {
                        MiaoNetFont.Draw(
                            item.AreaModeText,
                            position: new(x, drawY),
                            justify: Vector2.UnitX,
                            scale: Vector2.One * scale,
                            item.MapSideColor
                        );
                        x -= MiaoNetFont.Measure(item.AreaModeText).X * scale;
                        x -= spaceWidth;
                    }

                    // draw name or sid
                    bool liveMode = MiaoNetModule.Settings.LiveMode;

                    string mapName = item.IsLocallyKnownMap ? item.MapName! : liveMode ? "*" : item.MapName!;
                    MiaoNetFont.Draw(
                        mapName,
                        position: new(x, drawY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        item.MapNameColor
                    );
                    x -= MiaoNetFont.Measure(mapName).X * scale;
                    x -= spaceWidth;

                    // draw a colon
                    MiaoNetFont.Draw(
                        ":",
                        position: new(x, drawY),
                        justify: Vector2.UnitX,
                        scale: Vector2.One * scale,
                        Color.LightGray
                    );
                    x -= colonWidth;

                    // draw room name, or debug map texture
                    if (!loc.IsInDebugMap)
                    {
                        MiaoNetFont.Draw(
                            liveMode ? "*" : item.MapRoom!,
                            position: new(x, drawY),
                            justify: Vector2.UnitX,
                            scale: Vector2.One * scale,
                            Color.LightGray
                        );
                        x -= MiaoNetFont.Measure(item.MapRoom!).X * scale;
                    }
                    else
                    {
                        float texScale = lineHeight / texPlayerDebugMap.Height;
                        texPlayerDebugMap.DrawJustified(new(x, drawY), Vector2.UnitX, Color.White, Vector2.One * texScale);
                        x -= texScale * texPlayerDebugMap.Width;
                    }
                }

                curY += playerEntryHeight;
            }
        }
    }
}
