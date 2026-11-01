using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.MiaoNetCelesteNetShim;

namespace Celeste.Mod.CelesteNet.Client;

/// <summary>
/// The data types which cannot do what they are for while they travel through this shim, each with
/// the reason, so that the transport can stop them instead of paying a frame for them.
/// <para>
/// A mod which sends one of these keeps its own view: the packet is still handed to its handler
/// when it asked for that, and only the wire is skipped. What a peer loses is exactly what the
/// reason names, so an entry only stays correct as long as its reason holds and has to be removed
/// together with it.
/// </para>
/// </summary>
internal static class CelesteNetCompatInertData
{
    /// <summary>
    /// Keyed by the wire id, which is what identifies a data type to the receiving side.
    /// </summary>
    private static readonly Dictionary<string, string> inert = new(StringComparer.Ordinal)
    {
        ["GravityHelper_PlayerGravity"] =
            "it is only ever read to invert the CelesteNet Ghost of the player who sent it — " +
            "GravityHelper matches the sender against the Ghost entities of the scene and the shim " +
            "creates none (MiaoNet draws remote players with its own entity) — and nothing else " +
            "carries a player's gravity across a MiaoNet session, not MiaoNet and not CoopHelper",
        ["SkinModHelperPlus/Data"] =
            "it is only ever read for the dash counter of a CelesteNet Ghost, and the shim creates " +
            "no Ghost (MiaoNet draws remote players with its own entity), so the lookup misses",
    };

    /// <summary>
    /// Says whether <paramref name="data"/> is one of them, and reports each of them once.
    /// <para>
    /// Once, because these are exactly the types which are sent every tick: the note that one of
    /// them is no longer travelling says everything there is to say about it, and saying it sixty
    /// times a second is the log flooding this list exists to stop.
    /// </para>
    /// </summary>
    internal static bool IsInert(DataType data, DataContext context)
    {
        string id = data.GetTypeID(context);
        if (!inert.TryGetValue(id, out string? reason))
            return false;

        CelesteNetCompatBackend.DiagOnce($"inert:{id}", $"{data.GetType().Name} ('{id}') is not sent: {reason}.");

        return true;
    }
}
