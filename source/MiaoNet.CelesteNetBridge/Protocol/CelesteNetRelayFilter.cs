using System.Collections.Generic;

namespace MiaoNet.CelesteNetBridge.Protocol;

/// <summary>
/// Decides which frames the local real client produces are worth sending over MiaoNet.
/// </summary>
/// <remarks>
/// CelesteNet's own DataTypes describe the connection itself (handshake, tick rate, player info, ghosts),
/// and both ends of the bridge rebuild that state from scratch, so relaying them would duplicate it. A
/// mod's own DataTypes are the entire point of the bridge and always travel. Which is which is read from
/// the assembly that declares the type, so a CelesteNet update that adds a type needs no change here.
/// </remarks>
public static class CelesteNetRelayFilter
{
    /// <summary>Assemblies that declare CelesteNet's own DataTypes.</summary>
    public static readonly IReadOnlySet<string> UpstreamSources =
        new HashSet<string>(System.StringComparer.Ordinal) { "CelesteNet.Shared", "CelesteNet.Client" };

    /// <param name="typeId">The frame's type id.</param>
    /// <param name="dataSource">
    /// Assembly that declares the DataType, or null when the local client does not know the type at all.
    /// Unknown types are relayed, because dropping a frame nobody understands is the worse guess.
    /// </param>
    public static bool IsRelayable(string typeId, string? dataSource)
        => dataSource is null || !UpstreamSources.Contains(dataSource);
}
