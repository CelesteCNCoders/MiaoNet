namespace MiaoNet.Server;

public interface IMiaoCertificateService
{
    public MiaoCertificateLease AcquireCertificate();
}
