using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

public sealed class MiaoCertificateService : BackgroundService, IMiaoCertificateService
{
    private readonly ILogger<MiaoCertificateService> logger;

    private readonly PeriodicTimer timer;

    private readonly string certPath;
    private readonly string keyPath;
    private DateTime lastCertModifiedTime;
    private DateTime lastKeyModifiedTime;

    private volatile X509Certificate2 cert;

    public MiaoCertificateService(ILogger<MiaoCertificateService> logger, IOptions<MiaoServerOptions> options)
    {
        var oc = options.Value.Certificate;
        if (oc.CertificatePath is null || oc.CertificateKeyPath is null)
        {
            throw new Exception("Certificate must be configured when using MiaoCertificateService.");
        }
        certPath = oc.CertificatePath;
        keyPath = oc.CertificateKeyPath;
        timer = new PeriodicTimer(TimeSpan.FromHours(4));
        this.logger = logger;
        lastCertModifiedTime = File.GetLastWriteTimeUtc(certPath);
        lastKeyModifiedTime = File.GetLastWriteTimeUtc(keyPath);
        Reload();
    }

    [MemberNotNull(nameof(cert))]
    private void Reload()
    {
        logger.LogInformation(AppEvents.Certificate, "Reloading certificate...");
        var newCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        cert = newCert;
        logger.LogInformation(
            AppEvents.Certificate,
            "Reloaded, not before: {a}, not after: {b}, name: {c}.",
            cert.NotBefore,
            cert.NotAfter,
            cert.SubjectName.Name
        );
    }

    public X509Certificate2 GetCertificate()
        => cert;

    private void CheckAndReload()
    {
        logger.LogInformation(AppEvents.Certificate, "Check if certificate is reload needed...");
        var certModifiedTime = File.GetLastWriteTimeUtc(certPath);
        var KeyModifiedTime = File.GetLastWriteTimeUtc(keyPath);
        if (lastCertModifiedTime != certModifiedTime || lastKeyModifiedTime != KeyModifiedTime)
        {
            logger.LogInformation(AppEvents.Certificate, "Certificate modified: cert: {cd}, key: {kd}.", certModifiedTime, KeyModifiedTime);
            lastCertModifiedTime = certModifiedTime;
            lastKeyModifiedTime = KeyModifiedTime;
            Reload();
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
}
