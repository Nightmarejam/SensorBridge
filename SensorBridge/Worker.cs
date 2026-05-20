using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SensorBridge;

public class HardwareWorker : BackgroundService
{
    private readonly ILogger<HardwareWorker> _logger;
    private readonly Computer _computer;

    public HardwareWorker(ILogger<HardwareWorker> logger)
    {
        _logger = logger;
        
        // Initialize the hardware components you want to monitor
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true, // For fan controllers
            IsStorageEnabled = true     // Essential for NVMe/SSD temps
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Enforce Elevated Privileges
        if (!IsRunningAsAdmin())
        {
            _logger.LogCritical("SensorBridge must be run as ADMINISTRATOR to access kernel-level hardware sensors! Shutting down.");
            Environment.Exit(1);
        }

        _logger.LogInformation("Initializing LibreHardwareMonitor driver...");
        try
        {
            _computer.Open();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to open LibreHardwareMonitor computer instance.");
            Environment.Exit(1);
        }

        _logger.LogInformation("SensorBridge hardware polling loop started.");

        // 2. The Main Telemetry Loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Traverse the hardware tree and poll the sensors
                foreach (IHardware hardware in _computer.Hardware)
                {
                    hardware.Update(); // Instructs the library to query the hardware driver

                    foreach (ISensor sensor in hardware.Sensors)
                    {
                        // Filter for what you need (e.g., Temperatures, Fan Speeds, Loads)
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            _logger.LogDebug("Hardware: {Name} | Sensor: {SensorName} | Value: {Value}°C", 
                                hardware.Name, sensor.Name, sensor.Value.Value);
                            
                            // TODO: Cache these values locally so your gRPC service 
                            // or WMI provider implementation can immediately serve them.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while polling hardware sensors.");
            }

            // Poll interval (e.g., every 2 seconds to keep overhead low)
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        // 3. Clean Driver Tear-Down
        _logger.LogInformation("Closing hardware monitor and unloading driver...");
        _computer.Close();
    }

    private static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}