namespace MiaoNet.Shared;

[Flags]
public enum PlayerGlobalFlags : ushort
{
    None,
    Paused = 1 << 0,
    Typing = 1 << 1,
    LiveMode = 1 << 2,
    Interactions = 1 << 3,
    TakingGolden = 1 << 4,
    GroupPhotoMode = 1 << 5
}