namespace Celeste.Mod.MiaoNet;

internal readonly record struct CommandArgument(int Start, int End);

internal static class CommandArgumentSplitter
{
    public static IReadOnlyList<CommandArgument> Split(
        string text, int contentStart, int segmentCount, bool captureRest
    )
    {
        if (captureRest && segmentCount == 0)
            throw new ArgumentException(SR.SegmentsEmptyButCapture, nameof(captureRest));

        List<CommandArgument> args = [];
        int maxCount = captureRest ? segmentCount : int.MaxValue;
        int i = contentStart;

        while (args.Count < maxCount)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length)
                break;

            int start = i;
            int end;
            if (captureRest && args.Count == segmentCount - 1)
            {
                end = text.Length;
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                end = i;
            }

            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;

            if (end <= start)
            {
                break;
            }

            args.Add(new CommandArgument(start, end));
        }

        return args;
    }
}
