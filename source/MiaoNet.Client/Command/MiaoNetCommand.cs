namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetCommand
{
    /// <returns>Error string, or <see langword="null"/> if no error.</returns>
    public delegate string? ExecuteHandler(Context context);

    public string Name { get; }

    public IReadOnlyCollection<string>? Aliases { get; }

    public IReadOnlyCollection<CommandSegmentType> Segments { get; }

    public bool CaptureRestSegments { get; }

    public ExecuteHandler OnExecute { get; }

    public MiaoNetCommand(
        string name,
        IReadOnlyCollection<string>? aliases,
        IReadOnlyCollection<CommandSegmentType> segments,
        bool captureRestSegments,
        ExecuteHandler onExecute
    )
    {
        Name = name;
        Aliases = aliases;
        Segments = segments;
        CaptureRestSegments = captureRestSegments;
        OnExecute = onExecute;

        if (captureRestSegments && segments.Count == 0)
            throw new ArgumentException(SR.SegmentsEmptyButCapture, nameof(captureRestSegments));
    }
}