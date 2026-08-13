namespace MiaoNet.Server;

public sealed record SuspensionInfo(string PlayerName, string? Reason, string? Message, DateTime Until);
