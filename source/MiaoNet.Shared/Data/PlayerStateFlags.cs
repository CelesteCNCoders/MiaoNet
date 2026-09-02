namespace MiaoNet.Shared;

[Flags]
public enum PlayerStateFlags : byte
{
    None = 0,
    FacingLeft = 1 << 0,
    Dashing = 1 << 1,
    StarFlying = 1 << 2,
    Interactions = 1 << 3,
    Ducking = 1 << 4,
    Tired = 1 << 5,
    Dead = 1 << 6,
    RedBoosted = 1 << 7
}
