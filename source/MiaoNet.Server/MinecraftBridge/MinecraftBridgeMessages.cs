namespace MiaoNet.Server;

// DTOs for the /ws/minecraft JSON protocol.
// Outgoing messages are serialized from anonymous objects with lowercase names;
// these classes only model incoming (Paper -> server) messages.

public sealed class BridgeHelloMessage
{
    public string? ServerName { get; set; }

    public int Protocol { get; set; }
}

public sealed class BridgePlayerEntry
{
    public string Name { get; set; } = string.Empty;

    public string Dimension { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }
}

public sealed class BridgePlayersMessage
{
    public List<BridgePlayerEntry> Players { get; set; } = new();
}

public sealed class BridgeChatInMessage
{
    public string Player { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Pure diff between a session's current Minecraft player set (name -> dimension)
/// and an incoming full snapshot. Extracted for testability.
/// </summary>
public static class MinecraftPlayerListDiffer
{
    public static void Diff(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> incoming,
        out List<string> added,
        out List<string> removed,
        out List<string> dimensionChanged
    )
    {
        added = new();
        removed = new();
        dimensionChanged = new();

        foreach (var name in current.Keys)
        {
            if (!incoming.ContainsKey(name))
                removed.Add(name);
        }
        foreach (var (name, dimension) in incoming)
        {
            if (!current.TryGetValue(name, out string? currentDimension))
                added.Add(name);
            else if (!string.Equals(currentDimension, dimension, StringComparison.Ordinal))
                dimensionChanged.Add(name);
        }
    }
}
