namespace Celeste.Mod.MiaoNet;

public static class MentionBoundary
{
    public static bool IsMentionStart(string text, int index)
        => text[index] == '@';

    public static int WordEnd(string text, int start, int limit)
    {
        int end = start;
        while (end < limit && !char.IsWhiteSpace(text[end]))
            end++;
        return end;
    }

    /// <summary>
    /// Enumerates the mention spans in <paramref name="text"/> as <c>(start, end)</c>,
    /// where <c>end</c> is exclusive.
    /// </summary>
    public static IEnumerable<(int Start, int End)> Enumerate(string text, int from, int limit)
    {
        for (int i = from; i < limit; i++)
        {
            if (!IsMentionStart(text, i))
                continue;

            int end = WordEnd(text, i + 1, limit);
            if (end > i + 1)
                yield return (i, end);

            // The first '@' claims the whole word, whether the name is known or not.
            i = end - 1;
        }
    }
}
