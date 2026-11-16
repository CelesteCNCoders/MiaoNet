namespace MiaoNet.Shared;

public enum PacketScope : byte
{
    Global = 1,
    Channel = 2,
    Map = 3,
    Player = 4,
    Players = 5
}

internal static class PacketScopeExtensions
{
    public static bool IsBroadcast(this PacketScope scope)
        => scope is PacketScope.Global or PacketScope.Channel or PacketScope.Map;
}
