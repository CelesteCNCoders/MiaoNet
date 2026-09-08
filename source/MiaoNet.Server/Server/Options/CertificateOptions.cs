namespace MiaoNet.Server;

public sealed class CertificateOptions
{
    public required string CertificatePath { get; set; }

    public required string CertificateKeyPath { get; set; }

    public static bool IsConfigured(CertificateOptions options)
        => !string.IsNullOrWhiteSpace(options.CertificatePath)
            && !string.IsNullOrWhiteSpace(options.CertificateKeyPath);
}
