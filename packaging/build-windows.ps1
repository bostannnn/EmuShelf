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

# The dotnet on PATH may be the runtime-only host under Program Files, which cannot run
# `test` or `publish`. Prefer whichever candidate actually reports an installed SDK.
function Resolve-DotnetSdk {
    $candidates = @(Join-Path $HOME '.dotnet/dotnet.exe')
    $onPath = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($onPath) { $candidates += $onPath.Source }

    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) { continue }
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            $env:DOTNET_ROOT = Split-Path -Parent $candidate
            Write-Host "Using SDK: $candidate ($(@($sdks)[-1]))" -ForegroundColor DarkGray
            return $candidate
        }
    }
    throw 'No .NET SDK was found. Checked ~/.dotnet and PATH (the PATH dotnet may be runtime-only).'
}

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $dotnet = Resolve-DotnetSdk

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

    Write-Host '==> Verifying portable payload' -ForegroundColor Cyan
    $required = @(
        'EmuShelf.exe',
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
