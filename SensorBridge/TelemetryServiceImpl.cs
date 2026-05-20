using Grpc.Core;
using Microsoft.Extensions.Logging;
using SensorBridge.Grpc;

namespace SensorBridge;

public sealed class TelemetryServiceImpl : TelemetryService.TelemetryServiceBase
{
    private readonly ITelemetrySampleProvider _samples;
    private readonly ILogger<TelemetryServiceImpl> _logger;

    public TelemetryServiceImpl(ITelemetrySampleProvider samples, ILogger<TelemetryServiceImpl> logger)
    {
        _samples = samples;
        _logger = logger;
    }

    public override async Task StreamTelemetry(
        StreamTelemetryRequest request,
        IServerStreamWriter<TelemetrySnapshot> responseStream,
        ServerCallContext context)
    {
        var intervalMs = request.SampleIntervalMs == 0 ? 5000u : request.SampleIntervalMs;
        intervalMs = Math.Clamp(intervalMs, 100u, 60_000u);

        while (!context.CancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _samples.GetSnapshotAsync(context.CancellationToken).ConfigureAwait(false);
                await responseStream.WriteAsync(snapshot).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StreamTelemetry iteration failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(intervalMs), context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
