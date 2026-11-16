using System.Collections.Immutable;
using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public static class ChatMentionParser
{
    public static bool SplitMentionSegments(
        ImmutableArray<ChatTextSegment>.Builder builder,
        ChatTextSegment segment,
        IReadOnlySet<string> names,
        string? selfName,
        Color mentionColor
    )
    {
        bool mentionsSelf = false;
        string text = segment.Text;
        if (text.IndexOf('@', StringComparison.Ordinal) < 0)
        {
            builder.Add(segment);
            return false;
        }

        int start = 0;
        foreach ((int mentionStart, int mentionEnd) in MentionBoundary.Enumerate(text, 0, text.Length))
        {
            string name = text[(mentionStart + 1)..mentionEnd];
            if (!names.Contains(name))
                continue;

            if (string.Equals(name, selfName, StringComparison.Ordinal))
                mentionsSelf = true;
            if (mentionStart > start)
                builder.Add(new ChatTextSegment(segment.Style, segment.Color, text[start..mentionStart]));
            builder.Add(new ChatTextSegment(segment.Style, mentionColor, text[mentionStart..mentionEnd]));
            start = mentionEnd;
        }

        if (start < text.Length)
            builder.Add(new ChatTextSegment(segment.Style, segment.Color, text[start..]));

        return mentionsSelf;
    }
}
