namespace Celeste.Mod.MiaoNet;

internal sealed class MiaoNetChatInputValidityContext : IChatInputValidityContext
{
    private readonly IChatInputState state;
    private readonly CommandParser parser;
    private readonly Func<string, bool> isChannelType;
    private readonly Func<string, bool> isEmoji;

    public MiaoNetChatInputValidityContext(
        IChatInputState state,
        CommandParser parser,
        Func<string, bool> isChannelType,
        Func<string, bool> isEmoji
    )
    {
        this.state = state;
        this.parser = parser;
        this.isChannelType = isChannelType;
        this.isEmoji = isEmoji;
    }

    public ChatInputValidity CheckCommand(string name)
        => CommandParser.MatchCommand(parser.Commands, name) is not null
            ? ChatInputValidity.Valid
            : ChatInputValidity.Invalid;

    // Player names are matched case-sensitively,
    // mirroring MiaoNetCommand.Commands.GetSameChannelPlayer.
    public ChatInputValidity CheckPlayer(string name, CommandSegmentType scope)
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

    // Channel names are matched case-insensitively,
    // mirroring ServerState.TryGetChannelByName.
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
}
