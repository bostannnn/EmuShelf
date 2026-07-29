#!/usr/bin/env bash
# Builds a Deck-ready AppImage from a native Linux checkout, in place.
#
# On Windows, do NOT use this script: run `pwsh packaging/build-linux.ps1` instead. It
# cross-publishes linux-x64 with the Windows SDK and only shells into WSL for appimagetool,
# which avoids both copying the tree and compiling across the /mnt/c filesystem.
set -euo pipefail

script_dir=$(cd "$(dirname "$0")" && pwd)
source_root=$(cd "$script_dir/../.." && pwd)
output_dir="$source_root/artifacts"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$HOME/.local/bin:$PATH"

case "$source_root" in
  /mnt/*)
    echo "This checkout lives on a Windows mount ($source_root); compiling here is slow." >&2
    echo "Run 'pwsh packaging/build-linux.ps1' from Windows instead." >&2
    exit 1
    ;;
esac

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found. Install the .NET 10 SDK before building." >&2
  exit 1
fi

if ! command -v appimagetool >/dev/null 2>&1; then
  echo "appimagetool was not found at ~/.local/bin/appimagetool." >&2
  exit 1
fi

cd "$source_root"
dotnet test -c Release
dotnet publish src/EmuShelf.App \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:InvariantGlobalization=false \
  -o publish/EmuShelf

# Bundle rclone so the local AppImage matches the CI artifact; cloud save sync shells out to it.
rclone_staging=$(mktemp -d)
trap 'rm -rf "$rclone_staging"' EXIT
curl -L -o "$rclone_staging/rclone.zip" https://downloads.rclone.org/rclone-current-linux-amd64.zip
unzip -o -q "$rclone_staging/rclone.zip" -d "$rclone_staging/extract"
rclone_dir="$(dirname "$(find "$rclone_staging/extract" -name rclone -type f | head -n1)")"
install -m 0755 "$rclone_dir/rclone" publish/EmuShelf/rclone
mkdir -p publish/EmuShelf/ThirdParty/rclone
# rclone's release archives ship no license file, but we redistribute the binary and its MIT
# terms require the notice, so pull COPYING from the matching upstream tag.
tag="$(basename "$rclone_dir" | sed -n 's/^rclone-\(v[0-9.]*\)-.*$/\1/p')"
[ -n "$tag" ] || { echo "Could not derive the rclone version from $rclone_dir" >&2; exit 1; }
curl -fL -o publish/EmuShelf/ThirdParty/rclone/LICENSE.txt \
  "https://raw.githubusercontent.com/rclone/rclone/$tag/COPYING"

mkdir -p "$output_dir"
bash packaging/appimage/build-appimage.sh \
  publish/EmuShelf \
  "$output_dir/EmuShelf-linux-x64.AppImage"

chmod +x "$output_dir/EmuShelf-linux-x64.AppImage"
"$output_dir/EmuShelf-linux-x64.AppImage" --appimage-extract-and-run --version
(
  cd "$output_dir"
  sha256sum EmuShelf-linux-x64.AppImage > EmuShelf-linux-x64.sha256
  sha256sum -c EmuShelf-linux-x64.sha256
  ls -lh EmuShelf-linux-x64.AppImage EmuShelf-linux-x64.sha256
)
