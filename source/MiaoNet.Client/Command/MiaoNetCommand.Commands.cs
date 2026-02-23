using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Celeste.Mod.ChatInputBox;
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
            ),
            new MiaoNetCommand(
                name: "back",
                aliases: null,
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Back)
            ),
            new MiaoNetCommand(
                name: "group-photo-mode",
                aliases: ["gpm", "HeYing", "hy"],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(GroupPhotoMode)
            ),
            new MiaoNetCommand(
                name: "interactions",
                aliases: ["int"],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Interactions)
            ),
            new MiaoNetCommand(
                name: "locate",
                aliases: ["lc"],
                segments: [CommandSegmentType.Player],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Locate)
            ),
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

    #region Teleport
    private static void NotifyTeleportBehaviour(Context context)
    {
        foreach (var item in Dialog.Clean("miaonet_commands_teleport_notice").EnumerateLines())
            context.TipMessage(item.ToString());
    }

    private static string? TeleportNoSession(Context context)
    {
        if (!MiaoNetModule.Settings.TippedTeleport)
        {
            MiaoNetModule.Settings.TippedTeleport = true;
            MiaoNetModule.Instance.SaveSettings();
            NotifyTeleportBehaviour(context);
            return null;
        }

        string? error;

        error = MatchNotSelfPlayer(context, context.Segments[0], out var player);
        if (error is not null)
            return error;

        error = EnsurePlayerInExistedMap(player!, out AreaData? area);
        if (error is not null)
            return error;

        PlayerLocation loc = player!.Location;
        AreaKey areaKey = new(area!.ID, loc.Side);
        bool moveToDebugSave = MiaoNetModule.Settings.TeleportTempSave;
        StartTeleportRoutine(
            context, moveToDebugSave, null, areaKey, loc.MapRoom,
            () => NoticeTeleportFinished(context, moveToDebugSave, true, player.Info.Name)
        );

        return null;
    }

    private static string? TeleportWithSession(Context context)
    {
        if (!MiaoNetModule.Settings.TippedTeleport)
        {
            MiaoNetModule.Settings.TippedTeleport = true;
            MiaoNetModule.Instance.SaveSettings();
            NotifyTeleportBehaviour(context);
            return null;
        }

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

            bool moveToDebugSave = MiaoNetModule.Settings.TeleportTempSave;
            var sessionData = response.Session;
            StartTeleportRoutine(
                context, moveToDebugSave, sessionData, areaKey, loc.MapRoom,
                () => NoticeTeleportFinished(context, moveToDebugSave, false, player.Info.Name)
            );
        }

        return null;
    }

    private static void StartTeleportRoutine(Context context, bool moveToDebugSave, PlayerSessionData? sessionData, AreaKey areaKey, string mapRoom, Action onFinished)
    {
        Entity e = new();
        e.Add(new Coroutine(MoveToRoutine(context, moveToDebugSave, sessionData, areaKey, mapRoom, onFinished)));
        Engine.Scene.Add(e);

        static IEnumerator MoveToRoutine(Context context, bool moveToDebugSave, PlayerSessionData? sessionData, AreaKey areaKey, string mapRoom, Action onFinished)
        {
            Level? level = Engine.Scene as Level;
            if (moveToDebugSave)
            {
                if (level is not null && SaveData.Instance.FileSlot != -1)
                {
                    context.MiaoNetContext.MainComponent.LastLocationBeforeTeleport = (level.Session, SaveData.Instance, SaveData.Instance.FileSlot);
                    // save data first
                    UserIO.SaveHandler(true, true);
                    // once saved, the routine will be null
                    while (Celeste.SaveRoutine is not null)
                        yield return null;
                    if (UserIO.SavingResult == false)
                        yield return null;
                }

                // switch to debug save
                SaveData.InitializeDebugMode();
                var ins = SaveData.Instance;
                SafeGuard.Assert(ins.DebugMode);
                ins.VariantMode = true;
                ins.AssistMode = true;
                ins.CheatMode = true;
            }
            else
            {
                // ensure at least there's a save
                if (SaveData.Instance is null)
                    SaveData.InitializeDebugMode();
            }

            ScreenWipe wipe;
            if (level is not null)
            {
                level.DoScreenWipe(false);
                wipe = level.Wipe;
            }
            else
            {
                wipe = new WindWipe(Engine.Scene, false);
            }

            while (!wipe.Completed)
                yield return null;

            // create the session (it relies static SaveData instance)
            Session session;
            if (sessionData is not null)
                session = sessionData.CreateSession(areaKey, mapRoom);
            else
                session = new Session(areaKey, mapRoom);

            // then goto the level
            if (sessionData is not null)
                MiaoNetModule.NextPlayerSpawnPosition = sessionData.Position;
            Engine.Scene = new LevelLoader(session)
            {
                PlayerIntroTypeOverride = Player.IntroTypes.Respawn,
            };
            onFinished();

            yield break;
        }
    }

    private static void NoticeTeleportFinished(Context context, bool moveToDebugSave, bool noSession, string playerName)
    {
        context.TipMessage(
            Dialog.Get(noSession ? "miaonet_commands_teleport_success_nosession" : "miaonet_commands_teleport_success")
                  .Replace("(0)", playerName)
        );
        if (moveToDebugSave)
            context.TipMessage(Dialog.Get("miaonet_commands_teleport_back_notice"));
    }

    private static string? Back(Context context)
    {
        var mc = context.MiaoNetContext.MainComponent;
        var lt = mc.LastLocationBeforeTeleport;
        if (lt.session is null)
            return Dialog.Get("miaonet_commands_back_no_back");

        SaveData.Start(lt.saveData, lt.slot);
        LevelEnter.Go(lt.session, true);
        context.TipMessage(Dialog.Get("miaonet_commands_back_backed"));
        mc.LastLocationBeforeTeleport = (null, null, 0);

        return null;
    }

    private static string? Teleport(Context context)
        => MiaoNetModule.Settings.TeleportBehaviour switch
        {
            TeleportBehaviour.NoSession => TeleportNoSession(context),
            TeleportBehaviour.WithSession => TeleportWithSession(context),
            _ => null,
        };
    #endregion

    #region Help
    private static string? Help(Context context)
    {
        // == MiaoNet Command Help (2) ==
        // /cmd1 : desc of cmd1 (Aliases: c1, a1)
        // /cmd2 <player> <text> : desc of cmd2
        //     <player> : desc of param1
        //     <text> : desc of param2

        context.TipMessage(CommandHelpTitle.Replace("(0)", Commands.Count.ToString()));
        foreach (var command in Commands)
            TipCommandHelp(context, command);
        return null;
    }

    private static string? HelpCommand(Context context)
    {
        string name = context.Segments[0];
        MiaoNetCommand? command = UniqueMatcher.MatchBy(
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
    #endregion

    private static string? Whisper(Context context)
    {
        if (MiaoNetModule.Settings.LiveMode)
            return Dialog.Get("miaonet_chat_disabled");

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
                context.MiaoNetContext.ChatComponent.OnSentPrivateMessage(response.DateTime, player, content);
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

    private static string? GroupPhotoMode(Context context)
    {
        var settings = MiaoNetModule.Settings;
        bool p = settings.GroupPhotoMode;
        settings.GroupPhotoMode = !p;
        string key = p ? "miaonet_commands_group_photo_mode_off" : "miaonet_commands_group_photo_mode_on";
        context.TipMessage(Dialog.Get(key));
        return null;
    }

    private static string? Interactions(Context context)
    {
        var settings = MiaoNetModule.Settings;
        bool p = settings.PlayerInteractions;
        settings.PlayerInteractions = !p;
        string key = p ? "miaonet_commands_interactions_off" : "miaonet_commands_interactions_on";
        context.TipMessage(Dialog.Get(key));
        return null;
    }

    private static string? Locate(Context context)
    {
        string? error = MatchNotSelfPlayer(context, context.Segments[0], out OnlinePlayer? player);
        if (error is not null)
            return error;

        error = EnsurePlayerInExistedMap(player!, out AreaData? othersArea);
        if (error is not null)
            return error;

        string m = Dialog.Get("miaonet_commands_locate_message")
            .Replace("(0)", player!.Info.Name)
            .Replace("(1)", Dialog.Get(othersArea!.Name));

        context.AddLocalChat(MiaoNetChatText.CreateCommandTip(m));

        return null;
    }

    private static string? MatchNotSelfPlayer(Context context, string playerName, out OnlinePlayer? player)
    {
        player = null;
        var clientState = context.MiaoNetContext.ClientState!;
        var allPlayers = from pair in clientState.SelfChannel.Players select pair.Value;
        var matchedPlayer = UniqueMatcher.MatchBy(allPlayers.Append(clientState.Self), p => p.Info.Name, playerName);
        if (matchedPlayer is null)
            return PlayerNotFound.Replace("(0)", playerName);
        if (matchedPlayer == clientState.Self)
            return PlayerIsSelf.Replace("(0)", matchedPlayer.Info.Name);

        player = matchedPlayer;
        return null;
    }

    private static string? EnsurePlayerInExistedMap(OnlinePlayer player, out AreaData? othersArea)
    {
        othersArea = null;

        PlayerLocation loc = player.Location;
        if (!loc.IsInMap)
            return PlayerNotInMap.Replace("(0)", player.Info.Name);

        bool liveMode = MiaoNetModule.Settings.LiveMode;
        var area = AreaData.Get(loc.MapSid);
        if (area is null || area.Mode.Length <= (int)loc.Side)
            return PlayerMapMissing.Replace("(0)", player.Info.Name).Replace("(1)", liveMode ? "*" : loc.ToString());

        othersArea = area;
        return null;
    }
}