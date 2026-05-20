#!/usr/bin/env sh
# Run on Gen8 from this directory (after copying the whole gen8_smoke_client folder).
set -e
cd "$(dirname "$0")"
python3 -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. ./SensorBridge.proto
echo "OK: SensorBridge_pb2.py and SensorBridge_pb2_grpc.py - run:"
echo "  export POWER_AMP_HOST=192.158.1.101"
echo "  python3 client.py"
