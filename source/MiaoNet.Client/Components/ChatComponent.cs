using System.Diagnostics;
using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

#pragma warning disable CA1305

public sealed partial class ChatComponent : MiaoNetComponent
{
    // from CelesteNet
    private sealed class PauseUpdateOverlay : Overlay
    {
        public override void Update()
        {
            base.Update();

            Level level = SceneAs<Level>();

            foreach (Entity e in Engine.Scene[Tags.PauseUpdate])
                if (e.Active && e is not TextMenu)
                    e.Update();

            level.HudRenderer.BackgroundFade = Calc.Approach(
                level.HudRenderer.BackgroundFade,
                level.Paused ? 1f : 0f,
                8f * Engine.RawDeltaTime
            );
        }
    }

    // I hate these "previous" things
    private bool previousCommandsEnabled = false;
    private bool previousScenePaused = false;
    private bool previousAllowHudHide = true;
    private readonly PauseUpdateOverlay dummyOverlay;

    private bool active;
    private readonly InputBox inputBox;
    private readonly ChatMessageBox chatMessageBox;

    private readonly CommandParser cmdParser;

    private readonly ChatMessageFactory chatMessageFactory;

    private readonly ScalelessChatTextRenderer textRenderer;

    private string lastInput = string.Empty;
    private readonly List<string> inputHistory;
    private int historyIndex;
    private int? selectedSendChannelID;
    private int? focusedTabIndex;

    public bool Active => active;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        inputHistory = new();
        float scale = MiaoNetModule.Settings.ChatUIScaleValue;
        textRenderer = new ScalelessChatTextRenderer(scale, MiaoNetFont.ENZhsLineHeight * scale);
        dummyOverlay = new();
        cmdParser = new(MiaoNetCommand.Commands);
        chatMessageFactory = new(context);
        inputBox = new InputBox(textRenderer, new ChatCompletionProvider(context, cmdParser));
        chatMessageBox = new(textRenderer);
        ChatMessageBoxSetup();

        context.ClientInitialized += Context_ClientInitialized;
        context.ChatMessageReceived += Context_ChatMessageReceived;
        context.PlayerJoined += Context_PlayerJoined;
        context.PlayerLeft += Context_PlayerLeft;
        context.SelfChannelMoveFailed += Context_SelfChannelMoveFailed;

        var settings = MiaoNetModule.Settings;
        MiaoNetModule.Settings.SettingsChanged += Settings_SettingsChanged;
        Settings_SettingsChanged(settings, SettingsCategory.VisualsUI);
    }

    private void Settings_SettingsChanged(MiaoNetModuleSettings settings, SettingsCategory category)
    {
        if (category is not SettingsCategory.VisualsUI)
            return;
        chatMessageBox.ChatMessageListView.MessageYPadding = settings.ChatMessagePadding;
        chatMessageBox.ChatMessageListView.BackgroundOpacity = settings.ChatBackgroundOpacityValue;
        chatMessageBox.ChatMessageListView.TextOpacity = settings.ChatTextOpacityValue;
        chatMessageBox.ChatMessageListView.ShowDuration = settings.ChatDisplayDuration;
        chatMessageBox.ChatMessageListView.NewMessagesShowing = settings.NewMessagesShowing switch
        {
            NewMessageShowingMode.ShowAll => ChatInputBox.NewMessageShowingMode.ShowAll,
            NewMessageShowingMode.WithTab => ChatInputBox.NewMessageShowingMode.WithTab,
            NewMessageShowingMode.HideAll => ChatInputBox.NewMessageShowingMode.HideAll,
            _ => ChatInputBox.NewMessageShowingMode.ShowAll
        };
        chatMessageBox.ChatMessageListView.IdleHeight = settings.IdleChatHeightValue;
        chatMessageBox.ChatMessageListView.ActiveHeight = settings.ActiveChatHeightValue;
        chatMessageBox.FoldWindowSeconds = settings.FoldWindowSeconds;
        chatMessageBox.ChatMessageListView.FancyFoldCounter = settings.FancyFoldCounter;
        float scale = settings.ChatUIScaleValue;
        textRenderer.Scale = scale;
        textRenderer.LineHeight = MiaoNetFont.ENZhsLineHeight * scale;
    }

    private void Context_PlayerJoined(OnlinePlayer player)
    {
        if (!MiaoNetModule.Settings.PlayerPresenceMessages)
            return;
        string text = PFormat.Format(context.PlayerPresenceMessage.PlayerJoined, player.GetDisplayName(false, context.ShowAvatar));
        AddLocalChat(MiaoNetChatText.CreateAnnouncement(text));
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (!MiaoNetModule.Settings.PlayerPresenceMessages)
            return;
        string text = PFormat.Format(context.PlayerPresenceMessage.PlayerLeft, player.GetDisplayName(false, context.ShowAvatar));
        AddLocalChat(MiaoNetChatText.CreateAnnouncement(text));
    }

    private void Context_SelfChannelMoveFailed(PacketPlayerChannelMoveFailed packet)
    {
        string message = packet.Reason switch
        {
            PacketPlayerChannelMoveFailed.FailedReason.InvalidChannelName =>
                PFormat.Format(Dialog.Get("miaonet_commands_channel_invalid_channel_name"), packet.TargetChannelName),
            _ => ConnectionStatus.InternalServerError,
        };
        AddLocalChat(MiaoNetChatText.CreateCommandError(message));
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        var chatDisabled = MiaoNetModule.Settings.LiveMode;
        if (chatDisabled && packet.Type is not ChatMessageType.Server and not ChatMessageType.ServerChat)
            return;

        ReceivedChatMessage received = chatMessageFactory.CreateReceived(player, packet);
        if (received.Text is not null)
        {
            int? channelID = packet.Type == ChatMessageType.Channel ? packet.ChatChannelID : null;

            string? foldKey = null;
            ChatText? foldedText = null;
            if (MiaoNetModule.Settings.MessageFolding)
                (foldKey, foldedText) = chatMessageFactory.CreateFoldInfo(player, packet, received.Content);

            chatMessageBox.AddChatMessage(packet.DateTime, received.Text, channelID, foldKey, foldedText);
        }
        else
            Logger.Warn(LT.MiaoNet, $"Received a null chat message for type {packet.Type}, content: {packet.Content}.");

        if (received.MentionsSelf)
            Audio.Play(MiaoNetSFX.ChatMention);
    }

    private void SyncChatChannelWithTab()
    {
        var state = context.ClientState;
        if (state is null)
            return;

        if (chatMessageBox.ActiveChannelId is int channelID
            && state.TryGetChatChannel(channelID, out var channel)
            && channel.CanSend)
        {
            selectedSendChannelID = channel.ID;
            return;
        }
        selectedSendChannelID = state.GetDefaultSendableChatChannelID();
    }

    public override void Update()
    {
        var settings = MiaoNetModule.Settings;

        if (!active)
        {
            var btn = settings.ChatButton;
            var btnCmd = settings.ChatCommandButton;
            if (btn.Pressed)
            {
                btn.ConsumePress();
                if (context.IsSuitableToOpenUI)
                    Activate();
            }
            else if (btnCmd.Pressed)
            {
                btnCmd.ConsumePress();
                if (context.IsSuitableToOpenUI)
                {
                    Activate();
                    inputBox.SetText(CommandParser.CommandPrefix);
                }
            }
        }
        else
        {
            Engine.Scene.Paused = true;

            if (focusedTabIndex is not null)
            {
                UpdateTabFocusMode(settings);
                chatMessageBox.Update();
                return;
            }

            if (settings.ChatTabFocusButton.Pressed)
            {
                settings.ChatTabFocusButton.ConsumePress();
                EnterTabFocusMode();
                chatMessageBox.Update();
                return;
            }

            if (MInput.Keyboard.Pressed(Keys.Escape))
            {
                MInputHack.ConsumeAllInputs();
                Deactivate();
                return;
            }
            else if (MInput.Keyboard.Pressed(Keys.Enter))
            {
                MInputHack.ConsumeAllInputs();
                string text = inputBox.Text;
                string trimmedText = text.Trim();
                if (trimmedText != string.Empty)
                {
                    inputHistory.Add(trimmedText);
                    if (!trimmedText.StartsWith(CommandParser.CommandPrefix, StringComparison.Ordinal))
                    {
                        if (!MiaoNetModule.Settings.LiveMode)
                            SendChat(trimmedText);
                        else
                            AddLocalChat(MiaoNetChatText.CreateCommandError(Dialog.Get("miaonet_chat_disabled")));
                    }
                    else
                    {
                        HandleCommand(trimmedText);
                    }
                }

                Deactivate();
                return;
            }

            if (MInput.Keyboard.CurrentState.IsKeyDown(Keys.LeftShift) ||
                MInput.Keyboard.CurrentState.IsKeyDown(Keys.RightShift))
            {
                if (MInput.Keyboard.Pressed(Keys.Left))
                {
                    chatMessageBox.CycleTabForward();
                    SyncChatChannelWithTab();
                }
                else if (MInput.Keyboard.Pressed(Keys.Right))
                {
                    chatMessageBox.CycleTabBackward();
                    SyncChatChannelWithTab();
                }
            }


            if (!inputBox.HasCompletions)
            {
                if (MInput.Keyboard.Pressed(Keys.Up))
                {
                    int i = historyIndex;
                    i -= 1;
                    if (i < 0) i = 0;
                    if (i != historyIndex)
                    {
                        if (historyIndex == inputHistory.Count)
                            lastInput = inputBox.Text;
                        historyIndex = i;
                        inputBox.SetSuppressCompletions();
                        inputBox.SetText(inputHistory[i]);
                    }
                }
                else if (MInput.Keyboard.Pressed(Keys.Down))
                {
                    int i = historyIndex;
                    i += 1;
                    if (i > inputHistory.Count)
                        i = inputHistory.Count;
                    if (i != historyIndex)
                    {
                        historyIndex = i;
                        if (i == inputHistory.Count)
                        {
                            inputBox.SetSuppressCompletions();
                            inputBox.SetText(lastInput);
                        }
                        else
                        {
                            inputBox.SetSuppressCompletions();
                            inputBox.SetText(inputHistory[i]);
                        }
                    }
                }
            }

            inputBox.Update();
        }
        chatMessageBox.Update();
    }

    public void SetSendChannel(int channelID)
    {
        if (context.ClientState is not null
            && context.ClientState.TryGetChatChannel(channelID, out var channel)
            && channel.CanSend)
        {
            selectedSendChannelID = channelID;
        }
    }

    public void SendChat(string text)
    {
        int? channelID = selectedSendChannelID ?? context.ClientState?.GetDefaultSendableChatChannelID();
        if (channelID is int id)
            context.QueuePacket(new PacketSendChatMessage(id, text));
    }

    public void AddLocalChat(ChatText message)
        => chatMessageBox.AddChatMessage(message);

    public void OnSentPrivateMessage(DateTime dateTime, OnlinePlayer other, string text)
        => chatMessageBox.AddChatMessage(dateTime, chatMessageFactory.CreateSentPrivateMessage(other, text), null);

    public void ClearChat()
        => chatMessageBox.CleanHistory();

    public void HandleCommand(string text)
    {
        var result = cmdParser.Parse(text, out var cmdName, out var cmd, out var args);

        chatMessageBox.AddChatMessage(MiaoNetChatText.CreateCommandEcho(text));

        if (result != CommandParser.ParseResult.Success)
        {
            TipCommandError(result, cmdName, cmd, args is null ? -1 : args.Count);
            return;
        }

        string? error = cmd!.OnExecute(new MiaoNetCommand.Context(context, args!));
        if (error is not null)
            AddLocalChat(MiaoNetChatText.CreateCommandError(error));

        void TipCommandError(CommandParser.ParseResult result, string cmdName, MiaoNetCommand? cmd, int argc)
        {
            string msg = result switch
            {
                CommandParser.ParseResult.NoSuchCommand =>
                    PFormat.Format(Dialog.Clean("miaonet_command_status_no_such_command"), cmdName),
                CommandParser.ParseResult.MissingArguments =>
                    PFormat.Format(Dialog.Clean("miaonet_command_status_missing_arguments"), cmdName, cmd!.Segments.Count, argc),
                CommandParser.ParseResult.TooManyArguments =>
                    PFormat.Format(Dialog.Clean("miaonet_command_status_too_many_arguments"), cmdName, cmd!.Segments.Count, argc),
            };
            AddLocalChat(MiaoNetChatText.CreateCommandError(msg));
        }
    }

    private void EnterTabFocusMode()
    {
        int defaultFocus = chatMessageBox.ActiveTabIndex >= 0
            ? chatMessageBox.ActiveTabIndex
            : (chatMessageBox.Channels.Count > 0 ? 0 : -1);
        focusedTabIndex = defaultFocus;
        chatMessageBox.ChatTabListView.FocusedTabIndex = defaultFocus;
        inputBox.SetInputPaused(true);
    }

    private void ExitTabFocusMode()
    {
        focusedTabIndex = null;
        chatMessageBox.ChatTabListView.FocusedTabIndex = null;
        inputBox.SetInputPaused(false);
    }

    private void UpdateTabFocusMode(MiaoNetModuleSettings settings)
    {
        if (MInput.Keyboard.Pressed(Keys.Escape) || settings.ChatTabFocusButton.Pressed)
        {
            settings.ChatTabFocusButton.ConsumePress();
            MInputHack.ConsumeAllInputs();
            ExitTabFocusMode();
            return;
        }

        int totalTabs = chatMessageBox.Channels.Count + 1;
        int current = focusedTabIndex!.Value;

        if (MInput.Keyboard.Pressed(Keys.Left) || Input.MenuLeft.Pressed)
        {
            Input.MenuLeft.ConsumePress();
            current = (((current + 1 - 1) % totalTabs) + totalTabs) % totalTabs - 1;
            focusedTabIndex = current;
            chatMessageBox.ChatTabListView.FocusedTabIndex = current;
        }
        else if (MInput.Keyboard.Pressed(Keys.Right) || Input.MenuRight.Pressed)
        {
            Input.MenuRight.ConsumePress();
            current = (((current + 1 + 1) % totalTabs) + totalTabs) % totalTabs - 1;
            focusedTabIndex = current;
            chatMessageBox.ChatTabListView.FocusedTabIndex = current;
        }
        else if (Input.MenuConfirm.Pressed)
        {
            Input.MenuConfirm.ConsumePress();
            if (current >= 0)
            {
                bool enabled = chatMessageBox.ToggleChannelInGlobal(current);
                if (context.ClientState is { } state
                    && current < chatMessageBox.Channels.Count
                    && state.TryGetChatChannel(chatMessageBox.Channels[current].Id, out var channel))
                {
                    settings.ChatChannelGlobalFilters[channel.Key] = enabled;
                    MiaoNetModule.Instance.SaveSettings();
                }
                Audio.Play(enabled ? SFX.ui_main_button_toggle_on : SFX.ui_main_button_toggle_off);
            }
            else
            {
                chatMessageBox.SetActiveTabIndex(-1);
                SyncChatChannelWithTab();
                Audio.Play(SFX.ui_main_button_select);
            }
        }
    }

    private void Context_ClientInitialized(ClientState state)
    {
        ChatMessageBoxSetup();
        var filters = MiaoNetModule.Settings.ChatChannelGlobalFilters;
        foreach (var channel in state.ChatChannels)
        {
            bool enabledInGlobal = filters.TryGetValue(channel.Key, out bool saved)
                ? saved
                : channel.DefaultVisibleInGlobal;
            chatMessageBox.AddChannel(channel.ID, channel.DisplayName, enabledInGlobal);
        }
        selectedSendChannelID = state.GetDefaultSendableChatChannelID();
    }

    // TODO TODO TODO we need a clean up method
    public override void OnDisconnected()
    {
        if (active)
            Deactivate();
        ChatMessageBoxSetup();
        inputHistory.Clear();
        historyIndex = 0;
        selectedSendChannelID = null;
    }

    private void ChatMessageBoxSetup()
    {
        chatMessageBox.CleanUp();
        chatMessageBox.ChatTabListView.InitialTabTitle = Dialog.Get("miaonet_initial_chat_tab_name");
    }

    private void Activate()
    {
        active = true;
        historyIndex = inputHistory.Count;
        inputBox.Activate();
        chatMessageBox.Activate();
        previousCommandsEnabled = Engine.Commands.Enabled;
        Engine.Commands.Enabled = false;
        previousScenePaused = Engine.Scene.Paused;
        Engine.Scene.Paused = true;

        if (Engine.Scene is Level level)
        {
            previousAllowHudHide = level.AllowHudHide;
            level.Add(dummyOverlay);
            level.AllowHudHide = false;
        }
        context.HasComponentFocus = true;
    }

    private void Deactivate()
    {
        active = false;
        ExitTabFocusMode();
        inputBox.Deactivate();
        lastInput = string.Empty;
        chatMessageBox.Deactivate();
        Engine.Commands.Enabled = previousCommandsEnabled;
        Engine.Scene.Paused = previousScenePaused;

        if (Engine.Scene is Level level)
        {
            level.CompletelyRemove(dummyOverlay);
            level.AllowHudHide = previousAllowHudHide;
        }
        context.HasComponentFocus = false;
    }

    public override void Render()
    {
        chatMessageBox.Render();
        if (active)
            inputBox.Render();
    }
}
