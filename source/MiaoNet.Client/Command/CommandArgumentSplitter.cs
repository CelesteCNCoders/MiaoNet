namespace Celeste.Mod.MiaoNet;

public readonly record struct CommandArgument(int Start, int End);

public static class CommandArgumentSplitter
{
    public static IReadOnlyList<CommandArgument> Split(
        string text, int contentStart, int segmentCount, bool captureRest
    )
    {
        List<CommandArgument> args = [];
        bool canCaptureRest = captureRest && segmentCount > 0;
        // A rest argument only consumes the declared segment count,
        // so with none declared there is nothing to capture and nothing is split.
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
            if (canCaptureRest && args.Count == segmentCount - 1)
            {
                // The rest argument is free text, so it spans to the end of the input.
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
                // The rest argument is empty, so there is nothing left to consume.
                break;
            }

            args.Add(new CommandArgument(start, end));
        }

        return args;
    }
}
