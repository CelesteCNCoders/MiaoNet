namespace MiaoNet.Shared;

public interface IPlayerListEntry
{
    public PlayerLocation Location { get; }

    public PlayerInfo PlayerInfo { get; }
}