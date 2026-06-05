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

    private float lastMouseScrollWheelValue;

    // i hate these "previous" things
    private bool previousCommandsEnabled = false;
    private bool previousScenePaused = false;
    private bool previousAllowHudHide = true;
    private readonly PauseUpdateOverlay dummyOverlay;

    private bool active;
    private readonly InputBox inputBox;
    private readonly ChatMessageBox chatMessageBox;

    private readonly CommandParser cmdParser;

    private readonly MiaoNetChatTextRenderer textRenderer;

    private string lastInput = string.Empty;
    private readonly List<string> inputHistory;
    private int historyIndex;

    public bool Active => active;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        inputHistory = new();
        float scale = MiaoNetModule.Settings.ChatUIScaleValue;
        textRenderer = new MiaoNetChatTextRenderer(scale, MiaoNetFont.ENZhsLineHeight * scale);
        dummyOverlay = new();
        cmdParser = new(MiaoNetCommand.Commands);
        inputBox = new InputBox(textRenderer, new ChatCompletionProvider(context, cmdParser));
        chatMessageBox = new(textRenderer);
        ChatMessageBoxSetup();
        lastMouseScrollWheelValue = Mouse.GetState().ScrollWheelValue;

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
        chatMessageBox.ChatMessageListView.NoNewMessagesShowing = settings.NoNewMessagesShowing;
        // TODO explain this factor
        float factor = 32f / 10f / (settings.ChatUIScaleValue * 24f / 10f);
        chatMessageBox.ChatMessageListView.IdleMaxCount = (int)(factor * settings.IdleChatHeight);
        chatMessageBox.ChatMessageListView.ActiveMaxCount = (int)(factor * settings.ActiveChatHeight);
        float scale = settings.ChatUIScaleValue;
        textRenderer.Scale = scale;
        textRenderer.LineHeight = MiaoNetFont.ENZhsLineHeight * scale;
    }

    private void Context_PlayerJoined(OnlinePlayer player)
    {
        if (!MiaoNetModule.Settings.PlayerPresenceMessages)
            return;
        string text = PFormat.Format(Dialog.Clean("miaonet_context_player_joined"), player.GetDisplayName(false, context.ShowAvatar));
        AddLocalChat(MiaoNetChatText.CreateAnnouncement(text));
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        if (!MiaoNetModule.Settings.PlayerPresenceMessages)
            return;
        string text = PFormat.Format(Dialog.Clean("miaonet_context_player_left"), player.GetDisplayName(false, context.ShowAvatar));
        AddLocalChat(MiaoNetChatText.CreateAnnouncement(text));
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        var chatDisabled = MiaoNetModule.Settings.LiveMode;
        switch (packet.Type)
        {
        case ChatMessageType.Chat:
            if (!chatDisabled)
                chatMessageBox.AddChatMessage(MiaoNetChatText.CreateGlobalChat(packet.DateTime, player!, packet.Content, context.ShowAvatar), "Global");
            break;
        case ChatMessageType.ChannelChat:
            if (!chatDisabled)
                chatMessageBox.AddChatMessage(MiaoNetChatText.CreateChannelChat(packet.DateTime, player!, packet.Content, context.ShowAvatar), "Channel");
            break;
        case ChatMessageType.MapChat:
            if (!chatDisabled)
                chatMessageBox.AddChatMessage(MiaoNetChatText.CreateMapChat(packet.DateTime, player!, packet.Content, context.ShowAvatar),"Map");
            break;
        case ChatMessageType.Server:
            chatMessageBox.AddChatMessage(MiaoNetChatText.CreateAnnouncement(packet.DateTime, packet.Content), "Server");
            break;
        case ChatMessageType.PrivateMessage:
            if (!chatDisabled)
                chatMessageBox.AddChatMessage(MiaoNetChatText.CreatePrivateChat(packet.DateTime, player!, packet.Content, context.ShowAvatar), string.Format("[(0)]", player!.GetDisplayName(false, false)));
            break;
        case ChatMessageType.ServerChat:
            chatMessageBox.AddChatMessage(MiaoNetChatText.CreateAnnouncement(packet.DateTime, packet.Content), "ServerChat");
            break;
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
            if (MInput.Keyboard.Pressed(Keys.Tab))
            {
                chatMessageBox.CycleTab();
                // TODO：Private Chat Switch
                var chatTabName = chatMessageBox.ActiveTabName ?? "Global";
                var chatChannel = ChatChannelMatcher.Match(chatTabName);
                if (chatChannel != (ChatChannel)(-1))
                {
                    settings.ChatChannel = chatChannel;
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
        => chatMessageBox.AddChatMessage(MiaoNetChatText.CreateSentPrivateChat(dateTime, other, context.ClientState!.Self, text, context.ShowAvatar));

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
