using System.Collections.Specialized;
using System.Net;
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
    private readonly IMiaoServerService miaoServerService;
    private readonly MiaoMetricsService miaoMetricsService;
    private readonly HttpListener httpListener;
    private readonly Dictionary<string, RequestHandler> requestHandlers;

    private readonly JsonSerializerOptions jsonSerializerOptions;

    public MiaoHttpService(
        ILogger<MiaoHttpService> logger,
        IOptions<HttpOptions> options,
        IMiaoServerService miaoServerService,
        MiaoMetricsService miaoMetricsService
    )
    {
        this.logger = logger;
        this.miaoServerService = miaoServerService;
        this.miaoMetricsService = miaoMetricsService;
        httpListener = new();
        httpListener.Prefixes.Add(options.Value.ListenerPrefix);

        jsonSerializerOptions = new()
        {
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
        logger.LogInformation(AppEvents.Http, "HttpListener started, listening on {ps}.", string.Join(';', httpListener.Prefixes));

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

            _ = HandleContextAsync(context);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
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

                if (requestHandlers.TryGetValue(path, out var handler))
                    await handler(query, context);
                else
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
            catch (Exception e)
            {
                logger.LogError(AppEvents.Http, e, "Error while handling request \"{url}\" from {ep}.", context.Request.RawUrl, context.Request.RemoteEndPoint);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                context.Response.Close();
            }
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Http, e, "Failed to finish request \"{url}\".", context.Request.RawUrl);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        httpListener.Stop();

        return base.StopAsync(cancellationToken);
    }
}
