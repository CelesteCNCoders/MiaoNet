namespace MiaoNet.Server;

public sealed class AuthenticationOptions
{
    public required string ClientID { get; set; }

    public required string ClientSecret { get; set; }

    public required string EncryptionPassword { get; set; }

    public static bool IsConfigured(AuthenticationOptions options)
        => !string.IsNullOrWhiteSpace(options.ClientID)
            && !string.IsNullOrWhiteSpace(options.ClientSecret)
            && !string.IsNullOrWhiteSpace(options.EncryptionPassword);
}
