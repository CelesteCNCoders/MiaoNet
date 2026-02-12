namespace MiaoNet.Shared;

public enum AuthenticationResultType
{
    Success,
    LoginExpired,
    InvalidTokenData,
    InternalError
}