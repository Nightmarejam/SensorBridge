using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SensorBridge;

/// <summary>
/// Continuously polls telemetry and writes to WMI regardless of gRPC client connections.
/// </summary>
public sealed class HardwareWorker : BackgroundService
{
    private readonly ITelemetrySampleProvider _samples;
    private readonly WmiWriter _wmi;
    private readonly ILogger<HardwareWorker> _logger;

    public HardwareWorker(
        ITelemetrySampleProvider samples,
        WmiWriter wmi,
        ILogger<HardwareWorker> logger)
    {
        _samples = samples;
        _wmi     = wmi;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HardwareWorker: WMI polling loop started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _samples.GetSnapshotAsync(stoppingToken)
                                             .ConfigureAwait(false);
                _wmi.Write(snapshot);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "HardwareWorker: poll iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("HardwareWorker: polling loop stopped.");
    }
}
