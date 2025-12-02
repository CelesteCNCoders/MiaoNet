namespace MiaoNet.Shared;

/// <summary>
/// Used to specify parts of command args, and also parts of command results
/// </summary>
public enum CommandSegmentType : byte
{
    Text,
    Boolean,
    Player,
    Channel
}