namespace Celeste.Mod.ChatInputBox;

public sealed class ChatChannelTab
{
    public int Id { get; }

    public string Title { get; }

    public bool EnabledInGlobal { get; set; }

    public ChatChannelTab(int id, string title, bool enabledInGlobal = true)
    {
        Id = id;
        Title = title;
        EnabledInGlobal = enabledInGlobal;
    }
}

public class ChatMessageManager
{
    private readonly record struct ChatLogEntry(ChatItem Item, int? ChannelId);

    // the line a fold key currently folds into
    private sealed class FoldEntry
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

    private int activeTabIndex = -1;
    private readonly List<ChatLogEntry> entries = new();
    private readonly List<ChatChannelTab> channels = new();
    private readonly Dictionary<int, ChatChannelTab> channelsById = new();
    private readonly Dictionary<string, FoldEntry> foldEntries = new();

    private int revision;
    private int allViewRevision = -1;
    private readonly List<ChatItem> allViewCache = new();
    private int globalViewRevision = -1;
    private readonly List<ChatItem> globalViewCache = new();
    private int activeViewRevision = -1;
    private int activeViewCachedTabIndex = int.MinValue;
    private readonly List<ChatItem> activeViewCache = new();

    public int ActiveTabIndex => activeTabIndex;

    public int? ActiveChannelId => activeTabIndex < 0 ? null : channels[activeTabIndex].Id;

    public string? ActiveTabName => activeTabIndex < 0 ? null : channels[activeTabIndex].Title;

    public IReadOnlyList<ChatChannelTab> Channels => channels;

    public List<ChatItem> AllChatLog => GetAllView();

    public List<ChatItem> GlobalChatLog => GetGlobalView();

    public List<ChatItem> ActiveChatLog => activeTabIndex < 0 ? GetGlobalView() : GetActiveChannelView();

    // messages sharing a fold key within this window (compared on message timestamps) fold together
    public double FoldWindowSeconds { get; set; } = 10f;

    public void AddChannel(int id, string title, bool enabledInGlobal = true)
    {
        if (channelsById.TryGetValue(id, out var existing))
        {
            existing.EnabledInGlobal = enabledInGlobal;
            revision++;
            return;
        }

        var tab = new ChatChannelTab(id, title, enabledInGlobal);
        channels.Add(tab);
        channelsById[id] = tab;
        revision++;
    }

    public bool ToggleChannelInGlobal(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= channels.Count)
            return false;

        var channel = channels[channelIndex];
        channel.EnabledInGlobal = !channel.EnabledInGlobal;
        revision++;
        return channel.EnabledInGlobal;
    }

    public void SetChannelEnabledInGlobal(int channelId, bool enabled)
    {
        if (!channelsById.TryGetValue(channelId, out var channel))
            return;

        if (channel.EnabledInGlobal == enabled)
            return;

        channel.EnabledInGlobal = enabled;
        revision++;
    }

    public void CycleTabForward()
        => CycleTab(-1);

    public void CycleTabBackward()
        => CycleTab(1);

    public void CycleTab(int offset)
    {
        int totalTabs = channels.Count + 1;
        activeTabIndex = (((activeTabIndex + 1 + offset) % totalTabs) + totalTabs) % totalTabs - 1;
    }

    public void SetActiveTabIndex(int index)
    {
        if (index < -1 || index >= channels.Count)
            return;
        activeTabIndex = index;
    }

    // Add to all tabs when channelId == null (system/announcement/private/local messages).
    // With a foldKey, a repeat within FoldWindowSeconds replaces the old line at the bottom
    // with foldedText and a bumped RepeatCount.
    public void AddChatMessage(
        ChatItem message,
        DateTime dateTime,
        int? channelId,
        string? foldKey = null,
        ChatText? foldedText = null
    )
    {
        if (foldKey is null || foldedText is null)
        {
            AppendEntry(message, channelId);
            return;
        }

        if (foldEntries.TryGetValue(foldKey, out var entry)
            && (dateTime - entry.LastTime).TotalSeconds <= FoldWindowSeconds)
        {
            RemoveEntry(entry.Item);
            var folded = new ChatItem(entry.FirstTime, foldedText)
            {
                RepeatCount = entry.Item.RepeatCount + 1
            };
            AppendEntry(folded, channelId);
            entry.Item = folded;
            entry.LastTime = dateTime;
            return;
        }

        AppendEntry(message, channelId);
        PruneFoldEntries(dateTime);
        foldEntries[foldKey] = new FoldEntry(message, dateTime);
    }

    private void AppendEntry(ChatItem message, int? channelId)
    {
        entries.Add(new ChatLogEntry(message, channelId));
        revision++;
    }

    private void RemoveEntry(ChatItem item)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(entries[i].Item, item))
            {
                entries.RemoveAt(i);
                revision++;
                return;
            }
        }
    }

    private List<ChatItem> GetAllView()
    {
        if (allViewRevision != revision)
        {
            allViewCache.Clear();
            foreach (var entry in entries)
                allViewCache.Add(entry.Item);
            allViewRevision = revision;
        }
        return allViewCache;
    }

    private List<ChatItem> GetGlobalView()
    {
        if (globalViewRevision != revision)
        {
            globalViewCache.Clear();
            foreach (var entry in entries)
            {
                if (entry.ChannelId is null
                    || !channelsById.TryGetValue(entry.ChannelId.Value, out var channel)
                    || channel.EnabledInGlobal)
                {
                    globalViewCache.Add(entry.Item);
                }
            }
            globalViewRevision = revision;
        }
        return globalViewCache;
    }

    private List<ChatItem> GetActiveChannelView()
    {
        if (activeViewRevision != revision || activeViewCachedTabIndex != activeTabIndex)
        {
            activeViewCache.Clear();
            int targetChannelId = channels[activeTabIndex].Id;
            foreach (var entry in entries)
            {
                if (entry.ChannelId is null || entry.ChannelId.Value == targetChannelId)
                    activeViewCache.Add(entry.Item);
            }
            activeViewRevision = revision;
            activeViewCachedTabIndex = activeTabIndex;
        }
        return activeViewCache;
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
        entries.Clear();
        channels.Clear();
        channelsById.Clear();
        foldEntries.Clear();
        activeTabIndex = -1;
        revision++;
    }

    public void CleanHistory()
    {
        entries.Clear();
        foldEntries.Clear();
        revision++;
    }
}
