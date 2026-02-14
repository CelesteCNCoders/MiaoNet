using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class CustomAuthenticator : IMiaoAuthenticator
{
    public Task<AuthenticationResult> AuthenticateAsync(byte[] authenticationData, AuthenticationType authenticationType, CancellationToken cancellationToken)
    {
        // authenticationData is a PlayerInfo here
        RefBinaryReader reader = new(authenticationData);
        PlayerInfo info = reader.Read<PlayerInfo>();

        return Task.FromResult(new AuthenticationResult(AuthenticationResultType.Success, info, null));
    }
}
