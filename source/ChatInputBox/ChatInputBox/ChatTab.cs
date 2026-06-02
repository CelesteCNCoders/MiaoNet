using System.ComponentModel;

namespace Celeste.Mod.ChatInputBox;

public class ChatTab
{
    public string Name { get; }
    public List<ChatItem> chatLog;

    public ChatTab(string name)
    {
        Name = name;
        chatLog = new();
    }
    
    public void AddChatItem(ChatItem chatItem)
    {
        chatLog.Add(chatItem);
    }

    public void CleanUp()
    {
        chatLog.Clear();
    }
}