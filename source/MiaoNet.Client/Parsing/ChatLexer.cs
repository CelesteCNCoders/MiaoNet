namespace Celeste.Mod.MiaoNet;

/// <summary>Tokenizes chat input into a flat list of <see cref="ChatInputToken"/>s.</summary>
/// <remarks>
/// Separator whitespace belongs to no token.
/// Command structure comes from <see cref="CommandStructureResolver.Resolve"/>.
/// Free text additionally recognizes whole-word <c>@mentions</c> and registered <c>:emoji:</c>.
/// </remarks>
public static class ChatLexer
{
    public static LexedChatInput Lex(
        string text, IReadOnlyCollection<MiaoNetCommand> commands, Func<string, bool> isEmoji
    )
    {
        List<ChatInputToken> tokens = [];

        if (text.Length == 0)
            return new LexedChatInput(text, tokens, null, 0, []);

        MiaoNetCommand? command = null;
        int nameEnd = 0;
        IReadOnlyList<CommandArgumentInfo> arguments = [];

        if (text[0] == CommandStructureResolver.CommandPrefix[0])
        {
            command = CommandStructureResolver.Resolve(text, commands, out nameEnd, out arguments);
            tokens.Add(new ChatInputToken(ChatInputTokenType.CommandName, 0, nameEnd));

            if (command is not null)
            {
                LexArguments(tokens, text, arguments, isEmoji);
            }
            else
            {
                // An unknown command has no structure:
                // after the separator whitespace, the rest is free text.
                int restStart = nameEnd;
                while (restStart < text.Length && char.IsWhiteSpace(text[restStart]))
                    restStart++;
                ScanFreeText(tokens, text, restStart, text.Length, isEmoji);
            }
        }
        else
        {
            ScanFreeText(tokens, text, 0, text.Length, isEmoji);
        }

        return new LexedChatInput(text, tokens, command, nameEnd, arguments);
    }

    private static void LexArguments(
        List<ChatInputToken> tokens, string text,
        IReadOnlyList<CommandArgumentInfo> arguments, Func<string, bool> isEmoji
    )
    {
        foreach (CommandArgumentInfo argument in arguments)
        {
            if (argument.IsOverflow)
            {
                tokens.Add(new ChatInputToken(
                    ChatInputTokenType.Overflow, argument.Start, argument.Length));
                continue;
            }

            if (argument.FreeText)
            {
                ScanFreeText(tokens, text, argument.Start, argument.End, isEmoji);
                continue;
            }

            tokens.Add(new ChatInputToken(
                ChatInputTokenType.Argument, argument.Start, argument.Length,
                argument.SegmentType));
        }
    }

    private static void ScanFreeText(
        List<ChatInputToken> tokens, string text, int from, int to, Func<string, bool> isEmoji
    )
    {
        List<(int Start, int Length)> emojiSpans = FindEmojiSpans(text, from, to, isEmoji);
        List<(int Start, int End)> mentionSpans = [.. MentionBoundary.Enumerate(text, from, to)];

        int textStart = from;
        int i = from;
        int emojiIndex = 0;
        int mentionIndex = 0;
        while (i < to)
        {
            while (emojiIndex < emojiSpans.Count && emojiSpans[emojiIndex].Start < i)
                emojiIndex++;
            while (mentionIndex < mentionSpans.Count && mentionSpans[mentionIndex].Start < i)
                mentionIndex++;

            if (mentionIndex < mentionSpans.Count && mentionSpans[mentionIndex].Start == i)
            {
                (int start, int end) = mentionSpans[mentionIndex];
                FlushText(tokens, text, textStart, start);
                tokens.Add(new ChatInputToken(ChatInputTokenType.Mention, start, end - start));
                i = end;
                textStart = i;
                mentionIndex++;
                continue;
            }

            if (emojiIndex < emojiSpans.Count && emojiSpans[emojiIndex].Start == i)
            {
                FlushText(tokens, text, textStart, i);
                tokens.Add(new ChatInputToken(ChatInputTokenType.Emoji, i, emojiSpans[emojiIndex].Length));
                i += emojiSpans[emojiIndex].Length;
                textStart = i;
                emojiIndex++;
                continue;
            }

            i++;
        }

        FlushText(tokens, text, textStart, to);
    }

    /// <summary>
    /// Finds <c>:name:</c> spans naming a registered emoji,
    /// mirroring Celeste.Mod.Emoji's implementation.
    /// </summary>
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
                // Only a colon at head can open a pair.
                if (head >= 0 && text[head] == ':')
                {
                    int nameLength = tail - head - 1;
                    if (isEmoji(text.Substring(head + 1, nameLength)))
                    {
                        spans.Add((head, tail + 1 - head));
                        // Let the character after the closing colon open the next pair.
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
