using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
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
        if (session is null)
        {
            Redirect(context, "/admin/login");
            return;
        }

        switch (path)
        {
        case "/admin":
            await AdminDashboardAsync(query, context, session);
            break;
        case "/admin/kick":
            await AdminKickAsync(context);
            break;
        case "/admin/announce":
            await AdminAnnounceAsync(context);
            break;
        case "/admin/logout":
            AdminLogout(context);
            break;
        default:
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            break;
        }
    }

    private AdminSessionStore.AdminSession? GetAdminSession(HttpListenerRequest request)
    {
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
            tokenResponse.EnsureSuccessStatusCode();
            using JsonDocument tokenDoc = await JsonDocument.ParseAsync(
                await tokenResponse.Content.ReadAsStreamAsync()
            );
            accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()!;
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
        bool isAdmin;
        try
        {
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "api/miaonet/admin-user");
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var userResponse = await adminHttpClient!.SendAsync(userRequest);
            userResponse.EnsureSuccessStatusCode();
            using JsonDocument userDoc = await JsonDocument.ParseAsync(
                await userResponse.Content.ReadAsStreamAsync()
            );
            var root = userDoc.RootElement;
            userID = root.GetProperty("id").GetInt32();
            userName = root.TryGetProperty("username", out var un) ? un.GetString() ?? string.Empty : string.Empty;
            nickName = root.TryGetProperty("nickname", out var nn) ? nn.GetString() ?? userName : userName;
            isAdmin = root.TryGetProperty("is_admin", out var ia) && ia.ValueKind is JsonValueKind.True;
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Http, e, "Failed to query admin user info from forum.");
            await WriteHtmlPageAsync(context, (int)HttpStatusCode.BadGateway, "登录失败",
                "<p>无法验证你的论坛身份，请稍后重试。</p>");
            return;
        }

        if (!isAdmin)
        {
            logger.LogInformation(AppEvents.Http, "Non-admin user {name}({id}) tried to login the admin panel.", userName, userID);
            await WriteHtmlPageAsync(context, (int)HttpStatusCode.Forbidden, "无权访问",
                "<p>你不是管理员，无权访问管理后台。</p>");
            return;
        }

        string sessionID = adminSessionStore.CreateSession(userID, userName, nickName);
        context.Response.AppendHeader("Set-Cookie", $"{AdminSessionCookieName}={sessionID}; HttpOnly; Path=/; SameSite=Lax");
        logger.LogInformation(AppEvents.Http, "Admin {name}({id}) logged in the admin panel.", userName, userID);
        Redirect(context, "/admin");
    }

    private async Task AdminDashboardAsync(NameValueCollection query, HttpListenerContext context, AdminSessionStore.AdminSession session)
    {
        StringBuilder sb = new();

        string? flash = query["msg"];
        if (!string.IsNullOrEmpty(flash))
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"flash\">{HtmlEncode(flash)}</div>");

        sb.Append(CultureInfo.InvariantCulture, $"<p>当前登录：{HtmlEncode(session.NickName)}（{HtmlEncode(session.UserName)}） ");
        sb.Append("| <a href=\"/admin\">刷新</a> | <a href=\"/admin/logout\">退出登录</a></p>");

        // 在线玩家
        sb.Append("<h2>在线玩家</h2>");
        sb.Append("<table><tr><th>连接 ID</th><th>名称</th><th>AuthID</th><th>位置</th></tr>");
        foreach (var p in miaoServerService.Players)
        {
            var info = p.Value.Player.Info;
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr><td>{p.Key}</td><td>{HtmlEncode(info.Name)}</td><td>{info.AuthID}</td>");
            sb.Append(CultureInfo.InvariantCulture,
                $"<td>{HtmlEncode(p.Value.Player.Location.ToString())}</td></tr>");
        }
        if (miaoServerService.Players.Count == 0)
            sb.Append("<tr><td colspan=\"4\">当前没有在线玩家</td></tr>");
        sb.Append("</table>");

        // 频道
        sb.Append("<h2>频道</h2>");
        sb.Append("<table><tr><th>ID</th><th>名称</th><th>玩家数</th></tr>");
        foreach (var c in miaoServerService.Channels)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr><td>{c.Key}</td><td>{HtmlEncode(c.Value.Info.Name)}</td><td>{c.Value.Players.Count}</td></tr>");
        }
        if (miaoServerService.Channels.Count == 0)
            sb.Append("<tr><td colspan=\"3\">当前没有频道</td></tr>");
        sb.Append("</table>");

        // 指标
        var metrics = miaoMetricsService.Get();
        sb.Append("<h2>服务器指标</h2>");
        sb.Append("<table>");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><th>在线玩家数</th><td>{miaoServerService.Players.Count}</td></tr>");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><th>累计会话数</th><td>{metrics.SessionsCount}</td></tr>");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><th>TCP 上传</th><td>{metrics.TcpUploadByPackets} 包 / {metrics.TcpUploadByBytes} 字节</td></tr>");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><th>TCP 下载</th><td>{metrics.TcpDownloadByPackets} 包 / {metrics.TcpDownloadByBytes} 字节</td></tr>");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><th>GC 总分配</th><td>{GC.GetTotalAllocatedBytes()} 字节</td></tr>");
        sb.Append("</table>");

        // 踢出玩家
        sb.Append("""
            <h2>踢出玩家</h2>
            <form method="post" action="/admin/kick">
              <label>AuthID：<input type="number" name="aid" required></label>
              <label>原因：<input type="text" name="reason" placeholder="选填"></label>
              <button type="submit">踢出</button>
            </form>
            """);

        // 广播公告
        sb.Append("""
            <h2>广播公告</h2>
            <form method="post" action="/admin/announce">
              <label>内容：<input type="text" name="msg" required size="40"></label>
              <button type="submit">发送</button>
            </form>
            """);

        await WriteHtmlPageAsync(context, (int)HttpStatusCode.OK, "管理后台", sb.ToString());
    }

    private async Task AdminKickAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "POST")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        var form = await ReadFormAsync(context.Request);
        if (!int.TryParse(form["aid"], CultureInfo.InvariantCulture, out int aid))
        {
            RedirectWithMessage(context, "参数错误：无效的 AuthID");
            return;
        }
        string reason = form["reason"] is { Length: > 0 } r ? r : "你已被管理员踢出";
        int kicked = await KickByAuthIDAsync(aid, reason);
        logger.LogInformation(AppEvents.Http, "Admin panel kicked {count} player(s) with AuthID {aid}, reason: {reason}.", kicked, aid, reason);
        RedirectWithMessage(context, kicked > 0 ? $"已踢出 {kicked} 名玩家" : "未找到该玩家");
    }

    private async Task AdminAnnounceAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "POST")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        var form = await ReadFormAsync(context.Request);
        string? msg = form["msg"];
        if (string.IsNullOrWhiteSpace(msg))
        {
            RedirectWithMessage(context, "参数错误：公告内容不能为空");
            return;
        }
        await BroadcastAnnouncementAsync(msg);
        logger.LogInformation(AppEvents.Http, "Admin panel broadcasted announcement: {msg}.", msg);
        RedirectWithMessage(context, "公告已发送");
    }

    private void AdminLogout(HttpListenerContext context)
    {
        string? sessionID = context.Request.Cookies[AdminSessionCookieName]?.Value;
        if (!string.IsNullOrEmpty(sessionID))
            adminSessionStore!.DeleteSession(sessionID);
        context.Response.AppendHeader("Set-Cookie", $"{AdminSessionCookieName}=; HttpOnly; Path=/; SameSite=Lax; Max-Age=0");
        Redirect(context, "/admin/login");
    }

    private static async Task<NameValueCollection> ReadFormAsync(HttpListenerRequest request)
    {
        using StreamReader reader = new(request.InputStream, request.ContentEncoding);
        string body = await reader.ReadToEndAsync();
        return HttpUtility.ParseQueryString(body);
    }

    private void RedirectWithMessage(HttpListenerContext context, string message)
        => Redirect(context, $"/admin?msg={Uri.EscapeDataString(message)}");

    private static void Redirect(HttpListenerContext context, string location)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Redirect;
        context.Response.RedirectLocation = location;
    }

    private static string HtmlEncode(string s) => WebUtility.HtmlEncode(s);

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
