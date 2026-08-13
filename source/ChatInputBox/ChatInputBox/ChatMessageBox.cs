namespace Celeste.Mod.ChatInputBox;

public class ChatMessageBox
{
    private readonly ChatMessageManager chatMessageManager;
    public ChatMessageListView ChatMessageListView;
    public ChatTabListView ChatTabListView;

    private bool active;
    public string? ActiveTabName => chatMessageManager.ActiveTabName;
    
    public ChatMessageBox(IScalelessTextRenderer textRenderer)
    {
        chatMessageManager = new();
        ChatMessageListView = new ChatMessageListView(chatMessageManager, textRenderer);
        ChatTabListView = new ChatTabListView(chatMessageManager, textRenderer);
    }
    
    public void AddChatMessage(ChatText message, string? tabName = null)
        => chatMessageManager.AddChatMessage(new(message), tabName);
    
    public void AddChatMessage(DateTime dateTime, ChatText message, string? tabName = null)
        => chatMessageManager.AddChatMessage(new(dateTime, message), tabName);
        

    public void CycleTab() => chatMessageManager.CycleTab();

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