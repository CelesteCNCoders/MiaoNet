using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class CommandParser
{
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
        SafeGuard.Assert(commandText.StartsWith(CommandStructureResolver.CommandPrefix, StringComparison.Ordinal));

        matchedCommand = CommandStructureResolver.Resolve(
            commandText, commandsToMatch, out int nameEnd, out IReadOnlyList<CommandArgumentInfo> parsedArgs);
        commandName = commandText[CommandStructureResolver.CommandPrefix.Length..nameEnd];
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
