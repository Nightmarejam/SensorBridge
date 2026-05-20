namespace SensorBridge;

/// <summary>
/// Optional substring matches on LHM sensor name or identifier string (case-insensitive).
/// When set, values override heuristic mapping for that field.
/// </summary>
public sealed class SensorMappingOptions
{
    public const string SectionName = "SensorBridge:SensorMapping";

    public string? CpuPackageTemp { get; set; }
    public string? SystemTemperature { get; set; }
    public string? VrmTemperature { get; set; }
    public string? StorageTemperature { get; set; }
    public string? Fan2 { get; set; }
    public string? Fan5 { get; set; }
    public string? Fan6 { get; set; }
    public string? Vcore { get; set; }
    public string? V12 { get; set; }
    public string? V5 { get; set; }
    public string? V33 { get; set; }

    public bool HasAnyMapping() =>
        !string.IsNullOrWhiteSpace(CpuPackageTemp) ||
        !string.IsNullOrWhiteSpace(SystemTemperature) ||
        !string.IsNullOrWhiteSpace(VrmTemperature) ||
        !string.IsNullOrWhiteSpace(StorageTemperature) ||
        !string.IsNullOrWhiteSpace(Fan2) ||
        !string.IsNullOrWhiteSpace(Fan5) ||
        !string.IsNullOrWhiteSpace(Fan6) ||
        !string.IsNullOrWhiteSpace(Vcore) ||
        !string.IsNullOrWhiteSpace(V12) ||
        !string.IsNullOrWhiteSpace(V5) ||
        !string.IsNullOrWhiteSpace(V33);
}
