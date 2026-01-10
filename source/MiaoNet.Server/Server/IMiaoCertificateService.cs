using System.Security.Cryptography.X509Certificates;

namespace MiaoNet.Server;

public interface IMiaoCertificateService
{
    public X509Certificate2 GetCertificate();
}
