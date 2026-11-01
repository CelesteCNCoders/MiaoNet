using Celeste.Mod.CelesteNet.Client;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNetCelesteNetShim;

/// <summary>
/// Carries CelesteNet data over MiaoNet.
/// <para>
/// Everything CelesteNet travels in one carrier packet whose payload is the original CelesteNet
/// frame, so the shim never declares one MiaoNet packet type per data type: the data types stay the
/// mods' business and are identified by their <c>DataID</c> inside the payload, exactly as they are
/// in CelesteNet itself.
/// </para>
/// <para>
/// The declaration happens in <see cref="Load"/>, which the compatibility module calls from its own
/// <c>Load</c>: the handshake advertises the declarations, so they have to exist before the player
/// can connect. It also means nothing of MiaoNet's has to be constructed for the shim to be
/// reachable — a mod registers at load time and is done.
/// </para>
/// </summary>
internal static class CelesteNetCompatBackend
{
    /// <summary>
    /// The name of the net-mod, i.e. of the protocol two shims speak to each other.
    /// <para>
    /// It is the shim's own name on purpose. The compatibility module has to call itself
    /// <c>CelesteNet.Client</c> so that CelesteNet mods resolve their dependency against it, but
    /// that is a name in Everest's mod list, not on the wire: what travels here is the shim's
    /// format, a real CelesteNet client could make no sense of it, and naming it after CelesteNet
    /// would suggest otherwise.
    /// </para>
    /// </summary>
    internal const string NetModName = "MiaoNetCelesteNetShim";

    /// <summary>
    /// Which revision of that protocol this shim speaks. Peers are matched by name and this
    /// number, so it has to be bumped whenever an older shim could no longer read what a newer
    /// one sends.
    /// </summary>
    internal const ushort NetModProtocolVersion = 1;

    /// <summary>Log tag of the diagnostics below.</summary>
    internal const string DiagTag = "CelesteNetShim";

    /// <summary>TEMPORARY: set to <see langword="false"/> to silence the diagnostics.</summary>
    internal static bool DiagnosticsEnabled = true;

    /// <summary>TEMPORARY: diagnostics of the compatibility layer.</summary>
    internal static void Diag(string message)
    {
        if (DiagnosticsEnabled)
            Logger.Info(DiagTag, message);
    }

    /// <summary>
    /// The keys already said, shared by <see cref="DiagOnce"/> and <see cref="WarnOnce"/>: a key is
    /// one thing, whatever is said about it.
    /// </summary>
    private static readonly HashSet<string> said = new(StringComparer.Ordinal);

    /// <summary>
    /// Reports a diagnostic once per <paramref name="key"/>.
    /// <para>
    /// Everything on the path of a packet a mod sends or receives has to go through here. Those are
    /// the mods which send every tick — the ones worth watching — so a line placed there is worth
    /// sixty a second, and sixty lines a second is what buries the one line which was wanted. The
    /// first report carries the information; what a repeat adds is only that the same thing is
    /// still happening, which the first line already says.
    /// </para>
    /// </summary>
    internal static void DiagOnce(string key, string message)
    {
        if (said.Add(key))
            Diag(message);
    }

    /// <summary>
    /// Reports a failure once per <paramref name="key"/>, on the same terms as
    /// <see cref="DiagOnce"/>.
    /// <para>
    /// Deliberately not behind <see cref="DiagnosticsEnabled"/>: that switch silences what the
    /// compatibility layer has to say about itself, not what it has to say when it could not do
    /// its job.
    /// </para>
    /// </summary>
    internal static void WarnOnce(string key, string message)
    {
        if (said.Add(key))
            Logger.Warn(DiagTag, message);
    }

    /// <summary>
    /// The declaration itself, which is also what the shim sends through and disposes of: the
    /// registry hands the <see cref="NetMod"/> back from <see cref="MiaoNetNetMods.Register"/>,
    /// so there is no separate token or per-packet handle to keep.
    /// </summary>
    private static NetMod? netMod;

    /// <summary>
    /// Declares the net-mod, once. Called from the compatibility module's <c>Load</c>.
    /// <para>
    /// The declaration is the whole of the registration: the packet list is fixed here, its order
    /// is how the two peers number the packets, and nothing can be added afterwards.
    /// </para>
    /// <para>
    /// The scope is <see cref="PacketScope.Map"/>: a MiaoNet map is the closest thing to the set of
    /// players a CelesteNet server would have shown a mod, and every CelesteNet mod re-checks the
    /// room it cares about on its own anyway.
    /// </para>
    /// </summary>
    internal static void Load()
    {
        if (netMod is not null)
            return;

        netMod = MiaoNetNetMods.Register(new NetMod(
            NetModName,
            NetModProtocolVersion,
            NetModPacketEntry.Create<Carrier>(PacketScope.Map, OnReceived)
        ));

        Diag($"Net-mod '{NetModName}' v{NetModProtocolVersion} declared.");
    }

    /// <summary>
    /// Retires the declaration. MiaoNet cannot keep a session running across it — the peers still
    /// believe the old declaration — so MiaoNet closes one instead.
    /// </summary>
    internal static void Unload()
    {
        if (netMod is null)
            return;

        Diag($"Net-mod '{NetModName}' undeclared.");
        netMod.Dispose();
        netMod = null;
    }

    /// <summary>
    /// Hands one CelesteNet payload to the peers on the sender's map.
    /// <para>
    /// A drop is a normal outcome rather than a failure: without a session there is nobody to send
    /// to and the game carries on, which is what a mod sending every tick needs.
    /// </para>
    /// </summary>
    internal static void Send(byte[] payload)
    {
        NetMod? mod = netMod;
        if (mod is null || !MiaoNetNetMods.IsConnected)
        {
            DiagOnce("send:no-session", $"Dropped {payload.Length} byte(s): there is no MiaoNet session.");
            return;
        }

        mod.Send(new Carrier { Payload = payload });
    }

    /// <summary>
    /// A frame arrived. The server stamps the sender while relaying, so it is always there; the
    /// payload itself is decoded by the side which owns the data context.
    /// </summary>
    private static void OnReceived(PacketEnvelope envelope, Carrier packet)
        => CelesteNetCompatTransport.Receive(envelope.SenderPlayerID, packet.Payload);

    /// <summary>
    /// The only packet type the shim declares. Its payload is an opaque CelesteNet frame, so the
    /// frame header's own size field is the only length there is.
    /// <para>
    /// It carries no scope of its own: the scope belongs to the declaration, and
    /// <see cref="NetMod.Send"/> reads it back from there.
    /// </para>
    /// </summary>
    private sealed class Carrier : IContextualPacket<Carrier>
    {
        internal byte[] Payload = [];

        public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
            => writer.WriteSpan(Payload);

        public static Carrier Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
            => new() { Payload = reader.ReadSpan(reader.BytesLeft).ToArray() };
    }
}
