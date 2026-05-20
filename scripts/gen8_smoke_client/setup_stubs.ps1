# Run from this directory on Windows (after clone/copy).
Set-Location $PSScriptRoot
python -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. .\SensorBridge.proto
Write-Host "OK: generated stubs. Run: `$env:POWER_AMP_HOST='192.158.1.101'; python client.py"
