using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService : BackgroundService
{
    private delegate Task RequestHandler(NameValueCollection query, HttpListenerContext context);

    private readonly ILogger<MiaoHttpService> logger;
    private readonly MiaoServerService miaoServerService;
    private readonly MiaoMetricsService miaoMetricsService;
    private readonly AdminLogBuffer adminLogBuffer;
    private readonly AdminChatBuffer adminChatBuffer;
    private readonly AdminMetricsSampler adminMetricsSampler;
    private readonly TemporaryFreezeStore temporaryFreezeStore;
    private readonly HttpListener httpListener;
    private readonly Dictionary<string, RequestHandler> requestHandlers;

    private readonly string apiToken;
    private readonly AdminPanelOptions adminOptions;
    private readonly AdminSessionStore? adminSessionStore;
    private readonly HttpClient? adminHttpClient;

    private readonly JsonSerializerOptions jsonSerializerOptions;

    public MiaoHttpService(
        ILogger<MiaoHttpService> logger,
        IOptions<MiaoServerOptions> options,
        MiaoServerService miaoServerService,
        MiaoMetricsService miaoMetricsService,
        AdminLogBuffer adminLogBuffer,
        AdminChatBuffer adminChatBuffer,
        AdminMetricsSampler adminMetricsSampler,
        TemporaryFreezeStore temporaryFreezeStore
    )
    {
        this.logger = logger;
        this.miaoServerService = miaoServerService;
        this.miaoMetricsService = miaoMetricsService;
        this.adminLogBuffer = adminLogBuffer;
        this.adminChatBuffer = adminChatBuffer;
        this.adminMetricsSampler = adminMetricsSampler;
        this.temporaryFreezeStore = temporaryFreezeStore;
        httpListener = new();
        httpListener.Prefixes.Add(options.Value.HttpListenerPrefix);

        apiToken = options.Value.ApiToken;
        adminOptions = options.Value.AdminPanel;
        if (adminOptions.Enabled)
        {
            adminSessionStore = new AdminSessionStore(TimeSpan.FromHours(adminOptions.SessionHours));
            adminHttpClient = new HttpClient
            {
                BaseAddress = new Uri(adminOptions.ForumBaseUrl.TrimEnd('/') + "/")
            };
            adminHttpClient.DefaultRequestHeaders.Add("User-Agent", "MiaoNet.Server.AdminPanel");
        }

        jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
#if DEBUG
            WriteIndented = true
#endif
        };

        requestHandlers = new()
        {
            ["/status"] = Status,
            ["/player"] = Player,
            ["/announce"] = Announce,
            ["/gc"] = DoGC,
            ["/metrics"] = GetMetrics
        };
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        httpListener.Start();
        logger.LogInformation(AppEvents.Http, "HttpListener start to listen on {ps}.", string.Join(';', httpListener.Prefixes));

        if (string.IsNullOrEmpty(apiToken))
        {
            logger.LogWarning(
                AppEvents.Http,
                "MiaoServer:ApiToken is not configured; mutating HTTP endpoints (DELETE /player, POST /announce, /gc) are unprotected."
            );
        }
        if (adminOptions.Enabled)
        {
            if (string.IsNullOrEmpty(adminOptions.ClientID) || string.IsNullOrEmpty(adminOptions.ClientSecret))
            {
                logger.LogWarning(
                    AppEvents.Http,
                    "Admin panel is enabled but MiaoServer:AdminPanel ClientID/ClientSecret is not configured; OAuth login will fail."
                );
            }
            logger.LogInformation(AppEvents.Http, "Admin panel is enabled on /admin, forum: {forum}.", adminOptions.ForumBaseUrl);
            if (adminOptions.DebugSkipAuth)
            {
                logger.LogWarning(
                    AppEvents.Http,
                    "Admin panel DebugSkipAuth is ON: OAuth login is bypassed and every visitor is treated as admin. Do NOT expose this listener publicly."
                );
            }
        }

        return base.StartAsync(cancellationToken);
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await httpListener.GetContextAsync();
            }
            catch (HttpListenerException e)
            when (e.ErrorCode == 995)
            {
                break;
            }
            catch (ObjectDisposedException e)
            when (e.ObjectName == "listener")
            {
                break;
            }
            // dispatch each connection to its own task so one slow request
            // never blocks the accept loop (admin page polls frequently)
            _ = ProcessConnectionAsync(context);
        }
    }

    private async Task ProcessConnectionAsync(HttpListenerContext context)
    {
        try
        {
            Uri? uri = context.Request.Url;
            if (uri is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            string path = uri.AbsolutePath;
            NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);

            await HandleRequestAsync(path, query, context);
        }
        catch (Exception e)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch
            {
                // response may already be half-written; nothing more we can do
            }
            logger.LogError(AppEvents.Http, e, "Error when handling request \"{url}\" from {ep}", context.Request.RawUrl, context.Request.RemoteEndPoint);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task HandleRequestAsync(string path, NameValueCollection query, HttpListenerContext context)
    {
        try
        {
            if (path == "/admin" || path.StartsWith("/admin/", StringComparison.Ordinal))
            {
                await HandleAdminRequestAsync(path, query, context);
            }
            else if (requestHandlers.TryGetValue(path, out var handler))
            {
                if (!CheckApiToken(path, context))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    context.Response.ContentType = "application/json";
                    await JsonSerializer.SerializeAsync(
                        context.Response.OutputStream,
                        new { error = "Missing or invalid X-Api-Token header." },
                        jsonSerializerOptions
                    );
                    return;
                }
                await handler(query, context);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (Exception e)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            logger.LogError(
                AppEvents.Http, e,
                "Error when handling request \"{url}\" from {ep}",
                context.Request.RawUrl, context.Request.RemoteEndPoint
            );
        }
    }

    private bool CheckApiToken(string path, HttpListenerContext context)
    {
        // only mutating endpoints are protected; /status and /metrics stay public
        bool isProtected = path is "/player" or "/gc"
            || (path == "/announce" && context.Request.HttpMethod == "POST");
        if (!isProtected)
            return true;
        // not configured: leave unprotected (a warning was logged at startup)
        if (string.IsNullOrEmpty(apiToken))
            return true;

        string? token = context.Request.Headers["X-Api-Token"];
        if (token is null)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(apiToken)
        );
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        httpListener.Stop();

        return base.StopAsync(cancellationToken);
    }
}
