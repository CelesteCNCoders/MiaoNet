
namespace MiaoNet.Shared;

public sealed class CommandAnnounce : MiaoNetCommand
{
    private static readonly IReadOnlyCollection<SegmentDescription> segments
        = [new("Text", "Text to announce.", CommandSegmentType.Text)];

    public override string Name => "announce";

    public override string Description => "Announce some messages.";

    public override IReadOnlyCollection<SegmentDescription> Segments => segments;
}