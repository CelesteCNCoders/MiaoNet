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

public sealed partial class CeleMiaoAuthenticator : IMiaoAuthenticator
{
    private readonly ILogger<CeleMiaoAuthenticator> logger;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly HttpClient httpClient;
    private readonly string clientID, clientSecret;
    private readonly byte[] signatureKey;

    public const string BaseAddress = "https://bbs.celemiao.com";
    public const string EndPointCodeAuth = "oauth/token";
    public const string EndPointAuth = "api/celeste/user?access_token=";

    public CeleMiaoAuthenticator(IOptions<MiaoServerOptions> options, ILogger<CeleMiaoAuthenticator> logger)
    {
        var authOptions = options.Value.Authentication;
        if (authOptions.ClientID is null || authOptions.ClientSecret is null || authOptions.SignatureKey is null)
        {
            throw new Exception("ClientID, ClientSecret and SignatureKey must be configured when using CeleMiaoAuthenticator.");
        }
        clientID = authOptions.ClientID;
        clientSecret = authOptions.ClientSecret;
        signatureKey = Encoding.UTF8.GetBytes(authOptions.SignatureKey);
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
    }

    public async Task<AuthenticationResult> AuthenticateAsync(byte[] data, AuthenticationType type, CancellationToken token)
    {
        try
        {
            switch (type)
            {
            case AuthenticationType.Authorize:
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
                var json = await res.Content.ReadAsStringAsync(token);
                (BbsOAuth2TokenResult? tokenResult, BbsAuthErrorResult? errorResult) =
                    DeserializeOrError<BbsOAuth2TokenResult>(json, jsonSerializerOptions);

                if (tokenResult is not null)
                {
                    (AuthenticationResultType resultType, PlayerInfo? playerInfo)
                        = await FetchPlayerInfoByAuthTokenAsync(tokenResult.AccessToken, tokenResult.RefreshToken, token);

                    if (resultType is not AuthenticationResultType.Success)
                        return new(resultType, null, null);

                    byte[] signature = Sign(playerInfo!, tokenResult.AccessToken, tokenResult.RefreshToken);
                    TokenObject tokenObject = new(playerInfo!, tokenResult.AccessToken, tokenResult.RefreshToken, signature);

                    MemoryStream ms = new(128);
                    RefBinaryWriter writer = new(ms);
                    writer.Write(tokenObject);

                    return new(resultType, playerInfo, ms.GetBuffer().AsSpan()[..(int)ms.Position].ToArray());
                }
                else if (errorResult is not null)
                {
                    logger.LogWarning(AppEvents.Auth, "Auth failed bbs-side with error {err}. {msg}.", errorResult.Error, errorResult.ErrorDescription);
                    return new(AuthenticationResultType.InvalidTokenData, null, null);
                }
                else
                {
                    logger.LogWarning(AppEvents.Auth, "Bbs-side sent null.");
                    return new(AuthenticationResultType.InternalError, null, null);
                }
            }
            case AuthenticationType.QuickLogin:
            {
                // TODO
                goto case AuthenticationType.SyncRefresh;
            }
            case AuthenticationType.SyncRefresh:
            {
                // data is a TokenObject here
                TokenObject tokenObject;
                RefBinaryReader reader = new RefBinaryReader(data);
                tokenObject = reader.Read<TokenObject>();

                bool signatureMatch = VerifySignature(tokenObject);
                if (!signatureMatch)
                    return new(AuthenticationResultType.InvalidTokenData, null, null);

                (AuthenticationResultType resultType, PlayerInfo? playerInfo)
                    = await FetchPlayerInfoByAuthTokenAsync(tokenObject.AccessToken, tokenObject.RefreshToken, token);
                return new AuthenticationResult(resultType, playerInfo, null);
            }
            }
            logger.LogWarning(AppEvents.Auth, "Unknown auth type {v}.", type);
            return new AuthenticationResult(AuthenticationResultType.InvalidTokenData);
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Auth, e, "Exception occurred when authing.");
            return new AuthenticationResult(AuthenticationResultType.InternalError);
        }
    }

    // TODO use refreshToken
    private async Task<(AuthenticationResultType, PlayerInfo?)> FetchPlayerInfoByAuthTokenAsync(string accessToken, string refreshToken, CancellationToken token)
    {
        var res = await httpClient.GetAsync($"{EndPointAuth}{Uri.EscapeDataString(accessToken)}", token);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(token);
        (BbsAuthResult? result, BbsAuthErrorResult? errorResult)
            = DeserializeOrError<BbsAuthResult>(json, jsonSerializerOptions);

        if (result is not null)
        {
            if (result.Color is not { Length: 7 } || !TryParseHexColor(result.Color.AsSpan(1), out Color color))
            {
                color = Color.White;
                logger.LogWarning(AppEvents.Auth, "Failed to parse color for player {name}, raw hex string: {hex}.", result.Username, result.Color);
            }
            return (AuthenticationResultType.Success, new(result.Username, result.Prefix ?? string.Empty, result.AvatarUrl ?? string.Empty, color));
        }
        else if (errorResult is not null)
        {
            // TODO return expiration info here
            logger.LogWarning(AppEvents.Auth, "Auth failed bbs-side with error {err}. {msg}.", errorResult.Error, errorResult.ErrorDescription);
            return (AuthenticationResultType.InvalidTokenData, null);
        }
        else
        {
            logger.LogWarning(AppEvents.Auth, "Bbs-side sent null.");
            return (AuthenticationResultType.InternalError, null);
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

    private byte[] Sign(PlayerInfo playerInfo, string accessToken, string refreshToken)
    {
        MemoryStream ms = new(128);
        RefBinaryWriter writer = new(ms);
        writer.Write(playerInfo);
        writer.Write(accessToken);
        writer.Write(refreshToken);
        return HMACSHA256.HashData(signatureKey, ms.GetBuffer().AsSpan()[..(int)ms.Position]);
    }

    private bool VerifySignature(TokenObject tokenObject)
    {
        byte[] signature = Sign(tokenObject.PlayerInfo, tokenObject.AccessToken, tokenObject.RefreshToken);
        return CryptographicOperations.FixedTimeEquals(signature, tokenObject.Signature);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryParseHexColor(ReadOnlySpan<char> hexSpan, out Color color)
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
    static int GetHexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1
    };
}
