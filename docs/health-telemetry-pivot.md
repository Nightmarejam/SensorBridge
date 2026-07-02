# SensorBridge repurposing: host→VM health telemetry (2026-05-22 decision)
Tier: asserted — extracted 2026-07-02 from the "Proof of useful work" session; verify
running state against the live PVE host before relying on it (host offline since).

## The decision
The original anti-cheat-adjacent scope stays **parked** (remaining blocker was ACPI
strings below the WMI layer; documented, nothing actionable). SensorBridge's live
purpose is now **the host-to-VM health telemetry layer**: give FAITHH visibility into
the physical node it runs on, feeding node-health signals (ties into Constella's
Proof-of-Life concept — see constella-framework docs/research/).

## State as built that session (on the PVE host)
- `nct6775` kernel module persisted via `/etc/modules` — `sensors` exposes NCT6798D
  (fans/VRM/chipset), k10temp (CPU Tctl/Tccd), NVMe composite, and GPU thermals
  host-side without nvidia-smi.
- **Port 9999**: original raw TCP forwarder, `/usr/local/bin/sensor-bridge.py`
  (NCT6798D → Windows LiveSensors path from the WMI work). Left running.
- **Port 9998**: new JSON API, `/root/sensor_bridge.py` = `sensors -j` over
  HTTP; systemd unit `sensor-bridge-json.service`.
- **Known open issue**: local curl works, cross-VM curl from the faithh VM returned
  empty — suspected Proxmox iptables blocking VM-subnet → host traffic. Unresolved.
- Companion collector on the faithh VM: `~/ai-stack/projects/crypto/gpu_telemetry.py`,
  cron `*/5`, CSV of 3090 temp/fan/power/vram/clocks. First diagnostic result: 75-76°C
  rock-steady under ~23h mining load → no urgent repaste needed.

## Next steps sketched in-thread
- Unified health view of both GPUs (3090 via nvidia-smi in VM 100; 1080 Ti needs a
  small Windows-side agent) + host sensors, one schema, FAITHH-readable.
- Fix the cross-VM firewall path for :9998, or scrape it from Gen8 Prometheus instead.
