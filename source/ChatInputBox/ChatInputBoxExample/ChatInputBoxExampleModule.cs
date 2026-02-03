namespace Celeste.Mod.ChatInputBoxExample;

public sealed class ChatInputBoxExampleModule : EverestModule
{
    public static ChatInputBoxExampleModule Instance { get; private set; }

    public override void Load()
    {
        Instance = this;
        Everest.Events.LevelLoader.OnLoadingThread += LevelLoader_OnLoadingThread;
    }

    public override void Unload()
    {
        Everest.Events.LevelLoader.OnLoadingThread -= LevelLoader_OnLoadingThread;
    }

    private static void LevelLoader_OnLoadingThread(Level level)
    {
        level.Add(new ChatInputBoxEntity());
    }
}