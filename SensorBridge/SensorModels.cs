namespace SensorBridge;

public sealed class SensorData
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

public sealed class VoltageData
{
    public double vcore { get; set; }
    public double v12 { get; set; }
    public double v5 { get; set; }
    public double v3_3 { get; set; }
}
