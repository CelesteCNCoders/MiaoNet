namespace Celeste.Mod.MiaoNet;

/// <summary>Which set of players a player-typed command argument may name.</summary>
public enum PlayerScope : byte
{
    Any,
    SameChannel,
    SameMap
}

public static class PlayerScopeExtensions
{
    /// <summary>Maps a player segment type onto its scope.</summary>
    public static PlayerScope ToPlayerScope(this CommandSegmentType segmentType)
        => segmentType switch
        {
            CommandSegmentType.Player => PlayerScope.Any,
            CommandSegmentType.PlayerSameChannel => PlayerScope.SameChannel,
            CommandSegmentType.PlayerSameMap => PlayerScope.SameMap,
            _ => throw new ArgumentOutOfRangeException(
                nameof(segmentType), segmentType, "Not a player segment type.")
        };
}
