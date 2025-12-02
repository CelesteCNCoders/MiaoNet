using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatComponent : MiaoNetComponent
{
    private bool active;
    private readonly InputBox inputBox;
    private readonly ChatMessageListView chatView;
    private readonly List<MiaoNetChatMessage> chatLog;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        ITextRenderer r = new MiaoNetTextRenderer();
        inputBox = new InputBox(r);
        chatView = new(r);
        chatLog = new();
        context.ChatMessageReceived += Context_ChatMessageReceived;
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        if (player is not null)
        {
            chatLog.Add(new(player.Info.Name, packet.Content));
        }
        else
        {
            chatLog.Add(new(null, packet.Content));
        }
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
            if (MInput.Keyboard.Pressed(Keys.Escape))
            {
                MInputHack.ConsumeAllInput();
                Deactive();
                return;
            }
            if (MInput.Keyboard.Pressed(Keys.Enter))
            {
                MInputHack.ConsumeAllInput();
                context.QueuePacket(new PacketSendChatMessage(inputBox.Text));
                Deactive();
                return;
            }
            inputBox.Update();
        }
        chatView.Update();
    }

    // TODO TODO TODO we need a clean up method
    public override void OnDisconnected()
    {
        Deactive();
        chatLog.Clear();
    }

    private void Active()
    {
        active = true;
        inputBox.Active();
        Engine.Scene.Paused = true;
    }

    private void Deactive()
    {
        active = false;
        inputBox.Deactive();
        Engine.Scene.Paused = false;
    }

    public override void Render()
    {
        if (active)
            inputBox.Render();
        chatView.Render(chatLog);
    }
}
