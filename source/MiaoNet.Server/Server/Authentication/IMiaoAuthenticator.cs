using MiaoNet.Shared;

namespace MiaoNet.Server;

public interface IMiaoAuthenticator
{
    public Task<AuthenticationResult> AuthenticateAsync(byte[] codeData, AuthenticationType authenticationType, CancellationToken cancellationToken);
}
