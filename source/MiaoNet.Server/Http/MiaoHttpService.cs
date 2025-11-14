using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService : BackgroundService
{
    private readonly ILogger<MiaoHttpService> logger;
    private readonly MiaoServerService miaoServerService;

    private readonly HttpListener httpListener;

    public MiaoHttpService(ILogger<MiaoHttpService> logger, MiaoServerService miaoServerService)
    {
        this.logger = logger;
        this.miaoServerService = miaoServerService;
        httpListener = new();
        httpListener.Prefixes.Add("http://+:8000/");
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
            var context = await httpListener.GetContextAsync();
            try
            {
                Uri? uri = context.Request.Url;
                if (uri is null)
                {
                    context.Response.Close();
                    continue;
                }

                string path = uri.AbsolutePath;
                NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);

                DispatchQuest(path, query, context);
            }
            catch (Exception e)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                logger.LogError(e, "Error when handling request \"{url}\"", context.Request.RawUrl);
            }
            context.Response.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        httpListener.Stop();

        return base.StopAsync(cancellationToken);
    }
}