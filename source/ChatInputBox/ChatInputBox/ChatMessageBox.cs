namespace Celeste.Mod.ChatInputBox;

public class ChatMessageBox
{
    private readonly ChatMessageManager chatMessageManager;
    public ChatMessageListView ChatMessageListView;
    public ChatTabListView ChatTabListView;

    private bool active;

    public int ActiveTabIndex => chatMessageManager.ActiveTabIndex;

    public int? ActiveChannelId => chatMessageManager.ActiveChannelId;

    public string? ActiveTabName => chatMessageManager.ActiveTabName;

    public IReadOnlyList<ChatChannelTab> Channels => chatMessageManager.Channels;

    public double FoldWindowSeconds
    {
        get => chatMessageManager.FoldWindowSeconds;
        set => chatMessageManager.FoldWindowSeconds = value;
    }

    public ChatMessageBox(IScalelessTextRenderer textRenderer)
    {
        chatMessageManager = new();
        ChatMessageListView = new ChatMessageListView(chatMessageManager, textRenderer);
        ChatTabListView = new ChatTabListView(chatMessageManager, textRenderer);
    }

    public void AddChatMessage(ChatText message, int? channelId = null)
        => chatMessageManager.AddChatMessage(new(message), default, channelId);

    // foldedText is the line to show once folded, without the counter itself
    public void AddChatMessage(
        DateTime dateTime,
        ChatText message,
        int? channelId = null,
        string? foldKey = null,
        ChatText? foldedText = null
    )
        => chatMessageManager.AddChatMessage(new(dateTime, message), dateTime, channelId, foldKey, foldedText);

    public void CycleTabForward() => chatMessageManager.CycleTabForward();

    public void CycleTabBackward() => chatMessageManager.CycleTabBackward();

    public void SetActiveTabIndex(int index) => chatMessageManager.SetActiveTabIndex(index);

    public void AddChannel(int id, string title, bool enabledInGlobal = true)
        => chatMessageManager.AddChannel(id, title, enabledInGlobal);

    public bool ToggleChannelInGlobal(int channelIndex)
        => chatMessageManager.ToggleChannelInGlobal(channelIndex);

    public void SetChannelEnabledInGlobal(int channelId, bool enabled)
        => chatMessageManager.SetChannelEnabledInGlobal(channelId, enabled);

    public void CleanUp()
    {
        chatMessageManager.CleanUp();
        ChatMessageListView.CleanUp();
    }

    public void CleanHistory() => chatMessageManager.CleanHistory();

    public void Activate()
    {
        ChatMessageListView.Activate();
        active = true;
    }

    public void Deactivate()
    {
        ChatMessageListView.Deactivate();
        active = false;
    }

    public void Update() => ChatMessageListView.Update();

    public void Render()
    {
        ChatMessageListView.Render();
        if (active)
            ChatTabListView.Render();
    }
}
