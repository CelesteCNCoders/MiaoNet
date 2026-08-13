using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService
{
    public const string AdminSessionCookieName = "miaonet_admin";

    private async Task HandleAdminRequestAsync(string path, NameValueCollection query, HttpListenerContext context)
    {
        if (!adminOptions.Enabled || adminHttpClient is null || adminSessionStore is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        switch (path)
        {
        case "/admin/login":
            AdminLogin(context);
            return;
        case "/admin/callback":
            await AdminCallbackAsync(query, context);
            return;
        }

        AdminSessionStore.AdminSession? session = GetAdminSession(context.Request);

        if (path.StartsWith("/admin/api/", StringComparison.Ordinal))
        {
            if (session is null)
            {
                await WriteJsonAsync(context, (int)HttpStatusCode.Unauthorized, new { error = "未登录或会话已过期" });
                return;
            }
            await HandleAdminApiRequestAsync(path, query, context, session);
            return;
        }

        if (session is null)
        {
            Redirect(context, "/admin/login");
            return;
        }

        switch (path)
        {
        case "/admin":
            await AdminPageAsync(context, session);
            break;
        case "/admin/logout":
            AdminLogout(context);
            break;
        default:
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            break;
        }
    }

    private static readonly AdminSessionStore.AdminSession DebugAdminSession
        = new(0, "debug", "调试管理员", DateTimeOffset.MaxValue);

    private AdminSessionStore.AdminSession? GetAdminSession(HttpListenerRequest request)
    {
        // debug escape hatch: bypass OAuth login entirely (see AdminPanelOptions.DebugSkipAuth)
        if (adminOptions.DebugSkipAuth)
            return DebugAdminSession;

        string? sessionID = request.Cookies[AdminSessionCookieName]?.Value;
        if (string.IsNullOrEmpty(sessionID))
            return null;
        return adminSessionStore!.GetSession(sessionID);
    }

    private void AdminLogin(HttpListenerContext context)
    {
        // already logged in
        if (GetAdminSession(context.Request) is not null)
        {
            Redirect(context, "/admin");
            return;
        }
        string state = adminSessionStore!.CreateState();
        string url =
            $"{adminOptions.ForumBaseUrl.TrimEnd('/')}/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(adminOptions.ClientID ?? string.Empty)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(adminOptions.RedirectUri)}" +
            $"&state={state}";
        if (!string.IsNullOrWhiteSpace(adminOptions.Scope))
            url += $"&scope={Uri.EscapeDataString(adminOptions.Scope)}";
        Redirect(context, url);
    }

    private async Task AdminCallbackAsync(NameValueCollection query, HttpListenerContext context)
    {
        string? state = query["state"];
        string? code = query["code"];
        if (state is null || code is null || !adminSessionStore!.ConsumeState(state))
        {
            logger.LogWarning(AppEvents.Http, "Admin login callback with invalid state from {ep}.", context.Request.RemoteEndPoint);
            await WriteHtmlPageAsync(context, (int)HttpStatusCode.BadRequest, "登录失败",
                "<p>登录状态无效或已过期，请重新 <a href=\"/admin/login\">登录</a>。</p>");
            return;
        }

        string accessToken;
        try
        {
            var tokenResponse = await adminHttpClient!.PostAsJsonAsync("oauth/token", new
            {
                grant_type = "authorization_code",
                client_id = adminOptions.ClientID,
                client_secret = adminOptions.ClientSecret,
                code,
                redirect_uri = adminOptions.RedirectUri
            });
            string tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            if (!tokenResponse.IsSuccessStatusCode)
                throw new InvalidDataException(
                    $"Forum token endpoint returned {(int)tokenResponse.StatusCode} ({tokenResponse.ReasonPhrase}): {TrimForLog(tokenJson)}");
            using JsonDocument tokenDoc = JsonDocument.Parse(tokenJson);
            var tokenRoot = tokenDoc.RootElement;
            // the forum may return an error body with a 200 status
            if (tokenRoot.TryGetProperty("error", out _))
                throw new InvalidDataException($"Forum token endpoint returned an error: {tokenRoot.GetRawText()}");
            if (!tokenRoot.TryGetProperty("access_token", out var accessTokenElement)
                || accessTokenElement.ValueKind is not JsonValueKind.String)
                throw new InvalidDataException($"Forum token endpoint response has no access_token: {tokenRoot.GetRawText()}");
            accessToken = accessTokenElement.GetString()!;
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Http, e, "Failed to exchange admin login code for token.");
            await WriteHtmlPageAsync(context, (int)HttpStatusCode.BadGateway, "登录失败",
                "<p>与论坛通信失败，请稍后重试。</p>");
            return;
        }

        int userID;
        string userName, nickName;
        bool canAdminPanel;
        try
        {
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "api/miaonet/admin-user");
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var userResponse = await adminHttpClient!.SendAsync(userRequest);
            string userJson = await userResponse.Content.ReadAsStringAsync();
            if (!userResponse.IsSuccessStatusCode)
                throw new InvalidDataException(
                    $"Forum admin-user endpoint returned {(int)userResponse.StatusCode} ({userResponse.ReasonPhrase}): {TrimForLog(userJson)}");
            using JsonDocument userDoc = JsonDocument.Parse(userJson);
            var root = userDoc.RootElement;
            // the forum may return an error body with a 200 status
            if (root.TryGetProperty("error", out _))
                throw new InvalidDataException($"Forum admin-user endpoint returned an error: {root.GetRawText()}");
            // the forum sometimes serializes ids as strings
            if (!root.TryGetProperty("id", out var idElement)
                || !TryGetInt32(idElement, out userID))
                throw new InvalidDataException($"Forum admin-user endpoint response has no id: {root.GetRawText()}");
            userName = root.TryGetProperty("username", out var un) ? un.GetString() ?? string.Empty : string.Empty;
            nickName = root.TryGetProperty("nickname", out var nn) ? nn.GetString() ?? userName : userName;
            // the forum plugin grants panel access via the miaonet.adminPanel permission;
            // fall back to is_admin for older plugin versions without that field
            canAdminPanel = root.TryGetProperty("can_admin_panel", out var cp)
                ? cp.ValueKind is JsonValueKind.True
                : root.TryGetProperty("is_admin", out var ia) && ia.ValueKind is JsonValueKind.True;
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Http, e, "Failed to query admin user info from forum.");
            await WriteHtmlPageAsync(context, (int)HttpStatusCode.BadGateway, "登录失败",
                "<p>无法验证你的论坛身份，请稍后重试。</p>");
            return;
        }

        if (!canAdminPanel)
        {
            logger.LogInformation(AppEvents.Http, "User {name}({id}) without admin panel permission tried to login the admin panel.", userName, userID);
            await WriteHtmlPageAsync(context, (int)HttpStatusCode.Forbidden, "无权访问",
                "<p>你不是管理员，无权访问管理后台。</p>");
            return;
        }

        string sessionID = adminSessionStore.CreateSession(userID, userName, nickName);
        context.Response.AppendHeader("Set-Cookie", $"{AdminSessionCookieName}={sessionID}; HttpOnly; Path=/; SameSite=Lax");
        logger.LogInformation(AppEvents.Http, "Admin {name}({id}) logged in the admin panel.", userName, userID);
        Redirect(context, "/admin");
    }

    private void AdminLogout(HttpListenerContext context)
    {
        string? sessionID = context.Request.Cookies[AdminSessionCookieName]?.Value;
        if (!string.IsNullOrEmpty(sessionID))
            adminSessionStore!.DeleteSession(sessionID);
        context.Response.AppendHeader("Set-Cookie", $"{AdminSessionCookieName}=; HttpOnly; Path=/; SameSite=Lax; Max-Age=0");
        Redirect(context, "/admin/login");
    }

    private static void Redirect(HttpListenerContext context, string location)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Redirect;
        context.Response.RedirectLocation = location;
    }

    private static string HtmlEncode(string s) => WebUtility.HtmlEncode(s);

    // keeps logged response bodies bounded (error pages can be huge HTML)
    private static string TrimForLog(string body)
        => body.Length <= 512 ? body : string.Concat(body.AsSpan(0, 512), "...");

    private static bool TryGetInt32(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
        case JsonValueKind.Number:
            return element.TryGetInt32(out value);
        case JsonValueKind.String:
            return int.TryParse(element.GetString(), out value);
        default:
            value = default;
            return false;
        }
    }

    private static async Task WriteHtmlPageAsync(HttpListenerContext context, int statusCode, string title, string bodyContent)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html; charset=utf-8";
        string html = $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{title}} - MiaoNet 管理后台</title>
            <style>
            body{background:#12141a;color:#e6e6e6;font-family:"Segoe UI","Microsoft YaHei",sans-serif;margin:0;padding:24px;}
            a{color:#7ab8ff;text-decoration:none;}
            a:hover{text-decoration:underline;}
            h1{font-size:22px;margin:0 0 16px;}
            h2{font-size:17px;margin-top:28px;border-bottom:1px solid #2a2e3a;padding-bottom:6px;}
            table{border-collapse:collapse;width:100%;max-width:900px;}
            th,td{border:1px solid #2a2e3a;padding:6px 10px;text-align:left;font-size:14px;}
            th{background:#1c1f29;}
            input{background:#1c1f29;border:1px solid #2a2e3a;color:#e6e6e6;padding:6px 8px;border-radius:4px;}
            button{background:#2f6fed;color:#fff;border:none;padding:7px 14px;border-radius:4px;cursor:pointer;}
            button:hover{background:#3d7dff;}
            form label{margin-right:12px;}
            .flash{background:#1d3a24;border:1px solid #2f6b3a;padding:8px 12px;border-radius:4px;margin:12px 0;max-width:900px;}
            </style>
            </head>
            <body>
            <h1>MiaoNet 管理后台</h1>
            {{bodyContent}}
            </body>
            </html>
            """;
        byte[] data = Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength64 = data.Length;
        await context.Response.OutputStream.WriteAsync(data);
    }
}
