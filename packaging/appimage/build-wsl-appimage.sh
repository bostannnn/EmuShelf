#!/usr/bin/env bash
# Builds a Deck-ready AppImage from WSL without writing intermediate build files
# to the Windows-mounted working tree. Invoke this script from an Ubuntu shell.
set -euo pipefail

script_dir=$(cd "$(dirname "$0")" && pwd)
source_root=$(cd "$script_dir/../.." && pwd)
output_dir="$source_root/artifacts"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$HOME/.local/bin:$PATH"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found. Install the .NET 10 SDK in WSL before building." >&2
  exit 1
fi

if ! command -v appimagetool >/dev/null 2>&1; then
  echo "appimagetool was not found at ~/.local/bin/appimagetool." >&2
  exit 1
fi

build_root=$(mktemp -d "$HOME/emushelf-appimage.XXXXXX")
trap 'rm -rf "$build_root"' EXIT

echo "Copying source to WSL workspace: $build_root"
cp -a "$source_root/." "$build_root/"
find "$build_root" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

cd "$build_root"
dotnet test -c Release
dotnet publish src/EmuShelf.App \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:InvariantGlobalization=false \
  -o publish/EmuShelf

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
