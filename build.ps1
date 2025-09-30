$DevShellPath = "18", "2022" | % {
    "${env:ProgramFiles}\Microsoft Visual Studio\$_\Insiders\Common7\Tools\Launch-VsDevShell.ps1"
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\$_\Insiders\Common7\Tools\Launch-VsDevShell.ps1"
} | ? {Test-Path $_} | select -First 1

if (-not $DevShellPath) {
    throw "Did not find a usable MSVC installation."
}

if (Test-Path $PSScriptRoot\bin) {
    rm -Recurse $PSScriptRoot\bin
}

"amd64", "x86" | % {
    pwsh -NoProfile -WorkingDirectory $PSScriptRoot -Args $DevShellPath, $_ {
        param($DevShellPath, $Arch)

        Write-Host ""
        Write-Host "Building hook for $Arch..."
        & $DevShellPath -Arch $Arch -SkipAutomaticLocation

        $BuildDir = ".\ProcessTracer.Client\cmake-build-release-$Arch"
        if (-not (Test-Path $BuildDir)) {
            cmake -S .\ProcessTracer.Client -B $BuildDir -DCMAKE_BUILD_TYPE=Release -G Ninja
        }
        cmake --build $BuildDir
    }
}

pwsh -NoProfile -WorkingDirectory $PSScriptRoot {
    Write-Host ""
    Write-Host "Building server..."
    cd .\ProcessTracer.Server
    dotnet publish
}

Write-Host ""
Write-Host "Copying 'launcher.ps1'..."
cp $PSScriptRoot\launcher.ps1 $PSScriptRoot\bin\launcher.ps1