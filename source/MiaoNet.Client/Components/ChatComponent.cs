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
    private readonly ChatMessageListView chatView;
    private float targetChatViewScroll;
    private readonly CommandParser cmdParser;

    private readonly MiaoNetChatTextRenderer textRenderer;

    private string lastInput = string.Empty;
    private readonly List<string> history;
    private int historyIndex;

    public bool Active => active;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        history = new();
        float scale = MiaoNetModule.Settings.ChatUIScaleValue;
        textRenderer = new MiaoNetChatTextRenderer(scale, MiaoNetFont.ENZhsLineHeight * scale);
        dummyOverlay = new();
        cmdParser = new(MiaoNetCommand.Commands);
        inputBox = new InputBox(textRenderer, new ChatCompletionProvider(context, cmdParser));
        chatView = new(textRenderer);
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
        chatView.BackgroundOpacity = settings.ChatBackgroundOpacityValue;
        chatView.TextOpacity = settings.ChatTextOpacityValue;
        chatView.ShowDuration = settings.ChatDisplayDuration;
        chatView.NoNewMessagesShowing = settings.NoNewMessagesShowing;
        // TODO explain this factor
        float factor = 32f / 10f / (settings.ChatUIScaleValue * 24f / 10f);
        chatView.IdleMaxCount = (int)(factor * settings.IdleChatHeight);
        chatView.ActiveMaxCount = (int)(factor * settings.ActiveChatHeight);
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
                chatView.AddChatMessage(MiaoNetChatText.CreatePublicChat(packet.DateTime, player!, packet.Content, context.ShowAvatar));
            break;
        case ChatMessageType.MapChat:
            if (!chatDisabled)
                chatView.AddChatMessage(MiaoNetChatText.CreateMapChat(packet.DateTime, player!, packet.Content, context.ShowAvatar));
            break;
        case ChatMessageType.Server:
            chatView.AddChatMessage(MiaoNetChatText.CreateAnnouncement(packet.DateTime, packet.Content));
            break;
        case ChatMessageType.PrivateMessage:
            if (!chatDisabled)
                chatView.AddChatMessage(MiaoNetChatText.CreatePrivateChat(packet.DateTime, player!, packet.Content, context.ShowAvatar));
            break;
        case ChatMessageType.ServerChat:
            chatView.AddChatMessage(MiaoNetChatText.CreateAnnouncement(packet.DateTime, packet.Content));
            break;
        }
    }

    public override void Update()
    {
        // this seems an fna bug...
        // we need to manually call `MouseState.Get()`
        float currentScrollWheelValue = Mouse.GetState().ScrollWheelValue;
        float scrollDelta = currentScrollWheelValue - lastMouseScrollWheelValue;
        lastMouseScrollWheelValue = currentScrollWheelValue;

        const float KeyboardScrollSpeed = 1024f;
        if (MInput.Keyboard.Check(Keys.PageUp))
            scrollDelta += KeyboardScrollSpeed * Engine.RawDeltaTime;
        else if (MInput.Keyboard.Check(Keys.PageDown))
            scrollDelta -= KeyboardScrollSpeed * Engine.RawDeltaTime;

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
                    history.Add(trimmedText);
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

            if (!inputBox.HasCompletions)
            {
                if (MInput.Keyboard.Pressed(Keys.Up))
                {
                    int i = historyIndex;
                    i -= 1;
                    if (i < 0) i = 0;
                    if (i != historyIndex)
                    {
                        if (historyIndex == history.Count)
                            lastInput = inputBox.Text;
                        historyIndex = i;
                        inputBox.SetSuppressCompletions();
                        inputBox.SetText(history[i]);
                    }
                }
                else if (MInput.Keyboard.Pressed(Keys.Down))
                {
                    int i = historyIndex;
                    i += 1;
                    if (i > history.Count)
                        i = history.Count;
                    if (i != historyIndex)
                    {
                        historyIndex = i;
                        if (i == history.Count)
                        {
                            inputBox.SetSuppressCompletions();
                            inputBox.SetText(lastInput);
                        }
                        else
                        {
                            inputBox.SetSuppressCompletions();
                            inputBox.SetText(history[i]);
                        }
                    }
                }
            }

            targetChatViewScroll += scrollDelta;
            targetChatViewScroll = chatView.ClampScrollValue(targetChatViewScroll);
            float maxMove = Math.Max(Math.Abs(targetChatViewScroll - chatView.Scroll), 8f) * 8f * Engine.RawDeltaTime;
            chatView.Scroll = Calc.Approach(chatView.Scroll, targetChatViewScroll, maxMove);

            inputBox.Update();
        }
        chatView.Update();
    }

    public void SendChat(string text)
        => context.QueuePacket(new PacketSendChatMessage(text));

    public void AddLocalChat(ChatText message)
        => chatView.AddChatMessage(message);

    public void OnSentPrivateMessage(DateTime dateTime, OnlinePlayer other, string text)
        => chatView.AddChatMessage(MiaoNetChatText.CreateSentPrivateChat(dateTime, other, context.ClientState!.Self, text, context.ShowAvatar));

    public void ClearChat()
        => chatView.CleanUp();

    public void HandleCommand(string text)
    {
        var result = cmdParser.Parse(text, out var cmdName, out var cmd, out var args);

        chatView.AddChatMessage(MiaoNetChatText.CreateCommandEcho(text));

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
        chatView.CleanUp();
        history.Clear();
        historyIndex = 0;
    }

    private void Activate()
    {
        active = true;
        historyIndex = history.Count;
        inputBox.Activate();
        chatView.Active = true;
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
        chatView.Active = false;
        targetChatViewScroll = 0f;
        chatView.Scroll = 0f;
        Engine.Commands.Enabled = previousCommandsEnabled;
        Engine.Scene.Paused = previousScenePaused;

        if (Engine.Scene is Level level)
        {
            Engine.Scene.CompletelyRemove(dummyOverlay);
            level.AllowHudHide = previousAllowHudHide;
        }
        context.HasComponentFocus = false;
    }

    public override void Render()
    {
        chatView.Render();
        if (active)
            inputBox.Render();
    }
}
