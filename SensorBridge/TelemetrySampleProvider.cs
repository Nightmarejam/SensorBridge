using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SensorBridge.Grpc;

namespace SensorBridge;

public interface ITelemetrySampleProvider
{
    Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Native samples via LibreHardwareMonitor, with optional legacy JSON-over-TCP override for migration.
/// </summary>
public sealed class TelemetrySampleProvider : ITelemetrySampleProvider, IDisposable
{
    private readonly IOptionsMonitor<LegacyTcpTelemetryOptions> _legacyTcp;
    private readonly IOptionsMonitor<SensorMappingOptions> _sensorMapping;
    private readonly ILogger<TelemetrySampleProvider> _logger;
    private readonly Computer? _computer;
    private readonly object _hardwareLock = new();
    private bool _disposed;

    public TelemetrySampleProvider(
        IOptionsMonitor<LegacyTcpTelemetryOptions> legacyTcp,
        IOptionsMonitor<SensorMappingOptions> sensorMapping,
        ILogger<TelemetrySampleProvider> logger)
    {
        _legacyTcp = legacyTcp;
        _sensorMapping = sensorMapping;
        _logger = logger;

        if (legacyTcp.CurrentValue.Enabled)
        {
            _logger.LogInformation("Legacy TCP telemetry is enabled; LibreHardwareMonitor is not started.");
            _computer = null;
            return;
        }

        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true
            };
            _computer.Open();
            _logger.LogInformation("LibreHardwareMonitor initialized for native telemetry.");
            if (_sensorMapping.CurrentValue.HasAnyMapping())
                _logger.LogInformation("SensorBridge:SensorMapping overrides are enabled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open LibreHardwareMonitor; telemetry will be zero until fixed.");
            _computer = null;
        }
    }

    public Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var opts = _legacyTcp.CurrentValue;
        if (opts.Enabled && !string.IsNullOrWhiteSpace(opts.Host))
        {
            return GetLegacyTcpSnapshotAsync(opts.Host, opts.Port, cancellationToken);
        }

        if (_computer == null)
            return Task.FromResult(CreateEmptySnapshot());

        TelemetrySnapshot snapshot;
        lock (_hardwareLock)
        {
            _computer.Accept(new UpdateVisitor());
            snapshot = BuildSnapshotFromHardware();
        }

        return Task.FromResult(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _computer?.Close();
    }

    async Task<TelemetrySnapshot> GetLegacyTcpSnapshotAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (json == null)
                return CreateEmptySnapshot();
            var data = JsonSerializer.Deserialize<SensorData>(json);
            if (data == null)
                return CreateEmptySnapshot();
            return MapJsonToSnapshot(data);
        }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Legacy TCP telemetry read failed");
            return CreateEmptySnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Legacy TCP telemetry read failed");
            return CreateEmptySnapshot();
        }
    }

    TelemetrySnapshot BuildSnapshotFromHardware()
    {
        var snap = new TelemetrySnapshot
        {
            SourceTimestampTicks = Stopwatch.GetTimestamp(),
            BoardDeviceId = "BOARD001",
            Cpu = new CpuTelemetry(),
            Thermals = new ThermalTelemetry(),
            Fans = new FanTelemetry(),
            Voltages = new VoltageTelemetry(),
            Storage = new StorageTelemetry()
        };

        if (_computer == null)
            return snap;

        foreach (var hw in EnumerateHardware(_computer.Hardware))
        {
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.Value is not float v)
                    continue;

                var val = (double)v;
                switch (sensor.SensorType)
                {
                    case SensorType.Temperature:
                        ApplyTemperature(hw, sensor, val, snap);
                        break;
                    case SensorType.Fan:
                        ApplyFan(sensor, val, snap);
                        break;
                    case SensorType.Voltage:
                        ApplyVoltage(sensor, val, snap);
                        break;
                }
            }
        }

        var map = _sensorMapping.CurrentValue;
        if (map.HasAnyMapping())
            ApplySensorMappingOverrides(map, _computer.Hardware, snap);

        return snap;
    }

    static void ApplySensorMappingOverrides(SensorMappingOptions map, IEnumerable<IHardware> roots, TelemetrySnapshot snap)
    {
        foreach (var hw in EnumerateHardware(roots))
        {
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.Value is not float v)
                    continue;
                var val = (double)v;
                ApplySingleSensorOverride(map, sensor, val, snap);
            }
        }
    }

    static void ApplySingleSensorOverride(SensorMappingOptions map, ISensor sensor, double val, TelemetrySnapshot snap)
    {
        if (Matches(sensor, map.CpuPackageTemp) && sensor.SensorType == SensorType.Temperature)
            snap.Cpu!.PackageTempC = val;
        if (Matches(sensor, map.SystemTemperature) && sensor.SensorType == SensorType.Temperature)
            snap.Thermals!.SystemTempC = val;
        if (Matches(sensor, map.VrmTemperature) && sensor.SensorType == SensorType.Temperature)
            snap.Thermals!.VrmTempC = val;
        if (Matches(sensor, map.StorageTemperature) && sensor.SensorType == SensorType.Temperature)
            snap.Storage!.NvmeTempC = val;
        if (Matches(sensor, map.Fan2) && sensor.SensorType == SensorType.Fan)
            snap.Fans!.Fan2Rpm = val;
        if (Matches(sensor, map.Fan5) && sensor.SensorType == SensorType.Fan)
            snap.Fans!.Fan5Rpm = val;
        if (Matches(sensor, map.Fan6) && sensor.SensorType == SensorType.Fan)
            snap.Fans!.Fan6Rpm = val;
        if (Matches(sensor, map.Vcore) && sensor.SensorType == SensorType.Voltage)
            snap.Voltages!.VcoreV = val;
        if (Matches(sensor, map.V12) && sensor.SensorType == SensorType.Voltage)
            snap.Voltages!.V12V = val;
        if (Matches(sensor, map.V5) && sensor.SensorType == SensorType.Voltage)
            snap.Voltages!.V5V = val;
        if (Matches(sensor, map.V33) && sensor.SensorType == SensorType.Voltage)
            snap.Voltages!.V33V = val;
    }

    static bool Matches(ISensor sensor, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        var id = sensor.Identifier.ToString();
        return sensor.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || id.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    static void ApplyTemperature(IHardware hw, ISensor sensor, double val, TelemetrySnapshot snap)
    {
        var name = sensor.Name;
        if (hw.HardwareType == HardwareType.Cpu)
        {
            if (IsCpuPackageName(name))
                snap.Cpu!.PackageTempC = PreferHigher(snap.Cpu.PackageTempC, val);
            else if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                     !name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
                snap.Cpu!.PackageTempC = PreferHigher(snap.Cpu.PackageTempC, val);
            return;
        }

        if (hw.HardwareType == HardwareType.Motherboard)
        {
            if (name.Contains("System", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SYSTIN", StringComparison.OrdinalIgnoreCase))
                snap.Thermals!.SystemTempC = PreferHigher(snap.Thermals.SystemTempC, val);
            else if (name.Contains("VRM", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("VR MOS", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("MOS", StringComparison.OrdinalIgnoreCase))
                snap.Thermals!.VrmTempC = PreferHigher(snap.Thermals.VrmTempC, val);
            return;
        }

        if (hw.HardwareType == HardwareType.Storage)
        {
            if (name.Contains("Temperature", StringComparison.OrdinalIgnoreCase) ||
                sensor.Identifier.ToString().Contains("temperature", StringComparison.OrdinalIgnoreCase))
                snap.Storage!.NvmeTempC = PreferHigher(snap.Storage.NvmeTempC, val);
        }
    }

    static bool IsCpuPackageName(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tdie", StringComparison.OrdinalIgnoreCase);

    static void ApplyFan(ISensor sensor, double val, TelemetrySnapshot snap)
    {
        var n = ExtractFanIndex(sensor.Name);
        if (n == null)
            return;
        snap.Fans ??= new FanTelemetry();
        switch (n.Value)
        {
            case 2: snap.Fans.Fan2Rpm = val; break;
            case 5: snap.Fans.Fan5Rpm = val; break;
            case 6: snap.Fans.Fan6Rpm = val; break;
        }
    }

    /// <summary>Extracts fan index from names like "Fan #2", "Fan 5", "Chassis Fan 6".</summary>
    static int? ExtractFanIndex(string name)
    {
        var m = Regex.Match(name, @"#\s*(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var a))
            return a;
        m = Regex.Match(name, @"Fan\s*(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var b))
            return b;
        return null;
    }

    static void ApplyVoltage(ISensor sensor, double val, TelemetrySnapshot snap)
    {
        var name = sensor.Name;
        snap.Voltages ??= new VoltageTelemetry();
        if (name.Contains("VCore", StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("CPU Core", StringComparison.OrdinalIgnoreCase) &&
             name.Contains("VID", StringComparison.OrdinalIgnoreCase)))
            snap.Voltages.VcoreV = val;
        else if (name.Contains("12V", StringComparison.OrdinalIgnoreCase) || name.Contains("+12", StringComparison.OrdinalIgnoreCase))
            snap.Voltages.V12V = val;
        else if (name.Contains("3.3", StringComparison.OrdinalIgnoreCase) || name.Contains("+3V3", StringComparison.OrdinalIgnoreCase))
            snap.Voltages.V33V = val;
        else if (name.Contains("5V", StringComparison.OrdinalIgnoreCase))
            snap.Voltages.V5V = val;
    }

    static double PreferHigher(double current, double candidate)
    {
        if (current <= 0)
            return candidate;
        if (candidate <= 0)
            return current;
        return Math.Max(current, candidate);
    }

    static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> roots)
    {
        foreach (var hw in roots)
        {
            yield return hw;
            foreach (var sub in EnumerateHardware(hw.SubHardware))
                yield return sub;
        }
    }

    static TelemetrySnapshot MapJsonToSnapshot(SensorData d)
    {
        var v = d.voltages;
        return new TelemetrySnapshot
        {
            SourceTimestampTicks = Stopwatch.GetTimestamp(),
            BoardDeviceId = "BOARD001",
            Cpu = new CpuTelemetry { PackageTempC = d.cpu_temp },
            Thermals = new ThermalTelemetry { SystemTempC = d.system_temp, VrmTempC = d.vrm_temp },
            Fans = new FanTelemetry { Fan2Rpm = d.fan2_rpm, Fan5Rpm = d.fan5_rpm, Fan6Rpm = d.fan6_rpm },
            Voltages = new VoltageTelemetry
            {
                VcoreV = v?.vcore ?? 0,
                V12V = v?.v12 ?? 0,
                V5V = v?.v5 ?? 0,
                V33V = v?.v3_3 ?? 0
            },
            Storage = new StorageTelemetry { NvmeTempC = d.nvme_temp }
        };
    }

    static TelemetrySnapshot CreateEmptySnapshot() =>
        new()
        {
            SourceTimestampTicks = Stopwatch.GetTimestamp(),
            BoardDeviceId = "BOARD001",
            Cpu = new CpuTelemetry(),
            Thermals = new ThermalTelemetry(),
            Fans = new FanTelemetry(),
            Voltages = new VoltageTelemetry(),
            Storage = new StorageTelemetry()
        };

    /// <summary>
    /// Logs the full LibreHardwareMonitor tree (including sub-hardware) after a refresh.
    /// </summary>
    public void DumpHardwareTree()
    {
        if (_computer == null)
        {
            _logger.LogWarning("DumpHardwareTree: LibreHardwareMonitor is not initialized (LegacyTcp enabled or startup failed).");
            return;
        }

        lock (_hardwareLock)
        {
            _computer.Accept(new UpdateVisitor());
            LogHardwareTree(_logger, _computer);
        }
    }

    /// <summary>
    /// One-shot discovery: open LHM, refresh, log all sensors, close. Does not start gRPC or the Windows service host.
    /// Run: <c>SensorBridge.exe --dump-sensors</c>
    /// </summary>
    public static void ProbeAndLogHardwareTree(ILogger logger)
    {
        Computer? computer = null;
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true
            };
            computer.Open();
            computer.Accept(new UpdateVisitor());
            LogHardwareTree(logger, computer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProbeAndLogHardwareTree failed");
            throw;
        }
        finally
        {
            computer?.Close();
        }
    }

    static void LogHardwareTree(ILogger logger, Computer computer)
    {
        foreach (var hw in EnumerateHardware(computer.Hardware))
        {
            logger.LogInformation("Hardware: {Name} ({HardwareType})", hw.Name, hw.HardwareType);
            foreach (var sensor in hw.Sensors)
            {
                logger.LogInformation(
                    "  Sensor type={SensorType} name={SensorName} id={SensorId} value={Value}",
                    sensor.SensorType,
                    sensor.Name,
                    sensor.Identifier,
                    sensor.Value);
            }
        }
    }
}

/// <summary>Matches LibreHardwareMonitor UI visitor: refresh hardware tree without visiting each sensor node.</summary>
internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
            subHardware.Accept(this);
    }

    public void VisitSensor(ISensor sensor) { }

    public void VisitParameter(IParameter parameter) { }
}
