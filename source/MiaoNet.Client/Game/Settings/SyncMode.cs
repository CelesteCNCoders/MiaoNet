namespace Celeste.Mod.MiaoNet;

[Flags]
public enum SyncMode
{
    None = 0b00,
    Receive = 0b01,
    Send = 0b10,
    Both = 0b11
}
