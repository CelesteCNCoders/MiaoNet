using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
                aliases: [ "?", "？", "h" ],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Help)
            ),
            new MiaoNetCommand(
                name: "help-command",
                aliases: [ "??", "？？", "hc" ],
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
                aliases: [ "e" ],
                segments: [CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Emote)
            ),
            new MiaoNetCommand(
                name: "teleport-no-session",
                aliases: [ "tpns" ],
                segments: [CommandSegmentType.PlayerSameChannel],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(TeleportNoSession)
            ),
            new MiaoNetCommand(
                name: "teleport-with-session",
                aliases: [ "tpws" ],
                segments: [CommandSegmentType.PlayerSameChannel],
                captureRestSegments:false,
                onExecute: new ExecuteHandler(TeleportWithSession)
            ),
            new MiaoNetCommand(
                name: "whisper",
                aliases: [ "w", "msg" ],
                segments: [CommandSegmentType.Player, CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Whisper)
            ),
            new MiaoNetCommand(
                name: "teleport",
                aliases: [ "tp" ],
                segments: [CommandSegmentType.PlayerSameChannel],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Teleport)
            ),
            new MiaoNetCommand(
                name: "clear",
                aliases: [ "cls" ],
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
                aliases: [ "gpm", "hy" ],
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
                aliases: [ "lc" ],
                segments: [CommandSegmentType.Player],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Locate)
            ),
            new MiaoNetCommand(
                name: "watch",
                aliases: [ "wt" ],
                segments: [CommandSegmentType.PlayerSameMap],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Watch)
            ),
            new MiaoNetCommand(
                name: "unwatch",
                aliases: [ "uw", "uwt" ],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(Unwatch)
            ),
            new MiaoNetCommand(
                name: "map-chat",
                aliases: [ "mc" ],
                segments: [CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(MapChat)
            ),
            new MiaoNetCommand(
                name: "channel-chat",
                aliases: [ "cc" ],
                segments: [CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(ChannelChat)
            ),
            new MiaoNetCommand(
                name: "global-chat",
                aliases: [ "gc" ],
                segments: [CommandSegmentType.Text],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(GlobalChat)
            ),
            new MiaoNetCommand(
                name: "chat",
                aliases: [ "c" ],
                segments: [CommandSegmentType.ChatChannelType],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(ChatType)
            ),
            new MiaoNetCommand(
                name: "random-teleport",
                aliases: [ "rtp" ],
                segments: [],
                captureRestSegments: false,
                onExecute: new ExecuteHandler(RandomTeleport)
            ),
            new MiaoNetCommand(
                name: "channel",
                aliases: [ "join" ],
                segments: [CommandSegmentType.Channel],
                captureRestSegments: true,
                onExecute: new ExecuteHandler(Channel)
            )
        ];
    }

    private static string PlayerIsSelf => Dialog.Clean("miaonet_command_status_player_is_self");
    private static string PlayerNotFound => Dialog.Clean("miaonet_command_status_player_not_found");
    private static string PlayerNotInMap => Dialog.Clean("miaonet_command_status_player_not_in_map");
    private static string PlayerMapMissing => Dialog.Clean("miaonet_command_status_player_map_missing");
    private static string NoAvailablePlayer => Dialog.Clean("miaonet_command_status_no_available_player");
    private static string NeedInMap => Dialog.Clean("miaonet_command_status_need_in_level");
    private static string CommandHelpTitle => Dialog.Clean("miaonet_command_help_title");
    private static string CommandHelpNotFound => Dialog.Clean("miaonet_command_help_not_found");

#pragma warning disable CA1305

    private static string? Say(Context context)
    {
        string content = context.Segments[0].Replace(@"\", @"\\", StringComparison.Ordinal);
        SendChat(context, MiaoNetModule.Settings.ChatChannel, content);
        return null;
    }

    private static string? Emote(Context context)
    {
        string text = context.Segments[0];
        var c = context.MiaoNetContext.EmoteComponent;
        bool success;
        if (EmoteData.TryParse(text, out EmoteData emoteData))
            success = c.SendEmote(emoteData);
        else
            success = c.SendEmote(text);
        if (!success)
            return NeedInMap;
        return null;
    }

    #region Teleport
    private static bool NotifyTeleportBehaviourOnce(Context context)
    {
        if (MiaoNetModule.Settings.TippedTeleport)
            return false;

        MiaoNetModule.Settings.TippedTeleport = true;
        MiaoNetModule.Instance.SaveSettings();
        foreach (var item in Dialog.Clean("miaonet_commands_teleport_notice").EnumerateLines())
            context.TipMessage(item.ToString());
        return true;
    }

    private static string? TeleportNoSession(Context context)
    {
        if (NotifyTeleportBehaviourOnce(context))
            return null;

        string? error;

        error = GetNotSelfPlayer(context, context.Segments[0], out var player);
        if (error is not null)
            return error;

        return TeleportNoSessionTo(context, player!);
    }
    private static string? TeleportNoSessionTo(Context context, OnlinePlayer player)
    {
        string? error = EnsurePlayerInExistedMap(player!, out AreaData? area);
        if (error is not null)
            return error;

        PlayerLocation loc = player!.Location;
        AreaKey areaKey = new(area!.ID, loc.Map.AreaMode);
        bool moveToDebugSave = MiaoNetModule.Settings.TeleportTempSave;
        StartTeleportRoutine(
            context, moveToDebugSave, null, areaKey, loc.Room,
            () => NoticeTeleportFinished(context, moveToDebugSave, true, player.Info.Name)
        );

        return null;
    }

    private static string? TeleportWithSession(Context context)
    {
        if (NotifyTeleportBehaviourOnce(context))
            return null;

        string? error;

        error = GetNotSelfPlayer(context, context.Segments[0], out var player);
        if (error is not null)
            return error;

        return TeleportWithSessionTo(context, player!);
    }

    private static string? TeleportWithSessionTo(Context context, OnlinePlayer player)
    {
        string? error = EnsurePlayerInExistedMap(player!, out AreaData? area);
        if (error is not null)
            return error;

        PlayerLocation loc = player!.Location;
        AreaKey areaKey = new(area!.ID, loc.Map.AreaMode);

        context.TipMessage(PFormat.Format(Dialog.Get("miaonet_commands_teleport_tip"), player.Info.Name));

        context.Request(new PacketTeleportRequest(player.ID), OnResponse);

        void OnResponse(PacketTeleportResponse response)
        {
            if (response.IsFailed)
            {
                context.TipErrorMessage(
                    PFormat.Format(
                        Dialog.Get("miaonet_commands_teleport_failed_tip"),
                        Dialog.Get($"miaonet_commands_teleport_failed_{response.FailedReason}")
                    )
                );
                return;
            }
            bool moveToDebugSave = MiaoNetModule.Settings.TeleportTempSave;
            var sessionData = response.Session;
            StartTeleportRoutine(
                context, moveToDebugSave, sessionData, areaKey, loc.Room,
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
        string msg = PFormat.Format(
            Dialog.Get(
                noSession
                ? "miaonet_commands_teleport_success_nosession"
                : "miaonet_commands_teleport_success"
            ), playerName
        );
        context.TipMessage(msg);
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

    private static string? RandomTeleport(Context context)
    {
        var error = GetRandomNotSelfPlayer(context, out var player);
        if (error is not null)
            return error;

        return MiaoNetModule.Settings.TeleportBehaviour switch
        {
            TeleportBehaviour.NoSession => TeleportNoSessionTo(context, player!),
            TeleportBehaviour.WithSession => TeleportWithSessionTo(context, player!),
            _ => null,
        };
    }
    #endregion

    #region Help
    private static string? Help(Context context)
    {
        // == MiaoNet Command Help (2) ==
        // /cmd1 : desc of cmd1 (Aliases: c1, a1)
        // /cmd2 <player> <text> : desc of cmd2
        //     <player> : desc of param1
        //     <text> : desc of param2

        context.TipMessage(PFormat.Format(CommandHelpTitle, Commands.Count));
        foreach (var command in Commands)
            TipCommandHelp(context, command);
        return null;
    }

    private static string? HelpCommand(Context context)
    {
        string name = context.Segments[0];
        MiaoNetCommand? command = Commands.FirstOrDefault(c => c.Name == name || c.Aliases?.Any(a => a == name) == true);

        if (command == null)
            return PFormat.Format(CommandHelpNotFound, name);

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

        string? error = GetNotSelfPlayer(context, playerName, out OnlinePlayer? player);
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
                // TODO localize
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
        string? error = GetNotSelfPlayer(context, context.Segments[0], out OnlinePlayer? player);
        if (error is not null)
            return error;

        error = EnsurePlayerInExistedMap(player!, out AreaData? othersArea);
        if (error is not null)
            return error;

        string m = PFormat.Format(Dialog.Get("miaonet_commands_locate_message"), player!.Info.Name, Dialog.Get(othersArea!.Name));

        context.TipMessage(m);

        return null;
    }

    private static string? Watch(Context context)
    {
        string? error = GetNotSelfPlayer(context, context.Segments[0], out OnlinePlayer? player);
        if (error is not null)
            return error;

        error = EnsurePlayerInExistedMap(player!, out AreaData? othersArea);
        if (error is not null)
            return error;

        var self = context.MiaoNetContext.ClientState!.Self;
        if (self.Location.Map != player!.Location.Map)
        {
            string m = PFormat.Format(Dialog.Get("miaonet_commands_watch_not_same_map"), player!.Info.Name, Dialog.Get(othersArea!.Name));
            return m;
        }

        context.MiaoNetContext.MainComponent.StartWatching(player!);
        context.TipMessage(PFormat.Format(Dialog.Get("miaonet_commands_watch_watching"), player!.Info.Name));

        return null;
    }

    private static string? Unwatch(Context context)
    {
        var player = context.MiaoNetContext.MainComponent.StopWatching();
        if (player is null)
        {
            return Dialog.Get("miaonet_commands_unwatch_none_unwatched");
        }
        else
        {
            string msg = PFormat.Format(Dialog.Get("miaonet_commands_unwatch_unwatched"), player.Info.Name);
            context.TipMessage(msg);
        }

        return null;
    }

    private static string? ChatType(Context context)
    {
        string name = context.Segments[0];
        var settings = MiaoNetModule.Settings;
        ChatChannel type = ChatChannelMatcher.Match(name);
        if (type != (ChatChannel)(-1))
        {
            settings.ChatChannel = type;
            string msg = PFormat.Format(Dialog.Get("miaonet_commands_chat_chat_channel_switched"), type);
            context.AddLocalChat(MiaoNetChatText.CreateCommandTip(msg));
        }
        else
        {
            return PFormat.Format(Dialog.Get("miaonet_commands_chat_chat_channel_type_not_found"), name);
        }
        return null;
    }

    private static string? GlobalChat(Context context)
        => SendChat(context, ChatChannel.Global, context.Segments[0]);

    private static string? ChannelChat(Context context)
        => SendChat(context, ChatChannel.Channel, context.Segments[0]);

    private static string? MapChat(Context context)
        => SendChat(context, ChatChannel.Map, context.Segments[0]);

    private static string? SendChat(Context context, ChatChannel chatChannel, string content)
    {
        if (MiaoNetModule.Settings.LiveMode)
            return Dialog.Get("miaonet_chat_disabled");

        context.QueuePacket(new PacketSendChatMessage(chatChannel, content));
        return null;
    }

    private static string? Channel(Context context)
    {
        string channelName = context.Segments[0];
        var channel = context.MiaoNetContext.ClientState
            !.Channels.FirstOrDefault(p => p.Value.Info.Name == channelName);

        if (channel.Value is null)
        {
            context.QueuePacket(new PacketChannelCreateAndJoin(new ChannelInfo(channelName)));
        }
        else
        {
            context.QueuePacket(new PacketPlayerChannelMove(channel.Value.ID));
        }

        return null;
    }

    #region helpers

    private static string? GetNotSelfPlayer(Context context, string playerName, out OnlinePlayer? player)
    {
        player = null;
        var clientState = context.MiaoNetContext.ClientState!;
        var foundPlayer = clientState.SelfChannel.Players
            .FirstOrDefault(p => p.Info.Name == playerName);
        if (foundPlayer is null)
        {
            return clientState.Self.Info.Name == playerName
                ? PFormat.Format(PlayerIsSelf, clientState.Self.Info.Name)
                : PFormat.Format(PlayerNotFound, playerName);
        }

        player = foundPlayer;
        return null;
    }

    private static string? EnsurePlayerInExistedMap(OnlinePlayer player, out AreaData? othersArea)
    {
        othersArea = null;

        PlayerLocation loc = player.Location;
        if (!loc.IsInMap)
            return PFormat.Format(PlayerNotInMap, player.Info.Name);

        bool liveMode = MiaoNetModule.Settings.LiveMode;
        var area = AreaData.Get(loc.Map.Sid);
        if (area is null || area.Mode.Length <= (int)loc.Map.AreaMode)
            return PFormat.Format(PlayerMapMissing, player.Info.Name, liveMode ? "*" : loc.Map.Sid);

        othersArea = area;
        return null;
    }

    private static bool IsPlayerInExistedMap(OnlinePlayer player)
    {
        PlayerLocation loc = player.Location;
        if (!loc.IsInMap)
            return false;

        var area = AreaData.Get(loc.Map.Sid);
        if (area is null || area.Mode.Length <= (int)loc.Map.AreaMode)
            return false;

        return true;
    }

    private static string? GetRandomNotSelfPlayer(Context context, out OnlinePlayer? player)
    {
        player = null;
        var clientState = context.MiaoNetContext.ClientState!;
        var candidates = clientState.SelfChannel.Players
            .Where(p => IsPlayerInExistedMap(p)) // Teleportable
            .Where(p => !p.GlobalFlags.HasFlag(PlayerGlobalFlags.TakingGolden)) // Not taking golden
            .ToList();

        if (candidates.Count == 0)
            return PFormat.Format(NoAvailablePlayer);

        int index = Random.Shared.Next(candidates.Count);
        player = candidates[index];
        return null;
    }
    #endregion

#pragma warning restore CA1305
}