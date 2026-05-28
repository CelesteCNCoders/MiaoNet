using System.ComponentModel;

namespace Celeste.Mod.ChatInputBox;

public class ChatTab
{
    public string Name { get; }
    public Func<ChatText, bool>? Filter { get; }

    public ChatTab(string name,  Func<ChatText, bool>? filter)
    {
        Name = name;
        Filter = filter;
    }
}