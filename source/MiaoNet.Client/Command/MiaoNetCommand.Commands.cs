using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetCommand
{
    public static readonly IReadOnlyList<MiaoNetCommand> Commands;

    static MiaoNetCommand()
    {
        // TODO using dialog
        Commands = [
            new MiaoNetCommand(
                name: "say",
                description: "Send a chat message.",
                aliases: null,
                segments: [new(CommandSegmentType.Text, "Text", "The message to send.")],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Say)
            ),
            new MiaoNetCommand(
                name: "emote",
                description: "Send a text emote.",
                aliases: ["e"],
                segments: [new(CommandSegmentType.Text, "Text", "The emote text to send.")],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Emote)
            ),
            new MiaoNetCommand(
                name: "teleport-no-session",
                description: "Teleport to a player but don't fetch their session.",
                aliases: ["tp-ns", "tpns"],
                segments: [new(CommandSegmentType.Player, "Target player", "The player that teleported to.")],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(TeleportNoSession)
            )
        ];
    }

    private static string PlayerIsSelf => Dialog.Clean("miaonet_command_status_player_is_self");
    private static string PlayerNotFound => Dialog.Clean("miaonet_command_status_player_not_found");
    private static string PlayerNotInMap => Dialog.Clean("miaonet_command_status_player_not_in_map");
    private static string PlayerMapMissing => Dialog.Clean("miaonet_command_status_player_map_missing");
    private static string NeedInMap => Dialog.Clean("miaonet_command_status_need_in_level");

    private static string? Say(MiaoNetContext context, IReadOnlyList<string> arguments)
    {
        context.QueuePacket(new PacketSendChatMessage(arguments[0]));
        return null;
    }

    private static string? Emote(MiaoNetContext context, IReadOnlyList<string> arguments)
    {
        bool success = context.EmoteComponent.SendEmote(arguments[0]);
        if (!success)
            return NeedInMap;
        return null;
    }

    private static string? TeleportNoSession(MiaoNetContext context, IReadOnlyList<string> arguments)
    {
        string playerName = arguments[0];
        var allPlayers = from pair in context.ClientState!.SelfChannel.Players select pair.Value;
        var player = MatchPlayerName(allPlayers, context.ClientState.Self, playerName);
        if (player is null)
            return PlayerNotFound.Replace("(0)", playerName);
        if (player == context.ClientState.Self)
            return PlayerIsSelf.Replace("(0)", player.Info.Name);

        PlayerLocation loc = player.Location;
        if (!loc.IsInMap)
            return PlayerNotInMap.Replace("(0)", player.Info.Name);

        AreaData? area = AreaData.Get(loc.MapSid);
        if (area is null || area.Mode.Length <= (int)loc.MapSide)
            return PlayerMapMissing.Replace("(0)", player.Info.Name).Replace("(1)", loc.ToString());

        AreaKey areaKey = new(area.ID, loc.MapSide);
        if (Engine.Scene is Level level)
        {
            // TODO tell player that this action will lose their current progress
            level.DoScreenWipe(false, () => Goto(areaKey, loc.MapRoom));
        }
        else
        {
            Goto(areaKey, loc.MapRoom);
        }
        static void Goto(AreaKey areaKey, string mapRoom)
        {
            SaveData.InitializeDebugMode();
            SaveData.Instance.LastArea_Safe = areaKey;
            Session session = new Session(areaKey);
            session.Level = mapRoom;
            Engine.Scene = new LevelLoader(session) { PlayerIntroTypeOverride = Player.IntroTypes.Respawn };
        }
        return null;
    }

    /// <summary>Case-insensitively match a <see cref="OnlinePlayer"/> by name prefix or full name.</summary>
    /// <returns>Matched <see cref="OnlinePlayer"/>, or <see langword="null"/> if there're multiple matches or no matches.</returns>
    private static OnlinePlayer? MatchPlayerName(IEnumerable<OnlinePlayer> players, OnlinePlayer self, string name)
    {
        OnlinePlayer? curMatched = null;
        foreach (var player in players.Prepend(self))
        {
            if (player.Info.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                if (curMatched is null)
                    curMatched = player;
                else
                    return null;
            }
        }
        return curMatched;
    }
}