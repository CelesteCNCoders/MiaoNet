namespace MiaoNet.Shared;

public static class NameValidator
{
    // Intentionally limited to whitespace and control characters,
    // which break command parsing, tokenization and mention matching.
    // Other format characters are currently still allowed.
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                return false;
        }
        return true;
    }
}
