namespace Celeste.Mod.CelesteNet.DataTypes;

[Flags]
public enum DataFlags : ushort
{
    None = 0,
    Unreliable = 1,
    Taskable = 0x1000,
    Small = 0x2000,
    CoreType = 0x10,
    NoStandardMeta = 0x20
}
