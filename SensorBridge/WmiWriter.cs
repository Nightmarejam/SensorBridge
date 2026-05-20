using System.Management;
using Microsoft.Extensions.Logging;
using SensorBridge.Grpc;

namespace SensorBridge;

/// <summary>
/// Writes telemetry snapshots to the root\SensorBridge\LiveSensors WMI class.
/// The WMI class must be provisioned first via Install-WmiClass.ps1.
/// </summary>
public sealed class WmiWriter : IDisposable
{
    private const string WmiNamespace = @"root\SensorBridge";
    private const string WmiClass     = "LiveSensors";
    private const string DeviceId     = "BOARD001";

    private readonly ILogger<WmiWriter> _logger;
    private ManagementObject? _instance;
    private ManagementScope?  _scope;
    private bool _disposed;
    private bool _available;

    public WmiWriter(ILogger<WmiWriter> logger)
    {
        _logger = logger;
        TryInitialize();
    }

    private void TryInitialize()
    {
        try
        {
            _scope = new ManagementScope($@"\\.\{WmiNamespace}");
            _scope.Connect();

            var path = new ManagementPath($"{WmiClass}.DeviceID=\"{DeviceId}\"");
            _instance = new ManagementObject(_scope, path, null);
            _instance.Get(); // throws if instance doesn't exist yet
            _available = true;
            _logger.LogInformation("WmiWriter: connected to {Namespace}:{Class}", WmiNamespace, WmiClass);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound)
        {
            // Instance doesn't exist yet — create it
            TryCreateInstance();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WmiWriter: could not connect to {Namespace}:{Class}. " +
                "Run Install-WmiClass.ps1 first. WMI writes disabled.",
                WmiNamespace, WmiClass);
            _available = false;
        }
    }

    private void TryCreateInstance()
    {
        try
        {
            var cls = new ManagementClass(_scope!, new ManagementPath(WmiClass), null);
            _instance = cls.CreateInstance();
            _instance["DeviceID"] = DeviceId;
            _instance.Put();
            _available = true;
            _logger.LogInformation("WmiWriter: created new {Class} instance.", WmiClass);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WmiWriter: failed to create WMI instance. " +
                "Run Install-WmiClass.ps1 first. WMI writes disabled.");
            _available = false;
        }
    }

    /// <summary>
    /// Writes a telemetry snapshot to WMI. No-ops silently if WMI is unavailable.
    /// </summary>
    public void Write(TelemetrySnapshot snap)
    {
        if (!_available || _instance == null || _disposed)
            return;

        try
        {
            // Temperatures
            if (snap.Cpu != null)
                _instance["CpuTemp"]    = snap.Cpu.PackageTempC;
            if (snap.Thermals != null)
            {
                _instance["SystemTemp"] = snap.Thermals.SystemTempC;
                _instance["VrmTemp"]    = snap.Thermals.VrmTempC;
            }
            if (snap.Storage != null)
                _instance["NvmeTemp"]   = snap.Storage.NvmeTempC;

            // Voltages
            if (snap.Voltages != null)
            {
                _instance["Vcore"] = snap.Voltages.VcoreV;
                _instance["V12"]   = snap.Voltages.V12V;
                _instance["V5"]    = snap.Voltages.V5V;
                _instance["V33"]   = snap.Voltages.V33V;
            }

            // Fans
            if (snap.Fans != null)
            {
                _instance["Fan2Rpm"] = snap.Fans.Fan2Rpm;
                _instance["Fan5Rpm"] = snap.Fans.Fan5Rpm;
                _instance["Fan6Rpm"] = snap.Fans.Fan6Rpm;
            }

            _instance.Put();
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound)
        {
            // Instance was deleted (e.g. Install-WmiClass.ps1 -Force was run) — recreate
            _logger.LogInformation("WmiWriter: instance lost, recreating...");
            TryCreateInstance();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WmiWriter: write failed, will retry next cycle.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instance?.Dispose();
    }
}
