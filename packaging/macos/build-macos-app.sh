#!/usr/bin/env bash
# Assembles a macOS .app bundle from a `dotnet publish` output directory.
#
# Usage: build-macos-app.sh <publish-dir> <output.app> [version]
#
# The published output already contains the self-contained apphost, managed dlls
# and native libraries; we just wrap them in the bundle layout macOS expects and
# drop in an Info.plist.
set -euo pipefail

PUBLISH_DIR="${1:?publish directory required}"
APP_PATH="${2:?output .app path required}"
VERSION="${3:-0.0.0-dev}"
EXECUTABLE="EmuShelf"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -e "$PUBLISH_DIR/$EXECUTABLE" ]; then
  echo "Published executable '$EXECUTABLE' not found in $PUBLISH_DIR" >&2
  exit 1
fi

rm -rf "$APP_PATH"
mkdir -p "$APP_PATH/Contents/MacOS" "$APP_PATH/Contents/Resources"

# .NET self-contained apps expect their files beside the apphost, so the entire
# publish output (managed dlls, native libs, licences) goes into MacOS/.
cp -R "$PUBLISH_DIR/." "$APP_PATH/Contents/MacOS/"
chmod +x "$APP_PATH/Contents/MacOS/$EXECUTABLE"

sed "s/@VERSION@/$VERSION/g" "$SCRIPT_DIR/Info.plist" > "$APP_PATH/Contents/Info.plist"

# A .app that is zipped and later unarchived — or downloaded in any way — is tagged with
# com.apple.quarantine. This bundle carries only the .NET apphost's ad-hoc signature (it is not
# notarized), so Gatekeeper then refuses to launch it: "\"EmuShelf\" is damaged and can't be
# opened." Strip the attribute from the freshly built bundle so a local run is clean.
#
# This cannot travel with the app: copying the .app to another Mac (or re-downloading it)
# re-applies quarantine on the receiving machine. Distributing to other Macs requires either a
# Developer ID signature + notarization, or the recipient running this once:
#
#     xattr -dr com.apple.quarantine /path/to/EmuShelf.app
#
# (Ad-hoc code signing does NOT satisfy Gatekeeper for a quarantined app, so it is deliberately
# not attempted here — it would only fail the build on the runtime data the portable app writes
# beside its own executable.)
xattr -dr com.apple.quarantine "$APP_PATH" 2>/dev/null || true

echo "Built $APP_PATH (version $VERSION)"
