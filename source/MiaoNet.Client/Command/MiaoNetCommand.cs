namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetCommand
{
    /// <returns>Error string, or <see langword="null"/> if no error.</returns>
    public delegate string? ExecuteHandler(Context context);

    public record Segment(
        CommandSegmentType Type,
        string Name,
        string? Description
    );

    public string Name { get; }

    public string? Description { get; }

    public IReadOnlyCollection<string>? Aliases { get; }

    public IReadOnlyCollection<Segment> Segments { get; }

    public bool CaptureRestSegments { get; }

    public ExecuteHandler OnExecute { get; }

    public MiaoNetCommand(
        string name,
        string? description,
        IReadOnlyCollection<string>? aliases,
        IReadOnlyCollection<Segment> segments,
        bool captureRestSegments,
        ExecuteHandler onExecute
    )
    {
        Name = name;
        Description = description;
        Aliases = aliases;
        Segments = segments;
        CaptureRestSegments = captureRestSegments;
        OnExecute = onExecute;

        if (captureRestSegments && segments.Count == 0)
            throw new ArgumentException(SR.SegmentsEmptyButCapture, nameof(captureRestSegments));
    }
}