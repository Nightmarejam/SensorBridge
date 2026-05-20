#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates the root\SensorBridge WMI namespace and LiveSensors class.

.DESCRIPTION
    Run this once before starting the SensorBridge Windows Service.
    The service writes sensor data to this class but does not create it.

    Safe to run multiple times — skips creation if the class already exists.

.EXAMPLE
    .\Install-WmiClass.ps1
    .\Install-WmiClass.ps1 -Force   # Recreates class even if it exists
#>

param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$NamespacePath = "root\SensorBridge"
$ClassName     = "LiveSensors"

function Write-Step($msg) { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  OK: $msg" -ForegroundColor Green }
function Write-Skip($msg) { Write-Host "  SKIP: $msg" -ForegroundColor Yellow }

Write-Host "`nSensorBridge WMI Setup" -ForegroundColor White
Write-Host "----------------------"

# ── Step 1: Create namespace if missing ──────────────────────────────────────
Write-Step "Checking namespace $NamespacePath ..."

$nsExists = $false
try {
    [wmiclass]"${NamespacePath}:__NAMESPACE" | Out-Null
    $nsExists = $true
} catch { }

if (-not $nsExists) {
    Write-Step "Creating namespace $NamespacePath ..."
    $rootNs = [wmiclass]"root:__NAMESPACE"
    $newNs  = $rootNs.CreateInstance()
    $newNs["Name"] = "SensorBridge"
    $newNs.Put() | Out-Null
    Write-Ok "Namespace created."
} else {
    Write-Skip "Namespace already exists."
}

# ── Step 2: Create class if missing (or Force) ───────────────────────────────
Write-Step "Checking class ${NamespacePath}:${ClassName} ..."

$classExists = $false
try {
    [wmiclass]"${NamespacePath}:${ClassName}" | Out-Null
    $classExists = $true
} catch { }

if ($classExists -and -not $Force) {
    Write-Skip "Class already exists. Use -Force to recreate."
} else {
    if ($classExists -and $Force) {
        Write-Step "Removing existing class (Force) ..."
        $existing = [wmiclass]"${NamespacePath}:${ClassName}"
        $existing.Delete()
        Write-Ok "Existing class removed."
    }

    Write-Step "Creating class $ClassName ..."

    $class = New-Object System.Management.ManagementClass(
        $NamespacePath, [string]::Empty, $null
    )
    $class["__CLASS"] = $ClassName

    # Key property — uniquely identifies the board instance
    $class.Properties.Add("DeviceID", [System.Management.CimType]::String, $false)
    $class.Properties["DeviceID"].Qualifiers.Add("Key", $true)

    # Temperature sensors (°C)
    $class.Properties.Add("CpuTemp",    [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("SystemTemp", [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("VrmTemp",    [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Gpu0Temp",    [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Gpu0PowerW",  [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Gpu0CoreMhz", [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Gpu0MemMhz",  [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Gpu1Temp",    [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Gpu1PowerW",  [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("NvmeTemp",   [System.Management.CimType]::Real64, $false)

    # Voltages (V)
    $class.Properties.Add("Vcore", [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("V12",   [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("V5",    [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("V33",   [System.Management.CimType]::Real64, $false)

    # Fan speeds (RPM)
    $class.Properties.Add("Fan2Rpm", [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Fan5Rpm", [System.Management.CimType]::Real64, $false)
    $class.Properties.Add("Fan6Rpm", [System.Management.CimType]::Real64, $false)

    $class.Put() | Out-Null
    Write-Ok "Class created with $(($class.Properties | Measure-Object).Count) properties."
}

# ── Step 3: Verify ────────────────────────────────────────────────────────────
Write-Step "Verifying ..."
$verify = [wmiclass]"${NamespacePath}:${ClassName}"
$props  = $verify.Properties | Select-Object Name, Type, @{N="Key";E={
    try { $_.Qualifiers["Key"].Value } catch { $false }
}}

Write-Host ""
Write-Host "  Class: $NamespacePath\$ClassName" -ForegroundColor White
$props | ForEach-Object {
    $keyMark = if ($_.Key) { " [KEY]" } else { "" }
    Write-Host ("    {0,-12} {1}{2}" -f $_.Name, $_.Type, $keyMark)
}

Write-Host ""
Write-Ok "WMI class is ready. You can now start the SensorBridge service."
Write-Host ""
Write-Host "  Verify after service starts:" -ForegroundColor Gray
Write-Host '  Get-WmiObject -Namespace "root\SensorBridge" -Class "LiveSensors"' -ForegroundColor Gray
Write-Host ""
