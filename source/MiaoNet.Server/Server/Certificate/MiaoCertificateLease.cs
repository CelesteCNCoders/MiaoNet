using System.Security.Cryptography.X509Certificates;

namespace MiaoNet.Server;

public sealed class MiaoCertificateLease : IDisposable
{
    private readonly ResourceLease<X509Certificate2> lease;

    internal MiaoCertificateLease(ResourceLease<X509Certificate2> lease)
        => this.lease = lease;

    public X509Certificate2 Certificate => lease.Value;

    public void Dispose()
        => lease.Dispose();
}
