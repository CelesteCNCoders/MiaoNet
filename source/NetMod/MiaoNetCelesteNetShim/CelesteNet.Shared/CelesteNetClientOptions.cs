namespace Celeste.Mod.CelesteNet;

/// <summary>
/// The original type lives in the shared assembly under <c>Celeste.Mod.CelesteNet</c>,
/// not next to the client module, and exposes these as fields.
/// </summary>
public class CelesteNetClientOptions
{
    public bool IsReconnect;
    public bool AvatarsDisabled;
    public ulong ClientID;
    public uint InstanceID;
    public CelesteNetSupportedClientFeatures SupportedClientFeatures = CelesteNetSupportedClientFeatures.None;
}

[Flags]
public enum CelesteNetSupportedClientFeatures : ulong
{
    None = 0,
    LocateCommand = 1 << 0
}
