using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService : BackgroundService
{
    private readonly ILogger<MiaoHttpService> logger;
    private readonly MiaoServerService miaoServerService;

    private readonly HttpListener httpListener;

    public MiaoHttpService(ILogger<MiaoHttpService> logger, IOptions<MiaoServerOptions> options, MiaoServerService miaoServerService)
    {
        this.logger = logger;
        this.miaoServerService = miaoServerService;
        httpListener = new();
        httpListener.Prefixes.Add(options.Value.HttpListenerPrefix);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        httpListener.Start();
        logger.LogInformation("HttpListener start to listen on {ps}.", string.Join(';', httpListener.Prefixes));

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
            try
            {
                Uri? uri = context.Request.Url;
                if (uri is null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    continue;
                }

                string path = uri.AbsolutePath;
                NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);

                _ = HandleRequestAsync(path, query, context);
            }
            catch (Exception e)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                logger.LogError(e, "Error when handling request \"{url}\" from {ep}", context.Request.RawUrl, context.Request.RemoteEndPoint);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        httpListener.Stop();

        return base.StopAsync(cancellationToken);
    }
}