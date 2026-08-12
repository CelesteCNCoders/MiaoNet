using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

public sealed class MiaoCertificateService : BackgroundService, IMiaoCertificateService
{
    private readonly ILogger<MiaoCertificateService> logger;
    private readonly PeriodicTimer timer;
    private readonly ReloadableResource<X509Certificate2> certificates;

    private readonly string certPath;
    private readonly string keyPath;
    private DateTime lastCertModifiedTime;
    private DateTime lastKeyModifiedTime;

    public MiaoCertificateService(ILogger<MiaoCertificateService> logger, IOptions<MiaoServerOptions> options)
    {
        var oc = options.Value.Certificate;
        if (oc.CertificatePath is null || oc.CertificateKeyPath is null)
        {
            throw new Exception("Certificate must be configured when using MiaoCertificateService.");
        }
        certPath = oc.CertificatePath;
        keyPath = oc.CertificateKeyPath;
        this.logger = logger;
        lastCertModifiedTime = File.GetLastWriteTimeUtc(certPath);
        lastKeyModifiedTime = File.GetLastWriteTimeUtc(keyPath);
        certificates = new ReloadableResource<X509Certificate2>(LoadCertificate());
        timer = new PeriodicTimer(TimeSpan.FromHours(4));
    }

    private X509Certificate2 LoadCertificate()
    {
        logger.LogInformation(AppEvents.Certificate, "Reloading certificate...");
        var newCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        logger.LogInformation(
            AppEvents.Certificate,
            "Reloaded, not before: {a}, not after: {b}, name: {c}.",
            newCert.NotBefore,
            newCert.NotAfter,
            newCert.SubjectName.Name
        );
        return newCert;
    }

    public MiaoCertificateLease AcquireCertificate()
        => new(certificates.Acquire());

    private void CheckAndReload()
    {
        logger.LogInformation(AppEvents.Certificate, "Check if certificate is reload needed...");
        var certModifiedTime = File.GetLastWriteTimeUtc(certPath);
        var keyModifiedTime = File.GetLastWriteTimeUtc(keyPath);
        if (lastCertModifiedTime != certModifiedTime || lastKeyModifiedTime != keyModifiedTime)
        {
            logger.LogInformation(AppEvents.Certificate, "Certificate modified: cert: {cd}, key: {kd}.", certModifiedTime, keyModifiedTime);
            lastCertModifiedTime = certModifiedTime;
            lastKeyModifiedTime = keyModifiedTime;
            var newCertificate = LoadCertificate();
            try
            {
                certificates.Replace(newCertificate);
            }
            catch
            {
                newCertificate.Dispose();
                throw;
            }
        }
        else
        {
            logger.LogInformation(AppEvents.Certificate, "No need to reload certificate.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await timer.WaitForNextTickAsync(stoppingToken))
            CheckAndReload();
    }

    public override void Dispose()
    {
        base.Dispose();
        timer.Dispose();
        certificates.Dispose();
    }
}
