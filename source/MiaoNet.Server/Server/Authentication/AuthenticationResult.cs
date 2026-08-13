using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public readonly struct AuthenticationResult
{
    public AuthenticationResultType Type { get; }

    public string? SuspendMessage { get; }

    public SuspensionInfo? Suspension { get; }

    public PlayerInfo? PlayerInfo { get; }

    public byte[]? TokenData { get; }

    [MemberNotNullWhen(false, nameof(PlayerInfo))]
    public readonly bool IsFailed => PlayerInfo is null;

    public AuthenticationResult(AuthenticationResultType type)
    {
        Debug.Assert(type != AuthenticationResultType.Success);
        Type = type;
    }

    public AuthenticationResult(AuthenticationResultType type, SuspensionInfo suspension)
    {
        Debug.Assert(type == AuthenticationResultType.Suspended);
        Type = type;
        Suspension = suspension;
        SuspendMessage = suspension.Message;
    }

    public AuthenticationResult(AuthenticationResultType type, PlayerInfo? playerInfo, byte[]? tokenData)
    {
        if (type == AuthenticationResultType.Success)
            Debug.Assert(playerInfo is not null);
        else
            Debug.Assert(playerInfo is null);
        Type = type;
        PlayerInfo = playerInfo;
        TokenData = tokenData;
    }
}
