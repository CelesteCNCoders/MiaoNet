using MiaoNet.Shared;

namespace MiaoNet.Server;

public interface IMiaoAuthenticator
{
    public Task<AuthenticationResult> AuthenticateAsync(byte[] authenticationData, bool isAuthorize, CancellationToken cancellationToken);
}
