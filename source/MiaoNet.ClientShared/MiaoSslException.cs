using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MiaoNet.ClientShared;

// AuthenticationException does not expose the certificate validation details,
// so preserve them for the client to present a useful connection error.
internal sealed class MiaoSslException : Exception
{
    public SslPolicyErrors SslPolicyErrors { get; }

    public X509ChainStatusFlags X509ChainStatusFlags { get; }

    public MiaoSslException(SslPolicyErrors errors, X509ChainStatusFlags x509ChainStatusFlags)
    {
        SslPolicyErrors = errors;
        X509ChainStatusFlags = x509ChainStatusFlags;
    }
}
