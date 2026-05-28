using AsmResolver.DotNet.Builder;

namespace Celeste.Mod.ChatInputBox;

public class ChatTabManager
{
    private List<ChatTab> tabs = new();
    private int activeTabIndex;
    private readonly ITextRenderer textRenderer;
    
    public IReadOnlyList<ChatTab> Tabs => tabs;
    
    public int? ActiveTabIndex => activeTabIndex;
    public ChatTab ActiveTab => tabs[activeTabIndex];


    public void AddTab(string name, Func<ChatText, bool> filter)
    {
        tabs.Add(new ChatTab(name, filter));
    }

    public void RemoveTab(string name)
    {
        var targetTabIdx = tabs.FindIndex(t => t.Name == name);
        tabs.RemoveAt(targetTabIdx);
    }
    
    public void CycleTab()
    {
        activeTabIndex = (activeTabIndex + 1) %  tabs.Count;
    }

    public void SetActiveTab(string tabId)
    {
        activeTabIndex = tabs.FindIndex(t => t.Name == tabId);
    }
    
    public ChatTabManager(ITextRenderer textRenderer)                                                      
    {
        tabs.Add(new ChatTab("Global", null));
        activeTabIndex = 0;
        this.textRenderer = textRenderer;                                                                  
    }
    
    public void Render() 
    {           
        const float Margin = 16f;
        const float Padding = 8f;
        Vector2 baseLoc = new Vector2(Margin, Engine.Height - Margin - textRenderer.LineHeight * 1.5f);
        var curX = baseLoc.X;
        for (int i = 0; i < tabs.Count; i++)                                                               
        {                                                                                                  
            var tab = tabs[i];                                                                             
            float textWidth = textRenderer.Measure(tab.Name).X;                                     
            float tabWidth = textWidth + 2 * Padding;                                                      
            bool isActive = i == activeTabIndex;                                                           
                                                                                                         
            float bgAlpha = isActive ? 0.5f : 0.15f;                                                       
            Draw.Rect(MathF.Floor(curX), MathF.Floor(baseLoc.Y - textRenderer.LineHeight), MathF.Floor(tabWidth), textRenderer.LineHeight,Color.Black * bgAlpha);                                                                                    
              
            float textAlpha = isActive ? 1f : 0.5f;                                                        
            textRenderer.Draw(
                tab.Name,                                                                           
                new Vector2(curX + Padding, baseLoc.Y),                                  
                new Vector2(0f, 1f),                                                                       
                Color.White * textAlpha                                                                    
            );                                                                                             
                                                                                                         
            curX += tabWidth + 2f;                                                                         
        }       
    }
}