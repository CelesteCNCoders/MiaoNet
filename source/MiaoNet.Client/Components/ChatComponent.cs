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

        context.ChatMessageReceived += Context_ChatMessageReceived;
        context.PlayerJoined += Context_PlayerJoined;
        context.PlayerLeft += Context_PlayerLeft;

        var settings = MiaoNetModule.Settings;
        MiaoNetModule.Settings.SettingsChanged += Settings_SettingsChanged;
        Settings_SettingsChanged(settings, SettingsCategory.VisualsUI);
    }

    private void Settings_SettingsChanged(MiaoNetModuleSettings settings, SettingsCategory category)
    {
        if (category is not SettingsCategory.VisualsUI)
            return;
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

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        var chatDisabled = MiaoNetModule.Settings.LiveMode;
        if (chatDisabled && packet.Type is not ChatMessageType.Server and not ChatMessageType.ServerChat)
            return;

        ReceivedChatMessage received = chatMessageFactory.CreateReceived(player, packet);
        if (received.Text is not null)
        {
            // Route to appropriate tab based on message type
            ChatChannel? chatChannel = packet.Type switch
            {
                ChatMessageType.Chat => ChatChannel.Global,
                ChatMessageType.ChannelChat => ChatChannel.Channel,
                ChatMessageType.MapChat => ChatChannel.Map,
                _ => null
            };
            string? tabName = ChatChannelMatcher.GetName(chatChannel);

            string? foldKey = null;
            ChatText? foldedText = null;
            if (MiaoNetModule.Settings.MessageFolding)
                (foldKey, foldedText) = chatMessageFactory.CreateFoldInfo(player, packet);

            chatMessageBox.AddChatMessage(packet.DateTime, received.Text, tabName, foldKey, foldedText);
        }
        else
            Logger.Warn(LT.MiaoNet, $"Null chat message received for type {packet.Type}. Content: {packet.Content}");

        if (received.MentionsSelf)
            Audio.Play(MiaoNetSFX.ChatMention);
    }
    
    private void SyncChatChannelWithTab()
    {
        var chatTabName = chatMessageBox.ActiveTabName ?? ChatChannelMatcher.GetName(ChatChannel.Global);
        var chatChannel = ChatChannelMatcher.Match(chatTabName!);
        if (chatChannel != (ChatChannel)(-1))
        {
            MiaoNetModule.Settings.ChatChannel = chatChannel;
        }
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

    public void SendChat(string text)
        => context.QueuePacket(new PacketSendChatMessage(MiaoNetModule.Settings.ChatChannel, text));

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

    // TODO TODO TODO we need a clean up method
    public override void OnDisconnected()
    {
        if (active)
            Deactivate();
        ChatMessageBoxSetup();
        inputHistory.Clear();
        historyIndex = 0;
    }

    private void ChatMessageBoxSetup()
    {
        chatMessageBox.CleanUp();
        List<string> tabNames = ["Global", "Channel", "Map"];
        foreach (var tabName in tabNames)
        {
            chatMessageBox.AddTab(tabName);
        }
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
