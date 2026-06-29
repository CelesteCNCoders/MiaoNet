namespace MiaoNet.Server.GameScope;

public record MoveResult(
    IReadOnlyCollection<ServerPlayer> PreviousPeers,
    IReadOnlyCollection<ServerPlayer> NewPeers
);