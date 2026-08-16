# Builds the SteamOS/Linux AppImage from Windows. The .NET SDK cross-publishes linux-x64
# natively, so only the final AppImage packaging step runs in WSL — no source tree copy and
# no second toolchain. Run from the repository root:
#
#   pwsh packaging/build-linux.ps1
#
# WSL prerequisites (one time, in the Ubuntu distro):
#   sudo apt-get install -y libice6 libsm6
#   appimagetool on PATH (~/.local/bin/appimagetool)
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$PublishDir = 'publish/EmuShelf-linux',
    [string]$ArtifactDir = 'artifacts',
    [string]$Distro = 'Ubuntu',
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

function Invoke-Wsl {
    param([string]$Script)
    # -lc so ~/.local/bin (appimagetool) is on PATH via the login profile.
    wsl -d $Distro -e bash -lc $Script
    if ($LASTEXITCODE -ne 0) { throw "WSL command failed: $Script" }
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

    Write-Host '==> Cross-publishing self-contained linux-x64 build' -ForegroundColor Cyan
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    & $dotnet publish src/EmuShelf.App `
        -c $Configuration `
        -r linux-x64 `
        --self-contained true `
        -p:InvariantGlobalization=false `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

    Write-Host '==> Verifying portable payload' -ForegroundColor Cyan
    $required = @(
        'EmuShelf',
        'THIRD-PARTY-NOTICES.md',
        'ThirdParty/OpenEmu/LICENSE.txt'
    )
    foreach ($relative in $required) {
        if (-not (Test-Path (Join-Path $PublishDir $relative))) {
            throw "Portable payload is missing $relative"
        }
    }

    New-Item -ItemType Directory -Force $ArtifactDir | Out-Null
    $wslRoot = (wsl -d $Distro -e wslpath -a ($root -replace '\\', '/')).Trim()
    if (-not $wslRoot) { throw "Could not resolve $root inside WSL distro '$Distro'" }

    Write-Host '==> Packaging AppImage in WSL' -ForegroundColor Cyan
    Invoke-Wsl "cd '$wslRoot' && command -v appimagetool >/dev/null || { echo 'appimagetool not on PATH in WSL' >&2; exit 1; }"
    # appimagetool writes a single ~100 MB file across the mount; the compile never touches it.
    Invoke-Wsl "cd '$wslRoot' && bash packaging/appimage/build-appimage.sh '$PublishDir' '$ArtifactDir/EmuShelf-linux-x64.AppImage'"

    Write-Host '==> Smoke-testing and checksumming' -ForegroundColor Cyan
    Invoke-Wsl "cd '$wslRoot/$ArtifactDir' && chmod +x EmuShelf-linux-x64.AppImage && ./EmuShelf-linux-x64.AppImage --appimage-extract-and-run --version"
    Invoke-Wsl "cd '$wslRoot/$ArtifactDir' && sha256sum EmuShelf-linux-x64.AppImage > EmuShelf-linux-x64.sha256 && sha256sum -c EmuShelf-linux-x64.sha256 && ls -lh EmuShelf-linux-x64.AppImage EmuShelf-linux-x64.sha256"
}
finally {
    Pop-Location
}
