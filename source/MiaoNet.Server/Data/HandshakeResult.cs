using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed record HandshakeResult(PlayerInfo PlayerInfo, HandshakeData HandshakeData, HandshakeAckData HandshakeAckData);