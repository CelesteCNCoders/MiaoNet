using AsmResolver.DotNet.Builder;

namespace Celeste.Mod.ChatInputBox;

public class ChatMessageManager
{
    private int activeTabIndex;
    private readonly List<ChatText> chatLog;
    public List<ChatTab> Tabs { get; }
    public int ActiveTabIndex => activeTabIndex;
    public List<ChatText> ActiveChatLog => activeTabIndex < 0 ? ChatLog : Tabs[activeTabIndex].ChatLog;
    public string? ActiveTabName => activeTabIndex < 0 ? null : Tabs[activeTabIndex].Name;
    public List<ChatText> ChatLog => chatLog;
    
    public ChatMessageManager()
    {
        chatLog = new();
        Tabs = new();
        activeTabIndex = -1;                                                             
    }

    private ChatTab GetOrAddTab(string name)
    {
        var targetTabIdx = Tabs.FindIndex(t => t.Name == name);
        if (targetTabIdx < 0)
        {
            Tabs.Add(new ChatTab(name));
            targetTabIdx = Tabs.Count - 1;
        }
        return Tabs[targetTabIdx];
    }
    
    public void AddTab(string name)
    {
        Tabs.Add(new ChatTab(name));
    }

    public void RemoveTab(string name)
    {
        var targetTabIdx = Tabs.FindIndex(t => t.Name == name);
        Tabs.RemoveAt(targetTabIdx);
        if (name == ActiveTabName) activeTabIndex %= Tabs.Count;
    }
    
    public void CycleTab()
    {
        activeTabIndex = (activeTabIndex + 2) % (Tabs.Count + 1) - 1;
    }

    public void SetActiveTab(string name)
    {
        var targetTabIndex = Tabs.FindIndex(t => t.Name == name);
        if  (targetTabIndex < 0) return;
        activeTabIndex = targetTabIndex;
    }

    // Add to all Tabs while tabName == null (For Local Announcement）
    public void AddChatMessage(ChatText message, string? tabName)
    {
        chatLog.Add(message);
        if (tabName == null)
        {
            foreach (var chatTab in Tabs)
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
        Tabs.Clear();
        activeTabIndex = -1;
    }

    public void CleanHistory()
    {
        foreach (var chatTab in Tabs)
        {
            chatTab.CleanUp();
        }
    }
}