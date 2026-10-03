namespace Celeste.Mod.MiaoNet;

public enum ChatInputTokenType : byte
{
    Text,
    CommandName,
    Argument,
    Mention,
    Emoji
}

public readonly struct ChatInputToken
{
    public ChatInputTokenType Type { get; }

    public int Start { get; }

    public int Length { get; }

    public CommandSegmentType SegmentType { get; }

    public ChatInputToken(
        ChatInputTokenType type, int start, int length,
        CommandSegmentType segmentType = CommandSegmentType.Text
    )
    {
        Type = type;
        Start = start;
        Length = length;
        SegmentType = segmentType;
    }

    public int End => Start + Length;
}
