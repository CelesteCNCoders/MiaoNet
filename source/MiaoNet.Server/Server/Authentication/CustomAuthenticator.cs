using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class CustomAuthenticator : IMiaoAuthenticator
{
    public async Task<AuthenticationResult> AuthenticateAsync(byte[] authenticationData, bool isAuthorize, CancellationToken cancellationToken)
    {
        // authenticationData is a PlayerInfo here
        PlayerInfo info = RefBinarySerialization.Deserialize<PlayerInfo>(authenticationData);

        return new AuthenticationResult(AuthenticationResultType.Success, info, null);
    }
}
