using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MiaoNet.ClientShared;

// unluckly we can't get anything from AuthenticationException
// so we need to handle these manually...
internal class MiaoSslException : Exception
{
    public SslPolicyErrors SslPolicyErrors { get; }

    public X509ChainStatusFlags X509ChainStatusFlags { get; }

    public MiaoSslException(SslPolicyErrors errors, X509ChainStatusFlags x509ChainStatusFlags)
    {
        SslPolicyErrors = errors;
        X509ChainStatusFlags = x509ChainStatusFlags;
    }
}
