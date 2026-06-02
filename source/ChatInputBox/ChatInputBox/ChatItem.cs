namespace Celeste.Mod.ChatInputBox;

public class ChatItem
{
    public ChatText Message;
    public float ShowTimer;
    public float FadeOut = 1f;

    public ChatItem(ChatText message, float showTimer)
    {
        Message = message;
        ShowTimer = showTimer;
    }
}