using System.Diagnostics.CodeAnalysis;
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

    public CommandParser(IReadOnlyCollection<MiaoNetCommand> commandsToMatch)
    {
        this.commandsToMatch = commandsToMatch;
    }

    /// <summary>
    /// Parse a command text(i.e. <![CDATA[/w <a player> <some text to whisper>]]>) into
    /// a <paramref name="matchedCommand"/> and <paramref name="arguments"/>.
    /// </summary>
    public ParseResult Parse(
        string commandText,
        out string commandName,
        out MiaoNetCommand? matchedCommand,
        out IReadOnlyList<string>? arguments
    )
    {
        SafeGuard.Assert(commandText.StartsWith(CommandPrefix));
        matchedCommand = null;
        arguments = null;

        int firstSpaceIndex = commandText.IndexOf(' ');
        if (firstSpaceIndex == -1) firstSpaceIndex = commandText.Length;

        string parsedCmdName = commandText[CommandPrefix.Length..firstSpaceIndex];
        commandName = parsedCmdName;

        var nameMatchedCmd = commandsToMatch.FirstOrDefault(
            c => c.Name.Equals(parsedCmdName, StringComparison.OrdinalIgnoreCase) ||
                (c.Aliases is not null && c.Aliases.Any(a => a.Equals(parsedCmdName, StringComparison.OrdinalIgnoreCase)))
        );

        if (nameMatchedCmd is null)
            return ParseResult.NoSuchCommand;
        matchedCommand = nameMatchedCmd;

        StringSplitOptions sso = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        if (nameMatchedCmd.CaptureRestSegments)
        {
            string[] splitedArgs = commandText[firstSpaceIndex..]
                .Split(' ', nameMatchedCmd.Segments.Count, sso);
            arguments = splitedArgs;
            if (splitedArgs.Length < nameMatchedCmd.Segments.Count)
                return ParseResult.MissingArguments;
            return ParseResult.Success;
        }
        else
        {
            string[] splitedArgs = commandText[firstSpaceIndex..].Split(' ', sso);
            arguments = splitedArgs;
            if (splitedArgs.Length < nameMatchedCmd.Segments.Count)
                return ParseResult.MissingArguments;
            if (splitedArgs.Length > nameMatchedCmd.Segments.Count)
                return ParseResult.TooManyArguments;
            return ParseResult.Success;
        }
    }
}