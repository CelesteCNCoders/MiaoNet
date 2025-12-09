using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatComponent : MiaoNetComponent
{
    private bool active;
    private readonly InputBox inputBox;
    private readonly ChatMessageListView<MiaoNetChatMessage> chatView;

    public ChatComponent(MiaoNetContext context)
        : base(context)
    {
        ITextRenderer r = new MiaoNetTextRenderer();
        inputBox = new InputBox(r);
        chatView = new(r);
        context.ChatMessageReceived += Context_ChatMessageReceived;
    }

    private void Context_ChatMessageReceived(OnlinePlayer? player, PacketChatMessage packet)
    {
        bool isAnnouncement = packet.Type == ChatMessageType.Server;
        if (player is not null)
        {
            chatView.AddChatMessage(new(player.Info.Name, packet.Content, isAnnouncement));
        }
        else
        {
            chatView.AddChatMessage(new(null, packet.Content, isAnnouncement));
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
                if (inputBox.Text != string.Empty && !inputBox.Text.All(char.IsWhiteSpace))
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
        if (!active)
            return;
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
