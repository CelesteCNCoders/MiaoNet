namespace Celeste.Mod.ChatInputBox;

public class ChatTabListView
{

    private readonly ChatMessageManager _chatMessageManager;
    private readonly IScalelessTextRenderer textRenderer;

    private List<ChatTab> tabs => _chatMessageManager.Tabs;
    private int  activeTabIndex => _chatMessageManager.ActiveTabIndex;

    public ChatTabListView(ChatMessageManager chatMessageManager, IScalelessTextRenderer textRenderer)
    {
        this._chatMessageManager = chatMessageManager;
        this.textRenderer = textRenderer;
    }
    
    public void Render() 
    {           
        const float Margin = 16f;
        const float Padding = 8f;

        float inputBoxTopY = Engine.Height - Margin - textRenderer.LineHeight - Padding * 2f;
        Vector2 baseLoc = new Vector2(Margin, inputBoxTopY - Padding);
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