"""
Gen8 smoke client for FAITHH SensorBridge (h2c / cleartext HTTP/2 on port 9999).

SensorBridge.proto in this folder must match SensorBridge/Protos/SensorBridge.proto byte-for-byte
(diff before release; mismatched field numbers decode wrong on the wire).
Copy the whole `gen8_smoke_client` directory to Gen8 (see INSTALL_GEN8.txt), then:

  cd /path/to/gen8_smoke_client
  python3 -m pip install --user --upgrade grpcio grpcio-tools
  ./setup_stubs.sh
  # or: python3 -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. ./SensorBridge.proto

Use `python3 -m grpc_tools.protoc` so you do not rely on ~/.local/bin being on PATH.

  export POWER_AMP_HOST=192.158.1.101   # FAITHH Windows (Power Amp); see INSTALL_GEN8.txt for .25/.12/.100/.11
  python3 client.py

Windows (Power Amp dev machine), from this folder:

  python -m pip install grpcio grpcio-tools
  .\\setup_stubs.ps1
  $env:POWER_AMP_HOST = "192.158.1.101"
  python client.py

Protobuf fields (match generated *_pb2): source_timestamp_ticks, board_device_id,
cpu.package_temp_c, thermals.system_temp_c, thermals.vrm_temp_c, fans.*, voltages.*, storage.nvme_temp_c.
"""

from __future__ import annotations

import os
import sys

try:
    import grpc
    import SensorBridge_pb2 as pb2
    import SensorBridge_pb2_grpc as pb2_grpc
except ImportError as e:
    print("Missing dependency or stubs. See docstring at top of client.py.", file=sys.stderr)
    raise SystemExit(1) from e


def main() -> None:
    host = os.environ.get("POWER_AMP_HOST", "192.158.1.101")
    target = f"{host}:9999"
    print(f"Insecure gRPC (h2c) -> {target}")

    channel = grpc.insecure_channel(
        target,
        options=[
            ("grpc.keepalive_time_ms", 10_000),
        ],
    )
    stub = pb2_grpc.TelemetryServiceStub(channel)

    req = pb2.StreamTelemetryRequest(sample_interval_ms=1000)
    try:
        for snapshot in stub.StreamTelemetry(req):
            cpu = snapshot.cpu.package_temp_c
            sys_t = snapshot.thermals.system_temp_c
            vrm = snapshot.thermals.vrm_temp_c
            f2 = snapshot.fans.fan2_rpm
            f5 = snapshot.fans.fan5_rpm
            f6 = snapshot.fans.fan6_rpm
            vc = snapshot.voltages.vcore_v
            v12 = snapshot.voltages.v12_v
            nv = snapshot.storage.nvme_temp_c
            print(
                f"ticks={snapshot.source_timestamp_ticks} board={snapshot.board_device_id!r} "
                f"cpu_c={cpu:.1f} sys_c={sys_t:.1f} vrm_c={vrm:.1f} "
                f"fan_rpm=({f2:.0f},{f5:.0f},{f6:.0f}) vcore={vc:.3f} v12={v12:.3f} nvme_c={nv:.1f}"
            )
    except grpc.RpcError as e:
        print(f"gRPC error: {e.code()} {e.details()}", file=sys.stderr)
        raise SystemExit(2) from e


if __name__ == "__main__":
    main()
