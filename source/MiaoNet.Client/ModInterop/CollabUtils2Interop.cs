#pragma warning disable CA2211

using MonoMod.ModInterop;

namespace Celeste.Mod.MiaoNet;

[ModImportName("CollabUtils2.LobbyHelper")]
public static class CollabUtils2Interop
{
    public static Func<string, string>? GetLobbyForMap;
}