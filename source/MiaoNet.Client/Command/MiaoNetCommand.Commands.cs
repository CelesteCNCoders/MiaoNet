using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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
                description: "miaonet_commands_help_description",
                aliases: ["?", "？", "h"],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Help)
            ),
            new MiaoNetCommand(
                name: "help-command",
                description: "miaonet_commands_help_command_description",
                aliases: ["??", "？？", "hc"],
                segments: [
                    new Segment(
                        CommandSegmentType.Text,
                        "miaonet_commands_help_command_p1_name",
                        "miaonet_commands_help_command_p1_description"
                    )
                ],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(HelpCommand)
            ),
            new MiaoNetCommand(
                name: "say",
                description: "miaonet_commands_say_description",
                aliases: null,
                segments: [
                    new Segment(
                        CommandSegmentType.Text,
                        "miaonet_commands_say_p1_name",
                        "miaonet_commands_say_p1_description"
                    )
                ],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Say)
            ),
            new MiaoNetCommand(
                name: "emote",
                description: "miaonet_commands_emote_description",
                aliases: ["e"],
                segments: [
                    new Segment(
                        CommandSegmentType.Text,
                        "miaonet_commands_emote_p1_name",
                        "miaonet_commands_emote_p1_description"
                     )
                ],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Emote)
            ),
            new MiaoNetCommand(
                name: "teleport-no-session",
                description: "miaonet_commands_teleport_no_session_description",
                aliases: ["tp-ns", "tpns"],
                segments: [
                    new Segment(
                        CommandSegmentType.Player,
                        "miaonet_commands_teleport_no_session_p1_name",
                        "miaonet_commands_teleport_no_session_p1_description"
                    )
                ],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(TeleportNoSession)
            ),
            new MiaoNetCommand(
                name: "teleport-with-session",
                description: "miaonet_commands_teleport_with_session_desciption",
                aliases: ["tp-ws", "tpws"],
                segments: [
                    new Segment(
                        CommandSegmentType.Player,
                        "miaonet_commands_teleport_with_session_p1_name",
                        "miaonet_commands_teleport_with_session_p1_description"
                    )
                ],
                captureRestSegments:false,
                onExecute: new ExecuteHandler(TeleportWithSession)
            ),
            new MiaoNetCommand(
                name: "whisper",
                description: "miaonet_commands_whisper_desciption",
                aliases: ["w", "msg"],
                segments: [
                    new Segment(
                        CommandSegmentType.Player,
                        "miaonet_commands_whisper_p1_name",
                        "miaonet_commands_whisper_p1_description"
                    ),
                    new Segment(
                        CommandSegmentType.Text,
                        "miaonet_commands_whisper_p2_name",
                        "miaonet_commands_whisper_p2_description"
                    )
                ],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Whisper)
            )
        ];
    }

    private static string PlayerIsSelf => Dialog.Clean("miaonet_command_status_player_is_self");
    private static string PlayerNotFound => Dialog.Clean("miaonet_command_status_player_not_found");
    private static string PlayerNotInMap => Dialog.Clean("miaonet_command_status_player_not_in_map");
    private static string PlayerMapMissing => Dialog.Clean("miaonet_command_status_player_map_missing");
    private static string NeedInMap => Dialog.Clean("miaonet_command_status_need_in_level");
    private static string CommandHelpTitle => Dialog.Clean("miaonet_command_help_title");
    private static string CommandHelpNotFound => Dialog.Clean("miaonet_command_help_not_found");

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
        string? error;

        error = MatchNotSelfPlayer(context, context.Segments[0], out var player);
        if (error is not null)
            return error;

        error = EnsurePlayerInExistedMap(player!, out AreaData? area);
        if (error is not null)
            return error;

        PlayerLocation loc = player!.Location;
        AreaKey areaKey = new(area!.ID, loc.MapSide);
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

    private static string? TeleportWithSession(Context context)
    {
        string? error;

        error = MatchNotSelfPlayer(context, context.Segments[0], out var player);
        if (error is not null)
            return error;

        error = EnsurePlayerInExistedMap(player!, out AreaData? area);
        if (error is not null)
            return error;

        PlayerLocation loc = player!.Location;
        AreaKey areaKey = new(area!.ID, loc.MapSide);

        context.TipMessage($"Teleporting to {player.Info.Name}...");

        context.Request(new PacketTeleportRequest(player.ID), OnResponse);

        void OnResponse(PacketTeleportResponse response)
        {
            if (response.IsFailed)
            {
                context.TipErrorMessage($"Teleport failed with reason {response.FailedReason}");
                return;
            }

            var sessionData = response.Session;
            Session session = sessionData.CreateSession(areaKey, loc.MapRoom);
            if (Engine.Scene is Level level)
            {
                // TODO tell player that this action will lose their current progress
                level.DoScreenWipe(false, () =>
                {
                    Goto(session, sessionData.Position);
                    context.TipMessage($"Teleported to {player.Info.Name}");
                });
            }
            else
            {
                Goto(session, sessionData.Position);
                context.TipMessage($"Teleported to {player.Info.Name}");
            }

            static void Goto(Session session, Vector2 position)
            {
                SaveData.InitializeDebugMode();
                SaveData.Instance.LastArea_Safe = session.Area;
                Engine.Scene = new LevelLoader(session)
                {
                    PlayerIntroTypeOverride = Player.IntroTypes.Respawn,
                };
                MiaoNetModule.NextPlayerSpawnPosition = position;
            }
        }

        return null;
    }

    private static string? Help(Context context)
    {
        // == MiaoNet Command Help (2) ==
        // /cmd1 : desc of cmd1 (Aliases: c1, a1)
        // /cmd2 <player> <text> : desc of cmd2
        //     <player> : desc of param1
        //     <text> : desc of param2

        // TODO dialog
        // also messges scrolling
        context.TipMessage(CommandHelpTitle.Replace("(0)", Commands.Count.ToString()));
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
            return CommandHelpNotFound.Replace("(0)", name);

        TipCommandHelp(context, command);

        return null;
    }

    private static string? Whisper(Context context)
    {
        string playerName = context.Segments[0];
        string content = context.Segments[1];

        string? error = MatchNotSelfPlayer(context, playerName, out OnlinePlayer? player);
        if (error is not null)
            return error;

        context.Request(new PacketSendPrivateChatMessage(player!.ID, content), OnResponse);

        void OnResponse(PacketSendPrivateChatMessageResponse response)
        {
            switch(response.Result)
            {
            case PacketSendPrivateChatMessageResponse.SendResult.Success:
                context.MiaoNetContext.ChatComponent.OnSentPrivateMessage(player, content);
                break;
            case PacketSendPrivateChatMessageResponse.SendResult.NoSuchPlayer:
                context.TipErrorMessage($"Could not find player {player.Info.Name}");
                break;
            case PacketSendPrivateChatMessageResponse.SendResult.Denied:
                context.TipErrorMessage($"{player.Info.Name} denied your message");
                break;
            }
        }

        return null;
    }

    private static void TipCommandHelp(Context context, MiaoNetCommand command)
    {
        context.TipMessage(
                $"/{command.Name} : {Dialog.Clean(command.Description)}" +
                $"{(command.Aliases is not null ? $" ({string.Join(", ", command.Aliases)})" : null)}"
            );
        if (command.Segments.Count != 0)
        {
            foreach (var segment in command.Segments)
                context.TipMessage($"    <{Dialog.Clean(segment.Name)}> : {Dialog.Clean(segment.Description)}");
        }
    }

    private static string? MatchNotSelfPlayer(
        Context context,
        string playerName,
        out OnlinePlayer? player
    )
    {
        player = null;
        var clientState = context.MiaoNetContext.ClientState!;
        var allPlayers = from pair in clientState.SelfChannel.Players select pair.Value;
        var matchedPlayer = UniqueMatchBy(allPlayers.Append(clientState.Self), p => p.Info.Name, playerName);
        if (matchedPlayer is null)
            return PlayerNotFound.Replace("(0)", playerName);
        if (matchedPlayer == clientState.Self)
            return PlayerIsSelf.Replace("(0)", matchedPlayer.Info.Name);

        player = matchedPlayer;
        return null;
    }

    private static string? EnsurePlayerInExistedMap(OnlinePlayer player, out AreaData? otherArea)
    {
        otherArea = null;

        PlayerLocation loc = player!.Location;
        if (!loc.IsInMap)
            return PlayerNotInMap.Replace("(0)", player.Info.Name);

        var area = AreaData.Get(loc.MapSid);
        if (area is null || area.Mode.Length <= (int)loc.MapSide)
            return PlayerMapMissing.Replace("(0)", player.Info.Name).Replace("(1)", loc.ToString());

        otherArea = area;
        return null;
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