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
                name: "help",
                description: "Show help messages of all commands.",
                aliases: ["?", "？", "h"],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Help)
            ),
            new MiaoNetCommand(
                name: "help-command",
                description: "Show help message of a command.",
                aliases: ["??", "？？", "hc"],
                segments: [new(CommandSegmentType.Text, "Text", "Command name.")],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(HelpCommand)
            ),
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

    private static string? Say(Context context)
    {
        context.QueuePacket(new PacketSendChatMessage(context.Segments[0]));
        return null;
    }

    private static string? Emote(Context context)
    {
        bool success = context.MiaoNetContext.EmoteComponent.SendEmote(context.Segments[0]);
        if (!success)
            return NeedInMap;
        return null;
    }

    private static string? TeleportNoSession(Context context)
    {
        string playerName = context.Segments[0];
        var clientState = context.MiaoNetContext.ClientState!;
        var allPlayers = from pair in clientState.SelfChannel.Players select pair.Value;
        var player = UniqueMatchBy(allPlayers.Append(clientState.Self), p => p.Info.Name, playerName);
        if (player is null)
            return PlayerNotFound.Replace("(0)", playerName);
        if (player == clientState.Self)
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

    private static string? Help(Context context)
    {
        // == MiaoNet Command Help (1/0) ==
        // /cmd1 : desc of cmd1 (Aliases: c1, a1)
        // /cmd2 <player> <text> : desc of cmd2
        //     <player> : desc of param1
        //     <text> : desc of param2

        // TODO dialog
        // also messges scrolling
        context.TipMessage($"== MiaoNet Command Help ({Commands.Count}) ==");
        foreach (var command in Commands)
            TipCommandHelp(context, command);
        return null;
    }

    private static string? HelpCommand(Context context)
    {
        string name = context.Segments[0];
        MiaoNetCommand? command = UniqueMatchBy(
            Commands,
            c => c.Aliases is null
                ? [c.Name]
                : c.Aliases.Append(c.Name),
            name
        );

        if (command == null)
            return $"Command \"{name}\" not found.";

        TipCommandHelp(context, command);

        return null;
    }

    private static void TipCommandHelp(Context context, MiaoNetCommand command)
    {
        context.TipMessage(
                $"/{command.Name} : {command.Description}" +
                $"{(command.Aliases is not null ? $" (Aliases: {string.Join(", ", command.Aliases)})" : null)}"
            );
        if (command.Segments.Count != 0)
        {
            foreach (var segment in command.Segments)
                context.TipMessage($"    <{segment.Name}> : {segment.Description}");
        }
    }

    /// <summary>
    /// Case-insensitively match a <typeparamref name="T"/> which contains <paramref name="value"/>.
    /// </summary>
    /// <returns>
    /// Matched <typeparamref name="T"/>, or <see langword="null"/> if there're multiple matches or no matches.
    /// </returns>
    private static T? UniqueMatchBy<T>(IEnumerable<T> items, Func<T, string> selector, string value)
        where T : class
    {
        T? curMatched = null;
        foreach (var item in items)
        {
            if (selector(item).Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                if (curMatched is null)
                    curMatched = item;
                else
                    return null;
            }
        }
        return curMatched;
    }

    /// <summary>
    /// Case-insensitively match a <typeparamref name="T"/> which contains <paramref name="value"/>.
    /// </summary>
    /// <returns>
    /// Matched <typeparamref name="T"/>, or <see langword="null"/> if there're multiple matches or no matches.
    /// </returns>
    private static T? UniqueMatchBy<T>(IEnumerable<T> items, Func<T, IEnumerable<string>> selector, string value)
        where T : class
    {
        T? curMatched = null;
        foreach (var item in items)
        {
            if (selector(item).Any(s => s.Contains(value, StringComparison.OrdinalIgnoreCase)))
            {
                if (curMatched is null)
                    curMatched = item;
                else
                    return null;
            }
        }
        return curMatched;
    }
}