namespace MiaoNet.Server;

/// <summary>
/// Where chat coming from Minecraft is relayed to.
/// </summary>
public enum MinecraftRelayScope
{
    /// <summary>Every MiaoNet client sees Minecraft chat.</summary>
    Global,

    /// <summary>Only clients inside the Minecraft channel see Minecraft chat.</summary>
    Channel
}

public sealed class MinecraftBridgeOptions
{
    public bool Enabled { get; set; } = false;

    public string ChannelName { get; set; } = "Minecraft";

    public MinecraftRelayScope RelayScope { get; set; } = MinecraftRelayScope.Global;
}
