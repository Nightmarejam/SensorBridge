# SensorBridge

A hardware sensor bridge for Proxmox Windows VMs. Reads real host hardware data via LibreHardwareMonitor and exposes it over gRPC streaming and WMI — giving your Windows guest authentic, live sensor readings instead of nothing.

## What it does

When Windows runs inside a Proxmox KVM guest, hardware monitoring software sees no sensors. This project fixes that by running a .NET 8 Windows Service that:

- Reads real CPU temps, fan RPMs, voltages, and NVMe temps via **LibreHardwareMonitor**
- Streams them over **gRPC (h2c)** on port 9999
- Writes them directly to a custom **WMI class** (`root\SensorBridge\LiveSensors`)
- Any Windows software that reads WMI sensors will see live, authentic hardware data

```
Windows VM
└── SensorBridge.exe (Windows Service)
    ├── LibreHardwareMonitor → reads NCT6798D, k10temp, NVMe
    ├── gRPC streaming server :9999
    └── root\SensorBridge\LiveSensors (WMI)

PVE Host (optional)
└── sensor-bridge.py → connects to VM :9999, re-serves as TCP JSON

Gen8 / Linux clients (optional)
└── scripts/gen8_smoke_client/client.py → gRPC stream consumer
```

## Confirmed working on

- Proxmox VE 7.0, ASRock X570 Steel Legend, Windows 11 guest
- GTX 1080 Ti passthrough, NCT6798D superIO, AMD k10temp

## WMI output example

```powershell
Get-WmiObject -Namespace "root\SensorBridge" -Class "LiveSensors" | Select-Object *

CpuTemp    : 56.6
SystemTemp : 40
VrmTemp    : 55
Vcore      : 0.696
Fan2Rpm    : 1560
Fan5Rpm    : 825
Fan6Rpm    : 2132
```

---

## Requirements

- Windows 11 (guest VM)
- .NET 8 Runtime or SDK
- Administrator rights (required for WMI class creation and LHM kernel driver)

---

## Build

```powershell
cd SensorBridge
dotnet publish -c Release -r win-x64 --self-contained true -o ..\service
```

Or open `SensorBridge.sln` in Visual Studio and publish from there.

---

## Install as Windows Service

```powershell
# From an elevated PowerShell prompt
sc.exe create SensorBridge binPath="C:\SensorBridge\service\SensorBridge.exe" start=auto
sc.exe description SensorBridge "Hardware Sensor Bridge"
sc.exe start SensorBridge

# Verify
Get-Service SensorBridge
Get-WmiObject -Namespace "root\SensorBridge" -Class "LiveSensors" | Select-Object CpuTemp, Fan2Rpm, VrmTemp
```

---

## Configuration

Edit `appsettings.json` to map sensor channels for your hardware:

```json
{
  "SensorMapping": {
    "CpuTempSensorName": "CPU Package",
    "SystemTempSensorName": "System",
    "Fan2SensorName": "Fan #2"
  }
}
```

Sensor names come from LibreHardwareMonitor — run LHM standalone first to find the exact names for your board.

---

## Proto schema

The gRPC interface is defined in `SensorBridge/Protos/SensorBridge.proto`. Key fields:

```protobuf
cpu.package_temp_c
thermals.system_temp_c
thermals.vrm_temp_c
fans.fan2_rpm / fan5_rpm / fan6_rpm
voltages.vcore_v / v12_v
storage.nvme_temp_c
source_timestamp_ticks
board_device_id
```

If you use the Gen8 smoke client, the `.proto` file in `scripts/gen8_smoke_client/` must match `SensorBridge/Protos/SensorBridge.proto` exactly.

---

## Gen8 / Linux smoke client

```bash
cd scripts/gen8_smoke_client
python3 -m pip install --user grpcio grpcio-tools
python3 -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. ./SensorBridge.proto

export POWER_AMP_HOST=<your-windows-vm-ip>
python3 client.py
```

See `scripts/gen8_smoke_client/INSTALL_GEN8.txt` for full setup.

---

## PVE host bridge (optional)

If you want the Proxmox host itself to read sensor data, `/usr/local/bin/sensor-bridge.py` connects to the Windows VM's gRPC endpoint and re-serves the data as simple TCP/JSON on port 9999.

```bash
# Install lm-sensors (used for fallback/validation)
apt install lm-sensors -y

# Copy sensor-bridge.py to /usr/local/bin/
# Install and enable the systemd service
systemctl enable --now sensor-bridge.service
```

---

## Anti-cheat / transparency context

This project is part of a broader effort to make a Proxmox Windows VM indistinguishable from bare metal. Real sensor data via WMI is one layer of that — it eliminates the "no hardware sensors" tell that some software uses to detect virtualization.

**What this does:** Fills the WMI sensor gap. Confirmed working against Overwatch and similar titles.  
**What this doesn't do:** Bypass EAC (Easy Anti-Cheat). EAC operates at a kernel/driver level below WMI. This is one layer of a larger fingerprint-elimination effort.

Other layers (separate work, not in this repo):
- Custom patched `pve-qemu-kvm` (SMBIOS spoofing, VMgenid disabled, FwCfg eliminated)
- Drive serial spoofing
- CPU flag configuration (kvm=off, hv_vendor_id)

---

## Known issues

- **v12 voltage mapping** is incorrect for the X570 Steel Legend — `in2` on NCT6798D is not the 12V rail on this board. Other readings are accurate. Fix pending correct channel identification.
- Sensor channel names in `appsettings.json` are board-specific. You will likely need to adjust them for your hardware.

---

## License

MIT

## Status (2026-07-02)
Repurposed: the live goal is **host-to-VM health telemetry** feeding FAITHH's node
health (see docs/health-telemetry-pivot.md); the anti-cheat-adjacent scope is parked.
License: MIT.

## The ecosystem (how this repo fits)

| Repo | Role |
|---|---|
| [constella-framework](https://github.com/Nightmarejam/constella-framework) | Civic governance framework — also the **logic basis** for everything here (confirmability tiers, concept lineage, Harmony bridge) |
| [faithh-pet-terminal](https://github.com/Nightmarejam/faithh-pet-terminal) | FAITHH — personal AI companion: Flask + ChromaDB RAG + vLLM on a Proxmox homelab |
| [SensorBridge](https://github.com/Nightmarejam/SensorBridge) | Host→VM hardware telemetry (gRPC/WMI); pivoted to node-health monitoring feeding FAITHH |
| [celestial-equilibrium](https://github.com/Nightmarejam/celestial-equilibrium) | Doctrine text (CC BY 4.0), consumed by constella as a submodule |
| [runbook-to-rule-them-all](https://github.com/Nightmarejam/runbook-to-rule-them-all) | Ops runbooks for the homelab systems |
| homelab / research-notes / tomcat-sound | Private: hardware+pipeline knowledge, theory notes, business records |

Work is human-directed and AI-assisted — see [PROVENANCE.md](PROVENANCE.md).
