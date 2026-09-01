namespace Celeste.Mod.ChatInputBox;

public class ChatMessageBox
{
    private readonly ChatMessageManager chatMessageManager;
    public ChatMessageListView ChatMessageListView;
    public ChatTabListView ChatTabListView;

    private bool active;
    public string? ActiveTabName => chatMessageManager.ActiveTabName;

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

    public void AddChatMessage(ChatText message, string? tabName = null)
        => chatMessageManager.AddChatMessage(new(message), default, tabName);

    // foldedText is the line to show once folded, without the counter itself
    public void AddChatMessage(DateTime dateTime, ChatText message, string? tabName = null,
        string? foldKey = null, ChatText? foldedText = null)
        => chatMessageManager.AddChatMessage(new(dateTime, message), dateTime, tabName, foldKey, foldedText);


    public void CycleTabForward() => chatMessageManager.CycleTabForward();

    public void CycleTabBackward() => chatMessageManager.CycleTabBackward();

    public void AddTab(string name) => chatMessageManager.AddTab(name);

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
