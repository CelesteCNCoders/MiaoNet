namespace Celeste.Mod.MiaoNet;

public interface IChatInputValidityContext
{
    public ChatInputValidity CheckCommand(string name);

    public ChatInputValidity CheckPlayer(string name, CommandSegmentType scope);

    public ChatInputValidity CheckMention(string name);

    public ChatInputValidity CheckChannel(string name);

    public ChatInputValidity CheckChannelType(string name);

    public ChatInputValidity CheckEmoji(string name);
}
