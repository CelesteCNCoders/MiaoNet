using AsmResolver.DotNet.Builder;

namespace Celeste.Mod.ChatInputBox;

public class ChatMessageManager
{
    
    private class ChatTab
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
    
    private int activeTabIndex;
    private readonly List<ChatText> chatLog;
    private List<ChatTab> tab { get; }
    public int ActiveTabIndex => activeTabIndex;
    public List<ChatText> ActiveChatLog => activeTabIndex < 0 ? ChatLog : tab[activeTabIndex].ChatLog;
    public string? ActiveTabName => activeTabIndex < 0 ? null : tab[activeTabIndex].Name;
    public List<ChatText> ChatLog => chatLog;
    public List<string> TabNameList => tab.Select(t => t.Name).ToList(); 
    
    public ChatMessageManager()
    {
        chatLog = new();
        tab = new();
        activeTabIndex = -1;                                                             
    }

    private ChatTab GetOrAddTab(string name)
    {
        var targetTabIdx = tab.FindIndex(t => t.Name == name);
        if (targetTabIdx < 0)
        {
            tab.Add(new ChatTab(name));
            targetTabIdx = tab.Count - 1;
        }
        return tab[targetTabIdx];
    }
    
    public void AddTab(string name)
    {
        tab.Add(new ChatTab(name));
    }

    public void RemoveTab(string name)
    {
        var targetTabIdx = tab.FindIndex(t => t.Name == name);
        if (targetTabIdx < 0)
            return;

        bool removingActiveTab = targetTabIdx == activeTabIndex;
        tab.RemoveAt(targetTabIdx);

        if (removingActiveTab)
            activeTabIndex = tab.Count == 0 ? -1 : Math.Min(activeTabIndex, tab.Count - 1);
        
        else if (targetTabIdx < activeTabIndex)
            activeTabIndex--;
    }
    
    public void CycleTab()
    {
        activeTabIndex = (activeTabIndex + 2) % (tab.Count + 1) - 1;
    }

    public void SetActiveTab(string name)
    {
        var targetTabIndex = tab.FindIndex(t => t.Name == name);
        if  (targetTabIndex < 0) return;
        activeTabIndex = targetTabIndex;
    }

    // Add to all Tabs while tabName == null (For Local Announcement）
    public void AddChatMessage(ChatText message, string? tabName)
    {
        chatLog.Add(message);
        if (tabName == null)
        {
            foreach (var chatTab in this.tab)
            {
                chatTab.AddChatMessage(message);
            }

            return;
        } 
        var tab = GetOrAddTab(tabName);
        tab.AddChatMessage(message);
    }

    public void CleanUp()
    {
        chatLog.Clear();
        tab.Clear();
        activeTabIndex = -1;
    }

    public void CleanHistory()
    {
        chatLog.Clear();
        foreach (var chatTab in tab)
        {
            chatTab.CleanUp();
        }
    }
}
