using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed class MiaoHttpService : BackgroundService
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
            StringBuilder sb = new(128);

            var state = miaoServerService.ServerState;

            sb.AppendLine($"Channels Count: {state.AllChannels.Count}");
            sb.AppendLine($"Players Count: {state.AllPlayers.Count}");
            sb.AppendLine();
            foreach ((_, var channel) in state.AllChannels)
            {
                sb.AppendLine($"Channel {channel}");
                foreach ((_, (var player, _)) in channel.Players)
                {
                    sb.AppendLine($"  Player {player.Info} at {player.StateInfo}");
                }
            }

            context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(sb.ToString()));
            context.Response.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        httpListener.Stop();

        return base.StopAsync(cancellationToken);
    }
}