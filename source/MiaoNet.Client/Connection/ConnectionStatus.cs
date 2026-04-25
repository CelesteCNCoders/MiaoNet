using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Celeste.Mod.MiaoNet;

public static class ConnectionStatus
{
    private static string Base => "miaonet_connection_status_{0}";

    public static string Connecting => Dialog.Get(string.Format(Base, "connecting"));

    public static string VersionNotMatch(Version local, Version remote)
        => PFormat.Format(Dialog.Get(string.Format(Base, "version_not_match")), local.ToString(3), remote.ToString(3));

    public static string Authenticating => Dialog.Get(string.Format(Base, "authenticating"));

    public static string DisconnectedExceptionally => Dialog.Get(string.Format(Base, "disconnected_exceptionally"));

    public static string Connected => Dialog.Get(string.Format(Base, "connected"));

    public static string Disconnected => Dialog.Get(string.Format(Base, "disconnected"));

    public static string Cancelled => Dialog.Get(string.Format(Base, "cancelled"));

    public static string InvalidTokenData => Dialog.Get(string.Format(Base, "invalid_token_data"));

    public static string InternalServerError => Dialog.Get(string.Format(Base, "internal_server_error"));

    public static string ConnectFailedWithReason(string reason)
        => PFormat.Format(Dialog.Get(string.Format(Base, "connect_failed_with_reason")), reason);

    public static string ConnectionSslError(SslPolicyErrors sslPolicyErrors, X509ChainStatusFlags x509ChainStatusFlags)
        => PFormat.Format(Dialog.Get(string.Format(Base, "ssl_error")), sslPolicyErrors, x509ChainStatusFlags);

    public static string ConnectionSslRevocationCheckFailed
        => Dialog.Get(string.Format(Base, "revocation_check_failed"));

    public static string DisconnectedWithReason(string reason)
        => PFormat.Format(Dialog.Get(string.Format(Base, "disconnected_exceptionally_with_reason")), reason);

    public static string DisconnectedWithLocalReason(string reason)
        => PFormat.Format(Dialog.Get(string.Format(Base, "disconnected_locally_exceptionally_with_reason")), reason);

    public static string Kicked(string reason)
        => PFormat.Format(Dialog.Get(string.Format(Base, "kicked")), reason);
}
