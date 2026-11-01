using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Client.Components;

/// <summary>
/// The player list, which the shim stands in for with the parts mods bind to: the list a mod reads
/// (<see cref="Channels"/>), the row types it is handed, and the hook the original raises while it
/// builds those rows.
/// </summary>
/// <remarks>
/// The shim draws no list — MiaoNet draws the players in its own — so nothing here renders and no
/// row is ever built.
/// </remarks>
public class CelesteNetPlayerListComponent : CelesteNetGameComponent
{
    /// <summary>
    /// Raised by the original for every player it builds a row for, so that a mod can change what
    /// that row shows.
    /// </summary>
    /// <remarks>
    /// Never raised here, and deliberately so: no row is ever drawn, so what a handler writes into
    /// the blob it is handed would be read by nobody. The declaration still has to exist, because
    /// mods are compiled against it and a missing member is a load time failure — CollabUtils2
    /// subscribes to it from <c>LobbyHelper.OnInitialize</c>. What CollabUtils2 changes through it,
    /// the lobby icon of a collab map, MiaoNet's own player list already does itself
    /// (<c>CollabUtils2Interop.GetLobbyForMap</c>), so not calling it loses nothing.
    /// </remarks>
#pragma warning disable CS0067 // Never raised on purpose, see the remarks above.
    public static event Action<BlobPlayer, DataPlayerState>? OnGetState;
#pragma warning restore CS0067

    public DataChannelList? Channels;

    public CelesteNetPlayerListComponent(CelesteNetClientContext context, Game game) : base(context, game) { }

    /// <summary>
    /// The state half of the original's row types: the fields a mod reads or writes while it is
    /// holding one.
    /// </summary>
    /// <remarks>
    /// The rendering half of those types (the font, measuring and drawing, and the icon texture
    /// cache behind <see cref="BlobLocation.Icon"/>) has no counterpart here, because the shim
    /// draws nothing.
    /// </remarks>
    public class Blob
    {
        // Fields on purpose: the original type exposes these as fields and mods bind by kind.
        public string Name = "";
        public Color Color = Color.White;
    }

    public class BlobPlayer : Blob
    {
        public DataPlayerInfo? Player;
        public BlobLocation Location = new();

        public int? PingMs;
        public bool Idle;
    }

    public class BlobLocation : Blob
    {
        public string Level = "";
        public string Icon = "";
        public string Side = "";
        public string SID = "";
    }
}
