using System.Diagnostics;

namespace Celeste.Mod.ChatInputBox;

[DebuggerDisplay("{Text}")]
public readonly struct ChatTextSegment
{
    public ChatTextStyle Style { get; }

    public Color Color { get;  }

    public string Text { get; }

    public ChatTextSegment(string text)
    {
        Text = text;
    }

    public ChatTextSegment(ChatTextStyle style, Color color, string text)
    {
        Style = style;
        Color = color;
        Text = text;
    }
}