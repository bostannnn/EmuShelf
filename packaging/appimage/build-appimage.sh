#!/usr/bin/env bash
set -euo pipefail

publish_dir=${1:?publish directory required}
output=${2:?output AppImage required}
appdir=$(mktemp -d)
trap 'rm -rf "$appdir"' EXIT

mkdir -p "$appdir/usr/bin" "$appdir/usr/lib"
cp -a "$publish_dir/." "$appdir/usr/bin/"
cp packaging/appimage/AppRun "$appdir/AppRun"
chmod +x "$appdir/AppRun"
cp packaging/appimage/emushelf.desktop "$appdir/emushelf.desktop"
cp packaging/appimage/emushelf.svg "$appdir/emushelf.svg"

# Bundle host shared libraries the app dlopens at runtime but the .NET runtime does not
# ship. libicu* backs globalization; libICE/libSM are the X11 session-management libraries
# Avalonia's X11 backend dlopens during platform init. On minimal SteamOS/gamescope images
# libICE/libSM are absent, so without them the app aborts before any window appears
# (System.DllNotFoundException: Unable to load shared library 'libICE.so.6'). Bundling makes
# the portable AppImage independent of whether the host installed them.
for library in libicudata libicui18n libicuuc libICE libSM; do
  path=$(ldconfig -p | awk -v name="$library" '$1 ~ ("^" name "\\.so") { print $NF; exit }')
  if [ -z "${path:-}" ] || [ ! -f "$path" ]; then
    echo "Required bundled library $library was not found" >&2
    exit 1
  fi
  cp -L "$path" "$appdir/usr/lib/"
done

# appimagetool packages the self-contained publish payload, including libicu when supplied
# by the .NET Linux runtime. AppPaths intentionally uses $APPIMAGE's parent, never $APPDIR.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 appimagetool "$appdir" "$output"
