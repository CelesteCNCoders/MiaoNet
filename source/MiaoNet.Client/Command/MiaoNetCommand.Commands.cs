using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetCommand
{
    public static readonly IReadOnlyList<MiaoNetCommand> Commands;

    static MiaoNetCommand()
    {
        Commands = [
            new MiaoNetCommand(
                name: "help",
                aliases: ["?", "？", "h"],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Help)
            ),
            new MiaoNetCommand(
                name: "help-command",
                aliases: ["??", "？？", "hc"],
                segments: [CommandSegmentType.Text],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(HelpCommand)
            ),
            new MiaoNetCommand(
                name: "say",
                aliases: null,
                segments: [CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Say)
            ),
            new MiaoNetCommand(
                name: "emote",
                aliases: ["e"],
                segments: [CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Emote)
            ),
            new MiaoNetCommand(
                name: "teleport-no-session",
                aliases: ["tp-ns", "tpns"],
                segments: [CommandSegmentType.Player],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(TeleportNoSession)
            ),
            new MiaoNetCommand(
                name: "teleport-with-session",
                aliases: ["tp-ws", "tpws"],
                segments: [CommandSegmentType.Player],
                captureRestSegments:false,
                onExecute: new ExecuteHandler(TeleportWithSession)
            ),
            new MiaoNetCommand(
                name: "whisper",
                aliases: ["w", "msg"],
                segments: [CommandSegmentType.Player, CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Whisper)
            ),
            new MiaoNetCommand(
                name: "teleport",
                aliases: ["tp"],
                segments: [CommandSegmentType.Player],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Teleport)
            ),
            new MiaoNetCommand(
                name: "clear",
                aliases: ["cls"],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Clear)
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
        context.QueuePacket(new PacketSendChatMessage(context.Segments[0].Replace(@"\", @"\\")));
        return null;
    }

    private static string? Emote(Context context)
    {
        string text = context.Segments[0];
        EmoteData? data = EmoteData.Parse(text);
        var c = context.MiaoNetContext.EmoteComponent;
        bool success;
        if (data is not null)
            success = c.SendEmote(data.Value);
        else
            success = c.SendEmote(text);
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
        AreaKey areaKey = new(area!.ID, loc.Side);
        if (Engine.Scene is Level level)
        {
            // TODO tell player that this action will lose their current progress
            level.DoScreenWipe(false, () => GotoAndTip(areaKey, loc.MapRoom, context, player.Info.Name));
        }
        else
        {
            GotoAndTip(areaKey, loc.MapRoom, context, player.Info.Name);
        }

        static void GotoAndTip(AreaKey areaKey, string mapRoom, Context context, string playerName)
        {
            SaveData.InitializeDebugMode();
            SaveData.Instance.LastArea_Safe = areaKey;
            Session session = new Session(areaKey);
            session.Level = mapRoom;
            Engine.Scene = new LevelLoader(session) { PlayerIntroTypeOverride = Player.IntroTypes.Respawn };

            context.TipMessage(Dialog.Get("miaonet_commands_teleport_success_nosession").Replace("(0)", playerName));
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
        AreaKey areaKey = new(area!.ID, loc.Side);

        context.TipMessage(Dialog.Get("miaonet_commands_teleport_tip").Replace("(0)", player.Info.Name));

        context.Request(new PacketTeleportRequest(player.ID), OnResponse);

        void OnResponse(PacketTeleportResponse response)
        {
            if (response.IsFailed)
            {
                context.TipErrorMessage(
                    Dialog.Get("miaonet_commands_teleport_failed_tip")
                    .Replace("(0)", Dialog.Get($"miaonet_commands_teleport_failed_{response.FailedReason}"))
                );
                return;
            }

            var sessionData = response.Session;
            Session session = sessionData.CreateSession(areaKey, loc.MapRoom);
            if (Engine.Scene is Level level)
            {
                // TODO tell player that this action will lose their current progress
                level.DoScreenWipe(false, () =>
                {
                    GotoAndTip(context, player.Info.Name, sessionData.Position, session);
                });
            }
            else
            {
                GotoAndTip(context, player.Info.Name, sessionData.Position, session);
            }

            static void GotoAndTip(Context context, string playerName, Vector2 position, Session session)
            {
                SaveData.InitializeDebugMode();
                SaveData.Instance.LastArea_Safe = session.Area;
                Engine.Scene = new LevelLoader(session)
                {
                    PlayerIntroTypeOverride = Player.IntroTypes.Respawn,
                };
                MiaoNetModule.NextPlayerSpawnPosition = position;

                context.TipMessage(Dialog.Get("miaonet_commands_teleport_success").Replace("(0)", playerName));
            }
        }

        return null;
    }

    private static string? Teleport(Context context)
        => MiaoNetModule.Settings.TeleportBehaviour switch
        {
            TeleportBehaviour.NoSession => TeleportNoSession(context),
            TeleportBehaviour.WithSession => TeleportWithSession(context),
            _ => null,
        };

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
            switch (response.Result)
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

    private static string? Clear(Context context)
    {
        context.MiaoNetContext.ChatComponent.ClearChat();
        return null;
    }

    private static void TipCommandHelp(Context context, MiaoNetCommand command)
    {
        string commandNameKey = command.Name.Replace('-', '_');
        string commandDescriptionKey = $"miaonet_commands_{commandNameKey}_description";
        context.TipMessage(
                $"/{command.Name} : {Dialog.Get(commandDescriptionKey)}" +
                $"{(command.Aliases is not null ? $" ({string.Join(", ", command.Aliases)})" : null)}"
            );
        if (command.Segments.Count != 0)
        {
            int i = 0;
            foreach (var segment in command.Segments)
            {
                string nameKey = $"miaonet_commands_{commandNameKey}_s{i}_name";
                string description = $"miaonet_commands_{commandNameKey}_s{i}_description";
                context.TipMessage($"    <{Dialog.Get(nameKey)}> : {Dialog.Get(description)}");
                i++;
            }
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
        if (area is null || area.Mode.Length <= (int)loc.Side)
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