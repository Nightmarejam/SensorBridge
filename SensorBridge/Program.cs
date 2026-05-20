using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SensorBridge;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var dumpSensors = args.Any(static a =>
            string.Equals(a, "--dump-sensors", StringComparison.OrdinalIgnoreCase));
        var hostArgs = args
            .Where(static a => !string.Equals(a, "--dump-sensors", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (dumpSensors)
        {
            using var logFactory = LoggerFactory.Create(static b =>
                b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
            var log = logFactory.CreateLogger("SensorBridge");
            TelemetrySampleProvider.ProbeAndLogHardwareTree(log);
            return;
        }

        var builder = Host.CreateDefaultBuilder(hostArgs)
            .UseWindowsService(o => o.ServiceName = "SensorBridge")
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(o => o.SingleLine = true);
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.ConfigureKestrel((_, options) =>
                {
                    options.ListenAnyIP(9999, listen =>
                    {
                        listen.Protocols = HttpProtocols.Http1AndHttp2;
                    });
                });

                web.ConfigureServices((ctx, services) =>
                {
                    services.Configure<LegacyTcpTelemetryOptions>(
                        ctx.Configuration.GetSection(LegacyTcpTelemetryOptions.SectionName));
                    services.Configure<SensorMappingOptions>(
                        ctx.Configuration.GetSection(SensorMappingOptions.SectionName));
                    services.AddSingleton<ITelemetrySampleProvider, TelemetrySampleProvider>();
                    services.AddSingleton<WmiWriter>();
                    services.AddHostedService<HardwareWorker>();
                    services.AddGrpc();
                });

                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<TelemetryServiceImpl>());
                });
            });

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}
