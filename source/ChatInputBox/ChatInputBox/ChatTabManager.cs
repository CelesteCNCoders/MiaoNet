using AsmResolver.DotNet.Builder;

namespace Celeste.Mod.ChatInputBox;

public class ChatTabManager
{
    private List<ChatTab> tabs;
    private int activeTabIndex;
    private readonly ITextRenderer textRenderer;
    
    public IReadOnlyList<ChatTab> Tabs => tabs;
    
    public ChatTab? ActiveTab => activeTabIndex < 0 ? null : tabs[activeTabIndex];
    public bool ShowAll => activeTabIndex < 0;
    
    public ChatTabManager(ITextRenderer textRenderer)
    {
        tabs = new();
        activeTabIndex = -1;
        this.textRenderer = textRenderer;                                                                  
    }

    private ChatTab GetOrAddTab(string name)
    {
        var targetTabIdx = tabs.FindIndex(t => t.Name == name);
        if (targetTabIdx < 0)
        {
            tabs.Add(new ChatTab(name));
            targetTabIdx = tabs.Count - 1;
        }
        return tabs[targetTabIdx];
    }
    public void AddTab(string name)
    {
        tabs.Add(new ChatTab(name));
    }

    public void RemoveTab(string name)
    {
        var targetTabIdx = tabs.FindIndex(t => t.Name == name);
        tabs.RemoveAt(targetTabIdx);
        if (name == ActiveTab?.Name) activeTabIndex %= tabs.Count;
    }
    
    public void CycleTab()
    {
        activeTabIndex = (activeTabIndex + 2) % (tabs.Count + 1) - 1;
    }

    public void SetActiveTab(string name)
    {
        var targetTabIndex = tabs.FindIndex(t => t.Name == name);
        if  (targetTabIndex < 0) return;
        activeTabIndex = targetTabIndex;
    }

    // Add to all Tabs while tabName == null (For Local Announcement）
    public void AddChatItem(ChatItem chatItem, string? tabName)
    {
        if (tabName == null)
        {
            foreach (var chatTab in tabs)
            {
                chatTab.AddChatItem(chatItem);
            }

            return;
        } 
        var tab = GetOrAddTab(tabName);
        tab.AddChatItem(chatItem);
    }

    public void CleanUp()
    {
        tabs.Clear();
        activeTabIndex = -1;
    }

    public void CleanHistory()
    {
        foreach (var chatTab in tabs)
        {
            chatTab.CleanUp();
        }
    }

    public void Render() 
    {           
        const float Margin = 16f;
        const float Padding = 8f;
        Vector2 baseLoc = new Vector2(Margin, Engine.Height - Margin - textRenderer.LineHeight * 1.5f);
        var curX = baseLoc.X;
        
        for (int i = -1; i < tabs.Count; i++)                                                               
        {                                                                                                  
            var title = i == -1 ? "ALL" : tabs[i].Name;                                                                          
            float textWidth = textRenderer.Measure(title).X;                                     
            float tabWidth = textWidth + 2 * Padding;                                                      
            bool isActive = i == activeTabIndex;                                                           
                                                                                                         
            float bgAlpha = isActive ? 0.5f : 0.15f;                                                       
            Draw.Rect(MathF.Floor(curX), MathF.Floor(baseLoc.Y - textRenderer.LineHeight), MathF.Floor(tabWidth), textRenderer.LineHeight,Color.Black * bgAlpha);                                                                                    
              
            float textAlpha = isActive ? 1f : 0.5f;                                                        
            textRenderer.Draw(
                title,                                                                           
                new Vector2(curX + Padding, baseLoc.Y),                                  
                new Vector2(0f, 1f),                                                                       
                Color.White * textAlpha                                                                    
            );                                                                                             
                                                                                                         
            curX += tabWidth + 2f;                                                                         
        }       
    }
}