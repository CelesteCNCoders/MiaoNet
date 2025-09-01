namespace MiaoNet.Shared;

[Flags]
public enum PacketFlags
{
    None = 0,
    PreferUdp = 1 << 0,
}