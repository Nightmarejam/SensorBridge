# SensorBridge

**In one line:** when a computer runs *inside* another computer (a virtual machine), it
can't see the real hardware around it — SensorBridge is the bridge that lets it see the
truth of the physical machine it lives on.

> One node in a personal AI ecosystem — see the [ecosystem map](https://github.com/Nightmarejam).
> Human-directed, AI-assisted (see [PROVENANCE.md](PROVENANCE.md)). MIT licensed.

---

## The idea (one concept)

A virtual machine is sealed off from real hardware — it sees fake, blank sensors. That
gap is usually just annoying (no temps, no fan data). But the *principle* underneath is
the seed of something bigger in this ecosystem: **a virtual thing cannot fake the
physical machine it runs on.** SensorBridge is the honest channel that proves the
physical to the virtual. That same principle — hardware you can't spoof — is the
foundation of the [attestation layer](https://github.com/Nightmarejam/faithh-pet-terminal/blob/main/docs/ATTESTATION_CONCEPT_2026-07-02.md)
the whole ecosystem is built on. This little service is where that idea started.

## What it does

A .NET 8 Windows Service that:

- Reads real CPU temps, fan RPMs, voltages, and NVMe temps via **LibreHardwareMonitor**
- Streams them over **gRPC (h2c)** on port 9999
- Writes them to a custom **WMI class** (`root\SensorBridge\LiveSensors`) so any Windows
  sensor software sees live, authentic hardware data

```
Windows VM
└── SensorBridge.exe (Windows Service)
    ├── LibreHardwareMonitor → NCT6798D, k10temp, NVMe
    ├── gRPC streaming server :9999
    └── root\SensorBridge\LiveSensors (WMI)
PVE Host / Linux clients (optional)
└── re-serve the stream as JSON for other consumers
```

## Status & scope (honest)

- **Confirmed working:** Proxmox VE 7.0, ASRock X570 Steel Legend, Windows 11 guest.
- **Current purpose:** the node-health telemetry layer feeding FAITHH — see
  [docs/health-telemetry-pivot.md](docs/health-telemetry-pivot.md). (An earlier
  anti-cheat-adjacent framing is parked.)
- Scoped to my setup — reference, not turnkey.
