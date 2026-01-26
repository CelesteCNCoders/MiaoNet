using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatComponent : MiaoNetComponent
{
    // from CelesteNet
    private class PauseUpdateOverlay : Overlay
    {
        public override void Update()
        {
            base.Update();

            Level level = SceneAs<Level>();

            foreach (Entity e in Engine.Scene[Tags.PauseUpdate])
                if (e.Active && e is not TextMenu)
                    e.Update();

            level.HudRenderer.BackgroundFade = Calc.Approach(level.HudRenderer.BackgroundFade, level.Paused ? 1f : 0f, 8f * Engine.RawDeltaTime);
        }
    }

    private float lastMouseScrollWheelValue;

    private bool previousCommandsEnabled = false;
    private bool previousScenePaused = false;
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

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        history = new();
        float scale = MiaoNetModule.Settings.ChatUIScaleValue;
        textRenderer = new MiaoNetChatTextRenderer(scale, MiaoNetFont.ENZhsLineHeight * scale);
        dummyOverlay = new();
        inputBox = new InputBox(textRenderer);
        chatView = new(textRenderer);
        cmdParser = new(MiaoNetCommand.Commands);
        lastMouseScrollWheelValue = Mouse.GetState().ScrollWheelValue;

        context.ChatMessageReceived += Context_ChatMessageReceived;
        context.PlayerJoined += Context_PlayerJoined;
        context.PlayerLeft += Context_PlayerLeft;
    }

    private void Context_PlayerJoined(OnlinePlayer player)
    {
        OnNotifyMessage(Dialog.Clean("miaonet_context_player_joined").Replace("(0)", player.Info.Name));
    }

    private void Context_PlayerLeft(OnlinePlayer player)
    {
        OnNotifyMessage(Dialog.Clean("miaonet_context_player_left").Replace("(0)", player.Info.Name));
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        var disableChat = MiaoNetModule.Settings.LiveMode;
        switch (packet.Type)
        {
        case ChatMessageType.Chat:
            if (!disableChat)
                chatView.AddChatMessage(MiaoNetChatText.CreatePublicChat(packet.DateTime, player!, packet.Content));
            break;
        case ChatMessageType.Server:
            chatView.AddChatMessage(MiaoNetChatText.CreateAnnouncement(packet.DateTime, packet.Content));
            break;
        case ChatMessageType.PrivateMessage:
            if (!disableChat)
                chatView.AddChatMessage(MiaoNetChatText.CreatePrivateChat(packet.DateTime, player!, packet.Content));
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
        float deltaScrollWheelValue = currentScrollWheelValue - lastMouseScrollWheelValue;
        lastMouseScrollWheelValue = currentScrollWheelValue;

        var settings = MiaoNetModule.Settings;

        // apply settings
        // any better ways?
        {
            chatView.BackgroundOpacity = settings.ChatBackgroundOpacityValue;
            chatView.TextOpacity = settings.ChatTextOpacityValue;
            chatView.ShowDuration = settings.ChatDisplayDuration;
            // TODO explain this factor
            float factor = 32f / 10f / (settings.ChatUIScaleValue * 24f / 10f);
            chatView.IdleMaxCount = (int)(factor * settings.IdleChatHeight);
            chatView.ActiveMaxCount = (int)(factor * settings.ActiveChatHeight);
            float scale = MiaoNetModule.Settings.ChatUIScaleValue;
            textRenderer.Scale = scale;
            textRenderer.LineHeight = MiaoNetFont.ENZhsLineHeight * scale;
        }

        if (!active)
        {
            var btn = settings.ChatButton;
            if (btn.Pressed)
            {
                btn.ConsumePress();
                if (MiaoNetContext.IsSuitableToOpenUI)
                    Activate();
            }
        }
        else
        {
            Engine.Scene.Paused = true;

            if (MInput.Keyboard.Pressed(Keys.Escape))
            {
                MInputHack.ConsumeAllInput();
                Deactivate();
                return;
            }
            else if (MInput.Keyboard.Pressed(Keys.Enter))
            {
                MInputHack.ConsumeAllInput();
                string text = inputBox.Text;
                string trimmedText = text.Trim();
                if (trimmedText != string.Empty)
                {
                    history.Add(trimmedText);
                    if (!trimmedText.StartsWith(CommandParser.CommandPrefix))
                    {
                        if (!MiaoNetModule.Settings.LiveMode)
                            SendChat(trimmedText);
                        else
                            TipErrorMessage(Dialog.Get("miaonet_chat_disabled"));
                    }
                    else
                    {
                        HandleCommand(trimmedText);
                    }
                }
                Deactivate();
                return;
            }
            else if (MInput.Keyboard.Pressed(Keys.Up))
            {
                int i = historyIndex;
                i -= 1;
                if (i < 0) i = 0;
                if (i != historyIndex)
                {
                    if (historyIndex == history.Count)
                        lastInput = inputBox.Text;
                    historyIndex = i;
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
                        inputBox.SetText(lastInput);
                    else
                        inputBox.SetText(history[i]);
                }
            }

            targetChatViewScroll += deltaScrollWheelValue;
            targetChatViewScroll = chatView.ClampScrollValue(targetChatViewScroll);
            chatView.Scroll = Calc.Approach(
                chatView.Scroll,
                targetChatViewScroll,
                Math.Max(Math.Abs(targetChatViewScroll - chatView.Scroll), 24f) * 8f * Engine.RawDeltaTime
            );


            inputBox.Update();
        }
        chatView.Update();
    }

    public void SendChat(string text)
        => context.QueuePacket(new PacketSendChatMessage(text));

    public void TipMessage(string text)
        => chatView.AddChatMessage(MiaoNetChatText.CreateCommandTip(text));

    public void TipErrorMessage(string text)
        => chatView.AddChatMessage(MiaoNetChatText.CreateCommandErrorEcho(text));

    public void OnNotifyMessage(string text)
        => chatView.AddChatMessage(MiaoNetChatText.CreateAnnouncement(text));

    public void OnSentPrivateMessage(DateTime dateTime, OnlinePlayer other, string text)
        => chatView.AddChatMessage(MiaoNetChatText.CreateSentPrivateChat(dateTime, other, context.ClientState!.Self, text));

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
            TipErrorMessage(error);

        void TipCommandError(CommandParser.ParseResult result, string cmdName, MiaoNetCommand? cmd, int argc)
        {
            string msg = result switch
            {
                CommandParser.ParseResult.NoSuchCommand =>
                    Dialog.Clean("miaonet_command_status_no_such_command")
                    .Replace("(0)", cmdName),
                CommandParser.ParseResult.MissingArguments =>
                    Dialog.Clean("miaonet_command_status_missing_arguments")
                    .Replace("(0)", cmdName)
                    .Replace("(1)", cmd!.Segments.Count.ToString())
                    .Replace("(2)", argc.ToString()),
                CommandParser.ParseResult.TooManyArguments =>
                    Dialog.Clean("miaonet_command_status_too_many_arguments")
                    .Replace("(0)", cmdName)
                    .Replace("(1)", cmd!.Segments.Count.ToString())
                    .Replace("(2)", argc.ToString()),
            };
            TipErrorMessage(msg);
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
            level.Add(dummyOverlay);
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
            level.Remove(dummyOverlay);
    }

    public override void Render()
    {
        chatView.Render();
        if (active)
            inputBox.Render();
    }
}
