if (Test-Path \\.\\pipe\\SpawnCamper) {
	Write-Host "SpawnCamper GUI is already running, connecting to the existing instance..."
} else {
    Start-Process $PSScriptRoot\server\SpawnCamper.Server.exe
    # wait for the named pipe to open
    while (-not (Test-Path \\.\\pipe\\SpawnCamper)) {
        sleep 0.1
    }
}

& $PSScriptRoot\SpawnCamper.exe @Args