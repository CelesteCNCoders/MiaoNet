namespace Celeste.Mod.ChatInputBox;

public readonly struct Completion
{
    public string Display { get; }

    public string Content { get; }

    public char Separator { get; }

    public bool CloseMenu { get; }

    public int Remove { get; }

    public Completion(string content, string display, int remove, char separator = ' ', bool closeMenu = false)
    {
        Content = content;
        Display = display;
        Separator = separator;
        CloseMenu = closeMenu;
        Remove = remove;
    }
}
