using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Celeste.Mod.MiaoNet;

#pragma warning disable CA1305

public static class ConnectionStatus
{
    private const string Base = "miaonet_connection_status_";

    public static string Connecting => Dialog.Get($"{Base}connecting");

    public static string VersionNotMatch(Version local, Version remote)
        => PFormat.Format(Dialog.Get($"{Base}version_not_match"), local.ToString(3), remote.ToString(3));

    public static string Authenticating => Dialog.Get($"{Base}authenticating");

    public static string DisconnectedExceptionally => Dialog.Get($"{Base}disconnected_exceptionally");

    public static string Connected => Dialog.Get($"{Base}connected");

    public static string Disconnected => Dialog.Get($"{Base}disconnected");

    public static string Cancelled => Dialog.Get($"{Base}cancelled");

    public static string InvalidTokenData => Dialog.Get($"{Base}invalid_token_data");

    public static string Suspended => Dialog.Get($"{Base}suspended");

    public static string InternalServerError => Dialog.Get($"{Base}internal_server_error");

    public static string ConnectFailedWithReason(string reason)
        => PFormat.Format(Dialog.Get($"{Base}connect_failed_with_reason"), reason);

    public static string ConnectionSslError(SslPolicyErrors sslPolicyErrors, X509ChainStatusFlags x509ChainStatusFlags)
        => PFormat.Format(Dialog.Get($"{Base}ssl_error"), sslPolicyErrors, x509ChainStatusFlags);

    public static string ConnectionSslRevocationCheckFailed
        => Dialog.Get($"{Base}revocation_check_failed");

    public static string DisconnectedWithReason(string reason)
        => PFormat.Format(Dialog.Get($"{Base}disconnected_exceptionally_with_reason"), reason);

    public static string DisconnectedWithLocalReason(string reason)
        => PFormat.Format(Dialog.Get($"{Base}disconnected_locally_exceptionally_with_reason"), reason);

    public static string Kicked(string reason)
        => PFormat.Format(Dialog.Get($"{Base}kicked"), reason);
}
