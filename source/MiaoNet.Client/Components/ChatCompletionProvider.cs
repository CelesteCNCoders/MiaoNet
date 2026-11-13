using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatCompletionProvider : ICompletionProvider
{
    private const StringComparison sc = StringComparison.OrdinalIgnoreCase;

    private readonly MiaoNetContext context;
    private readonly CommandParser parser;

    public ChatCompletionProvider(MiaoNetContext context, CommandParser parser)
    {
        this.context = context;
        this.parser = parser;
    }

    public IEnumerable<Completion>? GetCompletions(string input)
    {
        if (context.ClientState is null)
            return null;

        // Command arguments complete from the raw input: Emoji.Apply would mangle a typed value
        // that looks like ":emoji:" (e.g. a player named ":glad:glad:glad:") and hide its candidates.
        IEnumerable<Completion>? completions = GetCommandCompletions(input);
        if (completions is not null)
            return completions;

        // A "@..." word completes players, and may at the same time be an emoji context (a
        // following ":"), in which case both are offered with players first.
        IEnumerable<Completion>? mentions = GetMentionCompletions(input);
        IEnumerable<Completion>? emojis = GetEmojiCompletions(Emoji.Apply(input));

        if (mentions is not null && emojis is not null)
            return mentions.Concat(emojis);
        return mentions ?? emojis;
    }

    private IEnumerable<Completion>? GetMentionCompletions(string input)
    {
        int atIndex = FindMentionStartAtEnd(input);
        if (atIndex == -1)
            return null;

        string partial = input[(atIndex + 1)..];
        int remove = partial.Length;
        var state = context.ClientState!;
        return from p in state.AllPlayers
               let name = p.Info.Name
               where name.Contains(partial, sc)
               select new Completion(name, name, remove);

        int FindMentionStartAtEnd(string text)
        {
            int start = -1;
            foreach (ChatInputToken token in ChatLexer.Lex(text, parser.Commands, static _ => false).Tokens)
            {
                if (token.Type == ChatInputTokenType.Mention && token.End == text.Length)
                    start = token.Start;
            }

            // A bare '@' is not a token yet, but it already starts the word being typed.
            if (start < 0 && text.Length > 0 && MentionBoundary.IsMentionStart(text, text.Length - 1))
                return text.Length - 1;
            return start;
        }
    }

    private static IEnumerable<Completion>? GetEmojiCompletions(string input)
    {
        int lastColonIndex = input.LastIndexOf(':');
        if (lastColonIndex == -1)
            return null;

        string afterColon = input[(lastColonIndex + 1)..];
        if (!afterColon.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return null;

        int remove = input.Length - lastColonIndex - 1;
        return from e in Emoji.Registered
               where !e.StartsWith('\0')
               where e.Contains(afterColon, StringComparison.OrdinalIgnoreCase)
               select new Completion(e, $"{(char)(Emoji.Get(e) + Emoji.Start)} {e}", remove, ':', shouldCloseMenu: true);
    }

    private IEnumerable<Completion>? GetCommandCompletions(string input)
    {
        if (!input.StartsWith('/'))
            return null;

        LexedChatInput lexed = ChatLexer.Lex(input, parser.Commands, static _ => false);
        bool endsWithSpace = input.Length > 0 && char.IsWhiteSpace(input[^1]);

        if (lexed.NameEnd == input.Length)
            return CommandNameCompletion.Complete(parser.Commands, input[1..lexed.NameEnd]);

        if (lexed.Command is null)
            return null;

        int index = endsWithSpace ? lexed.Arguments.Count : lexed.Arguments.Count - 1;
        if (index < 0)
            return null;
        int remove = endsWithSpace ? 0 : lexed.Arguments[index].Length;

        if (index >= lexed.Command.Segments.Count)
            return []; // an argument beyond the declared segments: no completion

        CommandSegmentType segmentType = lexed.Command.Segments[index];
        string part = endsWithSpace
            ? string.Empty
            : input[lexed.Arguments[index].Start..lexed.Arguments[index].End];
        var state = context.ClientState!;

        switch (segmentType)
        {
        case CommandSegmentType.Player:
        case CommandSegmentType.PlayerSameChannel:
        case CommandSegmentType.PlayerSameMap:
            return GetPlayerNameCompletions(CommandSegmentResolver.ResolvePlayers(state, segmentType.ToPlayerScope()), part);
        case CommandSegmentType.Channel:
            return from c in CommandSegmentResolver.ResolveChannels(state)
                   let name = c.Info.Name
                   where name.Contains(part, sc)
                   select new Completion(name, name, remove);
        case CommandSegmentType.CommandName:
            return CommandNameCompletion.Complete(parser.Commands, part);
        case CommandSegmentType.ChatChannelType:
            return from n in ChatChannelMatcher.Names
                   where n.Contains(part, sc)
                   select new Completion(n, n, remove);
        default:
            return null;
        }
    }

    private static IEnumerable<Completion>? GetPlayerNameCompletions(IEnumerable<OnlinePlayer> players, string part)
        => from p in players
           let i = p.Info
           where i.Name.Contains(part, sc)
           select new Completion(i.Name, i.DisplayName, part.Length);
}
