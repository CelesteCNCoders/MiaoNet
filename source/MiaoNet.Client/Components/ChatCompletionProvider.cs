using System.Buffers;
using System.Text;
using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatCompletionProvider : ICompletionProvider
{
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

        if (!endsWithSpace && segments is null or { Count: 0 })
        {
            return from cmd in parser.Commands
                   where cmd.Name.Contains(commandName, StringComparison.OrdinalIgnoreCase)
                   || cmd.Aliases?.Any(a => a.Contains(commandName, StringComparison.OrdinalIgnoreCase)) == true
                   select new Completion(cmd.Name, cmd.Name, commandName.Length);
        }

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
                switch (segType)
                {
                case CommandSegmentType.Player:
                    return from pair in context.ClientState!.Players
                           let i = pair.Value.Info
                           where i.Name.Contains(part, StringComparison.OrdinalIgnoreCase)
                           select new Completion(i.Name, i.DisplayName, remove);
                case CommandSegmentType.PlayerSameMap:
                    return from pair in context.ClientState!.Players
                           let i = pair.Value.Info
                           where i.Name.Contains(part, StringComparison.OrdinalIgnoreCase)
                           where pair.Value.ShouldSyncFrom(context.ClientState.Self)
                           select new Completion(i.Name, i.DisplayName, remove);
                }
            }
        }

        return null;
    }
}