using System.Security.Cryptography.X509Certificates;

namespace MiaoNet.Server;

public sealed class LocalMiaoCertificateService : IMiaoCertificateService
{
    private readonly X509Certificate2 cert;

    public LocalMiaoCertificateService()
    {
        var certStream = typeof(MiaoCertificateService).Assembly.GetManifestResourceStream("localhost.pfx")!;
        byte[] certRawData = new byte[certStream.Length];
        certStream.ReadExactly(certRawData, 0, certRawData.Length);
        cert = X509CertificateLoader.LoadPkcs12(certRawData, null);
    }

    public X509Certificate2 GetCertificate() 
        => cert;
}
