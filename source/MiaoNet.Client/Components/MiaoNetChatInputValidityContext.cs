namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetChatInputValidityContext : IChatInputValidityContext
{
    private readonly IChatInputState state;
    private readonly CommandParser parser;
    private readonly Func<string, bool> isChannelType;
    private readonly IReadOnlyList<string> channelTypeNames;
    private readonly Func<string, bool> isEmoji;

    public MiaoNetChatInputValidityContext(
        IChatInputState state,
        CommandParser parser,
        Func<string, bool> isChannelType,
        IReadOnlyList<string> channelTypeNames,
        Func<string, bool> isEmoji
    )
    {
        this.state = state;
        this.parser = parser;
        this.isChannelType = isChannelType;
        this.channelTypeNames = channelTypeNames;
        this.isEmoji = isEmoji;
    }

    public ChatInputValidity CheckCommand(string name)
        => CommandStructureResolver.MatchCommand(parser.Commands, name) is not null
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;

    // Player names are case-sensitive, mirroring MiaoNetCommand.Commands.GetSameChannelPlayer.
    public ChatInputValidity CheckPlayer(string name, PlayerScope scope)
    {
        if (!state.IsConnected)
            return ChatInputValidity.Neutral;

        return state.PlayerNamesInScope(scope).Contains(name)
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;
    }

    public ChatInputValidity CheckMention(string name)
    {
        if (!state.IsConnected)
            return ChatInputValidity.Neutral;

        return state.AllPlayerNames.Contains(name)
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;
    }

    // Channel names are case-insensitive, mirroring ServerState.TryGetChannelByName.
    public ChatInputValidity CheckChannel(string name)
    {
        if (!state.IsConnected)
            return ChatInputValidity.Neutral;

        return state.PublicChannelNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;
    }

    public ChatInputValidity CheckChannelType(string name)
        => isChannelType(name)
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;

    public ChatInputValidity CheckEmoji(string name)
        => isEmoji(name)
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;

    // These mirror the completion predicates exactly, so "nothing left to complete" and
    // "can no longer become valid" stay the same statement.

    public bool CanStillCompleteCommand(string partial)
    {
        const StringComparison sc = StringComparison.OrdinalIgnoreCase;
        foreach (MiaoNetCommand command in parser.Commands)
        {
            if (command.Name.Contains(partial, sc))
                return true;
            if (command.Aliases is not null && command.Aliases.Any(a => a.Contains(partial, sc)))
                return true;
        }
        return false;
    }

    public bool CanStillCompletePlayer(string partial, PlayerScope scope)
    {
        if (!state.IsConnected)
            return true;
        const StringComparison sc = StringComparison.OrdinalIgnoreCase;
        return state.PlayerNamesInScope(scope).Any(n => n.Contains(partial, sc));
    }

    public bool CanStillCompleteMention(string partial)
    {
        if (!state.IsConnected)
            return true;
        const StringComparison sc = StringComparison.OrdinalIgnoreCase;
        return state.AllPlayerNames.Any(n => n.Contains(partial, sc));
    }

    public bool CanStillCompleteChannel(string partial)
    {
        if (!state.IsConnected)
            return true;
        const StringComparison sc = StringComparison.OrdinalIgnoreCase;
        return state.PublicChannelNames.Any(n => n.Contains(partial, sc));
    }

    public bool CanStillCompleteChannelType(string partial)
    {
        const StringComparison sc = StringComparison.OrdinalIgnoreCase;
        return channelTypeNames.Any(n => n.Contains(partial, sc));
    }
}
