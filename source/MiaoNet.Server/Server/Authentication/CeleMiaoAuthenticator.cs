using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

// TODO simplify this
public sealed partial class CeleMiaoAuthenticator : IMiaoAuthenticator
{
    private readonly ILogger<CeleMiaoAuthenticator> logger;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly HttpClient httpClient;
    private readonly string clientID, clientSecret;

    private readonly SymmetricAlgorithm alg;

    public const string BaseAddress = "https://bbs.celemiao.com";
    public const string EndPointCodeAuth = "oauth/token";
    public const string EndPointAuth = "api/celeste/user?access_token=";

    public CeleMiaoAuthenticator(IOptions<MiaoServerOptions> options, ILogger<CeleMiaoAuthenticator> logger)
    {
        var authOptions = options.Value.Authentication;
        if (authOptions.ClientID is null || authOptions.ClientSecret is null || authOptions.EncryptionPassword is null)
        {
            throw new Exception("ClientID, ClientSecret and EncryptionPassword must be configured when using CeleMiaoAuthenticator.");
        }
        clientID = authOptions.ClientID;
        clientSecret = authOptions.ClientSecret;

        alg = GetAes(Encoding.UTF8.GetBytes(authOptions.EncryptionPassword), "MiaoNetServer.TokenSalt"u8.ToArray());

        this.logger = logger;
        jsonSerializerOptions = new()
        {
            // blame bbs
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        httpClient = new HttpClient();
        // TODO add version info
        string ua = "MiaoNet.Server.CeleMiaoAuthenticator";
        logger.LogInformation(AppEvents.Auth, "Using User-Agent \"{ua}\"", ua);
        httpClient.BaseAddress = new Uri(BaseAddress);
        httpClient.DefaultRequestHeaders.Add("User-Agent", ua);

        static Aes GetAes(byte[] password, byte[] salt)
        {
            var aes = Aes.Create();
            Span<byte> keyAndIV = stackalloc byte[32 + 16];
            Rfc2898DeriveBytes.Pbkdf2(password, salt, keyAndIV, 1000, HashAlgorithmName.SHA512);
            aes.Key = keyAndIV[0..32].ToArray();
            aes.IV = keyAndIV[32..(32 + 16)].ToArray();
            return aes;
        }
    }

    public async Task<AuthenticationResult> AuthenticateAsync(byte[] data, bool isAuthorize, CancellationToken token)
    {
        try
        {
            if (isAuthorize)
            {
                // data is a utf8 string of code here
                string authCode = Encoding.UTF8.GetString(data);
                var content = new
                {
                    client_id = clientID,
                    client_secret = clientSecret,
                    grant_type = "authorization_code",
                    code = authCode,
                    redirect_uri = "http://localhost:21472/auth"
                };
                var res = await httpClient.PostAsJsonAsync(EndPointCodeAuth, content, token);
                res.EnsureSuccessStatusCode();
                DateTime resTime = (res.Headers.Date ?? DateTimeOffset.UtcNow).DateTime;
                var json = await res.Content.ReadAsStringAsync(token);
                (BbsOAuth2TokenResult? tokenResult, BbsAuthErrorResult? errorResult) =
                    DeserializeOrError<BbsOAuth2TokenResult>(json, jsonSerializerOptions);

                if (tokenResult is not null)
                {
                    AuthenticationResult result = await AuthenticateByTokenAsync(tokenResult.AccessToken, token);

                    if (result.Type is AuthenticationResultType.Success)
                    {
                        TokenObject tokenObject = new(
                            tokenResult.AccessToken,
                            tokenResult.RefreshToken,
                            resTime + TimeSpan.FromSeconds(tokenResult.ExpiresIn)
                        );
                        var authData = RefBinarySerialization.Serialize(tokenObject, 80);
                        var encryptedData = alg.EncryptCbc(authData, alg.IV);
                        return new(result.Type, result.PlayerInfo, encryptedData);
                    }
                    else
                    {
                        return result;
                    }
                }
                else if (errorResult is not null)
                {
                    logger.LogWarning(AppEvents.Auth, "Auth failed bbs-side with error {err}. {msg}.", errorResult.Error, errorResult.ErrorDescription);
                    return new(AuthenticationResultType.InvalidTokenData, null, null);
                }
                else
                {
                    logger.LogWarning(AppEvents.Auth, "Bbs-side sent null.");
                    return new(AuthenticationResultType.InternalServerError, null, null);
                }
            }
            else
            {
                byte[] decryptedData;
                try
                {
                    decryptedData = alg.DecryptCbc(data, alg.IV);
                }
                catch (CryptographicException e)
                {
                    logger.LogWarning(e, "Failed to decrypt data.");
                    return new AuthenticationResult(AuthenticationResultType.InvalidTokenData);
                }
                TokenObject tokenObject = RefBinarySerialization.Deserialize<TokenObject>(decryptedData);
                // the access token is expired, refresh it
                if (DateTime.UtcNow > tokenObject.ExpiredDateTime)
                {
                    var content = new
                    {
                        client_id = clientID,
                        client_secret = clientSecret,
                        grant_type = "refresh_token",
                        refresh_token = tokenObject.RefreshToken,
                        redirect_uri = "http://localhost:21472/auth"
                    };
                    var res = await httpClient.PostAsJsonAsync(EndPointCodeAuth, content, token);
                    res.EnsureSuccessStatusCode();
                    DateTime resTime = (res.Headers.Date ?? DateTimeOffset.UtcNow).DateTime;
                    var json = await res.Content.ReadAsStringAsync(token);
                    (BbsOAuth2RefreshedTokenResult? tokenResult, BbsAuthErrorResult? errorResult) =
                        DeserializeOrError<BbsOAuth2RefreshedTokenResult>(json, jsonSerializerOptions);

                    if (tokenResult is not null)
                    {
                        AuthenticationResult result = await AuthenticateByTokenAsync(tokenResult.AccessToken, token);

                        if (result.Type is AuthenticationResultType.Success)
                        {
                            TokenObject newTokenObject = new(
                                tokenResult.AccessToken,
                                tokenObject.RefreshToken,
                                resTime + TimeSpan.FromSeconds(tokenResult.ExpiresIn)
                            );
                            var authData = RefBinarySerialization.Serialize(tokenObject, 80);
                            var encryptedData = alg.EncryptCbc(authData, alg.IV);
                            return new(result.Type, result.PlayerInfo, authData);
                        }
                        else
                        {
                            return result;
                        }
                    }
                    else if (errorResult is not null)
                    {
                        logger.LogWarning(AppEvents.Auth, "Auth failed bbs-side with error {err}. {msg}.", errorResult.Error, errorResult.ErrorDescription);
                        return new(AuthenticationResultType.InvalidTokenData, null, null);
                    }
                    else
                    {
                        logger.LogWarning(AppEvents.Auth, "Bbs-side sent null.");
                        return new(AuthenticationResultType.InternalServerError, null, null);
                    }
                }
                return await AuthenticateByTokenAsync(tokenObject.AccessToken, token);
            }
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Auth, e, "Exception occurred when authing.");
            return new AuthenticationResult(AuthenticationResultType.InternalServerError);
        }
    }

    // TODO using logger scopes
    private async Task<AuthenticationResult> AuthenticateByTokenAsync(string accessToken, CancellationToken token)
    {
        var res = await httpClient.GetAsync($"{EndPointAuth}{Uri.EscapeDataString(accessToken)}", token);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(token);
        (BbsAuthResult? result, BbsAuthErrorResult? errorResult)
            = DeserializeOrError<BbsAuthResult>(json, jsonSerializerOptions);

        if (result is not null)
        {
            // if they are banned
            if (result.SuspendedUntil is not null && result.SuspendedUntil.Value > DateTime.UtcNow)
            {
                logger.LogInformation(
                    AppEvents.Auth,
                    "{pn}:{id} is suspended due to {reason}, message: {msg}. Until {until}",
                    result.UserName, result.ID,
                    result.SuspendReason, result.SuspendMessage,
                    result.SuspendedUntil
                );
                return new AuthenticationResult(AuthenticationResultType.Suspended, result.SuspendMessage);
            }

            if (result.Color is not { Length: 7 } || !TryParseHexColor(result.Color.AsSpan(1), out Color color))
            {
                color = Color.White;
                logger.LogWarning(AppEvents.Auth, "Failed to parse color for player {name}, raw hex string: {hex}.", result.UserName, result.Color);
            }
            return new AuthenticationResult(
                AuthenticationResultType.Success,
                new PlayerInfo(result.ID, result.UserName, result.Prefix ?? string.Empty, result.AvatarUrl ?? string.Empty, color),
                null
            );
        }
        else if (errorResult is not null)
        {
            logger.LogWarning(AppEvents.Auth, "Auth failed bbs-side with error {err}. {msg}.", errorResult.Error, errorResult.ErrorDescription);
            return new(AuthenticationResultType.InvalidTokenData);
        }
        else
        {
            logger.LogWarning(AppEvents.Auth, "Bbs-side sent null.");
            return new(AuthenticationResultType.InternalServerError);
        }
    }

    private static (T?, BbsAuthErrorResult?) DeserializeOrError<T>(string json, JsonSerializerOptions options)
        where T : class
    {
        var jsonObject = JsonSerializer.Deserialize<JsonObject>(json, options);
        if (jsonObject is null)
            return (null, null);
        if (!jsonObject.ContainsKey("error"))
        {
            T result = JsonSerializer.Deserialize<T>(jsonObject, options)!;
            return (result, null);
        }
        else
        {
            BbsAuthErrorResult errorResult = JsonSerializer.Deserialize<BbsAuthErrorResult>(jsonObject, options)!;
            return (null, errorResult);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseHexColor(ReadOnlySpan<char> hexSpan, out Color color)
    {
        int r1 = GetHexVal(hexSpan[0]);
        int r2 = GetHexVal(hexSpan[1]);
        int g1 = GetHexVal(hexSpan[2]);
        int g2 = GetHexVal(hexSpan[3]);
        int b1 = GetHexVal(hexSpan[4]);
        int b2 = GetHexVal(hexSpan[5]);

        if ((r1 | r2 | g1 | g2 | b1 | b2) == -1)
        {
            color = default;
            return false;
        }

        byte r = (byte)((r1 << 4) | r2);
        byte g = (byte)((g1 << 4) | g2);
        byte b = (byte)((b1 << 4) | b2);

        color = new Color(r, g, b, 255);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1
    };
}
