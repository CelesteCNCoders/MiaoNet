namespace MiaoNet.Server;

public sealed class AuthenticationOptions
{
    public string? ClientID { get; set; }

    public string? ClientSecret { get; set; }

    public string? SignatureKey { get; set; }
}
