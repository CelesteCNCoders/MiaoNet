namespace Celeste.Mod.ChatInputBox;

public readonly struct Completion
{
    public string Display { get; }

    public string Content { get; }

    public int Remove { get; }

    public Completion(string content, string display, int remove)
    {
        Content = content;
        Display = display;
        Remove = remove;
    }
}