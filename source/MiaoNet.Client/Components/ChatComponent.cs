using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatComponent : MiaoNetComponent
{
    private bool active;
    private readonly InputBox inputBox;
    private readonly ChatMessageListView<MiaoNetChatMessage> chatView;
    private readonly CommandParser cmdParser;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        ITextRenderer r = new MiaoNetTextRenderer();
        inputBox = new InputBox(r);
        chatView = new(r);
        cmdParser = new(MiaoNetCommand.Commands);
        context.ChatMessageReceived += Context_ChatMessageReceived;
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        bool isAnnouncement = packet.Type == ChatMessageType.Server;

        MiaoNetChatMessage msg = new(player, packet.Content);
        if (isAnnouncement) msg.SetIsAnnouncement();

        chatView.AddChatMessage(msg);
    }

    public override void Update()
    {
        if (!active)
        {
            if (!Engine.Scene.Paused)
            {
                var btn = MiaoNetModule.Settings.ChatButton;
                if (btn.Pressed)
                {
                    btn.ConsumePress();
                    Active();
                }
            }
        }
        else
        {
            // TODO custom keys?
            if (MInput.Keyboard.Pressed(Keys.Escape))
            {
                MInputHack.ConsumeAllInput();
                Deactive();
                return;
            }
            if (MInput.Keyboard.Pressed(Keys.Enter))
            {
                MInputHack.ConsumeAllInput();
                string text = inputBox.Text;
                string trimmedText = text.Trim();
                if (trimmedText != string.Empty)
                {
                    if (!trimmedText.StartsWith(CommandParser.CommandPrefix))
                        SendChat(trimmedText);
                    else
                        HandleCommand(trimmedText);
                }
                Deactive();
                return;
            }
            inputBox.Update();
        }
        chatView.Update();
    }

    public void SendChat(string text)
        => context.QueuePacket(new PacketSendChatMessage(text));

    public void TipMessage(string text)
    {
        MiaoNetChatMessage msg = new(text);
        msg.SetIsCommandTip();
        chatView.AddChatMessage(msg);
    }

    public void HandleCommand(string text)
    {
        var result = cmdParser.Parse(text, out var cmdName, out var cmd, out var args);

        MiaoNetChatMessage chatMsg = new(text);
        chatMsg.SetIsCommandEcho();
        chatView.AddChatMessage(chatMsg);

        if (result != CommandParser.ParseResult.Success)
        {
            TipCommandError(result, cmdName, cmd, args is null ? -1 : args.Count);
            return;
        }

        string? error = cmd!.OnExecute(new MiaoNetCommand.Context(context, args!));
        if (error is not null)
        {
            MiaoNetChatMessage errMsg = new(error);
            errMsg.SetIsCommandErrorEcho();
            chatView.AddChatMessage(errMsg);
        }

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
            MiaoNetChatMessage chatMsg = new(msg);
            chatMsg.SetIsCommandErrorEcho();
            chatView.AddChatMessage(chatMsg);
        }
    }

    // TODO TODO TODO we need a clean up method
    public override void OnDisconnected()
    {
        if (active)
            Deactive();
        chatView.CleanUp();
    }

    private void Active()
    {
        active = true;
        inputBox.Active();
        chatView.AlwaysShow = true;
        Engine.Scene.Paused = true;
    }

    private void Deactive()
    {
        active = false;
        inputBox.Deactive();
        chatView.AlwaysShow = false;
        Engine.Scene.Paused = false;
    }

    public override void Render()
    {
        if (active)
            inputBox.Render();
        chatView.Render();
    }
}
