using System;
using System.Net.Sockets;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace SensorBridge
{
    public class SensorData
    {
        public double cpu_temp { get; set; }
        public double system_temp { get; set; }
        public double vrm_temp { get; set; }
        public double nvme_temp { get; set; }
        public double fan2_rpm { get; set; }
        public double fan5_rpm { get; set; }
        public double fan6_rpm { get; set; }
        public VoltageData? voltages { get; set; }
    }

    public class VoltageData
    {
        public double vcore { get; set; }
        public double v12 { get; set; }
        public double v5 { get; set; }
        public double v3_3 { get; set; }
    }

    public class SensorWorker : BackgroundService
    {
        const string PVE_HOST = "192.158.1.25";
        const int PVE_PORT = 9999;
        const string WMI_NS = "root\\SensorBridge";
        const string WMI_CLASS = "LiveSensors";

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var data = GetSensorData();
                    if (data != null)
                    {
                        UpdateWMI(data);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                await Task.Delay(5000, stoppingToken);
            }
        }

        static SensorData? GetSensorData()
        {
            using var client = new TcpClient();
            client.Connect(PVE_HOST, PVE_PORT);
            client.ReceiveTimeout = 2000;
            using var reader = new StreamReader(client.GetStream());
            var json = reader.ReadLine();
            if (json == null) return null;
            return JsonSerializer.Deserialize<SensorData>(json);
        }

        static void UpdateWMI(SensorData data)
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope($"\\\\.\\{WMI_NS}"),
                new ObjectQuery($"SELECT * FROM {WMI_CLASS} WHERE DeviceID='BOARD001'")
            );

            ManagementObject? instance = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                instance = obj;
                break;
            }

            if (instance == null)
            {
                var wmiClass = new ManagementClass(
                    new ManagementScope($"\\\\.\\{WMI_NS}"),
                    new ManagementPath(WMI_CLASS),
                    null
                );
                instance = wmiClass.CreateInstance();
                instance["DeviceID"] = "BOARD001";
            }

            instance["CpuTemp"] = data.cpu_temp;
            instance["SystemTemp"] = data.system_temp;
            instance["VrmTemp"] = data.vrm_temp;
            instance["Fan2Rpm"] = data.fan2_rpm;
            instance["Fan5Rpm"] = data.fan5_rpm;
            instance["Fan6Rpm"] = data.fan6_rpm;
            instance["Vcore"] = data.voltages?.vcore ?? 0;
            instance.Put();
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(services =>
                {
                    services.AddHostedService<SensorWorker>();
                });

            await builder.Build().RunAsync();
        }
    }
}