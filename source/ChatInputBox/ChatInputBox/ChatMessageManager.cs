using AsmResolver.DotNet.Builder;

namespace Celeste.Mod.ChatInputBox;

public class ChatMessageManager
{

    private class ChatTab
    {
        public string Name { get; }
        public List<ChatItem> ChatLog { get; }

        public ChatTab(string name)
        {
            Name = name;
            ChatLog = new();
        }

        public void AddChatMessage(ChatItem chatMessageViewItem)
        {
            ChatLog.Add(chatMessageViewItem);
        }

        public void CleanUp()
        {
            ChatLog.Clear();
        }
    }

    // the line a fold key currently folds into
    private class FoldEntry
    {
        public ChatItem Item { get; set; }
        public DateTime FirstTime { get; }
        public DateTime LastTime { get; set; }

        public FoldEntry(ChatItem item, DateTime time)
        {
            Item = item;
            FirstTime = time;
            LastTime = time;
        }
    }

    private int activeTabIndex;
    public readonly List<ChatItem> chatLog;
    private List<ChatTab> tab { get; }
    private readonly Dictionary<string, FoldEntry> foldEntries = new();
    public int ActiveTabIndex => activeTabIndex;
    public List<ChatItem> ActiveChatLog => activeTabIndex < 0 ? ChatLog : tab[activeTabIndex].ChatLog;
    public string? ActiveTabName => activeTabIndex < 0 ? null : tab[activeTabIndex].Name;
    public List<ChatItem> ChatLog => chatLog;
    public List<string> TabNameList => tab.Select(t => t.Name).ToList();

    // messages sharing a fold key within this window (compared on message timestamps) fold together
    public double FoldWindowSeconds { get; set; } = 10f;

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

    public void CycleTabForward()
        => CycleTab(-1);

    public void CycleTabBackward()
        => CycleTab(1);

    public void CycleTab(int offset)
    {
        activeTabIndex = ((activeTabIndex + offset + 1) + (tab.Count + 1)) % (tab.Count + 1) - 1;
    }

    public void SetActiveTab(string name)
    {
        var targetTabIndex = tab.FindIndex(t => t.Name == name);
        if  (targetTabIndex < 0) return;
        activeTabIndex = targetTabIndex;
    }

    // Add to all Tabs while tabName == null (For Local Announcement）
    // With a foldKey, a repeat within FoldWindowSeconds does not append a new line:
    // the old line is removed from every log and foldedText is appended at the bottom
    // as a fresh message with a bumped RepeatCount.
    public void AddChatMessage(ChatItem message, DateTime dateTime, string? tabName,
        string? foldKey = null, ChatText? foldedText = null)
    {
        if (foldKey is null || foldedText is null)
        {
            AppendToLogs(message, tabName);
            return;
        }

        if (foldEntries.TryGetValue(foldKey, out var entry)
            && (dateTime - entry.LastTime).TotalSeconds <= FoldWindowSeconds)
        {
            RemoveFromLogs(entry.Item);
            var folded = new ChatItem(entry.FirstTime, foldedText)
            {
                RepeatCount = entry.Item.RepeatCount + 1
            };
            AppendToLogs(folded, tabName);
            entry.Item = folded;
            entry.LastTime = dateTime;
            return;
        }

        AppendToLogs(message, tabName);
        PruneFoldEntries(dateTime);
        foldEntries[foldKey] = new FoldEntry(message, dateTime);
    }

    private void AppendToLogs(ChatItem message, string? tabName)
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

    private void RemoveFromLogs(ChatItem item)
    {
        chatLog.Remove(item);
        foreach (var chatTab in tab)
        {
            chatTab.ChatLog.Remove(item);
        }
    }

    // keep fold entries from growing without bound in a long session
    private const int PruneThreshold = 128;
    private const double PruneIntervalSeconds = 30;
    private DateTime lastPruneTime = DateTime.MinValue;

    // sweep only at most once per PruneIntervalSeconds, and never on small dicts,
    // so a unique-message flood can't rescan the whole dict on every insert
    private void PruneFoldEntries(DateTime now)
    {
        if (foldEntries.Count <= PruneThreshold
            || (now - lastPruneTime).TotalSeconds < PruneIntervalSeconds)
            return;

        lastPruneTime = now;
        DateTime cutoff = now.AddSeconds(-Math.Max(FoldWindowSeconds * 6, 120));
        foreach (var pair in foldEntries.Where(p => p.Value.LastTime < cutoff).ToList())
            foldEntries.Remove(pair.Key);
    }

    public void CleanUp()
    {
        chatLog.Clear();
        tab.Clear();
        foldEntries.Clear();
        activeTabIndex = -1;
    }

    public void CleanHistory()
    {
        chatLog.Clear();
        foldEntries.Clear();
        foreach (var chatTab in tab)
        {
            chatTab.CleanUp();
        }
    }
}
