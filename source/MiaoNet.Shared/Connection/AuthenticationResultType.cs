namespace MiaoNet.Shared;

public enum AuthenticationResultType
{
    Success,
    Suspended,
    LoginExpired,
    InvalidTokenData,
    InternalError
}