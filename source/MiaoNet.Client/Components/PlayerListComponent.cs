using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class PlayerListComponent : MiaoNetComponent
{
    private List<(OnlineChannel, List<OnlinePlayer>)> channelPlayerList;

    public PlayerListComponent(MiaoNetContext context)
        : base(context)
    {
        BuildPlayerList();
    }

    [MemberNotNull(nameof(channelPlayerList))]
    private void BuildPlayerList()
    {
#if true
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
        channelPlayerList = [(cMain, mainChannelPlayerList), (cOther, otherChannelPlayerList)];
        mainChannelPlayerList.Sort(new PlayerListEntryComparer());
        return;
#else
        if (context.ClientState is null)
        {
            playerList = [];
            return;
        }
        playerList = new(context.ClientState.Players.Select(p => p.Value));
        playerList.Sort(new PlayerListComparer());
#endif
    }

    public override void Render()
    {
        /*
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

        const float Scale = 1 / 3f;

        const float RectXOffset = 16f;
        const float RectYOffset = 16f;
        const float RectXPadding = 16f;
        const float RectYPadding = 16f;
        const float MiddlePadding = 8f;
        float midTextWidth = MiaoNetFont.MeasurePlayerListEntry(" @ ").X * Scale;
        float lineHeight = MiaoNetFont.LineHeight * Scale;

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
                float width = MiaoNetFont.MeasurePlayerListEntry(player.Info.Name).X * Scale;
                width += MiaoNetFont.MeasurePlayerListEntry(player.Location.ToString()).X * Scale;
                maxLineWidth = Math.Max(maxLineWidth, width);
                totalHeight += lineHeight;
            }

            totalHeight += RectYPadding;
            channelHeights[i] = totalHeight - channelYOffsets[i];
            totalHeight += lineHeight; // empty line padding
        }

        maxLineWidth += MiddlePadding;
        maxLineWidth += midTextWidth;

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
            var tuple = channelPlayerList[i];
            float curX = RectXOffset + RectXPadding;
            float curY = channelYOffsets[i] + RectYPadding;
            MiaoNetFont.DrawPlayerListEntry(
                $"#{tuple.Item1.Name} {tuple.Item1.Players.Count} Players",
                new(curX, curY),
                Color.Yellow,
                Scale
            );
            curY += lineHeight;
            foreach (var player in tuple.Item2)
            {
                MiaoNetFont.DrawPlayerListEntry(
                    $"{player.Info.Name} @ {player.Location}",
                    new(curX, curY),
                    Color.White,
                    Scale
                );
                curY += lineHeight;
            }
        }
    }
}
