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
    private IReadOnlyList<ColoredTextRange>? cachedSpans;

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
    public IReadOnlyList<ColoredTextRange> Highlight(string text, int caret)
    {
        int version = state.Version;
        if (cachedSpans is not null && caret == cachedCaret && text == cachedText && version == cachedVersion)
            return cachedSpans;

        List<ColoredTextRange> spans = [];
        int cursor = 0;

        foreach (ChatInputToken token in ChatLexer.Lex(text, parser.Commands, isEmoji).Tokens)
        {
            if (token.Start > cursor)
                spans.Add(new ColoredTextRange(cursor, token.Start - cursor, ChatInputHighlightColors.Text));

            if (token.Length > 0)
                AddTokenSpans(spans, text, token, caret);

            if (token.End > cursor)
                cursor = token.End;
        }

        if (cursor < text.Length)
            spans.Add(new ColoredTextRange(cursor, text.Length - cursor, ChatInputHighlightColors.Text));

        cachedText = text;
        cachedCaret = caret;
        cachedVersion = version;
        cachedSpans = spans.AsReadOnly();
        return cachedSpans;
    }

    private void AddTokenSpans(List<ColoredTextRange> spans, string text, ChatInputToken token, int caret)
    {
        if (token.Type == ChatInputTokenType.Mention)
        {
            AddMentionSpans(spans, text, token, caret);
            return;
        }

        spans.Add(new ColoredTextRange(token.Start, token.Length, GetColor(text, token, caret)));
    }

    private void AddMentionSpans(List<ColoredTextRange> spans, string text, ChatInputToken token, int caret)
    {
        int start = token.Start;
        int end = token.End;

        ChatInputValidity result = validity.CheckMention(Slice(text, token)[1..]);
        if (result == ChatInputValidity.Valid)
        {
            spans.Add(new ColoredTextRange(start, token.Length, ChatInputHighlightColors.Player));
            return;
        }

        // A mention is an exact player name, so an empty roster match means no further typing
        // can rescue it. Emoji deliberately do not work this way: a lone colon is too common.
        bool doomed = result == ChatInputValidity.Invalid
            && !validity.CanStillCompleteMention(Slice(text, token)[1..]);
        Color plain = result == ChatInputValidity.Invalid
            && (doomed || IsDelimited(text, token, caret))
            ? ChatInputHighlightColors.Invalid
            : ChatInputHighlightColors.Text;

        IReadOnlyList<(int Start, int Length)> emojiSpans
            = ChatLexer.FindEmojiSpans(text, start, end, isEmoji);

        int runStart = start;
        Color runColor = ColorOf(start);
        for (int i = start + 1; i < end; i++)
        {
            Color color = ColorOf(i);
            if (color != runColor)
            {
                spans.Add(new ColoredTextRange(runStart, i - runStart, runColor));
                runStart = i;
                runColor = color;
            }
        }
        spans.Add(new ColoredTextRange(runStart, end - runStart, runColor));

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
        {
            string partial = Slice(text, token)[1..];
            return Resolve(
                validity.CheckCommand(partial), ChatInputHighlightColors.Command,
                validity.CanStillCompleteCommand(partial), text, token, caret);
        }

        case ChatInputTokenType.Argument:
            switch (token.SegmentType)
            {
            case CommandSegmentType.Player:
            case CommandSegmentType.PlayerSameChannel:
            case CommandSegmentType.PlayerSameMap:
            {
                string playerName = Slice(text, token);
                PlayerScope scope = token.SegmentType.ToPlayerScope();
                return Resolve(
                    validity.CheckPlayer(playerName, scope), ChatInputHighlightColors.Player,
                    validity.CanStillCompletePlayer(playerName, scope), text, token, caret);
            }

            case CommandSegmentType.Channel:
            {
                string channelName = Slice(text, token);
                if (!global::MiaoNet.Shared.NameValidator.IsValid(channelName))
                    return ChatInputHighlightColors.Invalid;
                // '!' marks a private channel, always color it so private names are not leaked.
                if (channelName.StartsWith('!'))
                    return ChatInputHighlightColors.PrivateChannel;
                return Resolve(
                    validity.CheckChannel(channelName), ChatInputHighlightColors.Channel,
                    validity.CanStillCompleteChannel(channelName), text, token, caret);
            }

            case CommandSegmentType.ChannelOrNew:
            {
                string channelName = Slice(text, token);
                if (!global::MiaoNet.Shared.NameValidator.IsValid(channelName))
                    return ChatInputHighlightColors.Invalid;
                // Private channels keep their own color either way.
                if (channelName.StartsWith('!'))
                    return ChatInputHighlightColors.PrivateChannel;
                // Any remaining name may become a new channel, so this slot is never doomed
                // by having no completions left.
                return validity.CheckChannel(channelName) switch
                {
                    ChatInputValidity.Valid => ChatInputHighlightColors.Channel,
                    ChatInputValidity.Invalid => ChatInputHighlightColors.NewChannel,
                    _ => ChatInputHighlightColors.Text,
                };
            }

            case CommandSegmentType.ChatChannelType:
            {
                string channelType = Slice(text, token);
                return Resolve(
                    validity.CheckChannelType(channelType), ChatInputHighlightColors.ChatChannelType,
                    validity.CanStillCompleteChannelType(channelType), text, token, caret);
            }

            case CommandSegmentType.CommandName:
            {
                string commandName = Slice(text, token);
                return Resolve(
                    validity.CheckCommand(commandName), ChatInputHighlightColors.Command,
                    validity.CanStillCompleteCommand(commandName), text, token, caret);
            }

            default:
                return ChatInputHighlightColors.Text;
            }

        case ChatInputTokenType.Emoji:
            return ChatInputHighlightColors.Emoji;

        case ChatInputTokenType.Overflow:
            return ChatInputHighlightColors.Invalid;

        default:
            return ChatInputHighlightColors.Text;
        }
    }

    // An invalid token turns red once it is delimited, or as soon as no completion is left
    // and further typing therefore cannot rescue it.
    private static Color Resolve(
        ChatInputValidity result, Color validColor, bool canStillComplete,
        string text, ChatInputToken token, int caret
    )
    {
        if (result == ChatInputValidity.Valid)
            return validColor;
        if (result == ChatInputValidity.Invalid && (!canStillComplete || IsDelimited(text, token, caret)))
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
