namespace Celeste.Mod.MiaoNet;

public interface IChatInputValidityContext
{
    public ChatInputValidity CheckCommand(string name);

    public ChatInputValidity CheckPlayer(string name, PlayerScope scope);

    public ChatInputValidity CheckMention(string name);

    public ChatInputValidity CheckChannel(string name);

    public ChatInputValidity CheckChannelType(string name);

    public ChatInputValidity CheckEmoji(string name);

    // Whether some valid value still contains the typed text, i.e. whether any completion is left.
    // Once this is false, no further typing can make the token valid.

    public bool CanStillCompleteCommand(string partial);

    public bool CanStillCompletePlayer(string partial, PlayerScope scope);

    // Mentions match every player, unlike a player argument that is scoped.
    public bool CanStillCompleteMention(string partial);

    public bool CanStillCompleteChannel(string partial);

    public bool CanStillCompleteChannelType(string partial);
}
