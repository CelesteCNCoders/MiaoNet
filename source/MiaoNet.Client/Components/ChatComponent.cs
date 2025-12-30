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

            foreach (Entity e in Engine.Scene[Tags.PauseUpdate])
                if (e.Active && e is not TextMenu)
                    e.Update();
        }
    }

    // this seems an fna bug...
    // we need to manually call `MouseState.Get()`
    private float lastMouseScrollWheelValue;

    private bool previousCommandsEnabled = false;
    private bool previousScenePaused = false;
    private PauseUpdateOverlay? dummyOverlay;

    private bool active;
    private readonly InputBox inputBox;
    private readonly ChatMessageListView chatView;
    private float targetChatViewScroll;
    private readonly CommandParser cmdParser;

    private string lastInput = string.Empty;
    private readonly List<string> history;
    private int historyIndex;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        history = new();
        ITextRenderer r = new MiaoNetTextRenderer();
        inputBox = new InputBox(r);
        chatView = new(r);
        cmdParser = new(MiaoNetCommand.Commands);
        context.ChatMessageReceived += Context_ChatMessageReceived;

        lastMouseScrollWheelValue = Mouse.GetState().ScrollWheelValue;
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        if (packet.Type == ChatMessageType.Chat)
            chatView.AddChatMessage(MiaoNetChatText.CreatePublicChat(player!, packet.Content));
        else if (packet.Type == ChatMessageType.Server)
            chatView.AddChatMessage(MiaoNetChatText.CreateAnnouncement(packet.Content));
        else if (packet.Type == ChatMessageType.PrivateMessage)
            chatView.AddChatMessage(MiaoNetChatText.CreatePrivateChat(player!, packet.Content));
        else
            throw new NotImplementedException();
    }

    public override void Update()
    {
        if (!active)
        {
            var btn = MiaoNetModule.Settings.ChatButton;
            if (btn.Pressed)
            {
                btn.ConsumePress();
                bool doNotOpenChatForNow =
                    Engine.Scene.Entities.FindFirst<KeyboardConfigUI>() != null ||
                    Engine.Scene.Entities.FindFirst<ButtonConfigUI>() != null;
                if (!doNotOpenChatForNow)
                {
                    Active();
                }
            }
        }
        else
        {
            if (MInput.Keyboard.Pressed(Keys.Escape))
            {
                MInputHack.ConsumeAllInput();
                Deactive();
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
                        SendChat(trimmedText);
                    else
                        HandleCommand(trimmedText);
                }
                Deactive();
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

            float cur = Mouse.GetState().ScrollWheelValue;
            float delta = cur - lastMouseScrollWheelValue;

            targetChatViewScroll += delta;
            targetChatViewScroll = chatView.ClampScrollValue(targetChatViewScroll);
            chatView.Scroll = Calc.Approach(
                chatView.Scroll,
                targetChatViewScroll,
                Math.Max(Math.Abs(targetChatViewScroll - chatView.Scroll), 24f) * 8f * Engine.RawDeltaTime
            );

            lastMouseScrollWheelValue = cur;

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

    public void OnSentPrivateMessage(OnlinePlayer other, string text)
        => chatView.AddChatMessage(MiaoNetChatText.CreateSentPrivateChat(other, context.ClientState!.Self, text));

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
            Deactive();
        chatView.CleanUp();
        history.Clear();
        historyIndex = 0;
    }

    private void Active()
    {
        active = true;
        historyIndex = history.Count;
        inputBox.Active();
        chatView.Active = true;
        previousCommandsEnabled = Engine.Commands.Enabled;
        Engine.Commands.Enabled = false;
        previousScenePaused = Engine.Scene.Paused;
        Engine.Scene.Paused = true;

        if (Engine.Scene is Level level)
            level.Overlay = dummyOverlay ??= new PauseUpdateOverlay();
    }

    private void Deactive()
    {
        active = false;
        inputBox.Deactive();
        lastInput = string.Empty;
        chatView.Active = false;
        targetChatViewScroll = 0f;
        chatView.Scroll = 0f;
        Engine.Commands.Enabled = previousCommandsEnabled;
        Engine.Scene.Paused = previousScenePaused;

        if (Engine.Scene is Level level)
            level.Overlay = null;
    }

    public override void Render()
    {
        chatView.Render();
        if (active)
            inputBox.Render();
    }
}
