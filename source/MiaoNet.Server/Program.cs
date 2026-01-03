using System.Diagnostics.CodeAnalysis;
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
            .AddJsonFile($"appsettings.{builder.Environment}.json", true);

        builder.Logging
            .AddConfiguration(builder.Configuration.GetRequiredSection("Logging"))
            .AddSimpleConsole();

        if (!builder.Environment.IsDevelopment())
            builder.Logging.AddFile($"logs/{DateTime.Now:yyyy-MM-dd}.log");

        builder.Services.AddSingleton<MiaoServerService>();
        builder.Services.AddHostedService(s => s.GetRequiredService<MiaoServerService>());
        builder.Services.Configure<MiaoServerOptions>(builder.Configuration.GetRequiredSection("MiaoServer"));

        builder.Services.AddHostedService<MiaoHttpService>();

        builder.Services.AddMetrics();

        using (IHost host = builder.Build())
        {
            host.Run();
        }
    }
}