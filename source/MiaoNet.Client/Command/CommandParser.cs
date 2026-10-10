using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class CommandParser
{
    public const string CommandPrefix = "/";

    public enum ParseResult
    {
        Success,
        NoSuchCommand,
        MissingArguments,
        TooManyArguments
    }

    private readonly IReadOnlyCollection<MiaoNetCommand> commandsToMatch;

    public IReadOnlyCollection<MiaoNetCommand> Commands => commandsToMatch;

    public CommandParser(IReadOnlyCollection<MiaoNetCommand> commandsToMatch)
    {
        this.commandsToMatch = commandsToMatch;
    }

    /// <summary>
    /// Finds the command whose name or an alias matches <paramref name="name"/>,
    /// compared case-insensitively, or <see langword="null"/> if there is none.
    /// </summary>
    public static MiaoNetCommand? MatchCommand(IReadOnlyCollection<MiaoNetCommand> commands, string name)
    {
        foreach (MiaoNetCommand command in commands)
        {
            if (command.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return command;
            if (command.Aliases is not null)
            {
                foreach (string alias in command.Aliases)
                {
                    if (alias.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return command;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the command matching the input and splits its arguments without validating their count,
    /// shared by <see cref="Parse"/> and the chat highlighter.
    /// </summary>
    public MiaoNetCommand? ParseStructure(
        string text,
        out int nameEnd,
        out IReadOnlyList<CommandArgument> arguments
    )
    {
        nameEnd = IndexOfWhitespace(text, CommandPrefix.Length);
        if (nameEnd == -1)
            nameEnd = text.Length;

        MiaoNetCommand? command = MatchCommand(commandsToMatch, text[CommandPrefix.Length..nameEnd]);
        arguments = command is null
            ? Array.Empty<CommandArgument>()
            : CommandArgumentSplitter.Split(text, nameEnd, command.Segments.Count, command.CaptureRestSegments);
        return command;
    }

    // The command name ends at the first whitespace, matching CommandArgumentSplitter.
    private static int IndexOfWhitespace(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Parse a command text(i.e. <![CDATA[/w <a player> <some text to whisper>]]>) into
    /// a <paramref name="matchedCommand"/> and <paramref name="segments"/>.
    /// </summary>
    public ParseResult Parse(
        string commandText,
        out string commandName,
        out MiaoNetCommand? matchedCommand,
        out IReadOnlyList<string>? segments
    )
    {
        SafeGuard.Assert(commandText.StartsWith(CommandPrefix, StringComparison.Ordinal));

        matchedCommand = ParseStructure(commandText, out int nameEnd, out IReadOnlyList<CommandArgument> parsedArgs);
        commandName = commandText[CommandPrefix.Length..nameEnd];
        segments = null;

        if (matchedCommand is null)
            return ParseResult.NoSuchCommand;

        string[] splitedArgs = new string[parsedArgs.Count];
        for (int i = 0; i < parsedArgs.Count; i++)
            splitedArgs[i] = commandText[parsedArgs[i].Start..parsedArgs[i].End];
        segments = splitedArgs;

        if (splitedArgs.Length < matchedCommand.Segments.Count)
            return ParseResult.MissingArguments;
        if (!matchedCommand.CaptureRestSegments && splitedArgs.Length > matchedCommand.Segments.Count)
            return ParseResult.TooManyArguments;
        return ParseResult.Success;
    }
}
