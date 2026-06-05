using System.ComponentModel;

namespace Celeste.Mod.ChatInputBox;

public class ChatTab
{
    public string Name { get; }
    public List<ChatText> ChatLog { get; }

    public ChatTab(string name)
    {
        Name = name;
        ChatLog = new();
    }
    
    public void AddChatMessage(ChatText chatMessageViewItem)
    {
        ChatLog.Add(chatMessageViewItem);
    }

    public void CleanUp()
    {
        ChatLog.Clear();
    }
}