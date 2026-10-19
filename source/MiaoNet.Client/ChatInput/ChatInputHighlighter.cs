using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatInputHighlighter : IChatInputHighlighter
{
    private readonly CommandParser parser;
    private readonly IChatInputValidityContext validity;
    private readonly IChatInputState state;
    private readonly Func<string, bool> isEmoji;

    private string? cachedText;
    private int cachedCaret;
    private int cachedVersion;
    private IReadOnlyList<ChatInputHighlightSpan>? cachedSpans;

    public ChatInputHighlighter(
        CommandParser parser,
        IChatInputValidityContext validity,
        IChatInputState state
    )
    {
        this.parser = parser;
        this.validity = validity;
        this.state = state;
        isEmoji = name => validity.CheckEmoji(name) == ChatInputValidity.Valid;
    }

    // Cached and recomputed only when the text, caret or live state version changes.
    public IReadOnlyList<ChatInputHighlightSpan> Highlight(string text, int caret)
    {
        int version = state.Version;
        if (cachedSpans is not null && caret == cachedCaret && text == cachedText && version == cachedVersion)
            return cachedSpans;

        List<ChatInputHighlightSpan> spans = [];
        int cursor = 0;

        foreach (ChatInputToken token in ChatInputTokenizer.Tokenize(text, parser, isEmoji))
        {
            if (token.Start > cursor)
                spans.Add(new ChatInputHighlightSpan(cursor, token.Start - cursor, ChatInputHighlightColors.Text));

            if (token.Length > 0)
                AddTokenSpans(spans, text, token, caret);

            if (token.End > cursor)
                cursor = token.End;
        }

        if (cursor < text.Length)
            spans.Add(new ChatInputHighlightSpan(cursor, text.Length - cursor, ChatInputHighlightColors.Text));

        cachedText = text;
        cachedCaret = caret;
        cachedVersion = version;
        cachedSpans = spans.AsReadOnly();
        return cachedSpans;
    }

    private void AddTokenSpans(List<ChatInputHighlightSpan> spans, string text, ChatInputToken token, int caret)
    {
        if (token.Type == ChatInputTokenType.Mention)
        {
            AddMentionSpans(spans, text, token, caret);
            return;
        }

        spans.Add(new ChatInputHighlightSpan(token.Start, token.Length, GetColor(text, token, caret)));
    }

    private void AddMentionSpans(List<ChatInputHighlightSpan> spans, string text, ChatInputToken token, int caret)
    {
        int start = token.Start;
        int end = token.End;

        ChatInputValidity result = validity.CheckMention(Slice(text, token)[1..]);
        if (result == ChatInputValidity.Valid)
        {
            spans.Add(new ChatInputHighlightSpan(start, token.Length, ChatInputHighlightColors.Player));
            return;
        }

        Color plain = result == ChatInputValidity.Invalid && IsDelimited(text, token, caret)
            ? ChatInputHighlightColors.Invalid
            : ChatInputHighlightColors.Text;

        IReadOnlyList<(int Start, int Length)> emojiSpans
            = ChatInputTokenizer.FindEmojiSpans(text, start, end, isEmoji);

        int runStart = start;
        Color runColor = ColorOf(start);
        for (int i = start + 1; i < end; i++)
        {
            Color color = ColorOf(i);
            if (color != runColor)
            {
                spans.Add(new ChatInputHighlightSpan(runStart, i - runStart, runColor));
                runStart = i;
                runColor = color;
            }
        }
        spans.Add(new ChatInputHighlightSpan(runStart, end - runStart, runColor));

        Color ColorOf(int index)
        {
            foreach ((int emojiStart, int emojiLength) in emojiSpans)
                if (index >= emojiStart && index < emojiStart + emojiLength)
                    return ChatInputHighlightColors.Emoji;
            return plain;
        }
    }

    private Color GetColor(string text, ChatInputToken token, int caret)
    {
        switch (token.Type)
        {
        case ChatInputTokenType.CommandName:
            return Resolve(validity.CheckCommand(Slice(text, token)[1..]), ChatInputHighlightColors.Command, text, token, caret);

        case ChatInputTokenType.Argument:
            switch (token.SegmentType)
            {
            case CommandSegmentType.Player:
            case CommandSegmentType.PlayerSameChannel:
            case CommandSegmentType.PlayerSameMap:
                return Resolve(validity.CheckPlayer(Slice(text, token), token.SegmentType), ChatInputHighlightColors.Player, text, token, caret);

            case CommandSegmentType.Channel:
            {
                string channelName = Slice(text, token);
                if (!global::MiaoNet.Shared.NameValidator.IsValid(channelName))
                    return ChatInputHighlightColors.Invalid;
                // '!' marks a private channel, always color it so private names are not leaked.
                if (channelName.StartsWith('!'))
                    return ChatInputHighlightColors.PrivateChannel;
                return Resolve(validity.CheckChannel(channelName), ChatInputHighlightColors.Channel, text, token, caret);
            }

            case CommandSegmentType.ChannelOrNew:
            {
                string channelName = Slice(text, token);
                if (!global::MiaoNet.Shared.NameValidator.IsValid(channelName))
                    return ChatInputHighlightColors.Invalid;
                // Private channels keep their own color either way.
                if (channelName.StartsWith('!'))
                    return ChatInputHighlightColors.PrivateChannel;
                // A non-existent channel would be created, so unlike the existing-only case,
                // color it right away, even while the name is still being typed.
                return validity.CheckChannel(channelName) switch
                {
                    ChatInputValidity.Valid => ChatInputHighlightColors.Channel,
                    ChatInputValidity.Invalid => ChatInputHighlightColors.NewChannel,
                    _ => ChatInputHighlightColors.Text,
                };
            }

            case CommandSegmentType.ChatChannelType:
                return Resolve(validity.CheckChannelType(Slice(text, token)), ChatInputHighlightColors.ChatChannelType, text, token, caret);

            case CommandSegmentType.CommandName:
                return Resolve(validity.CheckCommand(Slice(text, token)), ChatInputHighlightColors.Command, text, token, caret);

            default:
                return ChatInputHighlightColors.Text;
            }

        case ChatInputTokenType.Emoji:
            return ChatInputHighlightColors.Emoji;

        default:
            return ChatInputHighlightColors.Text;
        }
    }

    private static Color Resolve(
        ChatInputValidity result, Color validColor,
        string text, ChatInputToken token, int caret
    )
    {
        if (result == ChatInputValidity.Valid)
            return validColor;
        if (result == ChatInputValidity.Invalid && IsDelimited(text, token, caret))
            return ChatInputHighlightColors.Invalid;
        return ChatInputHighlightColors.Text;
    }

    private static bool IsDelimited(string text, ChatInputToken token, int caret)
    {
        if (caret > token.Start && caret < token.End)
            return false;
        return token.End < text.Length;
    }

    private static string Slice(string text, ChatInputToken token)
        => text.Substring(token.Start, token.Length);
}
