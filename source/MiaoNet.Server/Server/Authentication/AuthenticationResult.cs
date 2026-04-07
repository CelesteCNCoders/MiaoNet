using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public readonly struct AuthenticationResult
{
    public AuthenticationResultType Type { get; }

    public string? SuspendMessage { get; }

    public PlayerInfo? PlayerInfo { get; }

    public byte[]? TokenData { get; }

    [MemberNotNullWhen(false, nameof(PlayerInfo))]
    public readonly bool IsFailed => PlayerInfo is null;

    public AuthenticationResult(AuthenticationResultType type)
    {
        Debug.Assert(type != AuthenticationResultType.Success);
        Type = type;
    }

    public AuthenticationResult(AuthenticationResultType type, string? suspendMessage)
    {
        Debug.Assert(type == AuthenticationResultType.Suspended);
        Type = type;
        SuspendMessage = suspendMessage;
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
