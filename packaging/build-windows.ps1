# Builds the portable Windows release artifact locally, mirroring the `package` job in
# .github/workflows/build.yml. Run from the repository root:
#
#   pwsh packaging/build-windows.ps1
#
# Close a running EmuShelf.exe first — it locks src/EmuShelf.App/bin and the publish fails.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$PublishDir = 'publish/EmuShelf',
    [string]$ArtifactDir = 'artifacts',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $dotnet = if ($command) { $command.Source } else { Join-Path $HOME '.dotnet/dotnet.exe' }
    if (-not (Test-Path $dotnet)) { throw 'dotnet was not found on PATH or in ~/.dotnet' }

    if (-not $SkipTests) {
        Write-Host '==> dotnet test' -ForegroundColor Cyan
        & $dotnet test -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    }

    Write-Host "==> Publishing self-contained $Runtime build" -ForegroundColor Cyan
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    & $dotnet publish src/EmuShelf.App `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishReadyToRun=true `
        -p:PublishReadyToRunShowWarnings=true `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

    Write-Host '==> Bundling rclone (cloud save sync)' -ForegroundColor Cyan
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("emushelf-rclone-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Force $staging | Out-Null
    try {
        $zip = Join-Path $staging 'rclone.zip'
        Invoke-WebRequest -Uri 'https://downloads.rclone.org/rclone-current-windows-amd64.zip' -OutFile $zip
        Expand-Archive $zip -DestinationPath (Join-Path $staging 'extract') -Force
        $exe = Get-ChildItem (Join-Path $staging 'extract') -Recurse -Filter rclone.exe | Select-Object -First 1
        if (-not $exe) { throw 'rclone.exe was not found in the downloaded archive' }
        Copy-Item $exe.FullName (Join-Path $PublishDir 'rclone.exe')
        $licenseDir = Join-Path $PublishDir 'ThirdParty/rclone'
        New-Item -ItemType Directory -Force $licenseDir | Out-Null
        $license = Get-ChildItem $exe.Directory -Filter 'LICENSE*' | Select-Object -First 1
        if ($license) { Copy-Item $license.FullName (Join-Path $licenseDir 'LICENSE.txt') }
    }
    finally {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host '==> Verifying portable payload' -ForegroundColor Cyan
    $required = @(
        'EmuShelf.exe',
        'rclone.exe',
        'ThirdParty/rclone/LICENSE.txt',
        'THIRD-PARTY-NOTICES.md',
        'ThirdParty/OpenEmu/LICENSE.txt'
    )
    foreach ($relative in $required) {
        $path = Join-Path $PublishDir $relative
        if (-not (Test-Path $path)) { throw "Portable payload is missing $relative" }
    }

    Write-Host '==> Creating portable zip and checksum' -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $ArtifactDir | Out-Null
    $zipPath = Join-Path $ArtifactDir "EmuShelf-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $PublishDir -DestinationPath $zipPath
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  EmuShelf-$Runtime.zip" | Set-Content (Join-Path $ArtifactDir "EmuShelf-$Runtime.sha256")

    Get-Item $zipPath, (Join-Path $ArtifactDir "EmuShelf-$Runtime.sha256") |
        Select-Object Name, @{ n = 'Size'; e = { '{0:N1} MB' -f ($_.Length / 1MB) } }
}
finally {
    Pop-Location
}
