namespace SensorBridge;

public sealed class LegacyTcpTelemetryOptions
{
    public const string SectionName = "SensorBridge:LegacyTcp";

    /// <summary>
    /// When true, each snapshot is filled from a remote JSON-over-TCP line (migration only).
    /// </summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 9999;
}
