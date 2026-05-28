using System.Buffers;
using System.Text;
using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatCompletionProvider : ICompletionProvider
{
    private const StringComparison sc = StringComparison.CurrentCultureIgnoreCase;

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

        string emojiApplied = Emoji.Apply(input);

        IEnumerable<Completion>? completions;

        completions = GetEmojiCompletions(emojiApplied);
        if (completions is not null)
            return completions;

        completions = GetCommandCompletions(emojiApplied);
        if (completions is not null)
            return completions;

        return null;
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
               select new Completion(e, $"{(char)(Emoji.Get(e) + Emoji.Start)} {e}", remove);
    }

    private IEnumerable<Completion>? GetCommandCompletions(string input)
    {
        if (!input.StartsWith('/'))
            return null;

        // this impl is ugly but it just works
        bool endsWithSpace = input.EndsWith(' ');
        CommandParser.ParseResult result = parser.Parse(input, out string commandName, out MiaoNetCommand? matchedCommand, out var segments);

        // everest forced InvariantCulture, so the followings are actually equivalent to InvariantCultureIgnoreCase
        // we'll keep using CurrentCultureIgnoreCase to keep semantics
        if (!endsWithSpace && segments is null or { Count: 0 })
            return GetCommandNameCompletions(parser, commandName);

        if (matchedCommand is not null)
        {
            int curSegCount = segments!.Count;
            int ind = curSegCount - 1;
            if (endsWithSpace)
                ind++;
            if (ind < matchedCommand.Segments.Count)
            {
                var segType = matchedCommand.Segments[ind];
                string part = ind >= segments.Count ? string.Empty : segments[ind];
                int remove = part.Length;
                var state = context.ClientState!;
                switch (segType)
                {
                case CommandSegmentType.Player:
                    return GetPlayerNameCompletions(state.Players.Select(p => p.Value), part);
                case CommandSegmentType.PlayerSameChannel:
                    return GetPlayerNameCompletions(state.SelfChannel.Players, part);
                case CommandSegmentType.PlayerSameMap:
                    return GetPlayerNameCompletions(state.SelfChannel.Players.Where(p => p.ShouldSyncFrom(state.Self)), part);
                case CommandSegmentType.Channel:
                    return from pair in state.Channels
                           let i = pair.Value.Info
                           where i.Name.Contains(part, sc)
                           select new Completion(i.Name, i.Name, remove);
                case CommandSegmentType.CommandName:
                    return GetCommandNameCompletions(parser, part);
                case CommandSegmentType.ChatChannelType:
                    return from n in ChatChannelMatcher.Names
                           where n.Contains(part, sc)
                           select new Completion(n, n, remove);
                }
            }
        }

        return null;

        static IEnumerable<Completion>? GetCommandNameCompletions(CommandParser parser, string commandName)
            => from cmd in parser.Commands
               where cmd.Name.Contains(commandName, sc)
               || cmd.Aliases?.Any(a => a.Contains(commandName, sc)) == true
               select new Completion(cmd.Name, cmd.Name, commandName.Length);

        static IEnumerable<Completion>? GetPlayerNameCompletions(IEnumerable<OnlinePlayer> players, string part)
            => from p in players
               let i = p.Info
               where i.Name.Contains(part, sc)
               select new Completion(i.Name, i.DisplayName, part.Length);
    }
}