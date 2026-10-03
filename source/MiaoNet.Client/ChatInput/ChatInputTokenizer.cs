using System.Collections.Generic;

namespace Celeste.Mod.MiaoNet;

public static class ChatInputTokenizer
{
    public static IReadOnlyList<ChatInputToken> Tokenize(
        string text, CommandParser parser, Func<string, bool> isEmoji
    )
    {
        List<ChatInputToken> tokens = [];
        if (text.Length == 0)
            return tokens;

        if (text[0] == CommandParser.CommandPrefix[0])
        {
            MiaoNetCommand? matched = parser.ParseStructure(
                text, out int nameEnd, out IReadOnlyList<CommandArgument> args
            );
            tokens.Add(new ChatInputToken(ChatInputTokenType.CommandName, 0, nameEnd));

            if (matched is not null)
                TokenizeArguments(tokens, text, args, matched, isEmoji);
            else
                ScanFreeText(tokens, text, nameEnd, text.Length, isEmoji);

            return tokens;
        }

        ScanFreeText(tokens, text, 0, text.Length, isEmoji);
        return tokens;
    }

    private static void TokenizeArguments(
        List<ChatInputToken> tokens, string text,
        IReadOnlyList<CommandArgument> args, MiaoNetCommand command, Func<string, bool> isEmoji
    )
    {
        for (int i = 0; i < args.Count; i++)
        {
            CommandArgument arg = args[i];

            if (i >= command.Segments.Count)
            {
                // more arguments than declared, leave them as free text
                ScanFreeText(tokens, text, arg.Start, arg.End, isEmoji);
                continue;
            }

            AddArgument(tokens, text, arg.Start, arg.End, command.Segments[i], isEmoji);
        }
    }

    private static void AddArgument(
        List<ChatInputToken> tokens, string text, int start, int end, CommandSegmentType segmentType, Func<string, bool> isEmoji
    )
    {
        switch (segmentType)
        {
        case CommandSegmentType.Text:
        case CommandSegmentType.Emote:
            ScanFreeText(tokens, text, start, end, isEmoji);
            break;
        default:
            tokens.Add(new ChatInputToken(ChatInputTokenType.Argument, start, end - start, segmentType));
            break;
        }
    }

    private static void ScanFreeText(
        List<ChatInputToken> tokens, string text, int from, int to, Func<string, bool> isEmoji
    )
    {
        List<(int Start, int Length)> emojiSpans = FindEmojiSpans(text, from, to, isEmoji);

        int textStart = from;
        int i = from;
        int emojiIndex = 0;
        while (i < to)
        {
            while (emojiIndex < emojiSpans.Count && emojiSpans[emojiIndex].Start < i)
                emojiIndex++;

            if (emojiIndex < emojiSpans.Count && emojiSpans[emojiIndex].Start == i)
            {
                FlushText(tokens, text, textStart, i);
                tokens.Add(new ChatInputToken(ChatInputTokenType.Emoji, i, emojiSpans[emojiIndex].Length));
                i += emojiSpans[emojiIndex].Length;
                textStart = i;
                emojiIndex++;
                continue;
            }

            if (text[i] == '@' && (i == from || char.IsWhiteSpace(text[i - 1])))
            {
                int j = i + 1;
                while (j < to && !char.IsWhiteSpace(text[j]))
                    j++;

                if (j > i + 1)
                {
                    FlushText(tokens, text, textStart, i);
                    tokens.Add(new ChatInputToken(ChatInputTokenType.Mention, i, j - i));
                    i = j;
                    textStart = i;
                    continue;
                }
            }

            i++;
        }

        FlushText(tokens, text, textStart, to);
    }

    // Mirrors Celeste.Mod.Emoji's :name: pairing (CachedApply.Compute),
    // so a colon run only becomes an emoji token when it is a registered name.
    internal static List<(int Start, int Length)> FindEmojiSpans(
        string text, int from, int to, Func<string, bool> isEmoji
    )
    {
        List<(int Start, int Length)> spans = [];
        int head = -1;
        int tail = from;
        while (tail < to)
        {
            if (text[tail] == ':')
            {
                if (head >= 0 && text[head] == ':')
                {
                    string name = text.Substring(head + 1, (tail - 1) - (head + 1) + 1);
                    if (isEmoji(name))
                    {
                        spans.Add((head, tail + 1 - head));
                        tail++;
                        head = tail;
                        tail++;
                        continue;
                    }
                }
                head = tail;
            }
            tail++;
        }
        return spans;
    }

    private static void FlushText(List<ChatInputToken> tokens, string text, int start, int end)
    {
        if (end > start)
            tokens.Add(new ChatInputToken(ChatInputTokenType.Text, start, end - start));
    }
}
