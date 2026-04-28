namespace MiaoNet.Shared;

public interface IPlayerListEntry
{
    public PlayerLocation Location { get; }

    public bool IsLocallyKnownMap { get; }

    public PlayerInfo PlayerInfo { get; }
}