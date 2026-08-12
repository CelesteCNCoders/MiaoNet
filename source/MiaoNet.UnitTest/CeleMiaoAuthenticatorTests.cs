using System.Net;
using System.Text;
using System.Text.Json;
using MiaoNet.Server;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class CeleMiaoAuthenticatorTests
{
    [TestMethod]
    public async Task RefreshedTokenDataCanAuthenticateAgainWithoutAnotherRefresh()
    {
        var handler = new OAuthHandler();
        using var httpClient = new HttpClient(handler);
        var authenticator = new CeleMiaoAuthenticator(
            CreateOptions(),
            NullLogger<CeleMiaoAuthenticator>.Instance,
            httpClient
        );

        AuthenticationResult initialResult = await authenticator.AuthenticateAsync(
            Encoding.UTF8.GetBytes("authorization-code"),
            isAuthorize: true,
            CancellationToken.None
        );

        Assert.AreEqual(AuthenticationResultType.Success, initialResult.Type);
        Assert.IsNotNull(initialResult.TokenData);

        AuthenticationResult refreshResult = await authenticator.AuthenticateAsync(
            initialResult.TokenData,
            isAuthorize: false,
            CancellationToken.None
        );

        Assert.AreEqual(AuthenticationResultType.Success, refreshResult.Type);
        Assert.IsNotNull(refreshResult.TokenData);
        CollectionAssert.AreNotEqual(initialResult.TokenData, refreshResult.TokenData);

        AuthenticationResult reconnectResult = await authenticator.AuthenticateAsync(
            refreshResult.TokenData,
            isAuthorize: false,
            CancellationToken.None
        );

        Assert.AreEqual(AuthenticationResultType.Success, reconnectResult.Type);
        Assert.AreEqual("refreshed-access-token", handler.LastAuthenticatedAccessToken);
        Assert.AreEqual(1, handler.RefreshRequestCount);
    }

    private static IOptions<MiaoServerOptions> CreateOptions()
    {
        var announcements = new AnnouncementsStrings(string.Empty, string.Empty, string.Empty);
        return Options.Create(new MiaoServerOptions
        {
            Authentication = new AuthenticationOptions
            {
                ClientID = "client-id",
                ClientSecret = "client-secret",
                EncryptionPassword = "encryption-password"
            },
            Certificate = new CertificateOptions(),
            Announcements = new LocalizedOptions<AnnouncementsStrings>
            {
                SChinese = announcements,
                English = announcements
            }
        });
    }

    private sealed class OAuthHandler : HttpMessageHandler
    {
        public int RefreshRequestCount { get; private set; }

        public string? LastAuthenticatedAccessToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/oauth/token")
            {
                string requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                using JsonDocument requestDocument = JsonDocument.Parse(requestJson);
                string grantType = requestDocument.RootElement.GetProperty("grant_type").GetString()!;

                if (grantType == "authorization_code")
                {
                    return JsonResponse("""
                        {
                          "access_token": "initial-access-token",
                          "expires_in": -1,
                          "token_type": "Bearer",
                          "scope": "celeste",
                          "refresh_token": "refresh-token"
                        }
                        """);
                }

                Assert.AreEqual("refresh_token", grantType);
                Assert.AreEqual("refresh-token", requestDocument.RootElement.GetProperty("refresh_token").GetString());
                RefreshRequestCount++;
                Assert.AreEqual(1, RefreshRequestCount, "A valid refreshed token must not be refreshed again on reconnect.");
                return JsonResponse("""
                    {
                      "access_token": "refreshed-access-token",
                      "expires_in": 3600,
                      "token_type": "Bearer",
                      "scope": "celeste"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/celeste/user")
            {
                LastAuthenticatedAccessToken = GetQueryValue(request.RequestUri.Query, "access_token");
                return JsonResponse("""
                    {
                      "id": 42,
                      "username": "OAuth tester",
                      "avatar_url": null,
                      "is_email_confirmed": 1,
                      "prefix": null,
                      "color": "#123456",
                      "suspended_until": null,
                      "suspend_message": null,
                      "suspend_reason": null
                    }
                    """);
            }

            Assert.Fail($"Unexpected request: {request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }

        private static string? GetQueryValue(string query, string name)
        {
            foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split('=', 2);
                if (Uri.UnescapeDataString(parts[0]) == name)
                    return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            }
            return null;
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response.Headers.Date = DateTimeOffset.UtcNow;
            return response;
        }
    }
}
