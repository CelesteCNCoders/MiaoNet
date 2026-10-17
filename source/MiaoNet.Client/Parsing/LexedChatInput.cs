namespace Celeste.Mod.MiaoNet;

/// <summary>
/// The result of lexing chat input: the flat token stream plus the resolved command structure.
/// </summary>
public sealed class LexedChatInput
{
    public string Text { get; }

    /// <summary>Tokens covering every non-separator character of <see cref="Text"/> in order.</summary>
    public IReadOnlyList<ChatInputToken> Tokens { get; }

    public MiaoNetCommand? Command { get; }

    public int NameEnd { get; }

    public IReadOnlyList<CommandArgumentInfo> Arguments { get; }

    public LexedChatInput(
        string text,
        IReadOnlyList<ChatInputToken> tokens,
        MiaoNetCommand? command,
        int nameEnd,
        IReadOnlyList<CommandArgumentInfo> arguments
    )
    {
        Text = text;
        Tokens = tokens;
        Command = command;
        NameEnd = nameEnd;
        Arguments = arguments;
    }
}
