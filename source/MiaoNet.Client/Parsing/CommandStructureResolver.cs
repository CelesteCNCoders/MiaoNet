using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Resolves what a chat input names:
/// the command, and how the typed arguments map onto the segments that command declares.
/// Argument count is not validated.
/// </summary>
public static class CommandStructureResolver
{
    public const string CommandPrefix = "/";

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

    public static MiaoNetCommand? Resolve(
        string text,
        IReadOnlyCollection<MiaoNetCommand> commands,
        out int nameEnd,
        out IReadOnlyList<CommandArgumentInfo> arguments
    )
    {
        if (text.Length == 0)
        {
            nameEnd = 0;
            arguments = Array.Empty<CommandArgumentInfo>();
            return null;
        }

        SafeGuard.Assert(text.StartsWith(CommandPrefix, StringComparison.Ordinal));

        nameEnd = IndexOfWhitespace(text, CommandPrefix.Length);
        if (nameEnd == -1)
            nameEnd = text.Length;

        MiaoNetCommand? command = MatchCommand(commands, text[CommandPrefix.Length..nameEnd]);
        arguments = command is null
            ? Array.Empty<CommandArgumentInfo>()
            : ResolveArguments(text, nameEnd, command.Segments, command.CaptureRestSegments);
        return command;
    }

    private static CommandArgumentInfo[] ResolveArguments(
        string text, int contentStart, IReadOnlyList<CommandSegmentType> segments, bool captureRest
    )
    {
        IReadOnlyList<CommandArgument> raw
            = CommandArgumentSplitter.Split(text, contentStart, segments.Count, captureRest);

        CommandArgumentInfo[] result = new CommandArgumentInfo[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            bool overflow = i >= segments.Count;
            CommandSegmentType type = overflow ? CommandSegmentType.Text : segments[i];
            bool freeText = !overflow && type is CommandSegmentType.Text or CommandSegmentType.Emote;
            result[i] = new CommandArgumentInfo(raw[i].Start, raw[i].End, type, freeText, overflow);
        }
        return result;
    }

    private static int IndexOfWhitespace(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }
        return -1;
    }
}
