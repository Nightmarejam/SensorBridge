# SyncWMI.ps1
# Telemetry delta logging + throttle detection + remote log shipping to faithh

# ── Configuration ─────────────────────────────────────────────────────────────
$Namespace        = "root\SensorBridge"
$ClassName        = "LiveSensors"
$LogDir           = "C:\SensorBridge\logs"
$LogFile          = "$LogDir\telemetry_history.csv"
$PollIntervalSec  = 5

# Delta-T: alert if any tracked temp rises by this many °C in one interval
$DeltaTThreshold  = 5.0

# Remote shipping: push log to faithh every N successful writes
$RemoteShipEvery  = 12   # every 12 polls = ~1 min at 5s interval
# ── Local config (not committed to git) ──────────────────────────────────────
$ConfigFile = "$PSScriptRoot\SyncWMI.config.ps1"
if (Test-Path $ConfigFile) {
    . $ConfigFile
} else {
    Write-Host "ERROR: SyncWMI.config.ps1 not found. Copy SyncWMI.config.example.ps1 and fill in your values." -ForegroundColor Red
    exit 1
}

# ── Bootstrap ─────────────────────────────────────────────────────────────────
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

if (-not (Test-Path $LogFile)) {
    "Timestamp,DeviceID,CpuTemp,SystemTemp,VrmTemp,Fan2Rpm,Fan5Rpm,Fan6Rpm,Vcore,DeltaCpu,DeltaVrm,ThrottleFlag" `
        | Set-Content -Path $LogFile
}

# ── State ─────────────────────────────────────────────────────────────────────
$prevCpuTemp  = $null
$prevVrmTemp  = $null
$writeCount   = 0

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  SensorBridge — Delta Logging + Ship Engine  " -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "Log file  : $LogFile"
Write-Host "Shipping  : ${RemoteUser}@${RemoteHost}:${RemotePath} every $RemoteShipEvery polls"
Write-Host "Delta-T   : alert threshold = ${DeltaTThreshold}°C per ${PollIntervalSec}s interval`n"

# ── Main Loop ─────────────────────────────────────────────────────────────────
while ($true) {
    try {
        $s = Get-CimInstance -Namespace $Namespace -ClassName $ClassName -ErrorAction Stop
        $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

        # ── Delta-T calculation ───────────────────────────────────────────────
        $deltaCpu = if ($null -ne $prevCpuTemp) { [math]::Round($s.CpuTemp - $prevCpuTemp, 3) } else { 0 }
        $deltaVrm = if ($null -ne $prevVrmTemp) { [math]::Round($s.VrmTemp - $prevVrmTemp, 3) } else { 0 }

        $throttleFlag = if (($deltaCpu -gt $DeltaTThreshold) -or ($deltaVrm -gt $DeltaTThreshold)) {
            "THROTTLE_RISK"
        } else { "OK" }

        $prevCpuTemp = $s.CpuTemp
        $prevVrmTemp = $s.VrmTemp

        # ── Write CSV row ─────────────────────────────────────────────────────
        $row = "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}" -f `
            $ts, $s.DeviceID, $s.CpuTemp, $s.SystemTemp, $s.VrmTemp,
            $s.Fan2Rpm, $s.Fan5Rpm, $s.Fan6Rpm, $s.Vcore,
            $deltaCpu, $deltaVrm, $throttleFlag

        Add-Content -Path $LogFile -Value $row
        $writeCount++

        # ── Console dashboard ─────────────────────────────────────────────────
        Clear-Host
        Write-Host "── Telemetry [$ts] ─────────────────────────" -ForegroundColor Green
        Write-Host "Device      : $($s.DeviceID)"
        Write-Host "CPU Temp    : $($s.CpuTemp)°C   (Δ $deltaCpu°C)"
        Write-Host "System Temp : $($s.SystemTemp)°C"

        $vrmColor = if ($s.VrmTemp -gt 85) { "Red" }
                    elseif ($s.VrmTemp -gt 72) { "Yellow" }
                    else { "White" }
        $vrmLabel = if ($s.VrmTemp -gt 85) { "CRITICAL" } elseif ($s.VrmTemp -gt 72) { "WARN" } else { "Healthy" }
        Write-Host "VRM Temp    : $($s.VrmTemp)°C  (Δ $deltaVrm°C)  [$vrmLabel]" -ForegroundColor $vrmColor

        Write-Host "────────────────────────────────────────────"
        Write-Host "Fan 2       : $($s.Fan2Rpm) RPM"
        Write-Host "Fan 5       : $($s.Fan5Rpm) RPM"
        Write-Host "Fan 6       : $($s.Fan6Rpm) RPM"
        Write-Host "────────────────────────────────────────────"
        Write-Host "Vcore       : $($s.Vcore)V"

        # ── Throttle alert ────────────────────────────────────────────────────
        if ($throttleFlag -eq "THROTTLE_RISK") {
            Write-Host "`n⚠  THROTTLE RISK DETECTED — rapid thermal rise!" -ForegroundColor Red
            Write-Host "   CPU Δ: $deltaCpu°C   VRM Δ: $deltaVrm°C" -ForegroundColor Red
        }

        # ── Remote ship ───────────────────────────────────────────────────────
        if ($writeCount % $RemoteShipEvery -eq 0) {
            Write-Host "`n↑  Shipping log to faithh..." -ForegroundColor DarkCyan
            $scpResult = scp "$LogFile" "${RemoteUser}@${RemoteHost}:${RemotePath}" 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Host "   Shipped OK  (write #$writeCount)" -ForegroundColor DarkCyan
            } else {
                Write-Host "   Ship FAILED: $scpResult" -ForegroundColor DarkYellow
            }
        } else {
            $nextShip = $RemoteShipEvery - ($writeCount % $RemoteShipEvery)
            Write-Host "`n[Logged #$writeCount — next ship in $nextShip polls]" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "WMI query error: $_" -ForegroundColor Red
    }

    Start-Sleep -Seconds $PollIntervalSec
}