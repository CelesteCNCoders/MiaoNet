using System.Security.Cryptography.X509Certificates;

namespace MiaoNet.Server;

public sealed class LocalMiaoCertificateService : IMiaoCertificateService, IDisposable
{
    private readonly ReloadableResource<X509Certificate2> certificates;

    public LocalMiaoCertificateService()
    {
        using var certStream = typeof(MiaoCertificateService).Assembly.GetManifestResourceStream("localhost.pfx")!;
        byte[] certRawData = new byte[certStream.Length];
        certStream.ReadExactly(certRawData, 0, certRawData.Length);
        certificates = new ReloadableResource<X509Certificate2>(X509CertificateLoader.LoadPkcs12(certRawData, null));
    }

    public MiaoCertificateLease AcquireCertificate()
        => new(certificates.Acquire());

    public void Dispose()
        => certificates.Dispose();
}
