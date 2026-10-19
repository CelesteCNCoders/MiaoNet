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
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '@' || (i != 0 && !char.IsWhiteSpace(text[i - 1])))
                continue;

            int mentionEnd = i + 1;
            while (mentionEnd < text.Length && !char.IsWhiteSpace(text[mentionEnd]))
                mentionEnd++;

            string name = text[(i + 1)..mentionEnd];
            if (!names.Contains(name))
                continue;

            if (string.Equals(name, selfName, StringComparison.Ordinal))
                mentionsSelf = true;
            if (i > start)
                builder.Add(new ChatTextSegment(segment.Style, segment.Color, text[start..i]));
            builder.Add(new ChatTextSegment(segment.Style, mentionColor, text[i..mentionEnd]));
            i = mentionEnd - 1;
            start = mentionEnd;
        }

        if (start < text.Length)
            builder.Add(new ChatTextSegment(segment.Style, segment.Color, text[start..]));

        return mentionsSelf;
    }
}
