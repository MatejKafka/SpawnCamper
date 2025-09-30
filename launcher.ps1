if (Test-Path \\.\\pipe\\ProcessTracer-Server) {
	Write-Host "ProcessTracer GUI is already running, connecting to the existing instance..."
} else {
    Start-Process $PSScriptRoot\server\process-tracer-server.exe
    # wait for the named pipe to open
    while (-not (Test-Path \\.\\pipe\\ProcessTracer-Server)) {
        sleep 0.1
    }
}

& $PSScriptRoot\process-tracer.exe @Args