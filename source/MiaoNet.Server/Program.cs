using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using MiaoNet.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NReco.Logging.File;

namespace MiaoNet.Server;

public static class Program
{
    public static void Main(string[] args)
    {
        RuntimeHelpers.RunClassConstructor(typeof(PacketRegistry).TypeHandle);

        HostApplicationBuilderSettings options = new()
        {
            ApplicationName = "MiaoNet.Server",
            Args = args,
#if DEBUG
            EnvironmentName = Environments.Development
#else
            EnvironmentName = Environments.Production
#endif
        };

        var builder = Host.CreateEmptyApplicationBuilder(options);

        builder.Configuration
            .AddJsonFile("appsettings.json", false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true)
            .AddJsonFile("content.json", false);

        builder.Configuration.AddEnvironmentVariables("MIAONET:");

        builder.Logging
            .AddConfiguration(builder.Configuration.GetRequiredSection("Logging"))
            .AddSimpleConsole();

        if (!builder.Environment.IsDevelopment())
            builder.Logging.AddFile($"logs/{DateTime.Now:yyyy-MM-dd}.log");

        // admin panel realtime log buffer
        AdminLogBuffer adminLogBuffer = new();
        builder.Services.AddSingleton(adminLogBuffer);
        builder.Logging.AddProvider(new AdminLogBufferLoggerProvider(adminLogBuffer));

        builder.Services.AddSingleton<AdminChatBuffer>();
        builder.Services.AddSingleton<TemporaryFreezeStore>();
        builder.Services.AddSingleton<AdminMetricsSampler>();
        builder.Services.AddHostedService(p => p.GetRequiredService<AdminMetricsSampler>());

        builder.Services.AddSingleton<NetworkListenerFactory>(p =>
            o => new TlsTcpListener(
                p.GetRequiredService<IMiaoCertificateService>(),
                IPEndPoint.Parse(o.ListenEndPoint)
            )
        );

        builder.Services.AddSingleton<MiaoClientConnectionFactory>(p =>
            (con, player, server) => new MiaoClientConnection(
                con, player,
                p.GetRequiredService<ILogger<MiaoClientConnection>>(),
                server,
                p.GetRequiredService<MiaoMetricsService>()
            )
        );

        builder.Services.AddSingleton<MiaoServerService>();
        builder.Services.AddSingleton<IMiaoServerService>(p => p.GetRequiredService<MiaoServerService>());
        builder.Services.AddHostedService(s => s.GetRequiredService<MiaoServerService>());
        builder.Services.AddSingleton<MiaoMetricsService>();
#if USE_LOCALHOST_PFX
        builder.Services.AddSingleton<IMiaoCertificateService, LocalMiaoCertificateService>();
#else
        builder.Services.AddSingleton<IMiaoCertificateService, MiaoCertificateService>();
        // hm...?
        builder.Services.AddHostedService(s => (MiaoCertificateService)s.GetRequiredService<IMiaoCertificateService>());
#endif
#if USE_CELEMIAO_AUTH
        builder.Services.AddSingleton<IMiaoAuthenticator, CeleMiaoAuthenticator>();
#else
        builder.Services.AddSingleton<IMiaoAuthenticator, CustomAuthenticator>();
#endif
        builder.Services.Configure<MiaoServerOptions>(builder.Configuration.GetRequiredSection("MiaoServer"));

        builder.Services.AddHostedService<MiaoHttpService>();

        using (IHost host = builder.Build())
        {
            host.Run();
        }
    }
}
