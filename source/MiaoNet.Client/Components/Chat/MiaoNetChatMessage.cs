using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public struct MiaoNetChatMessage : IChatMessage
{
    public string? Sender { get; set; }

    public Color SenderColor { get; set; }

    public string Content { get; set; }

    public Color Color { get; set; }

    public MiaoNetChatMessage(string? sender, string content)
    {
        Sender = sender;
        SenderColor = Color.Yellow;
        Content = content;
        Color = Color.White;
    }
}
