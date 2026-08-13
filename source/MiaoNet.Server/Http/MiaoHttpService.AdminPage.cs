using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService
{
    private static readonly string AdminPagePath
        = Path.Combine(AppContext.BaseDirectory, "Http", "Admin", "admin.html");

    private static string? adminPageTemplate;
    private static DateTime adminPageTemplateTime;

    private async Task AdminPageAsync(HttpListenerContext context, AdminSessionStore.AdminSession session)
    {
        string? template = await GetAdminPageTemplateAsync();
        if (template is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return;
        }
        string html = template
            .Replace("{{NickName}}", HtmlEncode(session.NickName), StringComparison.Ordinal)
            .Replace("{{UserName}}", HtmlEncode(session.UserName), StringComparison.Ordinal);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        byte[] data = Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength64 = data.Length;
        await context.Response.OutputStream.WriteAsync(data);
    }

    // reloads the template whenever the file on disk changes, so the page can be
    // edited in the deployed folder without restarting the server
    private async Task<string?> GetAdminPageTemplateAsync()
    {
        try
        {
            DateTime writeTime = File.GetLastWriteTimeUtc(AdminPagePath);
            if (adminPageTemplate is null || writeTime != adminPageTemplateTime)
            {
                adminPageTemplate = await File.ReadAllTextAsync(AdminPagePath);
                adminPageTemplateTime = writeTime;
                logger.LogInformation(AppEvents.Http, "Admin page loaded from {path}.", AdminPagePath);
            }
        }
        catch (Exception e)
        {
            if (adminPageTemplate is null)
                logger.LogError(AppEvents.Http, e, "Failed to load the admin page from {path}.", AdminPagePath);
            else
                // keep serving the last good template
                logger.LogWarning(AppEvents.Http, e, "Failed to reload the admin page from {path}; serving the cached copy.", AdminPagePath);
        }
        return adminPageTemplate;
    }
}
