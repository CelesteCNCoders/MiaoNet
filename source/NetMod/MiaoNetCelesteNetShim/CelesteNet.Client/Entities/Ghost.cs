using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Client.Entities;

/// <summary>
/// Stand-in for the original's remote player entity, which mods match on
/// (<c>if (entity is Ghost ghost)</c>) and read <see cref="PlayerInfo"/> from.
/// </summary>
/// <remarks>
/// The shim never creates one: MiaoNet draws remote players with its own entity, so under the shim
/// a player is never a <c>Ghost</c> and every match is false. The type still has to exist, because
/// a mod's own IL names it and a type that cannot be resolved is a fatal <see cref="TypeLoadException"/>
/// rather than a match which simply fails. SkinModHelperPlus is such a mod: it resolves the dashes
/// of the CelesteNet ghosts it finds, and the code path runs for MiaoNet's own ghosts too, because
/// those are built from a <c>PlayerSprite</c> whose mode is marked exactly like the original's.
/// </remarks>
[Tracked]
public class Ghost : Actor
{
    // Fields on purpose: the original type exposes these as fields and mods bind by kind.
    public CelesteNetClientContext? Context;
    public DataPlayerInfo? PlayerInfo;

    public Ghost() : base(Vector2.Zero) { }
}
