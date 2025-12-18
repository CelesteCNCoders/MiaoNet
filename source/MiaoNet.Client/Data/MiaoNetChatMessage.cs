using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public struct MiaoNetChatMessage : IChatMessage
{
    public string? Sender { get; set; }

    public Color SenderColor { get; set; }

    public string Content { get; set; }

    public Color Color { get; set; }

    public MiaoNetChatMessage(OnlinePlayer? sender, string content)
        : this(sender?.Info.Name, content)
    {
    }

    public MiaoNetChatMessage(string? sender, string content)
    {
        Sender = sender;
        SenderColor = Color.Yellow;
        Content = content;
        Color = Color.White;
    }

    public MiaoNetChatMessage(string content)
        : this((string?)null, content)
    {
    }

    public void SetIsAnnouncement()
        => Color = Color.Cyan;

    public void SetIsCommandEcho()
        => Color = Color.DodgerBlue;

    public void SetIsCommandErrorEcho()
        => Color = Color.IndianRed;
}
